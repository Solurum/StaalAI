namespace Solurum.StaalAi.AICommands
{
    using System.Text;

    using Solurum.StaalAi.AIConversations;

    public sealed class StaalGetWorkingDirectoryStructure : IStaalCommand
    {
        public string Type { get; set; } = string.Empty;

        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_GET_WORKING_DIRECTORY_STRUCTURE]";
            logger.LogDebug(originalCommand);
            var allFiles = GetAllFilePaths(workingDirPath);
            conversation.AddReplyToBuffer(allFiles, originalCommand);
        }

        /// <summary>
        /// Gets all file paths in the specified directory and its subdirectories, 
        /// and returns them as a newline-separated string.
        /// </summary>
        /// <param name="directoryPath">The path to the directory.</param>
        /// <returns>A string containing each file's full path on a new line.</returns>
        public static string GetAllFilePaths(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("Directory path must not be null or empty.", nameof(directoryPath));
            }

            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException($"The directory '{directoryPath}' does not exist.");
            }

            var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
            var sb = new StringBuilder();

            foreach (var file in files)
            {
                sb.AppendLine(file);
            }

            return sb.ToString();
        }
    }

}