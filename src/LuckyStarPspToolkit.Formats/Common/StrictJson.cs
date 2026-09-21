using System.Text.Json;

namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>Reads operation manifests without ambiguous duplicate JSON property names.</summary>
public static class StrictJson
{
    /// <summary>Validates every object scope before deserializing the same bounded UTF-8 snapshot.</summary>
    /// <typeparam name="T">The manifest or translation model to instantiate.</typeparam>
    /// <param name="data">The complete JSON document, already bounded by the caller's text limit.</param>
    /// <param name="options">Serialization settings; their case sensitivity also applies to duplicate detection.</param>
    /// <param name="errorCode">The caller-specific code for malformed or null JSON documents.</param>
    /// <returns>The non-null deserialized model. Structural model validation remains the caller's responsibility.</returns>
    /// <exception cref="ToolkitException">A duplicate property, malformed JSON, or null root was encountered.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> data, JsonSerializerOptions options, string errorCode)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        // A single UTF-8 BOM is permitted; it is not part of JSON property names.
        if (data.StartsWith("\uFEFF"u8))
        {
            data = data[3..];
        }
        try
        {
            var reader = new Utf8JsonReader(data, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = options.MaxDepth == 0 ? 64 : options.MaxDepth
            });
            var scopes = new Stack<HashSet<string>?>();
            StringComparer comparer = options.PropertyNameCaseInsensitive
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        scopes.Push(new HashSet<string>(comparer));
                        break;
                    case JsonTokenType.StartArray:
                        scopes.Push(null);
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        scopes.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        string property = reader.GetString()!;
                        // Decoding the property name catches both literal and escaped duplicates.
                        if (!scopes.Peek()!.Add(property))
                        {
                            throw new ToolkitException("JSON_DUPLICATE_PROPERTY",
                                $"Duplicate JSON property '{property}' at byte {reader.TokenStartIndex}.");
                        }
                        break;
                }
            }
            T? result = JsonSerializer.Deserialize<T>(data, options);
            return result is null
                ? throw new ToolkitException(errorCode, "JSON document has a null root.")
                : result;
        }
        catch (JsonException ex)
        {
            throw new ToolkitException(errorCode, $"Invalid JSON document: {ex.Message}", ex);
        }
    }
}
