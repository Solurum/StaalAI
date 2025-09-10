namespace Solurum.StaalAi.Commands
{
    using System.Text;

    using Solurum.StaalAi.AIConversations;
    using Solurum.StaalAi.SystemCommandLine;

    internal enum EditMode
    {
        All,
        OnlyCode,
        OnlyTests,
        OnlyDocumentation
    }

    internal class GenerateCommand : Command
    {
        public GenerateCommand() :
            base(name: "generate", description: "Generates New Code")
        {
            AddOption(new Option<IFileInfoIO>(
                aliases: ["--prompt-file", "-pf"],
                description: "The path to a file containing your request input prompt.",
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

            var editModeOption = new Option<EditMode>(
                aliases: ["--ai-edit-mode", "-em"],
                description: "When provided, it will limit all code changes to specified content.");
            editModeOption.SetDefaultValue(EditMode.All);
            AddOption(editModeOption);
        }
    }

    internal class GenerateCommandHandler(ILogger<GenerateCommandHandler> logger, IConfiguration configuration) : ICommandHandler
    {
        /*
         * Automatic binding with System.CommandLine.NamingConventionBinder
         * The property names need to match with the command line argument names.
         * Example: --example-package-file will bind to ExamplePackageFile
         */

        public required IFileInfoIO PromptFile { get; set; }

        public IDirectoryInfoIO? WorkingDirectory { get; set; }

        public EditMode AiEditMode { get; set; }

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
                PromptGenerator prompt = new PromptGenerator(fs, AiEditMode, fs.File.ReadAllText(PromptFile.FullName), WorkingDirectory.FullName);
                string fullPrompt = prompt.GenerateComplete();

                // TODO, loop & parse all carbon files to find the settings and what ChatGPT to use.
                // TODO, Once we support more than one AI. Figure out how to make them all work together on the same content.
                // TODO, Round Robin, giving each configured AI the recent changes and results of the previous AI. Having them go back and forth until agreement.

                var openApiToken = configuration["StaalOpenApiToken"];
                var openApiModel = configuration["StaalOpenApiModel"];
                if (String.IsNullOrWhiteSpace(openApiToken)) throw new InvalidOperationException("Missing Environment Variable: StaalOpenApiToken");
                if (String.IsNullOrWhiteSpace(openApiModel)) throw new InvalidOperationException("Missing Environment Variable: StaalOpenApiModel");

                IConversation conversation = new ChatGPTConversation(logger, fs, WorkingDirectory.FullName, openApiToken, openApiModel);
                conversation.Start(fullPrompt);

                return (int)ExitCodes.Ok;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed the generate command.");
                return (int)ExitCodes.UnexpectedException;
            }
            finally
            {
                logger.LogDebug("Finished {method}.", nameof(GenerateCommand));
            }
        }
    }
}