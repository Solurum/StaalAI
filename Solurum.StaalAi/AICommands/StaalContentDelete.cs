namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;

    public sealed class StaalContentDelete : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

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
    }

}