using FluentAssertions;

using Moq;

using Skyline.DataMiner.CICD.FileSystem;

using Solurum.StaalAi.AICommands;
using Solurum.StaalAi.AIConversations;

namespace Solurum.StaalAi.Tests.Conversations
{
    [TestClass]
    public class AIGuardRails_ValidateAndParseResponse_Tests
    {
        // ---------- Helpers ----------------------------------------------------

        private static (AIGuardRails sut, Mock<IConversation> convo, Mock<IFileSystem> fs, Mock<IFileIO> file)
            CreateSut()
        {
            var convo = new Mock<IConversation>(MockBehavior.Strict);
            // We allow any AddReplyToBuffer unless a test wants to Verify specific calls:
            convo.Setup(c => c.AddReplyToBuffer(It.IsAny<string>(), It.IsAny<string>()));

            var file = new Mock<IFileIO>(MockBehavior.Strict);
            file.Setup(f => f.ReadAllText("AllowedCommands.txt"))
                .Returns("STAAL_* commands allowed list for tests.");

            var fs = new Mock<IFileSystem>(MockBehavior.Strict);
            fs.SetupGet(x => x.File).Returns(file.Object);

            var sut = new AIGuardRails(fs.Object, convo.Object);
            return (sut, convo, fs, file);
        }

        // Minimal, valid single-doc YAMLs we’ll reuse
        private static string StatusYaml(string msg = "I'm ready.") =>
            "type: STAAL_STATUS\nstatusMsg: |-\n  " + msg + "\n";

        private static string ContentChangeYaml(string path = "/abs/A.cs", string content = "namespace X { }") =>
            "type: STAAL_CONTENT_CHANGE\nfilePath: " + path + "\nnewContent: |-\n  " + content.Replace("\n", "\n  ") + "\n";

        private static string ContentRequestYaml(string path = "/abs/A.cs") =>
            "type: STAAL_CONTENT_REQUEST\nfilePath: " + path + "\n";


        // ---------- Tests: Total response limits -------------------------------

        [TestMethod]
        public void TotalResponses_Should_Warn_At_100_And_HardStop_At_200()
        {
            var (sut, convo, _, _) = CreateSut();

            // 1..99: no warning yet
            for (int i = 1; i <= 99; i++)
            {
                var yamlInner = ContentChangeYaml($"i:{i}");
                var cmds = sut.ValidateAndParseResponse(yamlInner);
                cmds.Should().NotBeNull();
            }

            // 100: expect a WARNING AddReplyToBuffer
            var yaml = StatusYaml();
            sut.ValidateAndParseResponse(yaml).Should().NotBeNull();
            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("WARNING! I have received 100 responses", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.Once);

            // 101..199: continue allowed
            for (int i = 101; i <= 199; i++)
            {
                var yamlInner = ContentChangeYaml($"i:{i}");
                sut.ValidateAndParseResponse(yamlInner).Should().NotBeNull();
            }

            // 200: hard stop
            Action act = () => sut.ValidateAndParseResponse(yaml);
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Max Response Count reached: 200*");
        }

        // ---------- Tests: Same-response detection -----------------------------

        [TestMethod]
        public void SameResponse_Should_Warn_Twice_Then_HardStop_On_Third_Repeat()
        {
            var (sut, convo, _, _) = CreateSut();
            var yaml = StatusYaml("same");

            // 1st => baseline (no warning)
            sut.ValidateAndParseResponse(yaml).Should().NotBeNull();

            // 2nd => warning #1
            sut.ValidateAndParseResponse(yaml).Should().NotBeNull();
            // 3rd => warning #2
            sut.ValidateAndParseResponse(yaml).Should().NotBeNull();

            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("same response", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.Exactly(2));

            // 4th => hard stop
            Action act = () => sut.ValidateAndParseResponse(yaml);
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Hard Stop - AI Replied with the exact same response 3 times*");
        }

        // ---------- Tests: No-document-edits window ----------------------------

        [TestMethod]
        public void NoDocumentEdits_Should_Warn_After_3_And_Stop_After_6()
        {
            var (sut, convo, _, _) = CreateSut();

            // Send 1..2 valid non-edit responses (STATUS or REQUEST) -> no warning yet
            sut.ValidateAndParseResponse(StatusYaml("step1")).Should().NotBeNull();
            sut.ValidateAndParseResponse(ContentRequestYaml("/abs/x.cs")).Should().NotBeNull();

            // Third non-edit -> warning begins
            sut.ValidateAndParseResponse(StatusYaml("step3")).Should().NotBeNull();
            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("did not contain any actual content change commands", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.AtLeastOnce);

            // Fourth & Fifth non-edit -> still warnings (no throw yet)
            sut.ValidateAndParseResponse(StatusYaml("step4")).Should().NotBeNull();
            sut.ValidateAndParseResponse(StatusYaml("step5")).Should().NotBeNull();

            // Sixth non-edit -> hard stop
            Action act = () => sut.ValidateAndParseResponse(StatusYaml("step6"));
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Hard Stop - AI Replied 6 times without any document edits*");
        }

        [TestMethod]
        public void StaalContentChange_Should_Reset_NoEdit_Counter_Because_ExactType_Is_Checked()
        {
            var (sut, convo, _, _) = CreateSut();

            // Build up 2 non-edit responses (under the warn threshold)
            sut.ValidateAndParseResponse(StatusYaml("noedit-1")).Should().NotBeNull();
            sut.ValidateAndParseResponse(ContentRequestYaml("/abs/a.cs")).Should().NotBeNull();

            // Now send an edit (STAAL_CONTENT_CHANGE) -> should reset the counter
            sut.ValidateAndParseResponse(ContentChangeYaml("/abs/file.cs", "class C {}")).Should().NotBeNull();

            // Send two more non-edit responses; since counter was reset,
            // we should NOT be at/over the warn threshold (3) yet.
            sut.ValidateAndParseResponse(StatusYaml("noedit-after-reset-1")).Should().NotBeNull();
            sut.ValidateAndParseResponse(StatusYaml("noedit-after-reset-2")).Should().NotBeNull();

            // Verify we never warned about “no edits” after the reset
            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("did not contain any actual content change commands", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.Never);
        }

        // ---------- Tests: Consecutive parse errors ----------------------------

        [TestMethod]
        public void ParseErrors_Should_AddRepairGuidance_UpTo_3_Times_Then_Rethrow_On_4th()
        {
            var (sut, convo, _, _) = CreateSut();
            // Intentionally invalid (no leading "type:")
            var bad = "this is not YAML for commands";

            // First 3 bad responses -> no throw, but ERROR guidance returned via conversation
            for (int i = 1; i <= 3; i++)
            {
                var result = sut.ValidateAndParseResponse(bad);
                result.Should().BeNull("on parse failure before threshold, method returns null");
            }

            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("Could not parse your response", StringComparison.OrdinalIgnoreCase)
                                     && s.Contains("Please resend your previous message as YAML-only commands.", StringComparison.OrdinalIgnoreCase)
                                     ),
                    "ERROR"),
                Times.Exactly(3));

            // Fourth bad response -> rethrow
            Action act = () => sut.ValidateAndParseResponse(bad);
            act.Should().Throw<Exception>(); // parser exception bubbles out
        }

        // ---------- Tests: Happy paths ----------------------------------------

        [TestMethod]
        public void Valid_Status_Bundle_Should_Return_Parsed_Commands()
        {
            var (sut, _, _, _) = CreateSut();
            var yaml = StatusYaml("I'm ready to start.");
            var cmds = sut.ValidateAndParseResponse(yaml);

            cmds.Should().NotBeNull();
            cmds!.Should().NotBeEmpty();
            cmds.Any(c => c is StaalStatus).Should().BeTrue();
        }

        [TestMethod]
        public void Valid_ContentChange_Bundle_Should_Count_As_Edit_And_Not_Warn()
        {
            var (sut, convo, _, _) = CreateSut();
            var yaml = ContentChangeYaml("/abs/StaalStatus.cs", "namespace X { }");

            var cmds = sut.ValidateAndParseResponse(yaml);

            cmds.Should().NotBeNull();
            cmds!.Any(c => c is StaalContentChange).Should().BeTrue();

            // No “no-edits” warning should be raised
            convo.Verify(c => c.AddReplyToBuffer(
                    It.Is<string>(s => s.Contains("did not contain any actual content change commands", StringComparison.OrdinalIgnoreCase)),
                    "WARNING"),
                Times.Never);
        }
    }
}