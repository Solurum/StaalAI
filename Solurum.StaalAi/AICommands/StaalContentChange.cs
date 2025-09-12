namespace Solurum.StaalAi.AICommands
{
    using Microsoft.VisualBasic;

    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Creates or updates a file with the provided content, restricted to the current working directory.
    /// </summary>
    public sealed class StaalContentChange : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Absolute file path to create or update.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// The new content to write to the file.
        /// </summary>
        public string NewContent { get; set; } = string.Empty;

        /// <summary>
        /// Executes the file content change operation. Ensures the target is within the working directory.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction for file and directory operations.</param>
        /// <param name="workingDirPath">The absolute working directory path used as a boundary for allowed changes.</param>
        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CONTENT_CHANGE] {FilePath}";
            logger.LogDebug($"{originalCommand} ({NewContent.Length} chars)");
            try
            {
                if (FilePath.Replace("\\", "/").ToLower().StartsWith(workingDirPath.Replace("\\", "/").ToLower()))
                {
                    try
                    {
                        // Ensure directory exists
                        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(FilePath));

                        // Write decoded content to file
                        fs.File.WriteAllText(FilePath, NewContent);

                        conversation.AddReplyToBuffer("OK", originalCommand);
                    }
                    catch (Exception ex)
                    {
                        conversation.AddReplyToBuffer($"ERROR: {ex.Message}", originalCommand);
                    }
                }
                else
                {
                    string errorMessage = $"ERR: file does not start with {workingDirPath} so is blocked. Only files in the designated working directory can be used. If this continues to happen please respond with STAAL_FINISH_NOK command.";
                    logger.LogError(errorMessage);
                    conversation.AddReplyToBuffer(errorMessage, originalCommand);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERR: Could not update filecontent {FilePath} with exception {ex}. If this continues to happen please respond with STAAL_FINISH_NOK command.";
                logger.LogError(errorMessage);
                conversation.AddReplyToBuffer(errorMessage, originalCommand);
            }
        }

        /// <summary>
        /// Validates that required arguments are present.
        /// </summary>
        /// <param name="output">When invalid, contains the reason of failure; otherwise empty.</param>
        /// <returns>True when <see cref="FilePath"/> and <see cref="NewContent"/> are provided; otherwise false.</returns>
        public bool IsValid(out string output)
        {
            output = String.Empty;
            if (String.IsNullOrWhiteSpace(FilePath))
            {
                output = "Invalid Command! Missing the filePath argument!";
                return false;
            }

            if (String.IsNullOrWhiteSpace(NewContent))
            {
                output = "Invalid Command! Missing the newContent argument!. If intending to remove a file, use the content delete command instead.)";
                return false;
            }
       
            return true;
        }
    }

}