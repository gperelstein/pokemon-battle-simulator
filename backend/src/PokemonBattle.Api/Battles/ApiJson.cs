using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokemonBattle.Api.Battles;

/// <summary>JSON conventions shared by HTTP and SignalR: web defaults with enums as strings.</summary>
public static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
