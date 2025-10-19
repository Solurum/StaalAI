namespace Solurum.StaalAi.AIConversations
{
    using System;
    using System.ClientModel;
    using System.ClientModel.Primitives;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    using Microsoft.Extensions.Logging;
    using Microsoft.VisualBasic;

    using OpenAI;
    using OpenAI.Chat;     // OpenAI 2.4.0 SDK (NuGet: OpenAI)

    using Serilog.Core;

    using Solurum.StaalAi.AICommands;
    using Solurum.StaalAi.Commands;
    using Solurum.StaalAi.Shell;

    /// <summary>
    /// Implements an OpenAI-based conversation loop with buffering, chunking, background response handling,
    /// and history pruning to respect token budgets.
    /// </summary>
    public class ChatGPTConversation : IConversation
    {
        private const int ChunkSize = 60000;


        // --- Pruning knobs ---
        private const int ApproxCharsPerToken = 4;       // coarse heuristic
        private const int MaxTokenBudget = 380_000; // start pruning above this
        private const int TargetTokenBudget = 320_000; // prune down to about this
        private const int PreserveNewestTurns = 6;       // keep at least N most recent messages

        private readonly string DefaultModel = "gpt-5";
        private readonly ILogger logger;
        private readonly IFileSystem fs;
        private readonly string workingDirPath;
        private readonly string openApiToken;

        private readonly List<ChatMessage> history = new();

        // Background handling of assistant replies to avoid recursive back-and-forth
        private readonly BlockingCollection<string> responses = new();
        private Thread responseThread;

        private ChatClient chat;
        private volatile bool running;

        // Metrics
        private DateTimeOffset? startedAt;
        private DateTimeOffset? stoppedAt;
        private int apiCalls;                     // number of CompleteChat requests
        private long bytesIn;                     // request bytes (approx, user content only)
        private long bytesOut;                    // response bytes (approx)
        private long inputTokens;                 // from SDK usage if available
        private long outputTokens;                // from SDK usage if available

        // Track up to which message index we've counted bytesIn previously
        private int lastSentMessageIndex = 0;

        // Limit amount of errors. Before stopping.

        AIGuardRails chatGptGuardRails;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChatGPTConversation"/> class.
        /// </summary>
        /// <param name="logger">The logger used for diagnostics and metrics.</param>
        /// <param name="fs">File system abstraction passed to executed commands.</param>
        /// <param name="workingDirPath">Absolute path used as the working directory for file operations by commands.</param>
        /// <param name="openApiToken">API token used to authenticate with the OpenAI service.</param>
        /// <param name="openApiModel">The model identifier to use for chat completion requests.</param>
        public ChatGPTConversation(ILogger logger, IFileSystem fs, string workingDirPath, string openApiToken, string openApiModel)
        {
            this.DefaultModel = openApiModel ?? throw new ArgumentNullException(nameof(openApiModel));
            this.openApiToken = openApiToken ?? throw new ArgumentNullException(nameof(openApiToken));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.fs = fs ?? throw new ArgumentNullException(nameof(fs));
            this.workingDirPath = workingDirPath ?? throw new ArgumentNullException(nameof(workingDirPath));
            chatGptGuardRails = new AIGuardRails(fs, this, logger);
        }

        /// <summary>
        /// Adds user content to the buffer. Oversized messages are split into 60k-character chunks.
        /// Each chunk except the last will end with an ellipsis to indicate continuation.
        /// </summary>
        /// <param name="message">The message content to add.</param>
        /// <param name="originalCommand">A short prefix that identifies the originating command or context.</param>
        public void AddReplyToBuffer(string message, string originalCommand)
        {
            logger.LogDebug("Adding Message to Buffer...");
            if (message == null) message = string.Empty;
            if (originalCommand == null) originalCommand = string.Empty;

            if (message.Length <= ChunkSize)
            {
                logger.LogDebug("Message is small enough. Adding directly.");
                string payload = originalCommand + " - " + message;
                history.Add(new UserChatMessage(payload));
                return;
            }

            int idx = 0;
            int chunkIndex = 0;
            int totalChunks = (message.Length + ChunkSize - 1) / ChunkSize;

            logger.LogDebug("Message is too big. Chunking per 60K characters.");
            while (idx < message.Length)
            {
                int len = Math.Min(ChunkSize, message.Length - idx);
                string chunk = message.Substring(idx, len);
                bool moreComing = (chunkIndex < totalChunks - 1);

                string payload = originalCommand + " - " + chunk + (moreComing ? "..." : string.Empty);
                history.Add(new UserChatMessage(payload));
                logger.LogDebug("Added a chunk.");
                idx += len;
                chunkIndex++;
            }
        }

        /// <summary>
        /// Indicates whether there is unsent content in the buffer.
        /// </summary>
        /// <returns>True when there are unsent messages; otherwise false.</returns>
        public bool HasNextBuffer()
        {
            if (history.Count <= lastSentMessageIndex) return false; // nothing new since last send
            return true;
        }

        /// <summary>
        /// Sends the buffered conversation (after pruning) and queues the assistant's response for background processing.
        /// </summary>
        /// <returns>True if content was sent; otherwise false when there was nothing to send.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the conversation was not started.</exception>
        public bool SendNextBuffer()
        {
            if (!running) throw new InvalidOperationException("Conversation not started. Call Start() first.");

            if (history.Count <= lastSentMessageIndex) return false; // nothing new since last send

            PruneHistoryIfNeeded();

            logger.LogDebug("Sending new chat history, waiting on response...");
            ChatCompletionOptions opt = new ChatCompletionOptions();
            ClientResult<ChatCompletion> completionRsp;
            try
            {
                completionRsp = chat.CompleteChat(history);
            }
            catch (Exception)
            {
                logger.LogWarning("OpenAPI Server Timed Out. Possibly waiting too long on response or network issues.");
                MakeNewChat();
                AddReplyToBuffer("WARNING! Our previous conversation failed with an exception. I suggest handling the files one by one so responses are quicker.", "WARNING");

                completionRsp = chat.CompleteChat(history);
            }

            var completion = completionRsp.Value;
            logger.LogDebug("Received AI Response..");

            apiCalls++;

            // Count new user bytes since last call
            for (int i = lastSentMessageIndex; i < history.Count; i++)
            {
                if (history[i] is UserChatMessage um)
                {
                    var userText = ExtractAllText(um);
                    bytesIn += Encoding.UTF8.GetByteCount(userText);
                }
            }

            var assistantText = ExtractAssistantTopText(completion);
            //logger.LogDebug($"Raw Response: {assistantText}");

            bytesOut += Encoding.UTF8.GetByteCount(assistantText);

            try
            {
                var usage = completion?.Usage;
                if (usage != null)
                {
                    inputTokens += usage.InputTokenCount;
                    outputTokens += usage.OutputTokenCount;
                }
            }
            catch { /* ignore */ }

            // Preserve full assistant content (including non-text parts) in history
            history.Add(new AssistantChatMessage(completion));
            lastSentMessageIndex = history.Count;

            // Hand the raw text to background processing to avoid inline recursion
            if (!string.IsNullOrEmpty(assistantText))
            {
                responses.Add(assistantText);
            }
            else
            {
                responses.Add("ERR: Nothing received");
            }

            return true;
        }

        /// <summary>
        /// Starts the conversation by sending the initial system prompt and queueing the first AI reply for processing.
        /// </summary>
        /// <param name="initialPrompt">The system prompt that initializes the conversation context.</param>
        /// <returns>True if the conversation ended with a failure during background processing; otherwise false.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the initial prompt is missing.</exception>
        public bool Start(string initialPrompt)
        {
            bool stoppedWithFailure = false;
            if (running) return true;

            MakeNewChat();

            startedAt = DateTimeOffset.UtcNow;
            history.Clear();

            if (string.IsNullOrWhiteSpace(initialPrompt))
                throw new InvalidOperationException("Initial Prompt is a hard requirement.");

            // Use the initial prompt as SYSTEM
            history.Add(new SystemChatMessage(initialPrompt));

            PruneHistoryIfNeeded();

            // First round to open the session
            logger.LogInformation("Initial Prompt Sent, waiting on response...");
            var completionRsp = chat.CompleteChat(history);
            var completion = completionRsp.Value;
            logger.LogInformation("Initial Prompt response received.");

            apiCalls++;
            bytesIn += Encoding.UTF8.GetByteCount(initialPrompt);

            var firstReply = ExtractAssistantTopText(completion);
            logger.LogDebug($"Raw Response: {firstReply}");
            bytesOut += Encoding.UTF8.GetByteCount(firstReply);

            try
            {
                var usage = completion?.Usage;
                if (usage != null)
                {
                    inputTokens += usage.InputTokenCount;
                    outputTokens += usage.OutputTokenCount;
                }
            }
            catch { /* ignore */ }

            // Store the assistant reply (full content) and queue text for background handling
            history.Add(new AssistantChatMessage(completion));
            lastSentMessageIndex = history.Count;

            if (!string.IsNullOrEmpty(firstReply))
            {
                responses.Add(firstReply);
            }
            else
            {
                responses.Add("ERR: Empty");
            }

            running = true;

            // Start background consumer AFTER running=true so it can loop
            responseThread = new Thread(() =>
            {
                try
                {
                    while (running || responses.Count > 0)
                    {
                        string next;
                        try
                        {
                            next = responses.Take(); // blocks until item or CompleteAdding
                        }
                        catch (InvalidOperationException)
                        {
                            break; // Completed
                        }

                        if (!string.IsNullOrEmpty(next))
                        {
                            try
                            {
                                HandleResponses(next);
                            }
                            catch (Exception ex)
                            {
                                stoppedWithFailure = true;
                                logger.LogError(ex, "Critical Error while handling ChatGPT response. Stopping.");
                                running = false;
                            }
                        }
                    }
                }
                catch (ThreadAbortException) { /* shutting down */ }
                catch (Exception ex)
                {
                    stoppedWithFailure = true;
                    logger.LogError(ex, "Response thread crashed.");
                }
            })
            {
                IsBackground = true,
                Name = "ChatGPTConversationResponseThread"
            };

            responseThread.Start();
            logger.LogDebug("Started Response Thread, main thread blocked until finished.");
            responseThread.Join();

            return stoppedWithFailure;
        }

        private void MakeNewChat()
        {
            var options = new OpenAIClientOptions
            {
                // How long to wait for any single network operation
                NetworkTimeout = TimeSpan.FromMinutes(10),

                // (Optional) Tweak retries; fewer retries can help fail fast instead of waiting 4x
                RetryPolicy = new ClientRetryPolicy(maxRetries: 2)
            };
           
            chat = new ChatClient(model: DefaultModel, credential: new ApiKeyCredential(openApiToken), options: options);
        }

        /// <summary>
        /// Stops the conversation, waits briefly for the response thread, and logs usage metrics.
        /// </summary>
        public void Stop()
        {
            if (!running) return;

            running = false;
            responses.CompleteAdding();
            try
            {
                // Avoid joining the current response thread (would deadlock or stall)
                if (responseThread != null && Thread.CurrentThread.ManagedThreadId != responseThread.ManagedThreadId)
                {
                    responseThread.Join(TimeSpan.FromSeconds(5));
                }
            }
            catch { /* ignore */ }

            stoppedAt = DateTimeOffset.UtcNow;

            var duration = (stoppedAt ?? DateTimeOffset.UtcNow) - (startedAt ?? DateTimeOffset.UtcNow);

            // Rough $ estimate (adjust if your account differs)
            //const decimal inputPerM = 1.25m;   // USD per 1M input tokens (GPT-5)
            //const decimal outputPerM = 10.00m; // USD per 1M output tokens (GPT-5)

            // Rough $ estimate (GPT-5-nano)
            // Source: OpenAI pricing page (gpt-5 product page)
            const decimal inputPerM = 0.05m;  // USD per 1M input tokens (GPT-5-nano)
            const decimal outputPerM = 0.40m;  // USD per 1M output tokens (GPT-5-nano)

            decimal inputCost = inputTokens / 1_000_000m * inputPerM;
            decimal outputCost = outputTokens / 1_000_000m * outputPerM;
            decimal totalCost = inputCost + outputCost;

            logger.LogInformation(
                "ChatGPT conversation stopped. Duration: {Duration}s | API Calls: {ApiCalls} | Bytes In: {BytesIn} | Bytes Out: {BytesOut} | Input Tokens: {InTok} | Output Tokens: {OutTok} | Est. Cost (USD): ${Cost:F4}",
                (long)duration.TotalSeconds, apiCalls, bytesIn, bytesOut, inputTokens, outputTokens, totalCost);

            history.Clear();
            lastSentMessageIndex = 0;
        }

        // ------------------------
        // Pruning implementation
        // ------------------------
        private void PruneHistoryIfNeeded()
        {
            int approxTokens = ApproximateTokens(history);
            if (approxTokens <= MaxTokenBudget) return;

            int systemIndex = history.FindIndex(m => m is SystemChatMessage);
            if (systemIndex < 0) systemIndex = 0; // safety

            int preserveTailStart = Math.Max(history.Count - PreserveNewestTurns, systemIndex + 1);

            int i = Math.Max(systemIndex + 1, 0);
            while (i < preserveTailStart && approxTokens > TargetTokenBudget && history.Count > (preserveTailStart - i))
            {
                var removed = history[i];
                approxTokens -= ApproximateTokens(removed);
                history.RemoveAt(i);
                preserveTailStart = Math.Max(history.Count - PreserveNewestTurns, systemIndex + 1);
            }

            while (approxTokens > TargetTokenBudget && history.Count > (systemIndex + 1 + 2))
            {
                int removableIdx = Math.Max(systemIndex + 1, history.Count - 1 - 2); // keep last 2 msgs
                var removed = history[removableIdx];
                approxTokens -= ApproximateTokens(removed);
                history.RemoveAt(removableIdx);
            }

            if (lastSentMessageIndex > history.Count)
                lastSentMessageIndex = history.Count;
        }

        private static int ApproximateTokens(IEnumerable<ChatMessage> messages)
        {
            int sum = 0;
            foreach (var m in messages)
                sum += ApproximateTokens(m);
            return sum;
        }

        private static int ApproximateTokens(ChatMessage message)
        {
            string text = ExtractAllText(message);
            int chars = text.Length;
            int tokens = chars / ApproxCharsPerToken;
            tokens += 8; // overhead cushion per message
            return tokens;
        }

        private static string ExtractAllText(ChatMessage message)
        {
            var sb = new StringBuilder();
            var content = message?.Content;
            if (content != null && content.Count > 0)
            {
                foreach (var part in content) // ChatMessageContent is enumerable in 2.4.0
                {
                    if (!string.IsNullOrEmpty(part.Text))
                        sb.Append(part.Text);
                }
            }
            return sb.ToString();
        }

        private static string ExtractAssistantTopText(ChatCompletion completion)
        {
            if (completion?.Content != null && completion.Content.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var part in completion.Content) // ChatMessageContent is enumerable
                {
                    if (!string.IsNullOrEmpty(part.Text))
                        sb.Append(part.Text);
                }
                return sb.ToString();
            }
            return string.Empty;
        }

        private void HandleResponses(string response)
        {
            var allCommands = chatGptGuardRails.ValidateAndParseResponse(response);

            if (allCommands != null)
            {
                foreach (var cmd in allCommands)
                {
                    if (!running)
                    {
                        // Conversation was stopped by a command; stop processing further commands.
                        break;
                    }

                    cmd.Execute(logger, this, fs, workingDirPath);
                }
            }

            // If conversation was stopped during command execution (e.g., FINISH_OK/NOK), do not send more.
            if (!running)
            {
                return;
            }

            if (!SendNextBuffer() && running)
            {
                throw new InvalidOperationException("Response Buffer was Empty, could not reply to the AI but expected to.");
            }
        }
    }
}