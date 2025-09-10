namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;
    using Solurum.StaalAi.Commands;

    // ----------------- Interface -----------------
    public interface IStaalCommand
    {
        void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath);
    }

}