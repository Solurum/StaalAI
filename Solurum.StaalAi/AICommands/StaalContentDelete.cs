namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Deletes a file located within the allowed working directory.
    /// </summary>
    public sealed class StaalContentDelete : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The absolute file path to delete.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Executes the delete operation, ensuring the file resides within the working directory.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction for file operations.</param>
        /// <param name="workingDirPath">The absolute working directory path used as a boundary for allowed deletions.</param>
        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CONTENT_DELETE] {FilePath}";
            logger.LogDebug(originalCommand);
            try
            {
                if (FilePath.Replace("\\","/").ToLower().StartsWith(workingDirPath.Replace("\\","/").ToLower()))
                {
                    fs.File.DeleteFile(FilePath);
                    conversation.AddReplyToBuffer("OK", originalCommand);
                }
                else
                {
                    string errorMessage = $"ERR: requested file does not start with {workingDirPath} so is blocked from deletion. Only files in the designated working directory can be adjusted. If this continues to happen please respond with STAAL_FINISH_NOK command.";
                    logger.LogError(errorMessage);
                    conversation.AddReplyToBuffer(errorMessage, originalCommand);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERR: Could not delete file {FilePath} with exception {ex}. If this continues to happen please respond with STAAL_FINISH_NOK command.";
                logger.LogError(errorMessage);
                conversation.AddReplyToBuffer(errorMessage, originalCommand);
            }

        }

        /// <summary>
        /// Validates that <see cref="FilePath"/> is provided.
        /// </summary>
        /// <param name="output">When invalid, contains the reason of failure; otherwise empty.</param>
        /// <returns>True when valid; otherwise false.</returns>
        public bool IsValid(out string output)
        {
            output = String.Empty;
            if (String.IsNullOrWhiteSpace(FilePath))
            {
                output = "Invalid Command! Missing the filePath argument!";
                return false;
            }

            return true;
        }
    }

}