namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Signals that the current task failed and stops the conversation.
    /// </summary>
    public sealed class StaalFinishNok : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// A human-readable error message describing the reason of failure (optional).
        /// </summary>
        public string ErrMessage { get; set; } = string.Empty;

        /// <summary>
        /// Logs the failure and stops the conversation.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to control.</param>
        /// <param name="fs">The file system abstraction (unused).</param>
        /// <param name="workingDirPath">The absolute working directory path (unused).</param>
        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            logger.LogDebug($"[STAAL_FINISH_NOK] {ErrMessage}");

            conversation.Stop();
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