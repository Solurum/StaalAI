using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Skyline.DataMiner.CICD.FileSystem;
using Solurum.StaalAi.AICommands;
using Solurum.StaalAi.AIConversations;

namespace Solurum.StaalAi.Tests.AICommands
{
    [TestClass]
    public class StaalBasicCommandsTests
    {
        [TestMethod]
        public void StaalStatus_IsValid_And_Execute_OK()
        {
            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Loose);

            conv.Setup(c => c.AddReplyToBuffer("OK", "[STAAL_STATUS] hello")).Verifiable();

            var cmd = new StaalStatus { StatusMsg = "hello" };
            cmd.IsValid(out var msg).Should().BeTrue();
            msg.Should().BeEmpty();
            cmd.Execute(logger.Object, conv.Object, fs.Object, "/repo");

            conv.Verify();
        }

        [TestMethod]
        public void StaalStatus_IsValid_False_When_Empty()
        {
            var cmd = new StaalStatus { StatusMsg = "" };
            cmd.IsValid(out var msg).Should().BeFalse();
            msg.Should().Contain("statusMsg was empty");
        }

        [TestMethod]
        public void StaalFinishOk_StopsConversation()
        {
            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Loose);

            conv.Setup(c => c.Stop()).Verifiable();

            var cmd = new StaalFinishOk { PrMessage = "done" };
            cmd.IsValid(out var msg).Should().BeTrue();
            cmd.Execute(logger.Object, conv.Object, fs.Object, "/repo");

            conv.Verify();
        }

        [TestMethod]
        public void StaalFinishNok_StopsConversation()
        {
            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Loose);

            conv.Setup(c => c.Stop()).Verifiable();

            var cmd = new StaalFinishNok { ErrMessage = "fail" };
            cmd.IsValid(out var msg).Should().BeTrue();
            cmd.Execute(logger.Object, conv.Object, fs.Object, "/repo");

            conv.Verify();
        }

        [TestMethod]
        public void StaalContinue_When_Buffer_HasNext_Adds_DONE()
        {
            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Loose);

            conv.Setup(c => c.HasNextBuffer()).Returns(true);
            conv.Setup(c => c.AddReplyToBuffer("DONE", "[STAAL_CONTINUE]")).Verifiable();

            var cmd = new StaalContinue();
            cmd.IsValid(out var msg).Should().BeTrue();
            cmd.Execute(logger.Object, conv.Object, fs.Object, "/repo");

            conv.Verify();
        }

        [TestMethod]
        public void StaalContinue_When_NoNextBuffer_DoesNothing()
        {
            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Loose);

            conv.Setup(c => c.HasNextBuffer()).Returns(false);

            var cmd = new StaalContinue();
            cmd.Execute(logger.Object, conv.Object, fs.Object, "/repo");

            // no Verify needed; just ensure no exceptions
        }
    }
}