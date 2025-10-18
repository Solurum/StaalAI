namespace Solurum.StaalAi.AICommands
{
    using System.Text.RegularExpressions;

    using YamlDotNet.Core;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    /// <summary>
    /// Thin factory: normalize once, split on strict separator, and deserialize.
    /// </summary>
    public static class StaalYamlCommandParser
    {
        public const string Separator =
            "=====<<" + "STAAL//YAML//SEPARATOR//" + "2AF2E3DE-0F7B-4D0D-8E7C-5D1B8B1A4F0C" + ">>" + "=====";

        private static readonly IDeserializer Yaml =
            new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

        /// <summary>
        /// Parse using normalization; returns empty for empty/whitespace input.
        /// Throws InvalidOperationException if canonical contains no valid commands.
        /// </summary>
        public static IReadOnlyList<IStaalCommand> ParseBundle(string bundle)
        {
            if (string.IsNullOrWhiteSpace(bundle)) return Array.Empty<IStaalCommand>(); // keep old behavior

            var normalized = StaalYamlNormalizer.Normalize(bundle, out _);
            return DeserializeCanonical(normalized);
        }

        /// <summary>
        /// Overload that returns the canonical YAML and whether normalization changed anything.
        /// </summary>
        public static IReadOnlyList<IStaalCommand> ParseBundle(string bundle, out string canonicalYaml, out bool changed)
        {
            if (string.IsNullOrWhiteSpace(bundle))
            {
                canonicalYaml = string.Empty;
                changed = false;
                return Array.Empty<IStaalCommand>();
            }

            canonicalYaml = StaalYamlNormalizer.Normalize(bundle, out changed);
            return DeserializeCanonical(canonicalYaml);
        }

        // ---------------- internals ----------------

        private static IReadOnlyList<IStaalCommand> DeserializeCanonical(string canonical)
        {
            if (string.IsNullOrEmpty(canonical))
                throw new InvalidOperationException("No valid STAAL commands were found. Each YAML document must include a 'type' key.");

            // IMPORTANT: Do NOT TrimEntries here — it removes the trailing blank line
            // that represents the final LF for '|' block scalars.
            var parts = canonical.Split(Separator, StringSplitOptions.None)
                                 .Where(p => !string.IsNullOrEmpty(p)) // manual remove empty, without trimming
                                 .ToList();

            var result = new List<IStaalCommand>(parts.Count);

            foreach (var doc in parts)
            {
                var cmd = ParseMappingDocByType(doc);
                if (cmd != null) result.Add(cmd);
            }

            if (result.Count == 0)
                throw new InvalidOperationException("No valid STAAL commands were found. Each YAML document must include a 'type' key.");

            return result;
        }

        private static IStaalCommand ParseMappingDocByType(string yaml)
        {
            Dictionary<string, object> peek;
            try
            {
                peek = Yaml.Deserialize<Dictionary<string, object>>(yaml);
            }
            catch (YamlException ex)
            {
                throw new InvalidOperationException($"Invalid YAML: {ex.Message}");
            }

            if (!peek.TryGetValue("type", out var tObj) || tObj is null)
                throw new InvalidOperationException("YAML command missing 'type'.");

            var type = tObj.ToString() ?? string.Empty;

            // Deserialize into the correct POCO
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
    }
}
