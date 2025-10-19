using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Solurum.StaalAi.AICommands;
using System.Linq;

namespace Solurum.StaalAiTests.AICommands
{
    [TestClass]
    public class StaalYamlNormalizer_RepairMessageAndAliases_Tests
    {
        [TestMethod]
        public void ContentChange_With_OriginalText_NewText_Normalizes_To_NewContent()
        {
            var yaml = string.Join("\n", new[]
            {
                "type: STAAL_CONTENT_CHANGE",
                "filePath: /tmp/demo.txt",
                "originalText: |-",
                "  old",
                "newText: |-",
                "  new",
                ""
            });

            var cmds = StaalYamlCommandParser.ParseBundle(yaml);
            cmds.Should().ContainSingle().Which.Should().BeOfType<StaalContentChange>();

            var cc = (StaalContentChange)cmds.Single();
            cc.FilePath.Should().Be("/tmp/demo.txt");
            cc.NewContent.Should().Be("new");
        }

        [TestMethod]
        public void RepairMessage_MultiDoc_With_Command_And_Args_Normalizes()
        {
            var yaml = string.Join("\n", new[]
            {
                "command: STAAL_STATUS",
                "args:",
                "  message: |",
                "    Ready.",
                "---",
                "command: STAAL_CONTENT_REQUEST",
                "args:",
                "  filePath: /home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt",
                ""
            });

            var cmds = StaalYamlCommandParser.ParseBundle(yaml);
            cmds.Should().HaveCount(2);

            cmds[0].Should().BeOfType<StaalStatus>();
            var s = (StaalStatus)cmds[0];
            s.StatusMsg.Should().Contain("Ready.");

            cmds[1].Should().BeOfType<StaalContentRequest>();
            var r = (StaalContentRequest)cmds[1];
            r.FilePath.Should().Be("/home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt");
        }
    }
}