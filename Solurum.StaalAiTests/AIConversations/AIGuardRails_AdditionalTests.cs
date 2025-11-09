using System;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Skyline.DataMiner.CICD.FileSystem;
using Solurum.StaalAi.AIConversations;

namespace Solurum.StaalAi.Tests.Conversations
{
    [TestClass]
    public class AIGuardRails_AdditionalTests
    {
        private static (AIGuardRails sut, Mock<IConversation> convo, Mock<IFileSystem> fs, Mock<IFileIO> file)
            CreateSut()
        {
            var convo = new Mock<IConversation>(MockBehavior.Strict);
            convo.Setup(c => c.AddReplyToBuffer(It.IsAny<string>(), It.IsAny<string>()));

            var file = new Mock<IFileIO>(MockBehavior.Strict);
            file.Setup(f => f.ReadAllText("AllowedCommands.txt"))
                .Returns("STAAL_* commands allowed list for tests.");

            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            fs.SetupGet(x => x.File).Returns(file.Object);

            Mock<ILogger> loggerMock = new Mock<ILogger>();
            var sut = new AIGuardRails(fs.Object, convo.Object, loggerMock.Object);
            return (sut, convo, fs, file);
        }

        private static string StatusYaml(string msg = "I'm ready.") =>
            "type: STAAL_STATUS\nstatusMsg: |-\n  " + msg + "\n";

        private static string ContentChangeYaml(string path = "/abs/A.cs", string content = "namespace X { }") =>
            "type: STAAL_CONTENT_CHANGE\nfilePath: " + path + "\nnewContent: |-\n  " + content.Replace("\n", "\n  ") + "\n";

        [TestMethod]
        public void Validate_Should_Warn_With_Canonical_When_Normalization_Applied()
        {
            var (sut, convo, _, _) = CreateSut();

            // Use 'command:' alias and 'message:' -> will normalize to type/statusMsg
            var messy = string.Join("\n", new[]
            {
                "- command: STAAL_STATUS",
                "  message: |-",
                "    Hello there",
            });

            sut.ValidateAndParseResponse(messy).Should().NotBeNull();

            // Expect a WARNING that contains canonical YAML with 'type: STAAL_STATUS'
            convo.Verify(c => c.AddReplyToBuffer(
                It.Is<string>(s => s.Contains("I managed to parse your response", StringComparison.OrdinalIgnoreCase)
                                   && s.Contains("type: STAAL_STATUS", StringComparison.Ordinal)),
                "WARNING"),
                Times.Once);
        }

        [TestMethod]
        public void Consecutive_Validate_Errors_Should_Throw_On_Third()
        {
            var (sut, convo, _, _) = CreateSut();

            // Missing statusMsg -> invalid command after parsing
            var invalid = "type: STAAL_STATUS\n";

            // First invalid -> returns null
            sut.ValidateAndParseResponse(invalid).Should().BeNull();
            // Second invalid -> returns null
            sut.ValidateAndParseResponse(invalid).Should().BeNull();
            // Third invalid -> throws
            Action act = () => sut.ValidateAndParseResponse(invalid);
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Hard Stop - AI Replied with Invalid Data 3 times*");
        }

        [TestMethod]
        public void Status_No_Warning_Before_Threshold()
        {
            var (sut, convo, _, _) = CreateSut();

            // Send 9 unique STATUS messages -> no "busy" warning yet
            for (int i = 1; i <= 9; i++)
            {
                sut.ValidateAndParseResponse(StatusYaml($"msg-{i}")).Should().NotBeNull();
            }

            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("You've been busy for a while", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.Never);
        }

        [TestMethod]
        public void Status_Warning_At_Threshold_And_Reset()
        {
            var (sut, convo, _, _) = CreateSut();

            // First 9 STATUS messages -> no "busy" warning
            for (int i = 1; i <= 9; i++)
            {
                sut.ValidateAndParseResponse(StatusYaml($"first-batch-{i}")).Should().NotBeNull();
            }

            // 10th -> expect 1 "busy" warning and status counter reset
            sut.ValidateAndParseResponse(StatusYaml("first-batch-10")).Should().NotBeNull();
            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("You've been busy for a while", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.Once);

            // Insert a content change to reset the "no document edits" counter,
            // so we don't hit the 20 non-edit hard stop while testing the second busy warning.
            sut.ValidateAndParseResponse(ContentChangeYaml("/abs/reset.cs", "class Reset {}")).Should().NotBeNull();

            // Next 9 -> no additional "busy" warnings yet
            for (int i = 11; i <= 19; i++)
            {
                sut.ValidateAndParseResponse(StatusYaml($"second-batch-{i}")).Should().NotBeNull();
            }

            // 20th overall STATUS (10 after reset of status counter) -> second "busy" warning
            sut.ValidateAndParseResponse(StatusYaml("second-batch-20")).Should().NotBeNull();

            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("You've been busy for a while", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.Exactly(2));
        }
    }
}