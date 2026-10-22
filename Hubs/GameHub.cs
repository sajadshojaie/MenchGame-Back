using System.Text.Json;
using LudoGameBackend.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace LudoGameBackend.Hubs;

public sealed class GameHub(RoomStore rooms) : Hub
{
    public async Task<string[]> Join(string roomId, string selfId, string role)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(selfId))
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
        return result.Others;
    }

    public Task Leave() => LeaveCore(explicitLeave: true);

    public async Task Send(JsonElement message, string? targetSelfId)
    {
        if (!rooms.TryGetMembership(Context.ConnectionId, out var membership)) return;

        if (!string.IsNullOrWhiteSpace(targetSelfId))
        {
            var target = rooms.ConnectionOf(membership.RoomId, targetSelfId);
            if (!string.IsNullOrEmpty(target))
                await Clients.Client(target).SendAsync("Receive", message, membership.SelfId);
            return;
        }

        await Clients.OthersInGroup(membership.RoomId).SendAsync("Receive", message, membership.SelfId);
    }

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
