using System.Text.Json;
using System.Text.Json.Serialization;
using Eden.Sim.Contracts;

namespace Eden.Mind;

/// <summary>Serializer settings that make the C# DTOs in IBrainProvider.cs match the JSON schema contracts.</summary>
public static class BrainJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
            new ConceptProposalConverter(),
            new ConceptEffectConverter(),
        },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };
}

/// <summary>
/// concepts.schema.json#/$defs/concept on the wire: <c>tags</c> is allowed (and dropped), anything else unknown
/// is rejected. ConceptProposal itself carries no JSON attributes because it lives in the Sim contract.
/// </summary>
public sealed class ConceptProposalConverter : JsonConverter<ConceptProposal>
{
    public override ConceptProposal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("concept must be an object");
        string? id = null, name = null, description = null;
        List<string>? prereqs = null;
        float? difficulty = null;
        List<ConceptEffect>? effects = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string prop = reader.GetString()!;
            reader.Read();
            switch (prop)
            {
                case "id": id = reader.GetString(); break;
                case "name": name = reader.GetString(); break;
                case "description": description = reader.GetString(); break;
                case "difficulty": difficulty = reader.GetSingle(); break;
                case "prerequisites": prereqs = JsonSerializer.Deserialize<List<string>>(ref reader, options); break;
                case "effects": effects = JsonSerializer.Deserialize<List<ConceptEffect>>(ref reader, options); break;
                case "tags": reader.Skip(); break;
                default: throw new JsonException($"unknown concept field '{prop}'");
            }
        }
        if (id is null || name is null || description is null || prereqs is null || difficulty is null || effects is null)
            throw new JsonException("concept is missing a required field");
        return new ConceptProposal(id, name, description, prereqs, difficulty.Value, effects);
    }

    public override void Write(Utf8JsonWriter writer, ConceptProposal value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("description", value.Description);
        writer.WritePropertyName("prerequisites");
        JsonSerializer.Serialize(writer, value.Prerequisites, options);
        writer.WriteNumber("difficulty", value.Difficulty);
        writer.WritePropertyName("effects");
        JsonSerializer.Serialize(writer, value.Effects, options);
        writer.WriteEndObject();
    }
}

/// <summary>concepts.schema.json#/$defs/effect: the discriminator is <c>type</c>, which maps to <see cref="ConceptEffect.Kind"/>.</summary>
public sealed class ConceptEffectConverter : JsonConverter<ConceptEffect>
{
    public override ConceptEffect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("effect must be an object");
        ConceptEffectKind? kind = null;
        string? resource = null, building = null, tool = null, text = null;
        float? factor = null;
        ActionKind? action = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string prop = reader.GetString()!;
            reader.Read();
            switch (prop)
            {
                case "type": kind = JsonSerializer.Deserialize<ConceptEffectKind>(ref reader, options); break;
                case "resource": resource = reader.GetString(); break;
                case "factor": factor = reader.GetSingle(); break;
                case "building": building = reader.GetString(); break;
                case "tool": tool = reader.GetString(); break;
                case "action": action = JsonSerializer.Deserialize<ActionKind>(ref reader, options); break;
                case "text": text = reader.GetString(); break;
                default: throw new JsonException($"unknown effect field '{prop}'");
            }
        }
        if (kind is null) throw new JsonException("effect without type");
        return new ConceptEffect(kind.Value, resource, factor, building, tool, action, text);
    }

    public override void Write(Utf8JsonWriter writer, ConceptEffect value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("type");
        JsonSerializer.Serialize(writer, value.Kind, options);
        if (value.Resource is not null) writer.WriteString("resource", value.Resource);
        if (value.Factor is { } f) writer.WriteNumber("factor", f);
        if (value.Building is not null) writer.WriteString("building", value.Building);
        if (value.Tool is not null) writer.WriteString("tool", value.Tool);
        if (value.Action is { } a) { writer.WritePropertyName("action"); JsonSerializer.Serialize(writer, a, options); }
        if (value.Text is not null) writer.WriteString("text", value.Text);
        writer.WriteEndObject();
    }
}
