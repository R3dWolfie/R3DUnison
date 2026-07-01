# R3D Unison

Online multiplayer mod for [A Dance of Fire and Ice](https://store.steampowered.com/app/977950/): play the same level together in real time.

**Status: early development (M0 — mod skeleton).** Nothing playable yet.

## Planned

- **Ghost race** (v1): rooms with a browser, everyone plays the same level simultaneously on their own screen — live accuracy/combo/progress per player, ghost planets on your track, spectate on death, shared results screen. No lockstep: clients exchange song-time-stamped events, so ping never touches your rhythm.
- **Co-op** (v2): the official local co-op, online.
- Networking: Steam lobbies + P2P first; transport is abstracted so a relay server (short join codes, non-Steam) can be added later.

## Building

Requires the .NET SDK and an ADOFAI install (the build references the game's `Managed` DLLs).

```sh
dotnet build src/R3DUnison -p:AdofaiDir="/path/to/A Dance of Fire and Ice"
```

`AdofaiDir` defaults to the path in `Directory.Build.props`. A successful build auto-copies the mod into the game's `Mods/R3DUnison/` folder.

## Installing (players)

1. Install [UnityModManager](https://www.nexusmods.com/site/mods/21) for ADOFAI.
2. Drop the release zip into UMM, or extract it to `<game>/Mods/R3DUnison/`.
3. In-game, Ctrl+F10 opens the mod list.

## Layout

- `src/R3DUnison/Core` — mod entry plumbing, main-thread dispatcher
- `src/R3DUnison/Transport` — `ITransport` seam; Steam P2P (M1), relay (later)
- `src/R3DUnison/Protocol` — wire messages
- `src/R3DUnison/Session` — room state, `IGameMode` (ghost race, later co-op)
- `src/R3DUnison/Game` — Harmony patches into the game
- `src/R3DUnison/UI` — room browser, lobby, in-race HUD, results
