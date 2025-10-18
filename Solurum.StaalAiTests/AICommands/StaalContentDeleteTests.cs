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
    public class StaalContentDeleteTests
    {
        [TestMethod]
        public void Execute_WithinWorkingDir_Deletes_And_OK()
        {
            var workingDir = "/repo";
            var p = "/repo/a.txt";

            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var file = new Mock<IFileIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.File).Returns(file.Object);
            file.Setup(f => f.DeleteFile(p));

            conv.Setup(c => c.AddReplyToBuffer("OK", "[STAAL_CONTENT_DELETE] " + p)).Verifiable();

            var cmd = new StaalContentDelete { FilePath = p };
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_OutsideWorkingDir_Blocked()
        {
            var workingDir = "/repo";
            var p = "/other/a.txt";

            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(s => s.StartsWith("ERR: requested file does not start with")),
                "[STAAL_CONTENT_DELETE] " + p)).Verifiable();

            var cmd = new StaalContentDelete { FilePath = p };
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_DeleteThrows_Err()
        {
            var workingDir = "/repo";
            var p = "/repo/a.txt";

            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var file = new Mock<IFileIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.File).Returns(file.Object);
            file.Setup(f => f.DeleteFile(p)).Throws(new IOException("locked"));

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(s => s.StartsWith("ERR: Could not delete file") && s.Contains("locked")),
                "[STAAL_CONTENT_DELETE] " + p)).Verifiable();

            var cmd = new StaalContentDelete { FilePath = p };
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void IsValid_EmptyPath_False()
        {
            var cmd = new StaalContentDelete();
            cmd.IsValid(out var output).Should().BeFalse();
            output.Should().Contain("Missing the filePath");
        }
    }
}