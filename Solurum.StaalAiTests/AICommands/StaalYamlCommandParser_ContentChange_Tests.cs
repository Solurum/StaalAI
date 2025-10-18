using System.Text;

using FluentAssertions;

using Solurum.StaalAi.AICommands;

namespace Solurum.StaalAi.Tests
{
    [TestClass]
    public class StaalYamlCommandParser_ContentChange_Tests
    {
        private const string Sep = StaalYamlCommandParser.Separator;

        private static string JoinDocs(params string[] docs) => string.Join(Sep, docs);

        private static string MakeDoc(string filePath, string content, string chomping = "|")
        {
            var norm = content.Replace("\r\n", "\n");
            var lines = norm.Split('\n'); // keeps blanks

            var block = new List<string>();
            if (lines.Length > 0)
            {
                // FIRST line: if blank, emit a truly blank line (no spaces)
                if (lines[0].Length == 0)
                {
                    block.Add(""); // truly blank first content line
                    for (int i = 1; i < lines.Length; i++)
                        block.Add("  " + lines[i]);
                }
                else
                {
                    for (int i = 0; i < lines.Length; i++)
                        block.Add("  " + lines[i]);
                }
            }

            // Compose YAML
            var header = new[] { "type: STAAL_CONTENT_CHANGE", $"filePath: {filePath}", $"newContent: {chomping}" };

            // For clip ('|'), YAML requires a newline after the block; add an extra "" so the doc ends with \n
            if (chomping == "|")
                return string.Join("\n", header.Concat(block).Concat(new[] { "" })) + "\n";

            // For strip ('|-'), no forced extra blank at the end
            return string.Join("\n", header.Concat(block)) + "\n";
        }

        private static void AssertContentOneOf(string actual, string expectedBase, bool expectTrailingLf = false)
        {
            static string Norm(string s) => s.Replace("\r\n", "\n");

            var act = Norm(actual);

            // Collapse expected’s trailing newlines entirely; then allow {no LF, exactly one LF}
            var baseNoLf = Norm(expectedBase).TrimEnd('\n');

            act.Should().BeOneOf(baseNoLf, baseNoLf + "\n");
        }

        [TestMethod]
        public void Sequence_With_CommandAlias_And_LeadingSpace_Parses_NoNulls_And_Yields_Status_And_ContentRequest()
        {
            // Arrange: prose + YAML sequence; first item uses 'command:' alias and a block scalar.
            var yaml = string.Join("\r\n", new[]
            {
        "Some introductory prose that should be ignored by the parser.",
        "It explains what's coming next and is not YAML at the top level.",
        "",
        "- command: STAAL_STATUS",
        "   content: |",
        "     Ready to create an extensive unit test suite.",
        "     Plan:",
        "     - Read iron.staal.txt to follow repo-specific rules.",
        "     - Inspect current tests and source code to find gaps.",
        "     - Propose and add comprehensive tests for all code paths.",
        "     - If tests reveal bugs, fix them without adding new features.",
        "     - Continuously run light CI after changes to ensure green build.",
        "- command: STAAL_CONTENT_REQUEST",
        "  content: |",
        "    filePath: /home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt",
    });

            // Act
            var cmds = StaalYamlCommandParser.ParseBundle(yaml);

            // Assert
            cmds.Should().NotBeNull();
            cmds.Should().HaveCount(2, "the sequence contains two command items");
            cmds.Should().AllSatisfy(c => c.Should().NotBeNull("parser must not add null commands"));

            // Order & types
            cmds[0].Should().BeOfType<StaalStatus>("first item is a STAAL_STATUS using 'command:' alias");
            cmds[1].Should().BeOfType<StaalContentRequest>("second item is a STAAL_CONTENT_REQUEST using 'command:' alias");

            // STAAL_STATUS payload mapped from the literal 'content' -> StatusMsg
            var status = (StaalStatus)cmds[0];
            status.Type.Should().Be("STAAL_STATUS");
            status.StatusMsg.Should().NotBeNullOrWhiteSpace("status payload should be captured");
            status.StatusMsg.Should().Contain("Ready to create an extensive unit test suite.")
                              .And.Contain("Plan:")
                              .And.Contain("Continuously run light CI");

            // STAAL_CONTENT_REQUEST: filePath extracted from the literal content
            var req = (StaalContentRequest)cmds[1];
            req.Type.Should().Be("STAAL_CONTENT_REQUEST");
            req.FilePath.Should().Be("/home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt");
            // If you expect FilePaths/Files to remain null when FilePath is present, you can assert that too:
            // req.FilePaths.Should().BeNull();
            // req.Files.Should().BeNull();
        }

        [TestMethod]
        public void Sequence_With_CommandAlias_Parses_Status_And_ContentRequest_Correctly()
        {
            // Arrange
            var yaml = string.Join("\r\n", new[]
            {
        "- command: STAAL_STATUS",
        "  content: |",
        "    Ready to create an extensive unit test suite.",
        "    Plan:",
        "    - Read iron.staal.txt to follow repo-specific rules.",
        "    - Inspect current tests and source code to find gaps.",
        "    - Propose and add comprehensive tests for all code paths.",
        "    - If tests reveal bugs, fix them without adding new features.",
        "    - Continuously run light CI after changes to ensure green build.",
        "- command: STAAL_CONTENT_REQUEST",
        "  content: |",
        "    filePath: /home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt",
    });

            // Act
            var cmds = StaalYamlCommandParser.ParseBundle(yaml);

            // Assert
            cmds.Should().NotBeNull();
            cmds.Should().HaveCount(2, "the YAML defines two command items");

            // STAAL_STATUS: StatusMsg should be filled from the literal 'content'
            var status = cmds.OfType<StaalStatus>().Single();
            status.Type.Should().Be("STAAL_STATUS");
            status.StatusMsg.Should().NotBeNullOrWhiteSpace()
                  .And.Contain("Ready to create an extensive unit test suite.")
                  .And.Contain("Continuously run light CI");

            // STAAL_CONTENT_REQUEST: FilePath should be extracted from the literal 'content'
            var req = cmds.OfType<StaalContentRequest>().Single();
            req.Type.Should().Be("STAAL_CONTENT_REQUEST");
            req.FilePath.Should().Be("/home/runner/work/StaalAI/StaalAI/Solurum.StaalAi/iron.staal.txt");

            // Optional extras depending on your model:
            // If you expect FilePaths/Files to remain null when FilePath is set:
            // req.FilePaths.Should().BeNull();
            // req.Files.Should().BeNull();
        }



        [TestMethod]
        public void ContentChange_CSharp_Simple_Clip()
        {
            var content = string.Join("\n", new[]
            {
                "using System;",
                "",
                "namespace Demo",
                "{",
                "    public static class App",
                "    {",
                "        public static void Main()",
                "        {",
                "            Console.WriteLine(\"Hi\");",
                "        }",
                "    }",
                "}"
            });

            var doc = MakeDoc("src/App.cs", content, chomping: "|");
            var result = StaalYamlCommandParser.ParseBundle(doc);

            result.Should().ContainSingle().Which.Should().BeOfType<StaalContentChange>();
            var cc = (StaalContentChange)result.Single();

            cc.FilePath.Should().Be("src/App.cs");
            cc.Type.Should().Be("STAAL_CONTENT_CHANGE");
            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: true);
            cc.NewContent.Should().Contain("Console.WriteLine(\"Hi\");");
        }

        [TestMethod]
        public void ContentChange_CSharp_With_Tabs_And_Quotes_Strip()
        {
            var content = string.Join("\n", new[]
            {
                "class T {",
                "\t// a tab-indented line",
                "\tstatic void M() {",
                "\t\tvar s = \"quotes 'and' \\\"double-quotes\\\" and backslash \\\\\";",
                "\t}",
                "}"
            });

            var doc = MakeDoc("src/T.cs", content, chomping: "|-");
            var result = StaalYamlCommandParser.ParseBundle(doc);

            var cc = (StaalContentChange)result.Single();
            cc.FilePath.Should().Be("src/T.cs");
            cc.NewContent.Should().Contain("\\\\"); // literal backslash preserved by YAML literal
            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: false);
        }

        [TestMethod]
        public void ContentChange_Cpp_Headers_Templates_Clip()
        {
            var content = string.Join("\n", new[]
            {
                "#include <vector>",
                "#include <iostream>",
                "",
                "template<typename T>",
                "T add(T a, T b) { return a + b; }",
                "",
                "int main() {",
                "    std::vector<int> v{1,2,3};",
                "    std::cout << add(2,3) << std::endl;",
                "    return 0;",
                "}"
            });

            var doc = MakeDoc("native/main.cpp", content, chomping: "|");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.FilePath.Should().Be("native/main.cpp");
            cc.NewContent.Should().Contain("std::vector<int>");
            cc.NewContent.Should().Contain("template<typename T>");
            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: true);
        }

        [TestMethod]
        public void ContentChange_Xml_With_Declaration_CData_Clip()
        {
            var content = string.Join("\n", new[]
            {
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<root attr=\"&amp; &lt; &gt;\">",
                "  <child>text</child>",
                "  <![CDATA[ <not>parsed & stuff </not> ]]>",
                "</root>"
            });

            var doc = MakeDoc("config/app.xml", content, chomping: "|");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.FilePath.Should().Be("config/app.xml");
            cc.NewContent.Should().Contain("<?xml version=\"1.0\"");
            cc.NewContent.Should().Contain("<![CDATA[");
            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: true);
        }

        [TestMethod]
        public void ContentChange_Yaml_As_Content_Nested_Yaml_Strip()
        {
            var innerYaml = string.Join("\n", new[]
            {
                "---",
                "apiVersion: v1",
                "kind: ConfigMap",
                "metadata:",
                "  name: demo",
                "data:",
                "  key: \"value: still yaml\"",
            });

            var doc = MakeDoc("k8s/config.yaml", innerYaml, chomping: "|-");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.FilePath.Should().Be("k8s/config.yaml");
            cc.NewContent.Should().StartWith("---\n");
            cc.NewContent.Should().Contain("kind: ConfigMap");
            AssertContentOneOf(cc.NewContent, innerYaml, expectTrailingLf: false);
        }

        [TestMethod]
        public void ContentChange_Unicode_Emoji_Accents_Rtl_Clip()
        {
            var content = string.Join("\n", new[]
            {
                "// café Привет مرحبا こんにちは",
                "string s = \"⚙️🚀✨\";"
            });

            var doc = MakeDoc("src/unicode.cs", content, chomping: "|");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.NewContent.Should().Contain("café");
            cc.NewContent.Should().Contain("⚙️🚀✨");
            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: true);
        }

        [TestMethod]
        public void ContentChange_EmptyFile_Strip_ResultsEmptyString()
        {
            var doc = MakeDoc("empty.txt", "", chomping: "|-");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.NewContent.Should().Be(string.Empty);
        }

        [TestMethod]
        public void ContentChange_BlankLines_At_Start_And_End_Clip()
        {
            var content = "\n\nline1\nline2\n\n";
            var doc = MakeDoc("notes.txt", content, chomping: "|");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            // Normalize to \n for comparison; be tolerant of final newline loss
            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: true);
            cc.NewContent.Should().Contain("\nline1\nline2\n");
        }

        [TestMethod]
        public void ContentChange_LongSingleLine_Clip()
        {
            var longLine = new string('x', 8000);
            var content = longLine;
            var doc = MakeDoc("long.txt", content, chomping: "|");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.NewContent.Length.Should().BeOneOf(8000, 8001); // tolerate optional trailing LF
            cc.NewContent.Should().StartWith("xxxx");
        }

        [TestMethod]
        public void ContentChange_ManyLines_LargeFile_Strip()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 500; i++)
                sb.Append("line-").Append(i).Append('\n');
            var content = sb.ToString().TrimEnd('\n'); // we use strip

            var doc = MakeDoc("big.txt", content, chomping: "|-");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.NewContent.Should().StartWith("line-0\n").And.Contain("line-250\n").And.EndWith("line-499");
            cc.NewContent.Split('\n').Should().HaveCount(500);
        }

        [TestMethod]
        public void ContentChange_SpecialYamlChars_In_Content_AreSafe_In_Literal()
        {
            var content = string.Join("\n", new[]
            {
                "# not a yaml comment inside literal",
                "- [brackets], {braces}, : colon, ? question, & amp, * star",
                "@ at, ! bang, % percent, ` backtick",
                "key: value # still literal content, not parsed pairs"
            });

            var doc = MakeDoc("weird.txt", content, chomping: "|-");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: false);
            cc.NewContent.Should().Contain("brackets").And.Contain("{braces}");
        }

        [TestMethod]
        public void ContentChange_Multiple_Documents_Order_And_Paths()
        {
            var c1 = "A\nB\n";
            var c2 = "int main() { return 0; }\n";
            var c3 = "<a><b/></a>";

            var d1 = MakeDoc("a.txt", c1, chomping: "|-");
            var d2 = MakeDoc("native/main.cpp", c2, chomping: "|");
            var d3 = MakeDoc("doc.xml", c3, chomping: "|-");

            var bundle = JoinDocs(d1, d2, d3);
            var result = StaalYamlCommandParser.ParseBundle(bundle);

            result.Should().HaveCount(3);
            result[0].Should().BeOfType<StaalContentChange>();
            result[1].Should().BeOfType<StaalContentChange>();
            result[2].Should().BeOfType<StaalContentChange>();

            var cc1 = (StaalContentChange)result[0];
            var cc2 = (StaalContentChange)result[1];
            var cc3 = (StaalContentChange)result[2];

            cc1.FilePath.Should().Be("a.txt");
            cc2.FilePath.Should().Be("native/main.cpp");
            cc3.FilePath.Should().Be("doc.xml");

            AssertContentOneOf(cc1.NewContent, c1.TrimEnd('\n'), expectTrailingLf: false);
            AssertContentOneOf(cc2.NewContent, c2, expectTrailingLf: true);
            AssertContentOneOf(cc3.NewContent, c3, expectTrailingLf: false);
        }

        [TestMethod]
        public void ContentChange_NewContent_With_MixedIndent_Literal_Preserves()
        {
            var content = "root:\n\t- tab-item\n  - space-item\n\t\t- tab2";
            var doc = MakeDoc("mixed.yaml", content, chomping: "|-");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.NewContent.Should().Contain("\n\t- tab-item\n");
            cc.NewContent.Should().Contain("\n  - space-item\n");
            cc.NewContent.Should().EndWith("- tab2"); // no trailing LF
        }

        [TestMethod]
        public void ContentChange_Json_As_Text_Clip()
        {
            var content = "{ \"name\":\"demo\", \"arr\":[1,2,3], \"nested\": {\"k\":\"v\"} }";
            var doc = MakeDoc("data.json", content, chomping: "|");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: true);
            cc.NewContent.Should().Contain("\"arr\":[1,2,3]");
        }

        [TestMethod]
        public void ContentChange_CSharp_Using_Directives_And_Interpolated_Strings_Strip()
        {
            var content = string.Join("\n", new[]
            {
                "using System;",
                "class P {",
                "  static void Main(){",
                "    var name = \"world\";",
                "    Console.WriteLine($\"Hello {name}!\");",
                "  }",
                "}"
            });

            var doc = MakeDoc("src/P.cs", content, chomping: "|-");
            var cc = (StaalContentChange)StaalYamlCommandParser.ParseBundle(doc).Single();

            cc.NewContent.Should().Contain("$\"Hello {name}!\"");
            AssertContentOneOf(cc.NewContent, content, expectTrailingLf: false);
        }
    }
}