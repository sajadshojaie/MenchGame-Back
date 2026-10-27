using System.Text.Json;

namespace MenchGameBackend.Models;

public sealed class RoomBusyException : Exception
{
    public RoomBusyException() : base("این کد اتاق همین حالا گرفته شده است.") { }
}

public sealed record Membership(string RoomId, string SelfId);

public sealed record JoinResult(string RoomId, string[] Others, string? ReplacedConnectionId);

public sealed class PlayerConn
{
    public required string SelfId { get; init; }
    public string ConnectionId { get; set; } = "";
    public bool IsHost { get; set; }
}

public sealed class Room
{
    public required string Code { get; init; }
    public string? HostSelfId { get; set; }
    public JsonElement? LastStateMessage { get; set; }
    public string? StateSenderId { get; set; }
    public Dictionary<string, PlayerConn> Players { get; } = new(StringComparer.Ordinal);
    public object Gate { get; } = new();
}

public sealed record RoomStateSnapshot(JsonElement Message, string SenderId);
