using System.Text;
using System.Text.RegularExpressions;

using Solurum.StaalAi.AICommands;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public static class StaalYamlCommandParser
{
    public const string Separator = "=====<<STAAL//YAML//SEPARATOR//2AF2E3DE-0F7B-4D0D-8E7C-5D1B8B1A4F0C>>=====";

    private static readonly IDeserializer Yaml =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    private static readonly ISerializer YamlSer =
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

    public static IReadOnlyList<IStaalCommand> ParseBundle(string bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle)) return Array.Empty<IStaalCommand>();

        // 0) FIRST: extract fenced YAML blocks (```yaml/```yml or ~~~yaml/~~~yml).
        // If we find any, we ONLY parse those blocks and ignore surrounding prose.
        var fenced = ExtractFencedYamlBlocks(bundle);
        List<string> docs;

        if (fenced.Count > 0)
        {
            docs = fenced;
        }
        else if (bundle.Contains(Separator, StringComparison.Ordinal))
        {
            // 1) Prefer the exact separator.
            // Preserve docs EXACTLY as-is (no trimming), because YAML '|' needs trailing LF.
            docs = bundle.Split(Separator, StringSplitOptions.None).ToList();
        }
        else
        {
            // 2) Accept bare "=====" lines as fallback (preserves exact text segments).
            docs = FallbackSplitOnEqualsLine(bundle);
        }

        // 3) Drop pure-whitespace docs; DO NOT trim ends (preserve block scalars).
        //    Also, strip any leading prose until the first YAML-looking key.
        docs = docs.Select(RemoveLeadingNonYaml)
                   .Where(s => !string.IsNullOrWhiteSpace(s))
                   .ToList();

        var result = new List<IStaalCommand>(docs.Count);
        bool singleDoc = docs.Count == 1;

        foreach (var raw in docs)
        {
            // Try single mapping doc
            if (TryParseMappingDoc(raw, out var oneOrMore))
            {
                result.AddRange(oneOrMore);
                continue;
            }

            // Try sequence-of-maps (flatten)
            try
            {
                var seq = Yaml.Deserialize<List<Dictionary<string, object>>>(raw);
                if (seq != null && seq.Count > 0)
                {
                    foreach (var map in seq)
                    {
                        if (map == null || map.Count == 0) continue;
                        if (!map.ContainsKey("type")) continue;

                        var yamlMap = YamlSer.Serialize(map);
                        var parsed = ParseMappingDocByType(yamlMap);
                        result.Add(parsed);
                    }
                    continue;
                }
            }
            catch (YamlException)
            {
                // ignore, try next rule
            }

            // If we reach here, this chunk could not be parsed into a command.
            if (singleDoc)
                throw new InvalidOperationException("YAML command missing 'type'.");
            // Otherwise, skip bad doc and continue (robustness for multi-doc bundles).
        }

        return result;
    }

    private static bool TryParseMappingDoc(string raw, out List<IStaalCommand> cmds)
    {
        cmds = new();
        try
        {
            var peek = Yaml.Deserialize<Dictionary<string, object>>(raw);
            if (peek == null || !peek.ContainsKey("type"))
                return false; // NOT a proper single mapping command; let caller try sequence fallback

            // This will throw InvalidOperationException if 'type' is missing/empty
            var cmd = ParseMappingDocByType(raw);
            cmds.Add(cmd);
            return true;
        }
        catch (YamlException)
        {
            // Not a mapping doc; caller will try sequence fallback
            return false;
        }
        // IMPORTANT: do NOT catch InvalidOperationException here.
        // Let it bubble to satisfy tests that expect a throw when a single mapping lacks 'type'.
    }

    private static IStaalCommand ParseMappingDocByType(string yaml)
    {
        var peek = Yaml.Deserialize<Dictionary<string, object>>(yaml);
        if (!peek.TryGetValue("type", out var tObj) || tObj is null)
            throw new InvalidOperationException("YAML command missing 'type'.");

        var type = tObj.ToString() ?? string.Empty;

        return type switch
        {
            "STAAL_CONTENT_REQUEST" => Yaml.Deserialize<StaalContentRequest>(yaml),
            "STAAL_CONTENT_DELETE" => Yaml.Deserialize<StaalContentDelete>(yaml),
            "STAAL_CONTENT_CHANGE" => Yaml.Deserialize<StaalContentChange>(yaml),
            "STAAL_GET_WORKING_DIRECTORY_STRUCTURE" => Yaml.Deserialize<StaalGetWorkingDirectoryStructure>(yaml),
            "STAAL_CI_LIGHT_REQUEST" => Yaml.Deserialize<StaalCiLightRequest>(yaml),
            "STAAL_CI_HEAVY_REQUEST" => Yaml.Deserialize<StaalCiHeavyRequest>(yaml),
            "STAAL_FINISH_OK" => Yaml.Deserialize<StaalFinishOk>(yaml),
            "STAAL_FINISH_NOK" => Yaml.Deserialize<StaalFinishNok>(yaml),
            "STAAL_STATUS" => Yaml.Deserialize<StaalStatus>(yaml),
            "STAAL_CONTINUE" => Yaml.Deserialize<StaalContinue>(yaml),
            _ => throw new NotSupportedException($"Unknown command type '{type}'.")
        };
    }

    // --- helpers ---

    /// <summary>
    /// Extracts all fenced YAML blocks (```yaml / ```yml, or ~~~yaml / ~~~yml).
    /// Preserves the inner content EXACTLY as-is (no trimming).
    /// If none are found, returns an empty list.
    /// </summary>
    private static List<string> ExtractFencedYamlBlocks(string s)
    {
        var docs = new List<string>();

        // Matches:
        //   - Opening fence of backticks or tildes, length >= 3, captured as group 1
        //   - Optional whitespace, then language "yaml" or "yml" (case-insensitive), then rest of the line
        //   - Newline
        //   - Lazy capture (group 2) of everything up to a closing fence with the exact same delimiter
        //   - Closing fence on its own line (optionally preceded by whitespace)
        //
        // Flags: Singleline (dot matches newline) + Multiline (^ and $ per line)
        var rx = new Regex(
            @"(?ims)^[ \t]*([`~]{3,})[ \t]*(?:ya?ml)\b[^\n]*\n(.*?)[ \t]*\n[ \t]*\1[ \t]*$",
            RegexOptions.CultureInvariant);

        var matches = rx.Matches(s);
        foreach (Match m in matches)
        {
            if (m.Success)
            {
                // Group 2 is the inner block. Preserve exactly as captured.
                var content = m.Groups[2].Value;
                docs.Add(content);
            }
        }

        return docs;
    }

    private static List<string> FallbackSplitOnEqualsLine(string s)
    {
        var docs = new List<string>();
        var regex = new Regex(@"^\s*={5,}\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        int lastIdx = 0;
        foreach (Match m in regex.Matches(s))
        {
            int start = m.Index;
            docs.Add(s.Substring(lastIdx, start - lastIdx)); // exact slice, no trimming
            lastIdx = m.Index + m.Length;

            // eat a single trailing newline after the separator, if present
            if (lastIdx < s.Length && s[lastIdx] == '\r') lastIdx++;
            if (lastIdx < s.Length && s[lastIdx] == '\n') lastIdx++;
        }
        if (lastIdx < s.Length)
            docs.Add(s.Substring(lastIdx));

        if (docs.Count == 0) docs.Add(s);
        return docs;
    }

    private static string RemoveLeadingNonYaml(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        using var sr = new StringReader(s);
        string? line;
        var sb = new StringBuilder();
        bool started = false;

        // Very permissive YAML key matcher: <non-#, non-space><...>:
        var keyLine = new Regex(@"^\s*[^#\s][^:]*\s*:\s*");

        while ((line = sr.ReadLine()) != null)
        {
            if (!started)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                if (!keyLine.IsMatch(line)) continue;   // still prose → skip
                started = true;                          // first YAML-looking key
            }
            sb.AppendLine(line);
        }

        // If we never found a key line, return original so single-doc logic can throw
        return started ? sb.ToString() : s;
    }
}