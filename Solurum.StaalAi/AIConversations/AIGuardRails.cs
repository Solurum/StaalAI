namespace Solurum.StaalAi.AIConversations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Policy;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Input;

    using Microsoft.Extensions.Logging;

    using Solurum.StaalAi.AICommands;

    /// <summary>
    /// Intended to guard and repair AI Response issues.
    /// </summary>
    public class AIGuardRails
    {
        string lastReceivedResponse = "";
        int maxSameResponseCount = 3;
        int sameResponseCount = 0;

        int maxConsecutiveErrors = 3;
        int currentConsecutiveErrors = 0;

        int maxResponsesWithoutDocumentEditsStop = 6;
        int maxResponsesWithoutDocumentEditsWarning = 3;
        int currentResponsesWithoutDocumentEdits = 0;

        int maxTotalResponses = 200;
        int warningTotalResponses = 100;
        int currentTotalResponses = 0;

        IFileSystem fs;

        IConversation conversation;

        ILogger logger;

        public AIGuardRails(IFileSystem fs, IConversation conversation, ILogger logger)
        {
            this.fs = fs;
            this.conversation = conversation;
            this.logger = logger;
        }

        public IReadOnlyList<AICommands.IStaalCommand> ValidateAndParseResponse(string response)
        {
            currentTotalResponses++;
            if (currentTotalResponses >= maxTotalResponses)
            {
                throw new InvalidOperationException($"ERR - Hard Stop - AI Max Response Count reached: {maxTotalResponses} times. Last Response: {response}");
            }
            else if (currentTotalResponses >= warningTotalResponses)
            {
                logger.LogWarning($"WARNING! I have received {currentTotalResponses} responses out of a maximum of {maxTotalResponses}. If you are finished, please reply with STAAL_FINISH_OK or STAAL_FINISH_NOK commands, if not, then please perform more actions within a single response. I will error and force stop when receiving {maxTotalResponses} total responses or more.");
                conversation.AddReplyToBuffer($"WARNING! I have received {currentTotalResponses} responses out of a maximum of {maxTotalResponses}. If you are finished, please reply with STAAL_FINISH_OK or STAAL_FINISH_NOK commands, if not, then please perform more actions within a single response. I will error and force stop when receiving {maxTotalResponses} total responses or more.", "WARNING");
            }

            if (response == lastReceivedResponse)
            {
                if (++sameResponseCount >= maxSameResponseCount)
                {
                    throw new InvalidOperationException($"ERR - Hard Stop - AI Replied with the exact same response {maxSameResponseCount} times. Response: {response}");
                }
                else
                {
                    logger.LogWarning($"WARNING! I have received the same response from you {sameResponseCount} times. If you are finished, please reply with STAAL_FINISH_OK or STAAL_FINISH_NOK commands. I will error and force stop when receiving the same response {maxSameResponseCount} times.");
                    conversation.AddReplyToBuffer($"WARNING! I have received the same response from you {sameResponseCount} times. If you are finished, please reply with STAAL_FINISH_OK or STAAL_FINISH_NOK commands. I will error and force stop when receiving the same response {maxSameResponseCount} times.", "WARNING");
                }
            }
            else
            {
                lastReceivedResponse = response;
                sameResponseCount = 0;
            }


            // Try to parse the response
            IReadOnlyList<IStaalCommand> allCommands;
            try
            {
                allCommands = StaalYamlCommandParser.ParseBundle(response);
                currentConsecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                currentConsecutiveErrors++;

                if (currentConsecutiveErrors <= maxConsecutiveErrors)
                {
                    var repairParseException =
             $@"Could not parse your response due to exception {ex}. Please resend your previous message as YAML-only commands.
            Rules:
            - Plain text is not allowed. If you need to report progress, send a STAAL_STATUS with statusMsg: |-.
            - Each YAML doc must start with: type: STAAL_...
            - Separate docs with exactly:
            " + StaalSeparator.value + $@"
            - No code fences. No prose. Indentation 2 spaces. LF newlines only.

            If your previous message was progress text, convert it to:
            type: STAAL_STATUS
            statusMsg: |- 
              (your lines here)

            Please use only the following command types:
            " + fs.File.ReadAllText("AllowedCommands.txt");

                    logger.LogWarning($"Had to send {currentConsecutiveErrors} repair messages to AI.");
                    conversation.AddReplyToBuffer(repairParseException, "ERROR");
                }
                else
                {
                    logger.LogError("AI kept sending invalid YAML. Last Response: " + response);
                    throw;
                }

                return null;
            }

            bool hadDocumentEdits = false;
            foreach (var command in allCommands)
            {
                if (!command.IsValid(out var output))
                {
                    conversation.AddReplyToBuffer(output, command.GetType().Name);
                }

                if (command.GetType() == typeof(StaalContentChange))
                {
                    hadDocumentEdits = true;
                }
            }

            if (!hadDocumentEdits)
            {
                currentResponsesWithoutDocumentEdits++;
            }
            else
            {
                currentResponsesWithoutDocumentEdits = 0;
            }

            if (currentResponsesWithoutDocumentEdits >= maxResponsesWithoutDocumentEditsStop)
            {
                throw new InvalidOperationException($"ERR - Hard Stop - AI Replied {maxResponsesWithoutDocumentEditsStop} times without any document edits. Last Response: {response}");
            }
            else if (currentResponsesWithoutDocumentEdits >= maxResponsesWithoutDocumentEditsWarning)
            {
                logger.LogWarning($"WARNING! The last {currentResponsesWithoutDocumentEdits} responses did not contain any actual content change commands. I will force stop after {maxResponsesWithoutDocumentEditsStop} responses without actual code changes.");
                conversation.AddReplyToBuffer($"WARNING! The last {currentResponsesWithoutDocumentEdits} responses did not contain any actual content change commands. I will force stop after {maxResponsesWithoutDocumentEditsStop} responses without actual code changes. Please consider the available commands again: " + fs.File.ReadAllText("AllowedCommands.txt"), "WARNING");
            }
            else
            {
                // all good
            }

            return allCommands;
        }
    }
}
