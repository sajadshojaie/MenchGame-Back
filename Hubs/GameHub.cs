using System.Text.Json;
using MenchGameBackend.Models;
using MenchGameBackend.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace MenchGameBackend.Hubs;

public sealed class GameHub(RoomStore rooms) : Hub
{
    public async Task<string[]> Join(string roomId, string selfId, string role)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(selfId) || string.IsNullOrWhiteSpace(role))
            throw new HubException("INVALID");

        if (rooms.TryGetMembership(Context.ConnectionId, out var previous)
            && !string.Equals(previous.RoomId, RoomStore.Normalize(roomId), StringComparison.OrdinalIgnoreCase))
        {
            await LeaveCore(explicitLeave: true);
        }

        JoinResult result;
        try
        {
            result = rooms.Join(Context.ConnectionId, roomId, selfId, role);
        }
        catch (RoomBusyException)
        {
            throw new HubException("ROOM_BUSY");
        }

        if (result.ReplacedConnectionId is { } old)
            await Groups.RemoveFromGroupAsync(old, result.RoomId);

        await Groups.AddToGroupAsync(Context.ConnectionId, result.RoomId);
        await Clients.OthersInGroup(result.RoomId).SendAsync("PeerJoined", selfId);
        var snapshot = rooms.GetLatestState(result.RoomId);
        if (snapshot is not null)
            await Clients.Caller.SendAsync("Receive", snapshot.Message, snapshot.SenderId);
        return result.Others;
    }

    public Task Leave() => LeaveCore(explicitLeave: true);

    public async Task Send(JsonElement message, string? targetSelfId)
    {
        if (!rooms.TryGetMembership(Context.ConnectionId, out var membership)) return;

        if (IsStateMessage(message) && rooms.IsHost(membership.RoomId, membership.SelfId))
            rooms.SaveState(membership.RoomId, message, membership.SelfId);

        if (!string.IsNullOrWhiteSpace(targetSelfId))
        {
            var target = rooms.ConnectionOf(membership.RoomId, targetSelfId);
            if (!string.IsNullOrEmpty(target))
                await Clients.Client(target).SendAsync("Receive", message, membership.SelfId);
            return;
        }

        await Clients.OthersInGroup(membership.RoomId).SendAsync("Receive", message, membership.SelfId);
    }

    public async Task PublishLobbySettings(JsonElement message)
    {
        if (!rooms.TryGetMembership(Context.ConnectionId, out var membership)
            || !rooms.TryPublishLobbySettings(membership.RoomId, membership.SelfId, message))
        {
            throw new HubException("LOBBY_SETTINGS_REJECTED");
        }

        await Clients.OthersInGroup(membership.RoomId)
            .SendAsync("LobbySettingsChanged", message, membership.SelfId);
    }

    private static bool IsStateMessage(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object
        && message.TryGetProperty("kind", out var kind)
        && kind.ValueKind == JsonValueKind.String
        && string.Equals(kind.GetString(), "state", StringComparison.Ordinal);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCore(explicitLeave: false);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeaveCore(bool explicitLeave)
    {
        var membership = rooms.Disconnect(Context.ConnectionId, dropSeat: explicitLeave);
        if (membership is null) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, membership.RoomId);
        await Clients.Group(membership.RoomId).SendAsync("PeerLeft", membership.SelfId, explicitLeave);
    }
}
