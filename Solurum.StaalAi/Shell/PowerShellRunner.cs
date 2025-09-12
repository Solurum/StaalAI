namespace Solurum.StaalAi.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Management.Automation;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;

    using Skyline.DataMiner.Sdk.Shell;

    /// <summary>
    /// Provides helpers to run PowerShell-based CI scripts from the tool.
    /// Falls back to invoking the OS shell when the embedded PowerShell API is unavailable.
    /// </summary>
    public static class PowerShellRunner
    {
        static string FindExecutable(string exeName)
        {
            // Check PATH
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(FileSystem.Instance.Path.PathSeparator) ?? new string[0];
            return paths.Select(path => FileSystem.Instance.Path.Combine(path, exeName)).FirstOrDefault(FileSystem.Instance.File.Exists);
        }

        /// <summary>
        /// Executes the lightweight CI PowerShell script (.heat/light_ci.ps1) within the specified working directory.
        /// Uses the PowerShell API when available, and falls back to the platform shell if necessary.
        /// </summary>
        /// <param name="logger">The logger used to report progress and errors.</param>
        /// <param name="fs">The file system abstraction used to resolve paths and check file existence.</param>
        /// <param name="workingDirPath">The absolute working directory in which to run the CI script.</param>
        /// <param name="psOutput">When the method returns, contains combined output and diagnostics from the run.</param>
        /// <returns>True if the script completed without errors; otherwise false.</returns>
        /// <exception cref="InvalidOperationException">Thrown when <paramref name="workingDirPath"/> is null.</exception>
        public static bool RunLightCI(ILogger logger, IFileSystem fs, string workingDirPath, out string psOutput)
        {
            if (workingDirPath == null)
            {
                throw new InvalidOperationException("workingDirectory is null");
            }
            string pathToPowershell = fs.Path.Combine(workingDirPath, ".heat", "light_ci.ps1");

            logger.LogInformation($"Starting {pathToPowershell}...");

            if (FileSystem.Instance.File.Exists(pathToPowershell))
            {
                try
                {
                    using (PowerShell ps = PowerShell.Create())
                    {
                        if (ps == null)
                        {
                            // FallBack. In VS editor, PowerShell works and gives a lot of details and logging.
                            // Falling back to use Shell if the PowerShell.Create() returns null. (slower, but works)
                            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

                            // Determine which PowerShell to use
                            string shellExe;
                            if (isWindows)
                            {
                                string powerShellPath = FindExecutable("pwsh.exe");
                                shellExe = powerShellPath ?? "powershell.exe";
                            }
                            else
                            {
                                string powerShellPath = FindExecutable("pwsh");
                                shellExe = powerShellPath ?? "pwsh";
                            }

                            var shell = ShellFactory.GetShell();
                            string command = $"\"{shellExe}\" \"{pathToPowershell}\" -WorkingDirectoryPath \"{workingDirPath}\"";
                            logger.LogWarning($"Could not execute with Powershell API, falling back to Shell with less runtime details...");

                            if (!shell.RunCommand(command, out string output, out string errors, CancellationToken.None))
                            {
                                string msg = $"Failed to run command '{command}' with output: {output} and errors: {errors}";
                                logger.LogError(msg);
                                psOutput = msg;
                                return false;
                            }
                            else
                            {
                                string msg = $"Successfully executed {pathToPowershell} with output: {output}";
                                logger.LogInformation(msg);
                                psOutput = msg;
                                return true;
                            }
                        }
                        else
                        {
                            StringBuilder psOutputSb = new StringBuilder();
                            ps.AddCommand(pathToPowershell)
                              .AddParameter("WorkingDirectoryPath", workingDirPath);

                            // Because the powershell HadErrors always returns true.
                            bool hadError = false;
                            logger.LogInformation($"Invoking {pathToPowershell}...");
                            psOutputSb.AppendLine($"Invoking {pathToPowershell}...");

                            // Error stream
                            ps.Streams.Error.DataAdded += (s, e) =>
                            {
                                var col = (PSDataCollection<ErrorRecord>)s;
                                var rec = col[e.Index];
                                logger.LogError($"{pathToPowershell} [Error] {rec}");
                                psOutputSb.AppendLine($"{pathToPowershell} [Error] {rec}");
                                hadError = true;
                            };

                            // Warning stream
                            ps.Streams.Warning.DataAdded += (s, e) =>
                            {
                                var col = (PSDataCollection<WarningRecord>)s;
                                var rec = col[e.Index];
                                logger.LogWarning($"{pathToPowershell} [Warning] {rec}");
                                psOutputSb.AppendLine($"{pathToPowershell} [Warning] {rec}");
                            };

                            // Verbose stream
                            ps.Streams.Verbose.DataAdded += (s, e) =>
                            {
                                var col = (PSDataCollection<VerboseRecord>)s;
                                var rec = col[e.Index];
                                logger.LogDebug($"{pathToPowershell} [Verbose] {rec}");
                                psOutputSb.AppendLine($"{pathToPowershell} [Verbose] {rec}");
                            };

                            // Debug stream
                            ps.Streams.Debug.DataAdded += (s, e) =>
                            {
                                var col = (PSDataCollection<DebugRecord>)s;
                                var rec = col[e.Index];
                                logger.LogDebug($"{pathToPowershell} [Debug] {rec}");
                                psOutputSb.AppendLine($"{pathToPowershell} [Debug] {rec}");
                            };

                            // Information stream
                            ps.Streams.Information.DataAdded += (s, e) =>
                            {
                                var col = (PSDataCollection<InformationRecord>)s;
                                var rec = col[e.Index];
                                logger.LogInformation($"{pathToPowershell} [Info] {rec}");
                                psOutputSb.AppendLine($"{pathToPowershell} [Info] {rec}");
                            };

                            // Progress stream
                            ps.Streams.Progress.DataAdded += (s, e) =>
                            {
                                var col = (PSDataCollection<ProgressRecord>)s;
                                var rec = col[e.Index];
                                logger.LogInformation($"{pathToPowershell} [Progress] {rec.Activity} ({rec.PercentComplete}%)");
                                psOutputSb.AppendLine($"{pathToPowershell} [Progress] {rec.Activity} ({rec.PercentComplete}%)");
                            };


                            ps.Invoke();
                            logger.LogInformation($"Finished {pathToPowershell}...");
                            psOutputSb.AppendLine($"Finished {pathToPowershell}...");

                            psOutput = psOutputSb.ToString();
                            if (hadError)
                            {
                                logger.LogError($"{pathToPowershell} indicated it had errors.");
                                return false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"Exception occurred, check the powershell script!: {ex}");
                    psOutput = $"Exception occurred, check the powershell script!: {ex}";
                    return false;
                }
            }
            else
            {
                psOutput = $"No powershell found at {pathToPowershell}...";
                logger.LogWarning($"No powershell found at {pathToPowershell}...");
            }

            return true;
        }

    }
}