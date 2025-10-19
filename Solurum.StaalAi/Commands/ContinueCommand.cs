namespace Solurum.StaalAi.Commands
{
    using Solurum.StaalAi.SystemCommandLine;
    using Skyline.DataMiner.Sdk.Shell;
    using Solurum.StaalAi.CI;

    internal class ContinueCommand : Command
    {
        public ContinueCommand() :
            base(name: "continue", description: "Resumes a paused Heavy CI run and collects results.")
        {
            this.AddAlias("resume");
            this.AddAlias("cont");

            AddOption(new Option<IDirectoryInfoIO?>(
                aliases: ["--repo-root"],
                description: "Repository root path. Defaults to current directory.",
                parseArgument: OptionHelper.ParseDirectoryInfo));

            AddOption(new Option<bool>(
                aliases: ["--no-fetch"],
                description: "Skips git fetch/pull for the recorded branch."));

            AddOption(new Option<int?>(
                aliases: ["--timeout-min"],
                description: "Override polling timeout in minutes."));

            AddOption(new Option<bool>(
                aliases: ["-v", "--verbose"],
                description: "Verbose output for troubleshooting."));
        }
    }

    internal class ContinueCommandHandler(ILogger<ContinueCommandHandler> logger) : ICommandHandler
    {
        public IDirectoryInfoIO? RepoRoot { get; set; }

        public bool NoFetch { get; set; }

        public int? TimeoutMin { get; set; }

        public bool Verbose { get; set; }

        private IFileSystem fs = FileSystem.Instance;

        public int Invoke(InvocationContext context)
        {
            return (int)ExitCodes.NotImplemented;
        }

        public async Task<int> InvokeAsync(InvocationContext context)
        {
            try
            {
                var root = RepoRoot?.FullName ?? Directory.GetCurrentDirectory();
                var heatDir = fs.Path.Combine(root, ".heat");
                var pendingPath = fs.Path.Combine(heatDir, "heavy_ci_pending.json");
                var convPath = fs.Path.Combine(heatDir, "conversation.json");

                if (!fs.File.Exists(pendingPath) || !fs.File.Exists(convPath))
                {
                    logger.LogError("No paused Heavy CI found.");
                    Console.Error.WriteLine("No paused Heavy CI found.");
                    return (int)ExitCodes.Fail;
                }

                var shell = ShellFactory.GetShell();
                IClock clock = new SystemClock();
                IGitHubCiProvider gh = new GitHubCiProvider(logger);
                var orchestrator = new HeavyCiOrchestrator(logger, fs, shell, clock, gh);

                var resumeResult = orchestrator.Resume(root, new ResumeOptions
                {
                    NoFetch = NoFetch,
                    TimeoutOverrideMinutes = TimeoutMin,
                    Verbose = Verbose
                });

                Console.WriteLine(resumeResult.ConsoleSummary);
                return resumeResult.ExitCode;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to resume Heavy CI.");
                Console.Error.WriteLine("Failed to resume Heavy CI.");
                return (int)ExitCodes.UnexpectedException;
            }
        }
    }
}