namespace Solurum.StaalAi.CI
{
    internal enum HeavyCiMode
    {
        Active,
        Waiting,
        Completed
    }

    internal sealed class HeavyCiResult
    {
        public HeavyCiMode Mode { get; set; } = HeavyCiMode.Active;
        public WorkflowConclusion Status { get; set; } = WorkflowConclusion.Unknown;
    }

    internal sealed class ResumeOptions
    {
        public bool NoFetch { get; set; }
        public int? TimeoutOverrideMinutes { get; set; }
        public bool Verbose { get; set; }
    }

    internal sealed class ResumeResult
    {
        public int ExitCode { get; set; }
        public string ConsoleSummary { get; set; } = string.Empty;
    }

    internal sealed class HeavyCiOrchestrator
    {
        private readonly ILogger logger;
        private readonly IFileSystem fs;
        private readonly Skyline.DataMiner.Sdk.Shell.IShell shell;
        private readonly IClock clock;
        private readonly IGitHubCiProvider gitHub;

        public HeavyCiOrchestrator(ILogger logger, IFileSystem fs, Skyline.DataMiner.Sdk.Shell.IShell shell, IClock clock, IGitHubCiProvider gitHub)
        {
            this.logger = logger;
            this.fs = fs;
            this.shell = shell;
            this.clock = clock;
            this.gitHub = gitHub;
        }

        public HeavyCiResult StartOrContinue(string repoRoot)
        {
            if (!HeavyCiConfig.TryLoad(fs, repoRoot, out var cfg))
            {
                logger.LogWarning("Heavy CI config invalid or missing.");
                return new HeavyCiResult { Mode = HeavyCiMode.Active, Status = WorkflowConclusion.Unknown };
            }

            var git = new GitHelper(logger, fs, shell);

            // Prepare branch and commit
            var branch = Safe(git.GetBranch(repoRoot));
            var sha = Safe(git.GetSha(repoRoot));
            git.EnsureCommitAndPush(repoRoot, "StaalAI: Heavy CI kick-off [skip ci]");

            // Persist CI context
            var heat = fs.Path.Combine(repoRoot, ".heat");
            fs.Directory.CreateDirectory(heat);
            var ctxPath = fs.Path.Combine(heat, "ci_context.json");
            var requestId = Guid.NewGuid().ToString("N");
            var ctx = new
            {
                timestamp = clock.UtcNow.ToString("o"),
                branch,
                commit = sha,
                requestType = "STAAL_CI_HEAVY_REQUEST",
                mode = "active"
            };
            fs.File.WriteAllText(ctxPath, System.Text.Json.JsonSerializer.Serialize(ctx, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Trigger external pipeline
            if (!gitHub.DispatchWorkflow(cfg, branch, requestId, out long runId, out string runUrl, out string msg))
            {
                logger.LogWarning($"Dispatch failed or token missing: {msg}");
                // Transition to waiting so user can resume later if dispatch actually happened.
                WritePending(repoRoot, runId, runUrl, branch, requestId, "dispatch_failed");
                return new HeavyCiResult { Mode = HeavyCiMode.Waiting, Status = WorkflowConclusion.Unknown };
            }

            // Poll up to 60 minutes (synchronous window)
            var start = clock.UtcNow;
            var pollSec = Math.Max(5, cfg.PollSeconds);
            var hardLimit = start.AddHours(1);
            while (clock.UtcNow < hardLimit)
            {
                var st = gitHub.GetRunStatus(cfg, runId, out var statusMsg);
                if (st != WorkflowConclusion.InProgress && st != WorkflowConclusion.Unknown)
                {
                    // Completed within window
                    WriteSummary(repoRoot, st, runUrl, start, clock.UtcNow, statusMsg);
                    return new HeavyCiResult { Mode = HeavyCiMode.Completed, Status = st };
                }

                Thread.Sleep(TimeSpan.FromSeconds(pollSec));
            }

            // Switch to Heavy mode
            WritePending(repoRoot, runId, runUrl, branch, requestId, "waiting");
            WriteNotice(repoRoot);
            WriteConversationStub(repoRoot);
            return new HeavyCiResult { Mode = HeavyCiMode.Waiting, Status = WorkflowConclusion.InProgress };
        }

        public ResumeResult Resume(string repoRoot, ResumeOptions options)
        {
            var heat = fs.Path.Combine(repoRoot, ".heat");
            var pendingPath = fs.Path.Combine(heat, "heavy_ci_pending.json");
            if (!fs.File.Exists(pendingPath))
            {
                return new ResumeResult { ExitCode = (int)ExitCodes.Fail, ConsoleSummary = "No paused Heavy CI found." };
            }

            var json = fs.File.ReadAllText(pendingPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var runId = doc.RootElement.GetProperty("workflowId").GetInt64();
            var branch = doc.RootElement.GetProperty("branch").GetString() ?? "";
            var runUrl = doc.RootElement.GetProperty("runUrl").GetString() ?? "";

            if (!HeavyCiConfig.TryLoad(fs, repoRoot, out var cfg))
            {
                return new ResumeResult { ExitCode = (int)ExitCodes.Fail, ConsoleSummary = "Invalid Heavy CI config." };
            }

            var git = new GitHelper(logger, fs, shell);

            if (!options.NoFetch)
            {
                git.FetchPull(repoRoot, branch);
            }

            var timeoutMin = options.TimeoutOverrideMinutes ?? 30;
            var start = clock.UtcNow;
            var end = start.AddMinutes(timeoutMin);

            WorkflowConclusion final = WorkflowConclusion.InProgress;
            string lastStatus = string.Empty;

            while (clock.UtcNow < end)
            {
                final = gitHub.GetRunStatus(cfg, runId, out lastStatus);
                if (final != WorkflowConclusion.InProgress && final != WorkflowConclusion.Unknown)
                {
                    break;
                }
                Thread.Sleep(TimeSpan.FromSeconds(Math.Max(10, cfg.PollSeconds)));
            }

            if (final == WorkflowConclusion.InProgress || final == WorkflowConclusion.Unknown)
            {
                return new ResumeResult { ExitCode = (int)ExitCodes.Ok, ConsoleSummary = $"Still running: {lastStatus}" };
            }

            // Completed: collect logs and artifacts
            var logsZip = fs.Path.Combine(heat, "ci_logs.zip");
            var artifactsDir = fs.Path.Combine(heat, "artifacts");
            Directory.CreateDirectory(artifactsDir);

            gitHub.DownloadLogsZip(cfg, runId, logsZip, out var logMsg);
            gitHub.DownloadArtifacts(cfg, runId, artifactsDir, out var artMsg);

            WriteSummary(repoRoot, final, runUrl, start, clock.UtcNow, lastStatus);

            // Remove pending
            try { fs.File.Delete(pendingPath); } catch { /* ignore */ }

            var summary = fs.Path.Combine(heat, "ci_summary.md");
            var exit = final == WorkflowConclusion.Success ? (int)ExitCodes.Ok : (int)ExitCodes.Fail;
            return new ResumeResult { ExitCode = exit, ConsoleSummary = fs.File.Exists(summary) ? fs.File.ReadAllText(summary) : $"Run finished with {final}.\n{logMsg}\n{artMsg}" };
        }

        private void WritePending(string repoRoot, long runId, string runUrl, string branch, string requestId, string mode)
        {
            var heat = fs.Path.Combine(repoRoot, ".heat");
            fs.Directory.CreateDirectory(heat);
            var pending = new
            {
                mode,
                workflowId = runId,
                runUrl,
                branch,
                requestId
            };
            fs.File.WriteAllText(fs.Path.Combine(heat, "heavy_ci_pending.json"), System.Text.Json.JsonSerializer.Serialize(pending, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private void WriteNotice(string repoRoot)
        {
            var heat = fs.Path.Combine(repoRoot, ".heat");
            fs.File.WriteAllText(fs.Path.Combine(heat, "heavy_ci_notice.txt"), "Heavy CI paused after 1 hour. Run 'StaalAI continue' later to resume and collect results.");
        }

        private void WriteConversationStub(string repoRoot)
        {
            var heat = fs.Path.Combine(repoRoot, ".heat");
            fs.File.WriteAllText(fs.Path.Combine(heat, "conversation.json"), "{\"note\":\"Conversation context placeholder saved by StaalAI.\"}");
        }

        private void WriteSummary(string repoRoot, WorkflowConclusion conclusion, string runUrl, DateTimeOffset start, DateTimeOffset end, string extra)
        {
            var heat = fs.Path.Combine(repoRoot, ".heat");
            var md = new System.Text.StringBuilder();
            md.AppendLine("# Heavy CI Summary");
            md.AppendLine($"- Conclusion: {conclusion}");
            md.AppendLine($"- Run URL: {runUrl}");
            md.AppendLine($"- Duration: {(end - start).TotalMinutes:F1} min");
            if (!string.IsNullOrWhiteSpace(extra))
            {
                md.AppendLine($"- Details: {extra}");
            }
            fs.File.WriteAllText(fs.Path.Combine(heat, "ci_summary.md"), md.ToString());
        }

        private static string Safe(string s) => s ?? string.Empty;
    }
}