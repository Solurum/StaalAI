namespace Solurum.StaalAi.AICommands
{
    using Microsoft.Extensions.Logging;

    using Solurum.StaalAi.AIConversations;

    public sealed class StaalContinue : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CONTINUE]";
            logger.LogDebug(originalCommand);

            if (conversation.HasNextBuffer())
            {
                conversation.AddReplyToBuffer("DONE", originalCommand);
            }
        }

        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }

}