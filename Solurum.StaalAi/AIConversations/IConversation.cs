namespace Solurum.StaalAi.AIConversations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public interface IConversation
    {
        /// <summary>
        /// Adds a message to eventually reply to the AI. If the message is too big, it will end with "..." and be 'chunked' into a buffer and sent in several parts.
        /// </summary>
        /// <param name="message"></param>
        void AddReplyToBuffer(string message, string originalCommand);

        /// <summary>
        /// If there is anything in the buffer waiting to be sent, it will send that and return true. If nothing is present this will return a false.
        /// </summary>
        bool SendNextBuffer();

        /// <summary>
        /// Start the AI Conversation.
        /// </summary>
        bool Start(string initialPrompt);

        /// <summary>
        /// Stop the AI Conversation.
        /// </summary>
        void Stop();

        // True when there is unsent content in buffer.
        bool HasNextBuffer();
    }
}
