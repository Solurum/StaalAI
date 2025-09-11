namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    public sealed class StaalContentChange : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string NewContent { get; set; } = string.Empty;

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
    }

}