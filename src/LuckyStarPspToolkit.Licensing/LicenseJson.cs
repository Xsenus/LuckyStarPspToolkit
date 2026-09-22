using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>One bounded and unambiguous JSON dialect for licensing requests, signed payloads and storage.</summary>
public static class LicenseJson
{
    /// <summary>Case-sensitive camel-case schema; comments, unknown fields, numeric strings and trailing commas are forbidden.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 24,
        WriteIndented = false
    };

    /// <summary>Serializes a protocol/storage model as UTF-8 without reflection-dependent formatting conventions.</summary>
    /// <typeparam name="T">Known schema model.</typeparam>
    /// <param name="value">Object to serialize.</param>
    /// <returns>Serialized UTF-8 bytes.</returns>
    public static byte[] Write<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    /// <summary>Rejects duplicates including escaped aliases before deserializing the exact same bounded snapshot.</summary>
    /// <typeparam name="T">Expected concrete schema.</typeparam>
    /// <param name="data">Complete JSON snapshot.</param>
    /// <param name="limit">Maximum permitted bytes, independently set for API and database data.</param>
    /// <returns>A non-null instance; business validation is still required.</returns>
    /// <exception cref="LicenseException">Size, shape, duplicate names or schema are invalid.</exception>
    public static T Read<T>(ReadOnlySpan<byte> data, int limit = 32768)
    {
        if (data.Length == 0 || data.Length > limit) throw new LicenseException("JSON_LIMIT", "Invalid licensing document size.");
        try
        {
            var reader = new Utf8JsonReader(data, new JsonReaderOptions { MaxDepth = 24 });
            var scopes = new Stack<HashSet<string>?>();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject: scopes.Push(new(StringComparer.Ordinal)); break;
                    case JsonTokenType.StartArray: scopes.Push(null); break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray: scopes.Pop(); break;
                    case JsonTokenType.PropertyName:
                        if (!scopes.Peek()!.Add(reader.GetString()!))
                            throw new LicenseException("JSON_DUPLICATE", "Duplicate licensing property.");
                        break;
                }
            }
            return JsonSerializer.Deserialize<T>(data, Options)
                ?? throw new LicenseException("JSON_NULL", "Null licensing document.");
        }
        catch (JsonException) { throw new LicenseException("JSON_INVALID", "Malformed licensing document."); }
    }
}
