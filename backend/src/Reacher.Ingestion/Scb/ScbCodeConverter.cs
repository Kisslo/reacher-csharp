using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reacher.Ingestion.Scb;

/// <summary>
/// Reads an SCB code that arrives as a JSON string or a JSON number and returns it as
/// text with no other change (D12): <c>1</c> and <c>"1"</c> both become <c>"1"</c>, and
/// <c>"01"</c> stays <c>"01"</c>. A number is copied from the raw JSON, never parsed.
/// </summary>
public sealed class ScbCodeConverter : JsonConverter<string>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.HasValueSequence
                ? Encoding.UTF8.GetString(reader.ValueSequence)
                : Encoding.UTF8.GetString(reader.ValueSpan),
            _ => throw new JsonException(
                $"Expected an SCB code as a string or a number, got {reader.TokenType}."),
        };

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
