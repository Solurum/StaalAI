namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    public sealed class StaalStatus : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;
        public string StatusMsg { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_STATUS] {StatusMsg}";
            logger.LogDebug(originalCommand);
            logger.LogInformation($"AI Status: {StatusMsg}");

            conversation.AddReplyToBuffer("OK", originalCommand);
        }
    }

}