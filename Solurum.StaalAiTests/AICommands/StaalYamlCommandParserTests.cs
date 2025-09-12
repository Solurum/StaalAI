using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using FluentAssertions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Solurum.StaalAi.AICommands;

namespace Solurum.StaalAi.Tests
{
    [TestClass]
    public class StaalYamlCommandParserTests
    {
        private const string Sep = StaalYamlCommandParser.Separator;

        private static string JoinDocs(params string[] docs) => string.Join(Sep, docs);
        private static string Yaml(params string[] lines) => string.Join("\n", lines) + "\n";

        [TestMethod]
        public void ParseBundle_EmptyOrWhitespace_ReturnsEmpty()
        {
            StaalYamlCommandParser.ParseBundle("").Should().BeEmpty();
            StaalYamlCommandParser.ParseBundle(" \n\t ").Should().BeEmpty();
        }

        [TestMethod]
        public void ParseBundle_Single_CONTENT_REQUEST_Parses()
        {
            var doc = Yaml(
                "type: STAAL_CONTENT_REQUEST",
                "filePath: src/Program.cs"
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().HaveCount(1);
            result[0].Should().BeOfType<StaalContentRequest>();
            var req = (StaalContentRequest)result[0];
            req.Type.Should().Be("STAAL_CONTENT_REQUEST");
            req.FilePath.Should().Be("src/Program.cs");
        }

        [TestMethod]
        public void ParseBundle_Single_CONTENT_DELETE_Parses()
        {
            var doc = Yaml(
                "type: STAAL_CONTENT_DELETE",
                "filePath: path/to/delete.txt"
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalContentDelete>();
            var del = (StaalContentDelete)result.Single();
            del.FilePath.Should().Be("path/to/delete.txt");
        }

        [TestMethod]
        public void ParseBundle_Single_GET_WORKING_DIRECTORY_STRUCTURE_Parses()
        {
            var doc = Yaml("type: STAAL_GET_WORKING_DIRECTORY_STRUCTURE");

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalGetWorkingDirectoryStructure>();
        }

        [TestMethod]
        public void ParseBundle_Single_CI_LIGHT_Parses()
        {
            var doc = Yaml("type: STAAL_CI_LIGHT_REQUEST");

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalCiLightRequest>();
        }

        [TestMethod]
        public void ParseBundle_Single_CI_HEAVY_Parses()
        {
            var doc = Yaml("type: STAAL_CI_HEAVY_REQUEST");

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalCiHeavyRequest>();
        }

        [TestMethod]
        public void ParseBundle_Single_FINISH_OK_Parses()
        {
            var doc = Yaml(
                "type: STAAL_FINISH_OK",
                "prMessage: |-",
                "  ## Summary",
                "  - X",
                "",
                "  **Breaking**",
                "  - Y"
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalFinishOk>();
            var ok = (StaalFinishOk)result.Single();
            ok.PrMessage.Should().Contain("## Summary").And.Contain("**Breaking**");
        }

        [TestMethod]
        public void ParseBundle_Single_FINISH_NOK_Parses()
        {
            var doc = Yaml(
                "type: STAAL_FINISH_NOK",
                "errMessage: |",
                "  Failure because reasons."
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalFinishNok>();
            var nok = (StaalFinishNok)result.Single();
            nok.ErrMessage.Should().Contain("Failure");
        }

        [TestMethod]
        public void ParseBundle_Single_STATUS_Parses()
        {
            var doc = Yaml(
                "type: STAAL_STATUS",
                "statusMsg: \"Refactoring step 1\""
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalStatus>();
            var st = (StaalStatus)result.Single();
            st.StatusMsg.Should().Be("Refactoring step 1");
        }

        [TestMethod]
        public void ParseBundle_Single_CONTINUE_Parses()
        {
            var doc = Yaml("type: STAAL_CONTINUE");

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle()
                  .Which.Should().BeOfType<StaalContinue>();
        }

        [TestMethod]
        public void ParseBundle_CONTENT_CHANGE_SimpleShape_Parses()
        {
            var contentLines = new[]
            {
                "using System;",
                "",
                "class App {",
                "  static void Main() {",
                "    Console.WriteLine(\"Hi\");",
                "  }",
                "}"
            };

            var doc = Yaml(
                "type: STAAL_CONTENT_CHANGE",
                "filePath: src/App.cs",
                "newContent: |",
                $"  {string.Join("\n  ", contentLines)}",
                ""
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle().Which.Should().BeOfType<StaalContentChange>();
            var cc = (StaalContentChange)result.Single();

            cc.Type.Should().Be("STAAL_CONTENT_CHANGE");
            cc.FilePath.Should().Be("src/App.cs");
            cc.NewContent.Should().Contain("Console.WriteLine(\"Hi\");");
            cc.NewContent.Should().EndWith("\n"); // YAML '|' keeps final newline
        }

        [TestMethod]
        public void ParseBundle_CONTENT_CHANGE_StripFinalNewline_ParsesWithoutTrailingLF()
        {
            var doc = Yaml(
                "type: STAAL_CONTENT_CHANGE",
                "filePath: src/NoFinalNewline.txt",
                "newContent: |-",
                "  line1",
                "  line2"
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle().Which.Should().BeOfType<StaalContentChange>();
            var cc = (StaalContentChange)result.Single();
            cc.NewContent.Should().Be("line1\nline2"); // no trailing newline due to '|-'
        }

        [TestMethod]
        public void ParseBundle_MultipleDocs_OrderIsPreserved()
        {
            var d1 = Yaml("type: STAAL_STATUS", "statusMsg: s1");
            var d2 = Yaml("type: STAAL_CONTENT_CHANGE", "filePath: a.txt", "newContent: |", "  A", "");
            var d3 = Yaml("type: STAAL_CONTINUE");

            var bundle = JoinDocs(d1, d2, d3);

            var result = StaalYamlCommandParser.ParseBundle(bundle);

            result.Should().HaveCount(3);
            result[0].Should().BeOfType<StaalStatus>();
            result[1].Should().BeOfType<StaalContentChange>();
            result[2].Should().BeOfType<StaalContinue>();
        }

        [TestMethod]
        public void ParseBundle_BlankFragments_And_DoubleSeparator_AreIgnored()
        {
            var d1 = Yaml("type: STAAL_CONTINUE");
            var d3 = Yaml("type: STAAL_STATUS", "statusMsg: ok");
            var bundle = d1 + Sep + "   " + Sep + Sep + d3;

            var result = StaalYamlCommandParser.ParseBundle(bundle);

            result.Should().HaveCount(2);
            result[0].Should().BeOfType<StaalContinue>();
            result[1].Should().BeOfType<StaalStatus>();
        }

        [TestMethod]
        public void ParseBundle_YamlCommentsInsideDocs_AreIgnored()
        {
            var doc = Yaml(
                "type: STAAL_STATUS",
                "# comment",
                "statusMsg: \"hello\" # trailing"
            );

            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle().Which.Should().BeOfType<StaalStatus>();
            ((StaalStatus)result.Single()).StatusMsg.Should().Be("hello");
        }

        [TestMethod]
        public void ParseBundle_UnknownType_ThrowsNotSupported()
        {
            var doc = Yaml("type: STAAL_UNKNOWN", "foo: bar");

            Action act = () => StaalYamlCommandParser.ParseBundle(doc);

            act.Should().Throw<NotSupportedException>()
               .WithMessage("*STAAL_UNKNOWN*");
        }

        [TestMethod]
        public void ParseBundle_MissingType_ThrowsInvalidOperation()
        {
            var doc = Yaml("filePath: something.txt");

            Action act = () => StaalYamlCommandParser.ParseBundle(doc);

            act.Should().Throw<InvalidOperationException>();
        }
    }
}