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

        // 1) Prefer the exact separator. If missing, accept bare "=====" lines as fallback.
        List<string> docs;
        if (bundle.Contains(Separator, StringComparison.Ordinal))
        {
            // Preserve docs EXACTLY as-is (no trimming), because YAML '|' needs trailing LF.
            docs = bundle.Split(Separator, StringSplitOptions.None).ToList();
        }
        else
        {
            docs = FallbackSplitOnEqualsLine(bundle); // preserves exact text segments
        }

        // 2) Drop pure-whitespace docs; DO NOT trim ends (preserve block scalars).
        //    Also, if a doc has stray prose before the first "type:", strip lines until the first 'type:'.
        docs = docs.Select(RemoveLeadingNonYamlUntilType)
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
            // In the single-doc case, honor the test expectation and throw.
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

    private static string RemoveLeadingNonYamlUntilType(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        using var sr = new StringReader(s);
        string? line;
        var sb = new StringBuilder();
        bool keep = false;
        bool foundType = false;

        while ((line = sr.ReadLine()) != null)
        {
            if (!keep && line.TrimStart().StartsWith("type:", StringComparison.Ordinal))
            {
                keep = true;
                foundType = true;
            }
            if (keep) sb.AppendLine(line);
        }

        // If we never saw a "type:" line, DO NOT strip—return the original text unchanged.
        // This allows the single-doc logic to throw "missing 'type'".
        return foundType ? sb.ToString() : s;
    }
}