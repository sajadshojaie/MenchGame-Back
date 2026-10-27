# LudoGameBackend

- Hub: `/hubs/game`
- The hub keeps the latest `state` message for each active room in memory and sends it to newly joined or reconnected clients through `Receive`. Lobby settings are part of this shared game state.
- Room state is temporary and is cleared when the room has no connected players or the backend process restarts.
