namespace Solurum.StaalAi.CI
{
    using Skyline.DataMiner.Sdk.Shell;

    internal sealed class GitHelper
    {
        private readonly IShell shell;
        private readonly IFileSystem fs;
        private readonly ILogger logger;

        public GitHelper(ILogger logger, IFileSystem fs, IShell shell)
        {
            this.logger = logger;
            this.fs = fs;
            this.shell = shell;
        }

        public string GetBranch(string repoRoot)
        {
            return RunGit("git rev-parse --abbrev-ref HEAD", repoRoot).Trim();
        }

        public string GetSha(string repoRoot)
        {
            return RunGit("git rev-parse HEAD", repoRoot).Trim();
        }

        public bool EnsureCommitAndPush(string repoRoot, string message)
        {
            RunGit("git add -A", repoRoot);
            // commit can fail if nothing to commit; that's ok
            RunGit($"git commit -m \"{Escape(message)}\"", repoRoot, allowFail: true);
            var res = RunGit("git push", repoRoot, allowFail: true);
            return true;
        }

        public void FetchPull(string repoRoot, string branch)
        {
            RunGit("git fetch --all", repoRoot, allowFail: true);
            RunGit($"git pull origin {Escape(branch)}", repoRoot, allowFail: true);
        }

        private string Escape(string s) => s.Replace("\"", "\\\"");

        private string RunGit(string cmd, string repoRoot, bool allowFail = false)
        {
            if (!shell.RunCommand(cmd, out var output, out var errors, CancellationToken.None, repoRoot))
            {
                if (!allowFail)
                {
                    logger.LogWarning($"Git command failed: {cmd}. Errors: {errors}");
                }
            }
            if (!string.IsNullOrWhiteSpace(errors))
            {
                logger.LogDebug(errors);
            }
            return output ?? string.Empty;
        }
    }
}