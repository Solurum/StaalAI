namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    public sealed class StaalCiHeavyRequest : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CI_HEAVY_REQUEST]";
            logger.LogDebug(originalCommand);

            // Options here?
            // GIT Commands, create special branch, commit & push.
            // Different CI/CD triggers on changes for those special branches. It adds heat files and commits/pushes.
            // This tool then fetches/pulls the branch until it gets changes with 'heat' files
            // Then this one continues.

            // Github won't trigger from pushes done within a branch normally.
            // We may need to trigger a github branch directly

            // Can I make this technology-independant somehow?
            // Triggering on a specific format tag? --> no then we don't know what branch that came from.

            // Wait WE CAN get a trigger to work with a PAT iso github access token.
            // Still technology dependent though. might aswel trigger a workflow run directly with dispatch.

            conversation.AddReplyToBuffer("NOT IMPLEMENTED YET, PLEASE NO LONGER USE THIS COMMAND", originalCommand); 
        }

        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }

}