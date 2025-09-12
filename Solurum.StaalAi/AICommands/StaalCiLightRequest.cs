namespace Solurum.StaalAi.AICommands
{
    using Solurum.StaalAi.AIConversations;
    using Solurum.StaalAi.Shell;

    /// <summary>
    /// Requests a light CI operation by invoking the configured PowerShell runner.
    /// </summary>
    public sealed class StaalCiLightRequest : IStaalCommand
    {
        /// <summary>
        /// The command type discriminator used by the YAML parser.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Executes the light CI flow and adds the outcome to the conversation buffer.
        /// </summary>
        /// <param name="logger">The logger to write diagnostics to.</param>
        /// <param name="conversation">The active conversation to write the response into.</param>
        /// <param name="fs">The file system abstraction.</param>
        /// <param name="workingDirPath">The absolute working directory path to run the CI in.</param>
        public void Execute(ILogger logger, IConversation conversation, IFileSystem fs, string workingDirPath)
        {
            string originalCommand = $"[STAAL_CI_LIGHT_REQUEST]";
            logger.LogDebug(originalCommand);

            if (PowerShellRunner.RunLightCI(logger, fs, workingDirPath, out string psOutput))
            {
                conversation.AddReplyToBuffer($"Finished CI run. Output Files are added to the .heat directory. Output: {psOutput}", originalCommand);
            }
            else
            {
                conversation.AddReplyToBuffer($"Could not run CI. Output: {psOutput}", originalCommand);
            }
        }

        /// <summary>
        /// Validates the command arguments.
        /// </summary>
        /// <param name="output">When invalid, contains the reason of failure; otherwise empty.</param>
        /// <returns>True. There are no required arguments at present.</returns>
        public bool IsValid(out string output)
        {
            output = String.Empty;
            return true;
        }
    }
}