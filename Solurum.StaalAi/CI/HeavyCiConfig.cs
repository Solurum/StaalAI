namespace Solurum.StaalAi.CI
{
    internal sealed class HeavyCiConfig
    {
        public string Technology { get; set; } = "github";
        public string Owner { get; set; } = string.Empty;
        public string Repo { get; set; } = string.Empty;
        public string Workflow { get; set; } = string.Empty;
        public int TimeoutMinutes { get; set; } = 720;
        public int PollSeconds { get; set; } = 30;

        public static bool TryLoad(IFileSystem fs, string repoRoot, out HeavyCiConfig cfg)
        {
            cfg = new HeavyCiConfig();
            var path = fs.Path.Combine(repoRoot, ".heat", "carbon.staal.xml");
            if (!fs.File.Exists(path)) return false;

            try
            {
                var xml = fs.File.ReadAllText(path);
                // super light parse to avoid extra deps
                string GetAttr(string src, string name)
                {
                    var t = name + "=\"";
                    var i = src.IndexOf(t, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) return string.Empty;
                    var j = src.IndexOf('"', i + t.Length);
                    if (j < 0) return string.Empty;
                    return src.Substring(i + t.Length, j - (i + t.Length));
                }

                // find <heavyci technology="github"> and nested <github ... />
                var heavyIdx = xml.IndexOf("<heavyci", StringComparison.OrdinalIgnoreCase);
                if (heavyIdx < 0) return false;
                var tech = GetAttr(xml, "technology");
                if (!string.IsNullOrWhiteSpace(tech)) cfg.Technology = tech;

                var ghIdx = xml.IndexOf("<github", heavyIdx, StringComparison.OrdinalIgnoreCase);
                if (ghIdx < 0) return false;

                cfg.Owner = GetAttr(xml, "owner");
                cfg.Repo = GetAttr(xml, "repo");
                cfg.Workflow = GetAttr(xml, "workflow");

                var tmo = GetAttr(xml, "timeoutMinutes");
                var pol = GetAttr(xml, "pollSeconds");
                if (int.TryParse(tmo, out int tm)) cfg.TimeoutMinutes = tm;
                if (int.TryParse(pol, out int ps)) cfg.PollSeconds = ps;
                return !string.IsNullOrWhiteSpace(cfg.Owner) && !string.IsNullOrWhiteSpace(cfg.Repo) && !string.IsNullOrWhiteSpace(cfg.Workflow);
            }
            catch
            {
                return false;
            }
        }
    }
}