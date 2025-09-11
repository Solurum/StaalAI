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

    using Solurum.StaalAi.AICommands;
    using Solurum.StaalAi.Commands;

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

        int maxConsecutiveErrors = 3;
        int currentConsecutiveErrors = 0;


        public ChatGPTConversation(ILogger logger, IFileSystem fs, string workingDirPath, string openApiToken, string openApiModel)
        {
            this.DefaultModel = openApiModel ?? throw new ArgumentNullException(nameof(openApiModel));
            this.openApiToken = openApiToken ?? throw new ArgumentNullException(nameof(openApiToken));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.fs = fs ?? throw new ArgumentNullException(nameof(fs));
            this.workingDirPath = workingDirPath ?? throw new ArgumentNullException(nameof(workingDirPath));
        }

        // Adds user content directly to history (chunking >60k, appending "..." on all but last chunk)
        public void AddReplyToBuffer(string message, string originalCommand)
        {
            if (message == null) message = string.Empty;
            if (originalCommand == null) originalCommand = string.Empty;

            if (message.Length <= ChunkSize)
            {
                string payload = originalCommand + " - " + message;
                history.Add(new UserChatMessage(payload));
                return;
            }

            int idx = 0;
            int chunkIndex = 0;
            int totalChunks = (message.Length + ChunkSize - 1) / ChunkSize;

            while (idx < message.Length)
            {
                int len = Math.Min(ChunkSize, message.Length - idx);
                string chunk = message.Substring(idx, len);
                bool moreComing = (chunkIndex < totalChunks - 1);

                string payload = originalCommand + " - " + chunk + (moreComing ? "..." : string.Empty);
                history.Add(new UserChatMessage(payload));

                idx += len;
                chunkIndex++;
            }
        }

        public bool HasNextBuffer()
        {
            if (history.Count <= lastSentMessageIndex) return false; // nothing new since last send
            return true;
        }

        // Sends the whole history (pruned) and enqueues the assistant text to the background thread
        public bool SendNextBuffer()
        {
            if (!running) throw new InvalidOperationException("Conversation not started. Call Start() first.");

            if (history.Count <= lastSentMessageIndex) return false; // nothing new since last send

            PruneHistoryIfNeeded();

            var completionRsp = chat.CompleteChat(history);
            var completion = completionRsp.Value;
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

            return true;
        }

        public void Start(string initialPrompt)
        {
            if (running) return;

            var options = new OpenAIClientOptions
            {
                // How long to wait for any single network operation
                NetworkTimeout = TimeSpan.FromMinutes(30),

                // (Optional) Tweak retries; fewer retries can help fail fast instead of waiting 4x
                RetryPolicy = new ClientRetryPolicy(maxRetries: 2)
            };


            chat = new ChatClient(model: DefaultModel, credential: new ApiKeyCredential(openApiToken), options: options);

            startedAt = DateTimeOffset.UtcNow;
            history.Clear();

            if (string.IsNullOrWhiteSpace(initialPrompt))
                throw new InvalidOperationException("Initial Prompt is a hard requirement.");

            // Use the initial prompt as SYSTEM
            history.Add(new SystemChatMessage(initialPrompt));

            PruneHistoryIfNeeded();

            // First round to open the session
            var completionRsp = chat.CompleteChat(history);
            var completion = completionRsp.Value;

            apiCalls++;
            bytesIn += Encoding.UTF8.GetByteCount(initialPrompt);

            var firstReply = ExtractAssistantTopText(completion);
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
                                logger.LogError(ex, "Critical Error while handling ChatGPT response. Stopping.");
                                running = false;
                            }
                        }
                    }
                }
                catch (ThreadAbortException) { /* shutting down */ }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Response thread crashed.");
                }
            })
            {
                IsBackground = true,
                Name = "ChatGPTConversationResponseThread"
            };
            responseThread.Start();

            // Queue first reply for handling (do this after thread started; if you prefer,
            // you can queue before and the Take() will pick it up once thread starts)
            if (!string.IsNullOrEmpty(firstReply))
            {
                responses.Add(firstReply);
            }

            responseThread.Join();
        }

        public void Stop()
        {
            if (!running) return;

            running = false;
            responses.CompleteAdding();
            try
            {
                responseThread?.Join(TimeSpan.FromSeconds(5));
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

        private void HandleResponses(string response)
        {
            try
            {
                var allCommands = StaalYamlCommandParser.ParseBundle(response);

                foreach (var cmd in allCommands)
                {
                    cmd.Execute(logger, this, fs, workingDirPath);
                }

                currentConsecutiveErrors = 0;
            }
            catch (Exception ex)
            {

                logger.LogError(ex, "Could not parse ChatGPT Response. Requesting YAML-only resend.");

                var repair =
        $@"Could not parse your response due to exception {ex}. Please resend your previous message as YAML-only commands.
Rules:
- Plain text is not allowed. If you need to report progress, send a STAAL_STATUS with statusMsg: |-.
- Each YAML doc must start with: type: STAAL_...
- Separate docs with exactly:
=====<<STAAL//YAML//SEPARATOR//2AF2E3DE-0F7B-4D0D-8E7C-5D1B8B1A4F0C>>=====
- No code fences. No prose. Indentation 2 spaces. LF newlines only.

If your previous message was progress text, convert it to:
type: STAAL_STATUS
statusMsg: |- 
  (your lines here)

Please use only the following command types.
";

                repair += fs.File.ReadAllText("AllowedCommands.txt");
                currentConsecutiveErrors++;
                if (currentConsecutiveErrors < maxConsecutiveErrors)
                {
                    // Send the repair instruction into the conversation
                    AddReplyToBuffer(repair, "FORMAT_REPAIR");
                    SendNextBuffer();
                }
                else
                {
                    throw;
                }
            }

            SendNextBuffer();
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
    }
}