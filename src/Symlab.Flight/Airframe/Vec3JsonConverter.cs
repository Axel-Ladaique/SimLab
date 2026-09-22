using System.Text.Json;
using System.Text.Json.Serialization;
using Symlab.Flight.Geometry;

namespace Symlab.Flight.Airframe;

internal sealed class Vec3JsonConverter : JsonConverter<Vec3>
{
    public override Vec3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = JsonSerializer.Deserialize<double[]>(ref reader, options);
        if (values is not { Length: 3 }) throw new JsonException("Expected a vector [x, y, z].");
        return new Vec3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}
