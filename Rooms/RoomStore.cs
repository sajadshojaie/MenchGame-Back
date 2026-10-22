using System.Collections.Concurrent;

namespace LudoGameBackend.Rooms;

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
    public Dictionary<string, PlayerConn> Players { get; } = new(StringComparer.Ordinal);
    public object Gate { get; } = new();
}

public sealed class RoomStore
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Membership> _byConnection = new(StringComparer.Ordinal);

    public static string Normalize(string roomId) => roomId.Trim().ToUpperInvariant();

    public bool TryGetMembership(string connectionId, out Membership membership) =>
        _byConnection.TryGetValue(connectionId, out membership!);

    public JoinResult Join(string connectionId, string roomId, string selfId, string role)
    {
        roomId = Normalize(roomId);
        selfId = selfId.Trim();
        var isHost = string.Equals(role, "host", StringComparison.OrdinalIgnoreCase);
        var room = _rooms.GetOrAdd(roomId, code => new Room { Code = code });

        lock (room.Gate)
        {
            if (isHost
                && !string.IsNullOrEmpty(room.HostSelfId)
                && room.HostSelfId != selfId
                && room.Players.TryGetValue(room.HostSelfId, out var liveHost)
                && !string.IsNullOrEmpty(liveHost.ConnectionId))
            {
                throw new RoomBusyException();
            }

            if (isHost) room.HostSelfId = selfId;

            string? replaced = null;
            if (room.Players.TryGetValue(selfId, out var existing))
            {
                if (!string.IsNullOrEmpty(existing.ConnectionId) && existing.ConnectionId != connectionId)
                    replaced = existing.ConnectionId;
                existing.ConnectionId = connectionId;
                existing.IsHost |= isHost;
            }
            else
            {
                room.Players[selfId] = new PlayerConn
                {
                    SelfId = selfId,
                    ConnectionId = connectionId,
                    IsHost = isHost,
                };
            }

            _byConnection[connectionId] = new Membership(roomId, selfId);
            if (replaced is not null) _byConnection.TryRemove(replaced, out _);

            var others = room.Players.Values
                .Where(player => player.SelfId != selfId && !string.IsNullOrEmpty(player.ConnectionId))
                .Select(player => player.SelfId)
                .ToArray();

            return new JoinResult(roomId, others, replaced);
        }
    }

    public Membership? Disconnect(string connectionId, bool dropSeat)
    {
        if (!_byConnection.TryRemove(connectionId, out var membership)) return null;
        if (!_rooms.TryGetValue(membership.RoomId, out var room)) return membership;

        lock (room.Gate)
        {
            if (!room.Players.TryGetValue(membership.SelfId, out var player)) return membership;
            if (player.ConnectionId != connectionId) return null;

            if (dropSeat)
            {
                room.Players.Remove(membership.SelfId);
                if (room.HostSelfId == membership.SelfId) room.HostSelfId = null;
            }
            else
            {
                player.ConnectionId = "";
            }

            if (room.Players.Values.All(item => string.IsNullOrEmpty(item.ConnectionId)))
                _rooms.TryRemove(membership.RoomId, out _);
        }

        return membership;
    }

    public string? ConnectionOf(string roomId, string selfId)
    {
        if (!_rooms.TryGetValue(Normalize(roomId), out var room)) return null;
        lock (room.Gate)
        {
            return room.Players.TryGetValue(selfId, out var player) ? player.ConnectionId : null;
        }
    }
}
