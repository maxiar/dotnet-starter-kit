using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FSH.CLI.Infrastructure;

/// <summary>
/// Sets string values in an <c>appsettings*.json</c> file without rewriting the rest of it.
/// </summary>
/// <remarks>
/// The template's settings files carry <c>//</c> comments that explain values a project must
/// change (Data Protection's application name, the Redis key prefix). A parse-and-serialize round
/// trip either throws on them (<c>JsonNode.Parse</c> rejects comments by default) or, with
/// comments skipped, silently drops them from the scaffold.
/// So the values are located with a comment-tolerant reader and only their tokens are replaced.
/// </remarks>
internal static class JsonSettingsEditor
{
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions ValueOptions = new()
    {
        // Keep connection strings readable ("Password=..;" rather than "=").
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Sets each <c>Section:Key</c> to its value in <paramref name="path"/>. Returns false, leaving
    /// the file untouched, when the file is missing or any key is not an existing string value.
    /// </summary>
    internal static bool TrySetStrings(string path, IReadOnlyDictionary<string, string> values)
    {
        if (!File.Exists(path)) return false;

        string? updated = SetStrings(File.ReadAllText(path), values);
        if (updated is null) return false;

        File.WriteAllText(path, updated);
        return true;
    }

    /// <summary>
    /// The JSON text with every <c>Section:Key</c> string value replaced, everything else (comments,
    /// layout, key order) byte-for-byte unchanged; null when a key is missing or not a string.
    /// </summary>
    internal static string? SetStrings(string json, IReadOnlyDictionary<string, string> values)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, ReaderOptions);

        // Property names of the enclosing containers; the root object contributes no segment.
        var containers = new List<string?>();
        string? property = null;
        var edits = new List<(int Start, int End, string Value)>();
        var found = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        property = reader.GetString();
                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        containers.Add(property);
                        property = null;
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        containers.RemoveAt(containers.Count - 1);
                        break;

                    default:
                        // A scalar directly under a named property (array items have no name).
                        if (property is not null && reader.TokenType == JsonTokenType.String)
                        {
                            string key = string.Join(':', containers.Skip(1).Append(property));
                            if (values.TryGetValue(key, out string? value) && found.Add(key))
                                edits.Add(((int)reader.TokenStartIndex, (int)reader.BytesConsumed, value));
                        }

                        property = null;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        if (found.Count != values.Count) return null;

        // Back to front, so earlier offsets stay valid.
        var output = new List<byte>(bytes);
        foreach ((int start, int end, string value) in edits.OrderByDescending(edit => edit.Start))
        {
            output.RemoveRange(start, end - start);
            output.InsertRange(start, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, ValueOptions)));
        }

        return Encoding.UTF8.GetString([.. output]);
    }
}
