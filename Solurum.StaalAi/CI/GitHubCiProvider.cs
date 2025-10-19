namespace Solurum.StaalAi.CI
{
    internal enum WorkflowConclusion
    {
        Success,
        Failure,
        Cancelled,
        TimedOut,
        InProgress,
        Unknown
    }

    internal interface IGitHubCiProvider
    {
        bool DispatchWorkflow(HeavyCiConfig cfg, string branch, string requestId, out long runId, out string runUrl, out string message);
        WorkflowConclusion GetRunStatus(HeavyCiConfig cfg, long runId, out string statusMessage);
        bool DownloadLogsZip(HeavyCiConfig cfg, long runId, string saveZipPath, out string statusMessage);
        bool DownloadArtifacts(HeavyCiConfig cfg, long runId, string saveDir, out string statusMessage);
    }

    internal sealed class GitHubCiProvider : IGitHubCiProvider
    {
        private readonly ILogger logger;
        private readonly string? token;
        private readonly HttpClient http;

        public GitHubCiProvider(ILogger logger)
        {
            this.logger = logger;
            token = Environment.GetEnvironmentVariable("STAAL_GH_TOKEN");
            http = new HttpClient();
            if (!string.IsNullOrWhiteSpace(token))
            {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Solurum.StaalAi/1.0");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            }
        }

        public bool DispatchWorkflow(HeavyCiConfig cfg, string branch, string requestId, out long runId, out string runUrl, out string message)
        {
            runId = 0;
            runUrl = string.Empty;
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                message = "Missing environment variable STAAL_GH_TOKEN";
                logger.LogWarning(message);
                return false;
            }

            try
            {
                // Dispatch workflow
                var url = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/actions/workflows/{cfg.Workflow}/dispatches";
                var body = new
                {
                    @ref = branch,
                    inputs = new Dictionary<string, string> {
                        { "request_id", requestId }
                    }
                };
                var json = System.Text.Json.JsonSerializer.Serialize(body);
                var resp = http.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json")).Result;
                if (!resp.IsSuccessStatusCode)
                {
                    message = $"Workflow dispatch failed: {(int)resp.StatusCode} {resp.ReasonPhrase}";
                    logger.LogWarning(message);
                    return false;
                }

                // Find latest run for branch/workflow
                var listUrl = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/actions/runs?branch={Uri.EscapeDataString(branch)}&event=workflow_dispatch&per_page=20";
                var list = http.GetStringAsync(listUrl).Result;
                using var doc = System.Text.Json.JsonDocument.Parse(list);
                foreach (var run in doc.RootElement.GetProperty("workflow_runs").EnumerateArray())
                {
                    var wfName = run.GetProperty("name").GetString();
                    if (!string.IsNullOrWhiteSpace(wfName) && wfName.Contains(Path.GetFileNameWithoutExtension(cfg.Workflow), StringComparison.OrdinalIgnoreCase))
                    {
                        runId = run.GetProperty("id").GetInt64();
                        runUrl = run.GetProperty("html_url").GetString() ?? string.Empty;
                        message = "Workflow dispatched.";
                        return true;
                    }
                }

                message = "Workflow dispatched but no run could be identified yet.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Dispatch error: {ex.Message}";
                logger.LogError(ex, "DispatchWorkflow failed");
                return false;
            }
        }

        public WorkflowConclusion GetRunStatus(HeavyCiConfig cfg, long runId, out string statusMessage)
        {
            statusMessage = string.Empty;
            if (runId == 0) return WorkflowConclusion.Unknown;
            try
            {
                var url = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/actions/runs/{runId}";
                var json = http.GetStringAsync(url).Result;
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var status = doc.RootElement.GetProperty("status").GetString() ?? "";
                var conclusion = doc.RootElement.GetProperty("conclusion").GetString() ?? "";

                if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    statusMessage = $"Status: {status}";
                    return WorkflowConclusion.InProgress;
                }

                switch (conclusion.ToLowerInvariant())
                {
                    case "success": return WorkflowConclusion.Success;
                    case "failure": return WorkflowConclusion.Failure;
                    case "cancelled": return WorkflowConclusion.Cancelled;
                    case "timed_out": return WorkflowConclusion.TimedOut;
                    default: return WorkflowConclusion.Unknown;
                }
            }
            catch (Exception ex)
            {
                statusMessage = $"Status error: {ex.Message}";
                logger.LogError(ex, "GetRunStatus failed");
                return WorkflowConclusion.Unknown;
            }
        }

        public bool DownloadLogsZip(HeavyCiConfig cfg, long runId, string saveZipPath, out string statusMessage)
        {
            statusMessage = string.Empty;
            try
            {
                var url = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/actions/runs/{runId}/logs";
                var resp = http.GetAsync(url).Result;
                if (!resp.IsSuccessStatusCode)
                {
                    statusMessage = $"Failed to download logs: {(int)resp.StatusCode}";
                    return false;
                }
                using var fs = File.Create(saveZipPath);
                resp.Content.CopyToAsync(fs).Wait();
                statusMessage = $"Logs saved to {saveZipPath}";
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = $"Logs error: {ex.Message}";
                logger.LogError(ex, "DownloadLogsZip failed");
                return false;
            }
        }

        public bool DownloadArtifacts(HeavyCiConfig cfg, long runId, string saveDir, out string statusMessage)
        {
            statusMessage = string.Empty;
            try
            {
                var url = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/actions/runs/{runId}/artifacts";
                var json = http.GetStringAsync(url).Result;
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                foreach (var art in doc.RootElement.GetProperty("artifacts").EnumerateArray())
                {
                    var id = art.GetProperty("id").GetInt64();
                    var name = art.GetProperty("name").GetString() ?? id.ToString();
                    var dlUrl = $"https://api.github.com/repos/{cfg.Owner}/{cfg.Repo}/actions/artifacts/{id}/zip";
                    var resp = http.GetAsync(dlUrl).Result;
                    if (!resp.IsSuccessStatusCode) continue;
                    var zipPath = Path.Combine(saveDir, $"{name}.zip");
                    Directory.CreateDirectory(saveDir);
                    using var fs = File.Create(zipPath);
                    resp.Content.CopyToAsync(fs).Wait();
                }
                statusMessage = "Artifacts downloaded (if available).";
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = $"Artifacts error: {ex.Message}";
                logger.LogError(ex, "DownloadArtifacts failed");
                return false;
            }
        }
    }
}