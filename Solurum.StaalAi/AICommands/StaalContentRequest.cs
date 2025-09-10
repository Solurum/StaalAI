namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    // ----------------- Concrete commands -----------------
    public sealed class StaalContentRequest : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CONTENT_REQUEST] {FilePath}";
            logger.LogDebug(originalCommand);
            try
            {
                if (fs.File.Exists(FilePath))
                {
                    if (FilePath.Replace("\\", "/").ToLower().StartsWith(workingDirPath.Replace("\\", "/").ToLower()))
                    {
                        var fileContent = fs.File.ReadAllText(FilePath);
                        conversation.AddReplyToBuffer(fileContent, originalCommand);
                    }
                    else
                    {
                        string errorMessage = $"ERR: requested file does not start with {workingDirPath} so is blocked. Only files in the designated working directory can be used.";
                        logger.LogError(errorMessage);
                        conversation.AddReplyToBuffer(errorMessage, originalCommand);
                    }
                }
                else
                {
                    string errorMessage = $"ERR: requested file does not exist {FilePath}";
                    logger.LogError(errorMessage);
                    conversation.AddReplyToBuffer(errorMessage, originalCommand);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERR: Could not retrieve filecontent {FilePath} with exception {ex}";
                logger.LogError(errorMessage);
                conversation.AddReplyToBuffer(errorMessage, originalCommand);
            }
        }
    }

}