using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Solurum.StaalAi.AICommands
{
    public static class StaalNDJsonFactory
    {
        // Use strict, simple parsing – no polymorphic converters needed.
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false
        };

        public static List<IStaalCommand> FromNdjson(string ndjson)
        {
            var list = new List<IStaalCommand>();
            using var reader = new StringReader(ndjson ?? string.Empty);

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                using var doc = JsonDocument.Parse(trimmed, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = false
                });

                if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                    throw new JsonException("Missing 'type' property.");

                var type = typeProp.GetString() ?? string.Empty;

                // Single switch that creates the concrete class and fills its fields.
                switch (type)
                {
                    case "STAAL_CONTENT_REQUEST":
                        {
                            var cmd = new StaalContentRequest
                            {
                                FilePath = GetString(doc.RootElement, "filePath", required: true)
                            };
                            list.Add(cmd);
                            break;
                        }

                    case "STAAL_CONTENT_DELETE":
                        {
                            var cmd = new StaalContentDelete
                            {
                                FilePath = GetString(doc.RootElement, "filePath", required: true)
                            };
                            list.Add(cmd);
                            break;
                        }

                    case "STAAL_CONTENT_CHANGE":
                        {
                            var cmd = new StaalContentChange
                            {
                                FilePath = GetString(doc.RootElement, "filePath", required: true),
                                NewContent = GetString(doc.RootElement, "newContent", required: true)
                            };
                            list.Add(cmd);
                            break;
                        }

                    case "STAAL_GET_WORKING_DIRECTORY_STRUCTURE":
                        list.Add(new StaalGetWorkingDirectoryStructure());
                        break;

                    case "STAAL_CI_LIGHT_REQUEST":
                        list.Add(new StaalCiLightRequest());
                        break;

                    case "STAAL_CI_HEAVY_REQUEST":
                        list.Add(new StaalCiHeavyRequest());
                        break;

                    case "STAAL_FINISH_OK":
                        {
                            var cmd = new StaalFinishOk
                            {
                                PrMessage = GetString(doc.RootElement, "prMessage", required: true)
                            };
                            list.Add(cmd);
                            break;
                        }

                    case "STAAL_FINISH_NOK":
                        {
                            var cmd = new StaalFinishNok
                            {
                                ErrMessage = GetString(doc.RootElement, "errMessage", required: true)
                            };
                            list.Add(cmd);
                            break;
                        }

                    case "STAAL_STATUS":
                        {
                            var cmd = new StaalStatus
                            {
                                StatusMsg = GetString(doc.RootElement, "statusMsg", required: true)
                            };
                            list.Add(cmd);
                            break;
                        }

                    case "STAAL_CONTINUE":
                        list.Add(new StaalContinue());
                        break;

                    default:
                        throw new JsonException($"Unknown STAAL command type: '{type}'.");
                }
            }

            return list;
        }

        public static string ToNdjson(IEnumerable<IStaalCommand> commands)
        {

            var sb = new System.Text.StringBuilder();

            foreach (var cmd in commands)
            {
                string json = cmd switch
                {
                    StaalContentRequest c => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_CONTENT_REQUEST",
                        filePath = c.FilePath
                    }, Options),

                    StaalContentDelete c => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_CONTENT_DELETE",
                        filePath = c.FilePath
                    }, Options),

                    StaalContentChange c => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_CONTENT_CHANGE",
                        filePath = c.FilePath,
                        newContent = c.NewContent
                    }, Options),

                    StaalGetWorkingDirectoryStructure => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_GET_WORKING_DIRECTORY_STRUCTURE"
                    }, Options),

                    StaalCiLightRequest => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_CI_LIGHT_REQUEST"
                    }, Options),

                    StaalCiHeavyRequest => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_CI_HEAVY_REQUEST"
                    }, Options),

                    StaalFinishOk c => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_FINISH_OK",
                        prMessage = c.PrMessage
                    }, Options),

                    StaalFinishNok c => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_FINISH_NOK",
                        errMessage = c.ErrMessage
                    }, Options),

                    StaalStatus c => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_STATUS",
                        statusMsg = c.StatusMsg
                    }, Options),

                    StaalContinue => JsonSerializer.Serialize(new
                    {
                        type = "STAAL_CONTINUE"
                    }, Options),

                    _ => throw new NotSupportedException($"Unsupported command type: {cmd.GetType().Name}")
                };

                sb.AppendLine(json);
            }

            return sb.ToString();
        }

        private static string GetString(JsonElement root, string name, bool required)
        {
            if (root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString() ?? string.Empty;

            if (required)
                throw new JsonException($"Missing required string property '{name}'.");
            return string.Empty;
        }
    }

}