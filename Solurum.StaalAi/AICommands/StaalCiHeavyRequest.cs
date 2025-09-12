namespace Solurum.StaalAi.AICommands
{
    using Microsoft.Extensions.Logging;

    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Requests a heavy CI operation. Currently not implemented and provided for future use.
    /// </summary>
    public sealed class StaalCiHeavyRequest : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Executes the heavy CI request. At present, this command only logs a warning and returns a message to the conversation.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction (unused).</param>
        /// <param name="workingDirPath">The absolute working directory path (unused).</param>
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

            logger.LogWarning("STAAL_CI_HEAVY_REQUEST: NOT IMPLEMENTED YET, PLEASE NO LONGER USE THIS COMMAND");
            conversation.AddReplyToBuffer("NOT IMPLEMENTED YET, PLEASE NO LONGER USE THIS COMMAND", originalCommand); 
        }

        /// <summary>
        /// Validates the command arguments.
        /// </summary>
        /// <param name="output">When invalid, contains the reason of failure; otherwise empty.</param>
        /// <returns>True. There are no required arguments at present.</returns>
        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }

}