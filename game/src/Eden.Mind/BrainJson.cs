using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eden.Mind;

/// <summary>Serializer settings that make the C# DTOs in IBrainProvider.cs match the JSON schema contracts.</summary>
public static class BrainJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };
}
