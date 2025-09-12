namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;
    using Solurum.StaalAi.Commands;

    /// <summary>
    /// Defines a Staal AI command that can be executed within a conversation context.
    /// Implementations perform a specific action and may add results back to the conversation buffer.
    /// </summary>
    public interface IStaalCommand
    {
        /// <summary>
        /// Executes the command logic.
        /// </summary>
        /// <param name="logger">The logger to write diagnostic information to.</param>
        /// <param name="conversation">The active conversation to read from and write results to.</param>
        /// <param name="fs">The file system abstraction to use for file and directory operations.</param>
        /// <param name="workingDirPath">The absolute path of the working directory. Implementations must restrict file operations to this path.</param>
        void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath);

        /// <summary>
        /// Validates the command arguments.
        /// </summary>
        /// <param name="output">When validation fails, contains a human-readable error message; otherwise empty.</param>
        /// <returns>True when the command arguments are valid; otherwise false.</returns>
        bool IsValid(out string output);
    }

}