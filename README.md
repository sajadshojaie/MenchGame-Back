# LudoGameBackend

- Hub: `/hubs/game`
- The hub keeps the latest host `state` message for each active room in memory and sends it to newly joined or reconnected clients through `Receive`.
- Lobby setting changes use `PublishLobbySettings`; the hub accepts them only from the room host while the supplied game state is still in the lobby, stores the snapshot, and broadcasts `LobbySettingsChanged` to other clients.
- Room state is temporary and is cleared when the room has no connected players or the backend process restarts.
