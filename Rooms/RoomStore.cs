using System.Collections.Concurrent;
using System.Text.Json;
using MenchGameBackend.Models;

namespace MenchGameBackend.Rooms;

public sealed class RoomStore
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Membership> _byConnection = new(StringComparer.Ordinal);

    public static string Normalize(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return string.Empty;
        return roomId.Trim().ToUpperInvariant();
    }

    public bool TryGetMembership(string? connectionId, out Membership membership)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            membership = default!;
            return false;
        }

        return _byConnection.TryGetValue(connectionId, out membership!);
    }

    public JoinResult Join(string? connectionId, string? roomId, string? selfId, string? role)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("Connection id is required.", nameof(connectionId));

        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("Room id is required.", nameof(roomId));

        if (string.IsNullOrWhiteSpace(selfId))
            throw new ArgumentException("Self id is required.", nameof(selfId));

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

    public Membership? Disconnect(string? connectionId, bool dropSeat)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return null;
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

    public string? ConnectionOf(string? roomId, string? selfId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(selfId)) return null;

        if (!_rooms.TryGetValue(Normalize(roomId), out var room)) return null;
        lock (room.Gate)
        {
            return room.Players.TryGetValue(selfId.Trim(), out var player) ? player.ConnectionId : null;
        }
    }

    public void SaveState(string? roomId, JsonElement message, string senderId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(senderId)) return;
        if (!_rooms.TryGetValue(Normalize(roomId), out var room)) return;

        lock (room.Gate)
        {
            room.LastStateMessage = message.Clone();
            room.StateSenderId = senderId;
        }
    }

    public RoomStateSnapshot? GetLatestState(string? roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return null;
        if (!_rooms.TryGetValue(Normalize(roomId), out var room)) return null;

        lock (room.Gate)
        {
            if (room.LastStateMessage is not { } message || string.IsNullOrWhiteSpace(room.StateSenderId))
                return null;
            return new RoomStateSnapshot(message.Clone(), room.StateSenderId);
        }
    }
}
