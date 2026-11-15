using PokemonBattle.Api.Battles;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
builder.Services.AddBattles();

var app = builder.Build();
app.MapLobby();
app.Run();

/// <summary>Entry point, public for the integration tests' WebApplicationFactory.</summary>
public partial class Program;
