namespace Solurum.StaalAi.AICommands
{
    using System.Text;

    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Retrieves a list of all files within the configured working directory (recursively).
    /// </summary>
    public sealed class StaalGetWorkingDirectoryStructure : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Executes the request and adds a newline-separated list of absolute file paths to the conversation buffer.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction (unused).</param>
        /// <param name="workingDirPath">The absolute working directory path to enumerate.</param>
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