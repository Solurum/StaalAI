using System;

using FluentAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Skyline.DataMiner.CICD.FileSystem;

using Solurum.StaalAi.AICommands;
using Solurum.StaalAi.AIConversations;

namespace Solurum.StaalAi.Tests
{
    [TestClass]
    public class StaalContentChangeTests
    {
        [TestMethod]
        public void Execute_FilePathInsideWorkingDir_WritesFileAndRepliesOk()
        {
            var workingDir = "C:/repo";
            var filePath = "C:/repo/src/Program.cs";
            var content = "class P { static void Main(){} }";

            var logger = new Mock<ILogger>(MockBehavior.Loose);
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var dir = new Mock<IDirectoryIO>(MockBehavior.Loose);
            var file = new Mock<IFileIO>(MockBehavior.Strict);
            var path = new Mock<IPathIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.Directory).Returns(dir.Object);
            fs.SetupGet(f => f.File).Returns(file.Object);
            fs.SetupGet(f => f.Path).Returns(path.Object);

            path.Setup(p => p.GetDirectoryName(filePath)).Returns("C:/repo/src");
            dir.Setup(d => d.CreateDirectory("C:/repo/src"));
            file.Setup(f => f.WriteAllText(filePath, content));

            conv.Setup(c => c.AddReplyToBuffer("OK", $"[STAAL_CONTENT_CHANGE] {filePath}"))
                .Verifiable();

            var cmd = new StaalContentChange { FilePath = filePath, NewContent = content };

            // Act
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            // Assert
            conv.Verify();
        }

        [TestMethod]
        public void Execute_FilePathOutsideWorkingDir_ReturnsBlockedError()
        {
            var workingDir = "C:/repo";
            var filePath = "C:/other/file.txt";

            var logger = new Mock<ILogger>(MockBehavior.Loose);
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(msg => msg.StartsWith("ERR: file does not start with")),
                $"[STAAL_CONTENT_CHANGE] {filePath}"))
                .Verifiable();

            var cmd = new StaalContentChange { FilePath = filePath, NewContent = "abc" };

            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_CreateDirectoryThrows_ReturnsError()
        {
            var workingDir = "C:/repo";
            var filePath = "C:/repo/fail/file.txt";

            var logger = new Mock<ILogger>(MockBehavior.Loose);
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var dir = new Mock<IDirectoryIO>(MockBehavior.Strict);
            var file = new Mock<IFileIO>(MockBehavior.Strict);
            var path = new Mock<IPathIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.Directory).Returns(dir.Object);
            fs.SetupGet(f => f.File).Returns(file.Object);
            fs.SetupGet(f => f.Path).Returns(path.Object);

            path.Setup(p => p.GetDirectoryName(filePath)).Returns("C:/repo/fail");
            dir.Setup(d => d.CreateDirectory("C:/repo/fail"))
                .Throws(new UnauthorizedAccessException("no permission"));

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(msg => msg.StartsWith("ERROR: no permission")),
                $"[STAAL_CONTENT_CHANGE] {filePath}"))
                .Verifiable();

            var cmd = new StaalContentChange { FilePath = filePath, NewContent = "x" };

            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_WriteFileThrows_ReturnsError()
        {
            var workingDir = "C:/repo";
            var filePath = "C:/repo/file.txt";

            var logger = new Mock<ILogger>(MockBehavior.Loose);
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var dir = new Mock<IDirectoryIO>(MockBehavior.Loose);
            var file = new Mock<IFileIO>(MockBehavior.Strict);
            var path = new Mock<IPathIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.Directory).Returns(dir.Object);
            fs.SetupGet(f => f.File).Returns(file.Object);
            fs.SetupGet(f => f.Path).Returns(path.Object);

            path.Setup(p => p.GetDirectoryName(filePath)).Returns("C:/repo");
            dir.Setup(d => d.CreateDirectory("C:/repo"));
            file.Setup(f => f.WriteAllText(filePath, "content"))
                .Throws(new InvalidOperationException("disk full"));

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(msg => msg.StartsWith("ERROR: disk full")),
                $"[STAAL_CONTENT_CHANGE] {filePath}"))
                .Verifiable();

            var cmd = new StaalContentChange { FilePath = filePath, NewContent = "content" };

            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_OuterTryCatch_ExceptionReported()
        {
            var workingDir = "C:/repo";
            var filePath = "C:/repo/file.txt";

            var logger = new Mock<ILogger>(MockBehavior.Loose);
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);

            // Force fs.Path to throw when accessed
            fs.SetupGet(f => f.Path).Throws(new NullReferenceException("boom"));

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(msg => msg.StartsWith("ERR: Could not update filecontent")),
                $"[STAAL_CONTENT_CHANGE] {filePath}"))
                .Verifiable();

            var cmd = new StaalContentChange { FilePath = filePath, NewContent = "data" };

            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }
    }
}