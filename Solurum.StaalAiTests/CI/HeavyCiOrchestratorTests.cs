using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Skyline.DataMiner.CICD.FileSystem;
using Skyline.DataMiner.Sdk.Shell;
using Solurum.StaalAi.CI;
using System.Threading;

namespace Solurum.StaalAi.Tests.CI
{
    [TestClass]
    public class HeavyCiOrchestratorTests
    {
        private sealed class StepClock : IClock
        {
            private readonly DateTimeOffset baseTime;
            private int calls = 0;
            public StepClock(DateTimeOffset t) { baseTime = t; }
            public DateTimeOffset UtcNow
            {
                get
                {
                    calls++;
                    return calls <= 1 ? baseTime : baseTime.AddHours(2);
                }
            }
        }

        [TestMethod]
        public void StartOrContinue_SwitchesToHeavyMode_AfterOneHour_WritesPending()
        {
            // Arrange: real FS on a temp folder
            var fs = FileSystem.Instance;
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StaalAI_Hvy_" + Guid.NewGuid().ToString("N"));
            fs.Directory.CreateDirectory(root);

            // .heat/carbon.staal.xml
            var heat = fs.Path.Combine(root, ".heat");
            fs.Directory.CreateDirectory(heat);
            var xml = "<carbon><heavyci technology=\"github\"><github owner=\"o\" repo=\"r\" workflow=\"wf.yml\" timeoutMinutes=\"720\" pollSeconds=\"1\" /></heavyci></carbon>";
            fs.File.WriteAllText(fs.Path.Combine(heat, "carbon.staal.xml"), xml);

            // mocks
            var logger = new Mock<ILogger>();

            var shell = new Mock<IShell>();
            shell.Setup(s => s.RunCommand(It.Is<string>(c => c.Contains("rev-parse --abbrev-ref")), out It.Ref<string>.IsAny!, out It.Ref<string>.IsAny!, It.IsAny<CancellationToken>(), root))
                 .Returns((string cmd, out string output, out string errors, CancellationToken ct, string wd) =>
                 {
                     output = "test-branch\n"; errors = string.Empty; return true;
                 });
            shell.Setup(s => s.RunCommand(It.Is<string>(c => c.Contains("rev-parse HEAD")), out It.Ref<string>.IsAny!, out It.Ref<string>.IsAny!, It.IsAny<CancellationToken>(), root))
                 .Returns((string cmd, out string output, out string errors, CancellationToken ct, string wd) =>
                 {
                     output = "abc123\n"; errors = string.Empty; return true;
                 });
            shell.Setup(s => s.RunCommand(It.IsAny<string>(), out It.Ref<string>.IsAny!, out It.Ref<string>.IsAny!, It.IsAny<CancellationToken>(), root))
                 .Returns((string cmd, out string output, out string errors, CancellationToken ct, string wd) =>
                 {
                     output = ""; errors = ""; return true;
                 });

            var gh = new Mock<IGitHubCiProvider>();
            gh.Setup(g => g.DispatchWorkflow(It.IsAny<HeavyCiConfig>(), "test-branch", It.IsAny<string>(), out It.Ref<long>.IsAny!, out It.Ref<string>.IsAny!, out It.Ref<string>.IsAny!))
              .Callback((HeavyCiConfig c, string b, string r, out long id, out string url, out string msg) =>
              {
                  id = 42; url = "http://example/run/42"; msg = "ok";
              })
              .Returns(true);
            gh.Setup(g => g.GetRunStatus(It.IsAny<HeavyCiConfig>(), 42, out It.Ref<string>.IsAny!))
              .Callback((HeavyCiConfig c, long id, out string msg) => { msg = "in_progress"; })
              .Returns(WorkflowConclusion.InProgress);

            var clock = new StepClock(DateTimeOffset.UtcNow);

            var orchestrator = new HeavyCiOrchestrator(logger.Object, fs, shell.Object, clock, gh.Object);

            // Act
            var result = orchestrator.StartOrContinue(root);

            // Assert
            result.Mode.Should().Be(HeavyCiMode.Waiting);
            var pending = fs.Path.Combine(heat, "heavy_ci_pending.json");
            fs.File.Exists(pending).Should().BeTrue();

            // Optional debug info for flaky environments
            var notice = fs.Path.Combine(heat, "heavy_ci_notice.txt");
            var conv = fs.Path.Combine(heat, "conversation.json");
            TestContext.WriteLine($"Notice exists: {fs.File.Exists(notice)}");
            TestContext.WriteLine($"Conversation exists: {fs.File.Exists(conv)}");
        }

        public TestContext TestContext { get; set; } = null!;
    }
}