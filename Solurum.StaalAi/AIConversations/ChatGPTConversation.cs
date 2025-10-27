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

        // --- Token budgeting ---
        // Hard model cap observed in logs: 272,000 tokens total (messages + response).
        private const int ModelContextLimitTokens = 272_000;
        // Keep headroom for model output and safety cushion.
        private const int ReservedOutputTokens = 8_192;

        // --- Pruning knobs ---
        private const int ApproxCharsPerToken = 4;       // coarse heuristic
        private const int MaxTokenBudget = ModelContextLimitTokens - ReservedOutputTokens; // prune when exceeding this
        private const int TargetTokenBudget = ModelContextLimitTokens - ReservedOutputTokens - 32_000; // prune down to about this
        private const int PreserveNewestTurns = 6;       // keep at least N most recent messages

        // When sending, include only a small tail of prior context to keep continuity.
        private const int ContextTailTurnsForSend = 6;

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
        /// Sends the buffered conversation in safe-sized batches and queues the assistant's response for background processing.
        /// </summary>
        /// <returns>True if content was sent; otherwise false when there was nothing to send.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the conversation was not started.</exception>
        public bool SendNextBuffer()
        {
            if (!running) throw new InvalidOperationException("Conversation not started. Call Start() first.");

            if (history.Count <= lastSentMessageIndex) return false; // nothing new since last send

            // Build a window to send that fits within the model budget with output reserve.
            var sendHistory = BuildSendHistory(ReservedOutputTokens, out int newLastSentExclusive);
            if (newLastSentExclusive <= lastSentMessageIndex && sendHistory.Count == 0)
            {
                // Nothing to send (shouldn't happen)
                return false;
            }

            logger.LogDebug("Sending chat window, waiting on response...");
            var opt = new ChatCompletionOptions
            {
                MaxOutputTokenCount = ReservedOutputTokens
            };
            ClientResult<ChatCompletion> completionRsp;
            try
            {
                completionRsp = chat.CompleteChat(sendHistory, opt);
            }
            catch (ClientResultException cre) when (IsContextLengthExceeded(cre))
            {
                logger.LogWarning("context_length_exceeded detected. Rebuilding smaller window and retrying once.");
                // Rebuild with zero context tail to force minimal window
                var minimalHistory = BuildSendHistory(ReservedOutputTokens, out newLastSentExclusive, contextTailOverride: 0);
                completionRsp = chat.CompleteChat(minimalHistory, opt);
            }
            catch (Exception)
            {
                logger.LogWarning("OpenAPI Server Timed Out. Possibly waiting too long on response or network issues.");
                MakeNewChat();
                AddReplyToBuffer("WARNING! Our previous conversation failed with an exception. I suggest handling the files one by one so responses are quicker.", "WARNING");

                // Try again with fresh client and minimal window to be safe
                var minimalHistory = BuildSendHistory(ReservedOutputTokens, out newLastSentExclusive, contextTailOverride: 0);
                completionRsp = chat.CompleteChat(minimalHistory, opt);
            }

            var completion = completionRsp.Value;
            logger.LogDebug("Received AI Response..");

            apiCalls++;

            // Count only the user bytes we actually sent in this batch
            for (int i = lastSentMessageIndex; i < Math.Min(newLastSentExclusive, history.Count); i++)
            {
                if (history[i] is UserChatMessage um)
                {
                    var userText = ExtractAllText(um);
                    bytesIn += Encoding.UTF8.GetByteCount(userText);
                }
            }

            var assistantText = ExtractAssistantTopText(completion);
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
            lastSentMessageIndex = newLastSentExclusive;

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

            // First round to open the session (small enough, but still reserve output)
            logger.LogInformation("Initial Prompt Sent, waiting on response...");
            var opt = new ChatCompletionOptions
            {
                MaxOutputTokenCount = ReservedOutputTokens
            };
            ClientResult<ChatCompletion> completionRsp;
            try
            {
                completionRsp = chat.CompleteChat(history, opt);
            }
            catch (ClientResultException cre) when (IsContextLengthExceeded(cre))
            {
                logger.LogWarning("context_length_exceeded on initial prompt. Trimming and retrying once.");
                AggressivePruneToTarget();
                completionRsp = chat.CompleteChat(history, opt);
            }
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

            // Rough $ estimate (GPT-5-nano)
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
        // Pruning implementation and window builder
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

        private void AggressivePruneToTarget()
        {
            int approxTokens = ApproximateTokens(history);
            int systemIndex = history.FindIndex(m => m is SystemChatMessage);
            if (systemIndex < 0) systemIndex = 0;

            // Remove oldest messages after system until we reach TargetTokenBudget
            int i = Math.Max(systemIndex + 1, 0);
            while (i < history.Count - 2 && approxTokens > TargetTokenBudget)
            {
                var removed = history[i];
                approxTokens -= ApproximateTokens(removed);
                history.RemoveAt(i);
            }

            if (lastSentMessageIndex > history.Count)
                lastSentMessageIndex = history.Count;
        }

        private List<ChatMessage> BuildSendHistory(int reserveOutputTokens, out int newLastSentExclusive, int? contextTailOverride = null)
        {
            int limit = ModelContextLimitTokens - Math.Max(0, reserveOutputTokens);

            var result = new List<ChatMessage>(64);
            int tokensUsed = 0;

            int systemIndex = history.FindIndex(m => m is SystemChatMessage);
            if (systemIndex < 0) systemIndex = 0;

            // Always include the system message if present.
            if (history.Count > 0)
            {
                var sys = history[systemIndex];
                result.Add(sys);
                tokensUsed += ApproximateTokens(sys);
            }

            // Add a small tail of prior context (already-sent messages).
            int ctxTail = contextTailOverride ?? ContextTailTurnsForSend;
            int ctxStart = Math.Max(systemIndex + 1, Math.Min(lastSentMessageIndex, history.Count) - ctxTail);
            for (int i = ctxStart; i < lastSentMessageIndex; i++)
            {
                int t = ApproximateTokens(history[i]);
                if (tokensUsed + t > limit) break;
                result.Add(history[i]);
                tokensUsed += t;
            }

            // Add as many unsent messages as fit.
            int iUns = lastSentMessageIndex;
            int lastIncl = lastSentMessageIndex;
            while (iUns < history.Count)
            {
                int t = ApproximateTokens(history[iUns]);
                if (tokensUsed + t > limit) break;
                result.Add(history[iUns]);
                tokensUsed += t;
                lastIncl = iUns + 1; // exclusive
                iUns++;
            }

            // Ensure we make progress: if no unsent message fit, trim context and try to add at least one.
            if (lastIncl == lastSentMessageIndex && lastSentMessageIndex < history.Count)
            {
                // Clear all except system, then try to add one unsent.
                result.RemoveRange(1, result.Count - 1);
                tokensUsed = ApproximateTokens(result[0]);

                int t = ApproximateTokens(history[lastSentMessageIndex]);
                if (tokensUsed + t <= limit)
                {
                    result.Add(history[lastSentMessageIndex]);
                    tokensUsed += t;
                    lastIncl = lastSentMessageIndex + 1;
                }
                else
                {
                    // Fallback: forcibly send only the first unsent message alone (should not happen with our chunking).
                    result.Clear();
                    if (history.Count > 0)
                    {
                        var sys = history[systemIndex];
                        result.Add(sys);
                    }
                    result.Add(history[lastSentMessageIndex]);
                    lastIncl = lastSentMessageIndex + 1;
                }
            }

            newLastSentExclusive = lastIncl;
            return result;
        }

        private static bool IsContextLengthExceeded(ClientResultException ex)
        {
            // Fallback string check; SDK may not expose error code directly across versions.
            var msg = ex?.Message ?? string.Empty;
            return msg.IndexOf("context_length_exceeded", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("messages resulted in", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("Input tokens exceed the configured limit", StringComparison.OrdinalIgnoreCase) >= 0;
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