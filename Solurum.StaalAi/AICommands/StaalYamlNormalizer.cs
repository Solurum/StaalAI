namespace Solurum.StaalAi.AICommands
{
    using System.Text;
    using System.Text.RegularExpressions;

    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    /// <summary>
    /// Best-effort normalizer that turns messy LLM output into canonical YAML the parser expects.
    /// Output:
    /// - One or more single-mapping YAML documents separated by StaalYamlCommandParser.Separator.
    /// - Keys and shapes aligned to the target POCOs (e.g., type, statusMsg, filePath/newContent, etc.).
    /// - No prose, no fences, no sequences; just clean per-command docs.
    /// Returns 'changed' if any normalization was applied.
    /// </summary>
    public static class StaalYamlNormalizer
    {
        private static readonly IDeserializer Yaml =
            new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

        private static readonly ISerializer YamlSer =
            new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();

        // Recognizers
        private static readonly Regex FenceRx = new(
            @"(?ims)^[ \t]*([`~]{3,})[ \t]*(?:ya?ml)\b[^\n]*\n(.*?)[ \t]*\n[ \t]*\1[ \t]*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex FirstYamlKey = new(
            @"(?m)^\s*(-\s*)?(type|command)\s*:",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex TopLevelSeqItemStart = new(
            @"^(?<indent>[ \t]*)-\s*(?<rest>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex KeyColonAfterDash = new(
            @"^(type|command)\s*:\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BlockScalarKey = new(
            @"^(?<indent>[ \t]*)(?<key>[^:\r\n#]+?):\s*\|(?<chomp>\+|-)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FirstKeyLine = new(
            @"(?m)^(?<indent>[ \t]*)(?<k>type|command)\s*:(?<rest>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Detect newContent/newText chomp from raw segment text
        private static readonly Regex NewContentChompRx = new(
            @"(?mi)^\s*(newContent|newText)\s*:\s*\|(?<chomp>[+-])?\s*$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Normalize a bundle into canonical multi-doc YAML (separator-delimited).
        /// Returns true in 'changed' if any normalization was applied.
        /// </summary>
        public static string Normalize(string bundle, out bool changed)
        {
            changed = false;
            if (string.IsNullOrWhiteSpace(bundle)) return string.Empty;

            // 1) Extract source docs:
            //    - Fenced YAML blocks take precedence.
            //    - Else split on the strict STAAL separator if present.
            //    - Else split on YAML '---' document separators (top-level).
            //    - Else split on fallback "=====" lines.
            //    - Else use the entire bundle.
            List<string> docs;
            var fenced = ExtractFencedYamlBlocks(bundle);
            if (fenced.Count > 0)
            {
                docs = fenced;
            }
            else if (bundle.Contains(StaalYamlCommandParser.Separator, StringComparison.Ordinal))
            {
                docs = bundle.Split(StaalYamlCommandParser.Separator, StringSplitOptions.None).ToList();
            }
            else
            {
                // Start from a single chunk or fallback-equals-split, then expand by YAML '---'
                var prelim = FallbackSplitOnEqualsLine(bundle);
                docs = new List<string>();
                foreach (var part in prelim)
                {
                    var splits = SplitOnYamlDocSeparators(part);
                    if (splits.Count > 1) changed = true;
                    docs.AddRange(splits);
                }
            }

            // 2) Strip leading prose; keep exact content otherwise (important for block scalars)
            for (int i = 0; i < docs.Count; i++)
            {
                var original = docs[i];
                var stripped = RemoveLeadingNonYaml(original);
                if (!ReferenceEquals(original, stripped) && !string.Equals(original, stripped))
                    changed = true;
                docs[i] = stripped;
            }

            // 3) For each segment, produce one or more canonical command docs
            var canonicalDocs = new List<string>();

            foreach (var segment in docs.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                // Prefer single-mapping path when a top-level 'requests' key exists, to allow nested splitting later.
                bool hasTopLevelRequests = TryDetectTopLevelRequests(segment);

                // Sequence-of-commands path (only when no wrapper 'requests' key is present)
                if (!hasTopLevelRequests && TrySplitTopLevelSequenceItems(segment, out var items) && items.Count > 0)
                {
                    changed = true; // sequence -> multiple single docs
                    foreach (var item in items)
                    {
                        var mapping = NormalizeSingleSeqItemToMapping(item) ?? item;
                        var fixedIndent = FixInconsistentTopLevelIndent(mapping);
                        if (!ReferenceEquals(mapping, fixedIndent) && !string.Equals(mapping, fixedIndent))
                            changed = true;

                        var ok = TryDeserializeMapPermissive(fixedIndent, out var m);
                        var map = ok ? m : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                        // Detect chomp from RAW item text and carry it along
                        if (TryDetectNewContentKeep(item, out var keepFlag))
                        {
                            map["__keepTrailingLf"] = keepFlag;
                            changed = true;
                        }

                        var normalized = NormalizeMapShape(map, ref changed);
                        var yaml = BuildCanonicalYaml(normalized, ref changed);
                        if (!string.IsNullOrWhiteSpace(yaml))
                            canonicalDocs.Add(yaml);
                    }
                    continue;
                }

                // Single mapping path
                var withType = EnsureTypeKeyTextually(segment);
                if (!ReferenceEquals(withType, segment) && !string.Equals(withType, segment))
                    changed = true;

                var fixedDoc = FixInconsistentTopLevelIndent(withType);
                if (!ReferenceEquals(withType, fixedDoc) && !string.Equals(withType, fixedDoc))
                    changed = true;

                var okSingle = TryDeserializeMapPermissive(fixedDoc, out var peek);
                var singleMap = okSingle ? peek : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                // Detect chomp from RAW segment text and carry it along
                if (TryDetectNewContentKeep(segment, out var keepFlag2))
                {
                    singleMap["__keepTrailingLf"] = keepFlag2;
                    changed = true;
                }

                // Support nested 'requests' sequence under a single command.
                var nestedRequests = ExtractNestedRequests(singleMap, out var parentWithoutRequests, ref changed);

                // Emit parent command (if it has/normalizes to a valid 'type')
                var normalizedMap = NormalizeMapShape(parentWithoutRequests, ref changed);
                var yamlDoc = BuildCanonicalYaml(normalizedMap, ref changed);
                if (!string.IsNullOrWhiteSpace(yamlDoc))
                    canonicalDocs.Add(yamlDoc);

                // Emit each nested request as its own doc
                foreach (var reqMap in nestedRequests)
                {
                    var normalizedReq = NormalizeMapShape(reqMap, ref changed);
                    var yamlReq = BuildCanonicalYaml(normalizedReq, ref changed);
                    if (!string.IsNullOrWhiteSpace(yamlReq))
                        canonicalDocs.Add(yamlReq);
                }
            }

            // 4) Join canonical docs with strict separator
            return string.Join(StaalYamlCommandParser.Separator, canonicalDocs);
        }

        // ---------------- Core normalizations ----------------

        private static Dictionary<string, object> NormalizeMapShape(Dictionary<string, object> src, ref bool changed)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in src) map[kv.Key] = kv.Value;

            // alias command -> type
            if (!map.ContainsKey("type") && map.TryGetValue("command", out var aliasVal))
            {
                map["type"] = aliasVal;
                changed = true;
            }

            if (map.TryGetValue("type", out var tObj))
            {
                var t = tObj?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    map["type"] = t; // ensure string
                }
            }

            // Flatten args: { args: { k:v } } -> top-level k:v (do not overwrite explicit top-level)
            if (map.TryGetValue("args", out var argsObj) && TryToStringObjectDict(argsObj, out var argsDict) && argsDict.Count > 0)
            {
                foreach (var kv in argsDict)
                {
                    if (!map.ContainsKey(kv.Key))
                    {
                        map[kv.Key] = kv.Value!;
                        changed = true;
                    }
                }
                map.Remove("args");
                changed = true;
            }

            if (!map.TryGetValue("type", out var typeObj) || typeObj is null)
                return map;

            var type = typeObj.ToString();

            switch (type)
            {
                case "STAAL_STATUS":
                    {
                        // Map content/message/status -> statusMsg
                        if (!map.ContainsKey("statusMsg"))
                        {
                            if (TryGetString(map, "content", out var cs))
                            {
                                map["statusMsg"] = cs; changed = true;
                            }
                            else if (TryGetString(map, "message", out var ms))
                            {
                                map["statusMsg"] = ms; changed = true;
                            }
                            else if (TryGetString(map, "status", out var ss))
                            {
                                map["statusMsg"] = ss; changed = true;
                            }
                        }
                        DropKeys(map, ref changed, "content", "message", "status");
                        KeepOnly(map, ref changed, "type", "statusMsg");
                        break;
                    }

                case "STAAL_CONTENT_REQUEST":
                    {
                        var hasTarget = map.ContainsKey("filePath") || map.ContainsKey("filePaths") || map.ContainsKey("files");

                        if (!hasTarget && TryGetString(map, "content", out var literal) && !string.IsNullOrWhiteSpace(literal))
                        {
                            if (TryParseEmbeddedYaml(literal, out var inner))
                            {
                                foreach (var k in new[] { "filePath", "filePaths", "files" })
                                {
                                    if (inner.TryGetValue(k, out var v) && v != null && !map.ContainsKey(k))
                                    {
                                        map[k] = v; changed = true;
                                    }
                                }
                            }
                            else
                            {
                                var fp = TryExtractFilePathFromText(literal);
                                if (!string.IsNullOrWhiteSpace(fp) && !map.ContainsKey("filePath"))
                                {
                                    map["filePath"] = fp; changed = true;
                                }
                            }
                        }

                        DropKeys(map, ref changed, "content");
                        KeepOnly(map, ref changed, "type", "filePath", "filePaths", "files");
                        break;
                    }

                case "STAAL_CONTENT_CHANGE":
                    {
                        // Accept alias for newContent: content or newText/newtext
                        if (!map.ContainsKey("newContent"))
                        {
                            if (map.TryGetValue("content", out var c) && c != null)
                            {
                                map["newContent"] = c.ToString() ?? string.Empty; changed = true;
                            }
                            else if (map.TryGetValue("newText", out var nt) && nt != null)
                            {
                                map["newContent"] = nt.ToString() ?? string.Empty; changed = true;
                            }
                        }

                        // Keep only known keys (internal flag is okay to pass through)
                        KeepOnly(map, ref changed, "type", "filePath", "newContent", "__keepTrailingLf");
                        break;
                    }

                default:
                    // Unknown types: keep as-is but ensure 'type' is string.
                    break;
            }

            return map;
        }

        private static string BuildCanonicalYaml(Dictionary<string, object> normalized, ref bool changed)
        {
            if (!normalized.TryGetValue("type", out var tObj) || tObj is null)
                return string.Empty;

            var type = tObj.ToString();

            if (type == "STAAL_CONTENT_CHANGE")
            {
                var filePath = normalized.TryGetValue("filePath", out var fpVal) ? fpVal?.ToString() ?? string.Empty : string.Empty;
                var newContent = normalized.TryGetValue("newContent", out var ncVal) ? (ncVal?.ToString() ?? string.Empty) : string.Empty;

                // prefer source chomp if detected; else infer from value
                bool keep;
                if (normalized.TryGetValue("__keepTrailingLf", out var keepObj) && keepObj is bool b)
                    keep = b;
                else
                    keep = newContent.EndsWith("\n", StringComparison.Ordinal);

                var chomp = keep ? "|" : "|-";

                var sb = new StringBuilder(newContent.Length + 128);
                sb.AppendLine("type: STAAL_CONTENT_CHANGE");
                sb.Append("filePath: ").AppendLine(filePath ?? string.Empty);
                sb.Append("newContent: ").AppendLine(chomp);

                // Emit content exactly:
                // - indent non-empty lines by two spaces
                // - DO NOT indent empty lines (prevents "extra spaces in first line" with leading blanks)
                var lines = SplitPreserveAllLines(newContent);
                foreach (var line in lines)
                {
                    if (line.Length == 0)
                        sb.AppendLine();
                    else
                        sb.Append("  ").AppendLine(line);
                }

                // IMPORTANT: do NOT TrimEnd here — keep final newline semantics for '|'
                return sb.ToString();
            }

            // default path: use serializer for other commands
            var canon = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = type
            };

            switch (type)
            {
                case "STAAL_STATUS":
                    if (normalized.TryGetValue("statusMsg", out var sm) && sm is string s && !string.IsNullOrWhiteSpace(s))
                        canon["statusMsg"] = s;
                    break;

                case "STAAL_CONTENT_REQUEST":
                    if (normalized.TryGetValue("filePath", out var fp)) canon["filePath"] = fp;
                    if (normalized.TryGetValue("filePaths", out var fps)) canon["filePaths"] = fps;
                    if (normalized.TryGetValue("files", out var files)) canon["files"] = files;
                    break;

                default:
                    foreach (var kv in normalized)
                    {
                        if (kv.Key.Equals("type", StringComparison.OrdinalIgnoreCase)) continue;
                        if (kv.Key.Equals("__keepTrailingLf", StringComparison.OrdinalIgnoreCase)) continue; // internal flag
                        canon[kv.Key] = kv.Value!;
                    }
                    break;
            }

            return YamlSer.Serialize(canon).TrimEnd('\r', '\n');
        }

        // ---------------- Deserialization helpers ----------------

        private static bool TryDeserializeMapPermissive(string raw, out Dictionary<string, object> map)
        {
            map = null!;
            try
            {
                map = Yaml.Deserialize<Dictionary<string, object>>(raw);
                if (map != null) return true;
            }
            catch { /* fall through */ }

            var normalized = FixInconsistentTopLevelIndent(raw);
            try
            {
                map = Yaml.Deserialize<Dictionary<string, object>>(normalized);
                if (map != null) return true;
            }
            catch { /* fall through */ }

            var wrapped = "root:\n" + IndentAllLines(normalized, 2);
            try
            {
                var root = Yaml.Deserialize<Dictionary<string, object>>(wrapped);
                if (root != null && root.TryGetValue("root", out var obj))
                {
                    if (obj is Dictionary<object, object> ood)
                    {
                        map = ood.ToDictionary(k => k.Key?.ToString() ?? string.Empty, v => v.Value)!;
                        return true;
                    }
                    if (obj is Dictionary<string, object> dso)
                    {
                        map = dso;
                        return true;
                    }
                }
            }
            catch { /* ignore */ }

            return false;
        }

        // ---------------- Low-level text transforms ----------------

        private static List<string> ExtractFencedYamlBlocks(string s)
        {
            var docs = new List<string>();
            var matches = FenceRx.Matches(s);
            foreach (Match m in matches)
                if (m.Success) docs.Add(m.Groups[2].Value);
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
                docs.Add(s.Substring(lastIdx, start - lastIdx));
                lastIdx = m.Index + m.Length;

                if (lastIdx < s.Length && s[lastIdx] == '\r') lastIdx++;
                if (lastIdx < s.Length && s[lastIdx] == '\n') lastIdx++;
            }
            if (lastIdx < s.Length)
                docs.Add(s.Substring(lastIdx));

            if (docs.Count == 0) docs.Add(s);
            return docs;
        }

        private static List<string> SplitOnYamlDocSeparators(string s)
        {
            var docs = new List<string>();
            if (string.IsNullOrWhiteSpace(s))
            {
                docs.Add(s);
                return docs;
            }

            var lines = SplitPreserveAllLines(s);
            var current = new StringBuilder();
            bool inScalar = false;
            int scalarKeyIndent = -1;

            static int IndentOf(string line)
            {
                int i = 0;
                while (i < line.Length && line[i] == ' ') i++;
                return i;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (inScalar)
                {
                    int ind = IndentOf(line);
                    if (ind > scalarKeyIndent)
                    {
                        current.AppendLine(line);
                        continue;
                    }
                    else
                    {
                        inScalar = false;
                        // fall through to process this line again for '---' or keys
                    }
                }

                // Enter block scalar if key: |
                var sk = BlockScalarKey.Match(line);
                if (sk.Success)
                {
                    scalarKeyIndent = sk.Groups["indent"].Value.Length;
                    inScalar = true;
                    current.AppendLine(line);
                    continue;
                }

                // YAML document separator (top-level or with only spaces)
                if (Regex.IsMatch(line, @"^\s*---\s*$"))
                {
                    // Finish previous doc (avoid adding empty leading doc)
                    var chunk = current.ToString();
                    if (!string.IsNullOrEmpty(chunk))
                        docs.Add(chunk.TrimEnd('\r', '\n'));
                    current.Clear();
                    continue;
                }

                current.AppendLine(line);
            }

            if (current.Length > 0)
                docs.Add(current.ToString().TrimEnd('\r', '\n'));

            if (docs.Count == 0) docs.Add(s);
            return docs;
        }

        private static string RemoveLeadingNonYaml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.TrimStart('\uFEFF');
            var m = FirstYamlKey.Match(s);
            return m.Success ? s[m.Index..] : s;
        }

        private static bool TrySplitTopLevelSequenceItems(string s, out List<string> items)
        {
            items = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return false;

            var lines = SplitPreserveAllLines(s);
            var current = new StringBuilder();
            int? currentItemIndent = null;

            bool inBlockScalar = false;
            int blockScalarBaseIndent = 0;

            static int IndentOf(string line)
            {
                int i = 0;
                while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
                return i;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (inBlockScalar)
                {
                    if (line.Length == 0) { current.AppendLine(line); continue; }
                    int ind = IndentOf(line);
                    if (ind > blockScalarBaseIndent) { current.AppendLine(line); continue; }
                    inBlockScalar = false;
                }

                var bs = BlockScalarKey.Match(line);
                if (bs.Success)
                {
                    blockScalarBaseIndent = bs.Groups["indent"].Value.Length;
                    inBlockScalar = true;
                    current.AppendLine(line);
                    continue;
                }

                var m = TopLevelSeqItemStart.Match(line);
                if (m.Success)
                {
                    var indent = m.Groups["indent"].Value.Length;
                    var rest = m.Groups["rest"].Value;

                    if (KeyColonAfterDash.IsMatch(rest))
                    {
                        if (!currentItemIndent.HasValue) currentItemIndent = indent;
                        else if (indent <= currentItemIndent.Value)
                        {
                            items.Add(current.ToString().TrimEnd('\r', '\n'));
                            current.Clear();
                            currentItemIndent = indent;
                        }
                        current.AppendLine(line);
                        continue;
                    }
                }

                if (currentItemIndent.HasValue) current.AppendLine(line);
            }

            if (currentItemIndent.HasValue && current.Length > 0)
                items.Add(current.ToString().TrimEnd('\r', '\n'));

            return items.Count > 0;
        }

        private static string? NormalizeSingleSeqItemToMapping(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var lines = SplitPreserveAllLines(s);
            int firstIdx = -1;
            int dashIndent = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var m = TopLevelSeqItemStart.Match(line);
                if (!m.Success) return null;

                var rest = m.Groups["rest"].Value;
                if (!KeyColonAfterDash.IsMatch(rest)) return null;

                firstIdx = i;
                dashIndent = m.Groups["indent"].Value.Length;
                break;
            }

            if (firstIdx < 0) return null;

            var sb = new StringBuilder();

            // drop "- " and its indent; left-align the first key
            {
                var line = lines[firstIdx];
                var afterDash = line.Substring(dashIndent); // "- ..."
                int cut = 1;
                if (afterDash.Length > 1 && (afterDash[1] == ' ' || afterDash[1] == '\t')) cut = 2;
                var mappingFirst = afterDash.Substring(cut);
                sb.AppendLine(mappingFirst.TrimStart());
            }

            // dedent subsequent lines minimally
            for (int i = firstIdx + 1; i < lines.Count; i++)
            {
                var line = lines[i];

                int idx = 0;
                int toRemove = dashIndent;
                while (idx < line.Length && toRemove > 0 && (line[idx] == ' ' || line[idx] == '\t'))
                {
                    idx++; toRemove--;
                }

                if (idx < line.Length)
                {
                    if (idx + 1 < line.Length && line[idx] == ' ' && line[idx + 1] == ' ') idx += 2;
                    else if (line[idx] == ' ' || line[idx] == '\t') idx += 1;
                }

                sb.AppendLine(line.Substring(Math.Min(idx, line.Length)));
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        private static string EnsureTypeKeyTextually(string s)
        {
            var m = FirstKeyLine.Match(s);
            if (!m.Success) return s;
            var key = m.Groups["k"].Value;
            if (!key.Equals("command", StringComparison.OrdinalIgnoreCase)) return s;

            var idx = m.Index;
            var len = m.Length;
            var replacement = $"{m.Groups["indent"].Value}type:{m.Groups["rest"].Value}";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append(s, 0, idx);
            sb.Append(replacement);
            sb.Append(s, idx + len, s.Length - (idx + len));
            return sb.ToString();
        }

        /// <summary>
        /// Fix inconsistent indentation for top-level mapping keys (spaces only). Do not modify tabs.
        /// Adjust block scalar bodies minimally if the key line is shifted left.
        /// </summary>
        private static string FixInconsistentTopLevelIndent(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            var lines = SplitPreserveAllLines(s);

            var keyRegex = new Regex(@"^(?<indent>[ ]*)(?<key>[^:\r\n#]+?)\s*:(?<rest>.*)$", RegexOptions.CultureInvariant);

            int? firstKeyIndent = null;
            bool inScalar = false;
            int scalarKeyIndent = -1;

            // find min indent among top-level keys (spaces only)
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (inScalar)
                {
                    int ind = 0; while (ind < line.Length && line[ind] == ' ') ind++;
                    if (ind > scalarKeyIndent) continue;
                    inScalar = false;
                }

                var scalarKeyMatch = BlockScalarKey.Match(line);
                if (scalarKeyMatch.Success)
                {
                    int indLen = scalarKeyMatch.Groups["indent"].Value.Length;
                    firstKeyIndent ??= indLen;
                    scalarKeyIndent = indLen;
                    inScalar = true;
                    continue;
                }

                var m = keyRegex.Match(line);
                if (!m.Success) continue;

                int indentLen = m.Groups["indent"].Value.Length;
                firstKeyIndent ??= indentLen;
            }

            if (!firstKeyIndent.HasValue) return s;

            int targetIndent = firstKeyIndent.Value;

            var sb = new StringBuilder(s.Length + 16);
            inScalar = false;
            scalarKeyIndent = -1;
            int indentShiftForCurrentScalar = 0;

            static string LeftTrimSpaces(string str, int count)
            {
                int i = 0; while (i < str.Length && i < count && str[i] == ' ') i++;
                return str.Substring(i);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (inScalar)
                {
                    int leadingSpaces = 0; while (leadingSpaces < line.Length && line[leadingSpaces] == ' ') leadingSpaces++;
                    if (leadingSpaces > scalarKeyIndent)
                    {
                        int desired = Math.Max(scalarKeyIndent + 1, leadingSpaces - indentShiftForCurrentScalar);
                        int toRemove = leadingSpaces - desired;
                        if (toRemove > 0) line = LeftTrimSpaces(line, toRemove);
                    }
                    sb.AppendLine(line);
                    continue;
                }

                var sk = BlockScalarKey.Match(line);
                if (sk.Success)
                {
                    int currentIndent = sk.Groups["indent"].Value.Length;
                    int shift = currentIndent - targetIndent;
                    if (shift > 0) line = LeftTrimSpaces(line, shift);

                    inScalar = true;
                    scalarKeyIndent = targetIndent;
                    indentShiftForCurrentScalar = Math.Max(0, shift);

                    sb.AppendLine(line);
                    continue;
                }

                var km = keyRegex.Match(line);
                if (km.Success)
                {
                    int currentIndent = km.Groups["indent"].Value.Length;
                    int shift = currentIndent - targetIndent;
                    if (shift > 0) line = LeftTrimSpaces(line, shift);
                    sb.AppendLine(line);
                    continue;
                }

                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        // ---------------- tiny utils ----------------

        private static bool TryDetectNewContentKeep(string segmentText, out bool keep)
        {
            keep = false;
            var m = NewContentChompRx.Match(segmentText);
            if (!m.Success) return false;           // not specified on source
            var g = m.Groups["chomp"];
            if (!g.Success) { keep = true; return true; } // plain "|" => keep
            keep = g.Value != "-";                  // "|-" => clip, "|+" => keep
            return true;
        }

        private static bool TryGetString(Dictionary<string, object> map, string key, out string value)
        {
            value = string.Empty;
            if (!map.TryGetValue(key, out var obj) || obj == null) return false;
            value = obj.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static void DropKeys(Dictionary<string, object> map, ref bool changed, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (map.ContainsKey(k))
                {
                    map.Remove(k);
                    changed = true;
                }
            }
        }

        private static void KeepOnly(Dictionary<string, object> map, ref bool changed, params string[] keysToKeep)
        {
            var keep = new HashSet<string>(keysToKeep, StringComparer.OrdinalIgnoreCase);
            var drop = map.Keys.Where(k => !keep.Contains(k)).ToList();
            if (drop.Count > 0) changed = true;
            foreach (var k in drop) map.Remove(k);
        }

        private static string IndentAllLines(string s, int spaces)
        {
            var pad = new string(' ', spaces);
            var lines = SplitPreserveAllLines(s);
            var sb = new StringBuilder(s.Length + lines.Count * spaces + 8);
            foreach (var line in lines) sb.Append(pad).AppendLine(line);
            return sb.ToString();
        }

        private static List<string> SplitPreserveAllLines(string s)
        {
            var list = new List<string>();
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r')
                {
                    list.Add(s.Substring(start, i - start));
                    if (i + 1 < s.Length && s[i + 1] == '\n') i++;
                    start = i + 1;
                }
                else if (c == '\n')
                {
                    list.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start <= s.Length)
                list.Add(s.Substring(start));
            return list;
        }

        private static bool TryParseEmbeddedYaml(string text, out Dictionary<string, object> map)
        {
            map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var parsed = Yaml.Deserialize<object>(text);
                if (parsed is Dictionary<object, object> ood)
                {
                    foreach (var kv in ood) map[kv.Key?.ToString() ?? ""] = kv.Value!;
                    return map.Count > 0;
                }
                if (parsed is Dictionary<string, object> dso)
                {
                    foreach (var kv in dso) map[kv.Key] = kv.Value!;
                    return map.Count > 0;
                }
                return false;
            }
            catch { return false; }
        }

        private static string? TryExtractFilePathFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text, @"(?mi)^\s*filePath\s*:\s*(.+)\s*$");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        // NEW: helpers to extract nested requests under a single mapping
        private static List<Dictionary<string, object>> ExtractNestedRequests(Dictionary<string, object> input, out Dictionary<string, object> parentWithoutRequests, ref bool changed)
        {
            parentWithoutRequests = new Dictionary<string, object>(input, StringComparer.OrdinalIgnoreCase);

            if (!input.TryGetValue("requests", out var reqObj) || reqObj is null)
            {
                return new List<Dictionary<string, object>>();
            }

            var results = new List<Dictionary<string, object>>();

            if (reqObj is IEnumerable<object> objEnum)
            {
                foreach (var item in objEnum)
                {
                    if (TryToStringObjectDict(item, out var dict))
                    {
                        results.Add(dict);
                    }
                }
            }
            else if (TryToStringObjectDict(reqObj, out var single))
            {
                results.Add(single);
            }

            if (results.Count > 0)
            {
                // Remove 'requests' from the parent
                if (parentWithoutRequests.ContainsKey("requests"))
                {
                    parentWithoutRequests.Remove("requests");
                    changed = true;
                }
            }

            return results;
        }

        private static bool TryToStringObjectDict(object? obj, out Dictionary<string, object> dict)
        {
            dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (obj is null) return false;

            if (obj is Dictionary<string, object> dso)
            {
                foreach (var kv in dso) dict[kv.Key] = kv.Value!;
                return true;
            }

            if (obj is Dictionary<object, object> doo)
            {
                foreach (var kv in doo) dict[kv.Key?.ToString() ?? ""] = kv.Value!;
                return true;
            }

            // Attempt YAML round-trip if it's a scalar string block
            if (obj is string s && TryParseEmbeddedYaml(s, out var parsed))
            {
                foreach (var kv in parsed) dict[kv.Key] = kv.Value!;
                return dict.Count > 0;
            }

            return false;
        }

        private static bool TryDetectTopLevelRequests(string segment)
        {
            if (TryDeserializeMapPermissive(segment, out var map))
            {
                return map.ContainsKey("requests");
            }
            return false;
        }
    }
}