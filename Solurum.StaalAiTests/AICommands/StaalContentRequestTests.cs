using System;
using System.Collections.Generic;
using System.Linq;

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
    public class StaalContentRequestTests
    {
        [TestMethod]
        public void IsValid_WithFilePath_True()
        {
            var cmd = new StaalContentRequest { FilePath = "/abs/a.txt" };
            cmd.IsValid(out var output).Should().BeTrue();
            output.Should().BeEmpty();
        }

        [TestMethod]
        public void IsValid_WithFilePathsList_True()
        {
            var cmd = new StaalContentRequest { FilePaths = new List<string> { "/abs/a.txt", "/abs/b.txt" } };
            cmd.IsValid(out var output).Should().BeTrue("list shape should be accepted without single filePath");
            output.Should().BeEmpty();
        }

        [TestMethod]
        public void IsValid_WithFilesObjectList_True()
        {
            var cmd = new StaalContentRequest
            {
                Files = new List<StaalContentFileRef>
                {
                    new StaalContentFileRef { FilePath = "/abs/a.txt" },
                    new StaalContentFileRef { FilePath = "/abs/b.txt" },
                }
            };
            cmd.IsValid(out var output).Should().BeTrue("files:[{filePath}] shape should be accepted");
            output.Should().BeEmpty();
        }

        [TestMethod]
        public void IsValid_NoPaths_False()
        {
            var cmd = new StaalContentRequest();
            cmd.IsValid(out var output).Should().BeFalse();
            output.Should().Contain("No file paths were provided");
        }

        [TestMethod]
        public void Execute_FileExistsWithinWorkingDir_WrapsContent()
        {
            var workingDir = "/repo";
            var p = "/repo/src/a.txt";

            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var file = new Mock<IFileIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.File).Returns(file.Object);

            file.Setup(f => f.Exists(p)).Returns(true);
            file.Setup(f => f.ReadAllText(p)).Returns("hello");

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(s => s.StartsWith($"--- BEGIN {p} ---") && s.Contains("hello") && s.EndsWith($"--- END {p} ---")),
                "[STAAL_CONTENT_REQUEST] " + p
            )).Verifiable();

            var cmd = new StaalContentRequest { FilePath = p };
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_FileOutsideWorkingDir_Blocked()
        {
            var workingDir = "/repo";
            var p = "/other/file.txt";

            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var file = new Mock<IFileIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.File).Returns(file.Object);
            file.Setup(f => f.Exists(p)).Returns(true);

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(s => s.StartsWith("ERR: requested file does not start with")),
                "[STAAL_CONTENT_REQUEST] " + p
            )).Verifiable();

            var cmd = new StaalContentRequest { FilePath = p };
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_FileMissing_Err()
        {
            var workingDir = "/repo";
            var p = "/repo/src/missing.txt";

            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var file = new Mock<IFileIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.File).Returns(file.Object);
            file.Setup(f => f.Exists(p)).Returns(false);

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(s => s.StartsWith("ERR: requested file does not exist")),
                "[STAAL_CONTENT_REQUEST] " + p
            )).Verifiable();

            var cmd = new StaalContentRequest { FilePath = p };
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }

        [TestMethod]
        public void Execute_ReadThrows_Err()
        {
            var workingDir = "/repo";
            var p = "/repo/src/a.txt";

            var logger = new Mock<ILogger>();
            var conv = new Mock<IConversation>(MockBehavior.Strict);
            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            var file = new Mock<IFileIO>(MockBehavior.Strict);

            fs.SetupGet(f => f.File).Returns(file.Object);
            file.Setup(f => f.Exists(p)).Returns(true);
            file.Setup(f => f.ReadAllText(p)).Throws(new InvalidOperationException("boom"));

            conv.Setup(c => c.AddReplyToBuffer(
                It.Is<string>(s => s.StartsWith("ERR: Could not retrieve filecontent") && s.Contains("boom")),
                "[STAAL_CONTENT_REQUEST] " + p
            )).Verifiable();

            var cmd = new StaalContentRequest { FilePath = p };
            cmd.Execute(logger.Object, conv.Object, fs.Object, workingDir);

            conv.Verify();
        }
    }
}