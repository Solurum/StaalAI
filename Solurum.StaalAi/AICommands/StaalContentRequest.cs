namespace Solurum.StaalAi.AICommands
{
    using System.Collections.Generic;
    using System.Linq;

    using Microsoft.VisualBasic;

    using Solurum.StaalAi.AIConversations;

    /// <summary>
    /// Requests file content for one or more files within the working directory.
    /// Supports:
    /// 1) { "type":"STAAL_CONTENT_REQUEST", "filePath":"path" }
    /// 2) { "type":"STAAL_CONTENT_REQUEST", "filePaths":["path1","path2"] }
    /// 3) { "type":"STAAL_CONTENT_REQUEST", "files":[ { "filePath":"path1" }, { "filePath":"path2" } ] }
    /// </summary>
    public sealed class StaalContentRequest : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Single path (intended).</summary>
        public string? FilePath { get; set; }

        /// <summary>Multiple paths as plain strings.</summary>
        public List<string>? FilePaths { get; set; }

        /// <summary>Multiple paths as objects: files: [{ filePath: "..." }].</summary>
        public List<StaalContentFileRef>? Files { get; set; }

        /// <summary>
        /// Executes the content request by reading each requested file and adding its content to the conversation buffer.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction used to access files.</param>
        /// <param name="workingDirPath">The absolute working directory path used as a boundary for allowed reads.</param>
        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            var paths = CollectPaths();

            string originalCommand = $"[STAAL_CONTENT_REQUEST] {string.Join(", ", paths)}";
            logger.LogDebug(originalCommand);

            if (paths.Count == 0)
            {
                throw new InvalidOperationException("ERR: No file paths were provided. Acceptable shapes are: 'filePath', 'filePaths', or 'files: [{ filePath }]'.");
            }

            foreach (var path in paths)
            {
                try
                {
                    if (!fs.File.Exists(path))
                    {
                        string msg = $"ERR: requested file does not exist, filepath: '{path}'. Check the file is an absolute path returned from STAAL_GET_WORKING_DIRECTORY_STRUCTURE.";
                        logger.LogError(msg);
                        conversation.AddReplyToBuffer(msg, originalCommand);
                        continue;
                    }

                    // Simple within-working-dir check
                    bool withinWorkingDir = path.Replace("\\", "/").ToLower()
                        .StartsWith(workingDirPath.Replace("\\", "/").ToLower());

                    if (!withinWorkingDir)
                    {
                        string msg =
                            $"ERR: requested file does not start with {workingDirPath} so is blocked. Only files in the designated working directory can be used.  Check the file is an absolute path returned from STAAL_GET_WORKING_DIRECTORY_STRUCTURE.";
                        logger.LogError(msg);
                        conversation.AddReplyToBuffer(msg, originalCommand);
                        continue;
                    }

                    var fileContent = fs.File.ReadAllText(path);

                    // Wrap each file so multi-file responses are clearly segmented
                    var wrapped = $"--- BEGIN {path} ---\n{fileContent}\n--- END {path} ---";
                    conversation.AddReplyToBuffer(wrapped, originalCommand);
                }
                catch (Exception ex)
                {
                    string msg =
                        $"ERR: Could not retrieve filecontent {path} with exception {ex}. If this continues to happen please respond with STAAL_FINISH_NOK command.";
                    logger.LogError(msg);
                    conversation.AddReplyToBuffer(msg, originalCommand);
                }
            }
        }

        /// <summary>
        /// Validates that at least one path was provided in one of the supported shapes.
        /// </summary>
        /// <param name="output">When invalid, contains the reason of failure; otherwise empty.</param>
        /// <returns>True when a file path is provided; otherwise false.</returns>
        public bool IsValid(out string output)
        {
            output = String.Empty;
            var paths = CollectPaths();

            if (paths.Count == 0)
            {
                output = "ERR: No file paths were provided. Acceptable shapes are: 'filePath', 'filePaths', or 'files: [{ filePath }]'.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Normalizes inputs from filePath, filePaths, and files[*].filePath into a distinct ordered list.
        /// </summary>
        private List<string> CollectPaths()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            void AddIfValid(string? p)
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                if (seen.Add(p)) result.Add(p);
            }

            // 1) single
            AddIfValid(FilePath);

            // 2) list<string>
            if (FilePaths != null)
            {
                foreach (var p in FilePaths) AddIfValid(p);
            }

            // 3) files: [{ filePath }]
            if (Files != null)
            {
                foreach (var f in Files) AddIfValid(f?.FilePath);
            }

            return result;
        }
    }

    /// <summary>
    /// Supports the 'files: [{ filePath: "..." }]' input shape.
    /// </summary>
    public sealed class StaalContentFileRef
    {
        /// <summary>
        /// The file path to request content for.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;
    }
}