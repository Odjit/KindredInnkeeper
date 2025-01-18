using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unity.Mathematics;

public class Float2Converter : JsonConverter<float2>
{
    public override float2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException();
        }

        reader.Read();
        var x = reader.GetSingle();
        reader.Read();
        var y = reader.GetSingle();
        reader.Read();

        return new float2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, float2 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.x);
        writer.WriteNumberValue(value.y);
        writer.WriteEndArray();
    }
}   
