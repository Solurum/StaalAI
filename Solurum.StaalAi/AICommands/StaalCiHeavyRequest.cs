namespace Solurum.StaalAi.AICommands
{
    using Microsoft.Extensions.Logging;
    using Solurum.StaalAi.AIConversations;
    using Solurum.StaalAi.CI;

    /// <summary>
    /// Requests a heavy CI operation. Implements synchronous start with graceful transition to Heavy mode.
    /// </summary>
    public sealed class StaalCiHeavyRequest : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CI_HEAVY_REQUEST]";
            logger.LogDebug(originalCommand);

            var configPath = fs.Path.Combine(workingDirPath, ".heat", "carbon.staal.xml");
            if (!fs.File.Exists(configPath))
            {
                logger.LogWarning("STAAL_CI_HEAVY_REQUEST: Missing .heat/carbon.staal.xml, returning not-implemented message.");
                conversation.AddReplyToBuffer("NOT IMPLEMENTED YET, PLEASE NO LONGER USE THIS COMMAND", originalCommand);
                return;
            }

            try
            {
                var shell = Skyline.DataMiner.Sdk.Shell.ShellFactory.GetShell();
                IClock clock = new SystemClock();
                IGitHubCiProvider gh = new GitHubCiProvider(logger);
                var orchestrator = new HeavyCiOrchestrator(logger, fs, shell, clock, gh);

                var result = orchestrator.StartOrContinue(workingDirPath);

                if (result.Mode == HeavyCiMode.Completed)
                {
                    var summaryPath = fs.Path.Combine(workingDirPath, ".heat", "ci_summary.md");
                    conversation.AddReplyToBuffer($"Heavy CI finished. Summary written to: {summaryPath}", originalCommand);
                }
                else if (result.Mode == HeavyCiMode.Waiting)
                {
                    conversation.AddReplyToBuffer("Heavy CI is still running. Re-run the CLI 'staal continue' when results are available.", originalCommand);
                }
                else
                {
                    conversation.AddReplyToBuffer($"CI completed with status: {result.Status}", originalCommand);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "STAAL_CI_HEAVY_REQUEST failed unexpectedly.");
                conversation.AddReplyToBuffer("NOT IMPLEMENTED YET, PLEASE NO LONGER USE THIS COMMAND", originalCommand);
            }
        }

        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }
}