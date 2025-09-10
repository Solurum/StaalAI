namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;
    using Solurum.StaalAi.Shell;

    public sealed class StaalCiLightRequest : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CI_LIGHT_REQUEST]";
            logger.LogDebug(originalCommand);

            if (PowerShellRunner.RunLightCI(logger, fs, workingDirPath, out string psOutput))
            {
                conversation.AddReplyToBuffer($"Finished CI run. Output Files are added to the .heat directory. Output: {psOutput}", originalCommand);
            }
            else
            {
                conversation.AddReplyToBuffer($"Could not run CI. Output: {psOutput}", originalCommand);
            }
        }
    }
}