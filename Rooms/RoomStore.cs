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

    public bool IsHost(string? roomId, string? selfId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(selfId)) return false;
        if (!_rooms.TryGetValue(Normalize(roomId), out var room)) return false;

        lock (room.Gate)
        {
            return string.Equals(room.HostSelfId, selfId.Trim(), StringComparison.Ordinal);
        }
    }

    public void SaveState(string? roomId, JsonElement message, string senderId)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(senderId)) return;
        if (!_rooms.TryGetValue(Normalize(roomId), out var room)) return;

        lock (room.Gate)
        {
            if (!string.Equals(room.HostSelfId, senderId, StringComparison.Ordinal)) return;
            room.LastStateMessage = message.Clone();
            room.StateSenderId = senderId;
        }
    }

    public bool TryPublishLobbySettings(string? roomId, string senderId, JsonElement message)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(senderId)) return false;
        if (!IsValidLobbySettingsState(message)) return false;
        if (!_rooms.TryGetValue(Normalize(roomId), out var room)) return false;

        lock (room.Gate)
        {
            if (!string.Equals(room.HostSelfId, senderId, StringComparison.Ordinal)) return false;
            room.LastStateMessage = message.Clone();
            room.StateSenderId = senderId;
            return true;
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

    private static bool IsValidLobbySettingsState(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("kind", out var kind)
            || kind.ValueKind != JsonValueKind.String
            || kind.GetString() != "state"
            || !message.TryGetProperty("state", out var state)
            || state.ValueKind != JsonValueKind.Object
            || !state.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String
            || status.GetString() != "lobby"
            || !state.TryGetProperty("rollTimerEnabled", out var timer)
            || timer.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !state.TryGetProperty("players", out var players)
            || players.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return players.EnumerateArray().All(player =>
            player.ValueKind == JsonValueKind.Object
            && player.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String
            && player.TryGetProperty("seat", out var seat)
            && seat.ValueKind == JsonValueKind.Number
            && seat.TryGetInt32(out _));
    }
}
