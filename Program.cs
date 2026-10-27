using System.Text.Json;
using MenchGameBackend.Helper;
using MenchGameBackend.Hubs;
using MenchGameBackend.Rooms;

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
            .AllowAnyMethod()
            .AllowCredentials()
            .AllowAnyHeader();
    });
});

// builder.Services.AddCors(option => option.AddPolicy("CorsPolicy",
//     x => x.WithOrigins("https://mench.dev-future.ir", "http://localhost:3000")
//         .AllowAnyMethod().AllowCredentials().AllowAnyHeader()
//     ));

var app = builder.Build();
app.UseCors();
// app.UseCors("CorsPolicy");
app.MapHub<GameHub>("/hubs/game");
app.Run();

