namespace Solurum.StaalAiTests.AICommands
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Solurum.StaalAi.AICommands;

    [TestClass]
    public class StaalYamlNormalizer_RequestsBlock_Tests
    {
        [TestMethod]
        public void ParseBundle_Splits_TopLevel_Command_With_Requests_Into_Multiple_Docs()
        {
            // Arrange: one top-level command with nested requests
            var yaml = @"command: STAAL_STATUS
message: |
  Ready to create an extensive unit test suite.
  Plan:
  - Read iron.staal.txt and follow its rules.
  - Inspect project structure, code, and existing tests.
  - Identify untested code paths and edge cases.
  - Add comprehensive unit tests.
  - If defects arise, minimally fix code to stabilize tests.
  - Run light CI after changes and iterate.

requests:
  - command: STAAL_CONTENT_REQUEST
    filePath: /home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt
  - command: STAAL_GET_WORKING_DIRECTORY_STRUCTURE
    fromPath: /home/runner/work/StaalAI/StaalAI
    includeContentPattern: """"
    excludeContentPattern: ""*/.git/*""
";

            // Act
            var cmds = StaalYamlCommandParser.ParseBundle(yaml, out var canonical, out var changed);

            // Assert
            Assert.IsTrue(changed);
            Assert.AreEqual(3, cmds.Count);

            Assert.IsInstanceOfType(cmds[0], typeof(StaalStatus));
            var s0 = (StaalStatus)cmds[0];
            StringAssert.Contains(s0.StatusMsg, "Ready to create an extensive unit test suite.");

            Assert.IsInstanceOfType(cmds[1], typeof(StaalContentRequest));
            var s1 = (StaalContentRequest)cmds[1];
            Assert.AreEqual("/home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt", s1.FilePath);

            Assert.IsInstanceOfType(cmds[2], typeof(StaalGetWorkingDirectoryStructure));
        }

        [TestMethod]
        public void ParseBundle_WrapperWithOnlyRequests_Emits_Requests_And_Skips_Parent()
        {
            // Arrange: only a wrapper with requests, no top-level command/type
            var yaml = @"requests:
  - command: STAAL_STATUS
    message: ""ok""
  - command: STAAL_CONTENT_REQUEST
    filePath: /tmp/readme.md
";

            // Act
            var cmds = StaalYamlCommandParser.ParseBundle(yaml, out var canonical, out var changed);

            // Assert
            Assert.IsTrue(changed);
            Assert.AreEqual(2, cmds.Count);
            Assert.IsInstanceOfType(cmds[0], typeof(StaalStatus));
            Assert.IsInstanceOfType(cmds[1], typeof(StaalContentRequest));
        }
    }
}