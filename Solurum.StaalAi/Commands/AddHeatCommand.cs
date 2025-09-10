namespace Solurum.StaalAi.Commands
{
    using System.Text;

    using Solurum.StaalAi.AIConversations;
    using Solurum.StaalAi.SystemCommandLine;

    internal class AddHeatCommand : Command
    {
        public AddHeatCommand() :
            base(name: "add-heat", description: "Add result files, output, logfiles from testing for StaalAI to use.")
        {
            AddOption(new Option<IFileInfoIO>(
                aliases: ["--heat-file", "-hf"],
                description: "The path to a file containing output from testing or similar activities.",
                parseArgument: OptionHelper.ParseFileInfo!)
            {
                IsRequired = true
            }.LegalFilePathsOnly()!.ExistingOnly());

            AddOption(new Option<IDirectoryInfoIO?>(
                aliases: ["--working-directory", "-wd"],
                description: "All file changes will be limited to this folder and all subfolders.",
                parseArgument: OptionHelper.ParseDirectoryInfo)
            {
                IsRequired = true
            }.LegalFilePathsOnly()!.ExistingOnly());

            var heatGroupOption = new Option<string?>(
                aliases: ["--heat-group", "-hg"],
                description: "A identifier used to group together heat files. This defaults to latest and will overwrite.")
            {
                IsRequired = false
            };

            heatGroupOption.SetDefaultValue("latest");
            AddOption(heatGroupOption);
        }
    }

    internal class AddHeatCommandHandler(ILogger<AddHeatCommandHandler> logger, IConfiguration configuration) : ICommandHandler
    {
        /*
         * Automatic binding with System.CommandLine.NamingConventionBinder
         * The property names need to match with the command line argument names.
         * Example: --example-package-file will bind to ExamplePackageFile
         */

        public required IFileInfoIO HeatFile { get; set; }

        public required IDirectoryInfoIO WorkingDirectory { get; set; }

        public string? HeatGroup { get; set; }

        private IFileSystem fs = FileSystem.Instance;

        public int Invoke(InvocationContext context)
        {
            // InvokeAsync is called in Program.cs
            return (int)ExitCodes.NotImplemented;
        }

        public async Task<int> InvokeAsync(InvocationContext context)
        {
            logger.LogDebug("Starting {method}...", nameof(GenerateCommand));

            try
            {
                var heatDir = fs.Path.Combine(WorkingDirectory.FullName, ".heat", HeatGroup);
                var fileLocation = fs.Path.Combine(heatDir, HeatFile.Name);
                fs.File.Copy(HeatFile.FullName, fileLocation, true);

                return (int)ExitCodes.Ok;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed the add-heat command.");
                return (int)ExitCodes.UnexpectedException;
            }
            finally
            {
                logger.LogDebug("Finished {method}.", nameof(GenerateCommand));
            }
        }
    }
}