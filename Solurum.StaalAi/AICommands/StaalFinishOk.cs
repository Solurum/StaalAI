namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    public sealed class StaalFinishOk : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;
        public string PrMessage { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            logger.LogDebug($"[STAAL_FINISH_OK] {PrMessage}");

            conversation.Stop();
        }

        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }

}