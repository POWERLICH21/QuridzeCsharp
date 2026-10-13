# Bomb Decode Victory Mode (Unity, Netcode for GameObjects)

Free-for-all mode for 6 players. Every player is everyone's enemy.

- A **stationary bomb** sits in the map with a **45-second** timer.
- Stand next to it and **hold E for 6 seconds** to decode it.
- Only **one player can decode at a time**. Everyone else has to kill that player to stop them.
- Decoding resets if the decoder lets go of E, walks out of range, or dies.
- **The first player to finish decoding wins.** If the timer reaches 0, the bomb explodes and **nobody wins**.
- No respawns during a round: dead players are out. If everyone dies, nobody wins.

All game rules run on the server (host), so a client can't cheat by decoding from across the map.

## Requirements

- **Netcode for GameObjects** (Package Manager → `com.unity.netcode.gameobjects`, 1.x or 2.x)
- **TextMeshPro** (only for the HUD)
- Input uses the classic `Input.GetKey`. If your project uses **only** the new Input System, set
  *Project Settings → Player → Active Input Handling* to **Both**, or swap that one line in
  `PlayerBombInteractor.cs`.

## Files

| Script | Put it on |
|---|---|
| `BombVictoryGameManager.cs` | An empty GameObject in the game scene + a `NetworkObject` |
| `DecodableBomb.cs` | Your bomb model in the scene + a `NetworkObject` (no `NetworkTransform`) |
| `PlayerBombInteractor.cs` | Your **player prefab** (the one set as Player Prefab in `NetworkManager`) |
| `BombModeHUD.cs` | Your Canvas |

## Setup

1. Copy the `Scripts` folder into your project, e.g. `Assets/Scripts/BombMode/`.
2. **Game manager:** create an empty GameObject named `BombGameManager`. Add `NetworkObject` and
   `BombVictoryGameManager`, then drag your bomb into the **Bomb** field.
3. **Bomb:** select your bomb object, add `NetworkObject` and `DecodableBomb`. The yellow gizmo
   sphere shows how close players must be. Optionally assign an explosion prefab and a looping
   "decoding" audio source.
4. **Player prefab:** add `PlayerBombInteractor`.
5. **HUD:** add `BombModeHUD` to your Canvas and drag in TextMeshPro texts for the timer, status,
   prompt and end screen, plus an `Image` set to **Image Type = Filled** for the progress bar.
6. **Report deaths.** This is the only change needed in your existing code. In your player
   health script, where the player dies **on the server**, add:

   ```csharp
   if (IsServer && BombVictoryGameManager.Instance != null)
       BombVictoryGameManager.Instance.ReportPlayerDeath(OwnerClientId);
   ```

   Also turn off respawning while this mode is running.
7. **Everyone is an enemy:** if your damage code skips "teammates", make sure all 6 players are
   on different teams, or that friendly fire is on.

### Testing with fewer than 6 players

Set **Required Players** on the game manager to `1` or `2` while testing, then set it back to `6`.
Use ParrelSync or a build + editor to run two instances.

## Optional extras

- **Getting shot interrupts decoding:** in your damage code (on the server) call
  `DecodableBomb.Instance.ServerCancelDecodeBy(OwnerClientId);`
- **No shooting while decoding:** in your weapon script,
  `if (GetComponent<PlayerBombInteractor>().IsDecoding) return;`
- **Real player names:** edit `BombVictoryGameManager.GetPlayerDisplayName`.
- **Lobby cap of 6:** enforce it in your lobby or `NetworkManager` connection approval.

## Tuning (Inspector)

| Setting | Default | Where |
|---|---|---|
| Bomb timer | 45 s | Game manager → Bomb Timer Seconds |
| Pre-match countdown | 5 s | Game manager → Pre Match Countdown |
| Players needed | 6 | Game manager → Required Players |
| Time to decode | 6 s | Bomb → Decode Duration |
| Decode range | 2.5 m | Bomb → Interact Range |
| Decode key | E | Player → Decode Key |
