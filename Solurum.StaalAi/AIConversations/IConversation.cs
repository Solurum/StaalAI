namespace Solurum.StaalAi.AIConversations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the contract for an AI conversation coordinator that manages message buffering and lifecycle.
    /// </summary>
    public interface IConversation
    {
        /// <summary>
        /// Adds a message to eventually reply to the AI. If the message is too big, it will end with "..." and be chunked into several parts.
        /// </summary>
        /// <param name="message">The message payload to add to the outgoing buffer.</param>
        /// <param name="originalCommand">A short prefix that identifies the originating command or context.</param>
        void AddReplyToBuffer(string message, string originalCommand);

        /// <summary>
        /// Sends the next pending buffered messages to the AI (after any pruning) and enqueues the response for processing.
        /// </summary>
        /// <returns>True if something was sent; otherwise false when the buffer was empty.</returns>
        bool SendNextBuffer();

        /// <summary>
        /// Starts the conversation using the specified initial system prompt.
        /// </summary>
        /// <param name="initialPrompt">The system prompt that initializes the conversation context.</param>
        /// <returns>True if the conversation stopped with a failure; otherwise false.</returns>
        bool Start(string initialPrompt);

        /// <summary>
        /// Stops the conversation and releases any background resources.
        /// </summary>
        void Stop();

        /// <summary>
        /// Gets a value indicating whether there are unsent messages in the buffer.
        /// </summary>
        /// <returns>True when there is content waiting to be sent; otherwise false.</returns>
        bool HasNextBuffer();
    }
}