namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    public sealed class StaalFinishNok : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;
        public string ErrMessage { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            logger.LogDebug($"[STAAL_FINISH_NOK] {ErrMessage}");

            conversation.Stop();
        }

        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }

}