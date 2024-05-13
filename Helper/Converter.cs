using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hi3Helper.Sophon.Helper;

// Credit (by dbc from Stack Overflow):
// https://stackoverflow.com/a/68685773/13362680
public class BoolConverter : JsonConverter<bool>
{
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        writer.WriteBooleanValue(value);
    }

    public override bool Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        return reader.TokenType switch
               {
                   JsonTokenType.True => true,
                   JsonTokenType.False => false,
                   JsonTokenType.String => bool.TryParse(reader.GetString(), out var boolFromString)
                       ? boolFromString
                       : throw new JsonException(),
                   JsonTokenType.Number => reader.TryGetInt64(out var boolFromNumber)
                       ? Convert.ToBoolean(boolFromNumber)
                       : reader.TryGetDouble(out var boolFromDouble) && Convert.ToBoolean(boolFromDouble),
                   _ => throw new JsonException()
               };
    }
}