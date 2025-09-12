namespace Solurum.StaalAi.AICommands
{
    using Microsoft.Extensions.Logging;

    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Continues processing the conversation buffer when more responses are queued.
    /// </summary>
    public sealed class StaalContinue : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Executes the continue action and signals completion to the conversation when applicable.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction (unused).</param>
        /// <param name="workingDirPath">The absolute working directory path (unused).</param>
        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CONTINUE]";
            logger.LogDebug(originalCommand);

            if (conversation.HasNextBuffer())
            {
                conversation.AddReplyToBuffer("DONE", originalCommand);
            }
        }

        /// <summary>
        /// Validates the command arguments.
        /// </summary>
        /// <param name="output">When invalid, contains the reason of failure; otherwise empty.</param>
        /// <returns>True. There are no required arguments.</returns>
        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }

}