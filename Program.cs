using System.Text.Json;
using LudoGameBackend.Helper;
using LudoGameBackend.Hubs;
using LudoGameBackend.Rooms;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RoomStore>();
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var extraOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin => CorsHelper.IsAllowedOrigin(origin, extraOrigins))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();
app.UseCors();
app.MapHub<GameHub>("/hubs/game");
app.Run();

