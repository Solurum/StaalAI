namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Reports a status message from the AI and acknowledges receipt.
    /// </summary>
    public sealed class StaalStatus : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The status message reported by the AI.
        /// </summary>
        public string StatusMsg { get; set; } = string.Empty;

        /// <summary>
        /// Logs the status message and acknowledges with OK.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction (unused).</param>
        /// <param name="workingDirPath">The absolute working directory path (unused).</param>
        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_STATUS] {StatusMsg}";
            logger.LogInformation($"AI Status: {StatusMsg}");

            conversation.AddReplyToBuffer("OK", originalCommand);
        }

        /// <summary>
        /// Validates that <see cref="StatusMsg"/> is provided.
        /// </summary>
        /// <param name="output">When invalid, contains the reason of failure; otherwise empty.</param>
        /// <returns>True when valid; otherwise false.</returns>
        public bool IsValid(out string output)
        {
            if (String.IsNullOrWhiteSpace(StatusMsg))
            {
                output = "Invalid Command! statusMsg was empty.";
                return false;
            }

            output = String.Empty;
            return true;
        }
    }

}