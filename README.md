# TestGame

Multiplayer prototype for 2-4 players, made for ArtDock test task. Steam P2P and LAN, network physics cubes, host-controlled world

Download the Windows build from [GitHub Releases](https://github.com/vertoker/art-dock-test-task/releases)

#### Steam

1. Start Steam and the game on two PCs with different Steam accounts
2. Select `Steam` > `Create game`, set the name and player limit, press `Create`
3. The other player joins from the friend list, an invitation or the lobby ID shown by the host
4. The host presses `Start game`. Players can also join after the game starts

Lobbies are friends-only by default, with a private option. SteamID and Steam name are used for the player list

I use Facepunch with AppID `480` (`Spacewar`). The ID is set in code, the Steam library is included in the build. If Steam is unavailable, the game shows an error and LAN is still usable

`Invite friends` shows an in-game list and opens the overlay if available. For `Shift+Tab`, add the executable to Steam as a non-Steam game and launch it there with the overlay enabled. Keep both game instances open when checking invitations

#### LAN

1. Select `LAN` > `Create game` on the host
2. On the client, select `Join game` and enter the host's IPv4 address, e.g. `192.168.1.20:7777`, or use `Find local games`
3. The host presses `Start game`

For one PC, use `127.0.0.1:7777` and different `--profile` values for each instance. The same profile has the same player GUID, so a second connection with it is rejected

Allow the game through the firewall. Default UDP ports: `7777` for gameplay, `47778` for discovery. If discovery finds nothing, enter the IP manually

#### Controls

| Input | Action |
| --- | --- |
| `WASD`, mouse, `Space` | Move, look, jump |
| `N` | Create a cube |
| Left click | Grab, drop or use a dispenser |
| Right click | Throw the held cube |
| `Delete` | Delete the held cube |
| `C` | Change colour |
| Hold `Tab` | Player list and ping |
| `Esc` | Session menu |
| `F3` | Network diagnostics |

The host can kick players from the session menu. The menu releases the cursor and blocks local input, the game keeps running

#### Command line

Examples from the build folder in PowerShell:

```powershell
.\TestGame.exe --steam --host
.\TestGame.exe --steam --join <lobby-id>
.\TestGame.exe --host --profile host
.\TestGame.exe --join 127.0.0.1:7777 --profile client1
```

| Argument | Use |
| --- | --- |
| `--steam` or `--mode steam` | Steam mode, otherwise LAN |
| `--host` | Create a lobby on launch |
| `--join <address>` | Join by lobby ID or IPv4, depending on the mode |
| `--private` | Private Steam lobby |
| `--profile <id>` | LAN identity slot, default `default` |
| `--name <name>` | LAN display name |
| `--port <port>` | LAN port, default `7777` |
| `+connect_lobby <id>` | Steam lobby join argument |

`--host` opens the lobby. The host still presses `Start game` in the menu

#### Code

Unity `6000.3.9f1`, NGO `2.7.0`, UTP `2.6.0`, Input System `1.18.0`, VContainer `1.17.0`. Steam uses the [community Facepunch transport](https://github.com/Unity-Technologies/multiplayer-community-contributions/tree/0fab638470379ace12b0149dfb41c043d23dbce5/Transports/com.community.netcode.transport.facepunch)

Open `Assets/Scenes/Boot.unity` to run from Unity. For a Windows build, include `Boot`, `Arena`, `Menu` in that order

| Assembly | Contents |
| --- | --- |
| `Game.Core` | Approval, item claims, movement budget, interfaces. No Unity dependency |
| `Game.Networking` | Session, network objects, player control, physics and platform adapters |
| `Game.Runtime` | VContainer setup, UI Toolkit views, application loop and world buttons |

Dependencies: `Runtime -> Networking -> Core`. `ProjectScope` sets up the services. Spawned network objects get the session's settings and lookups through `NetworkContext`

- Settings: `Assets/Settings/GameSettings.asset`
- Add a transport: implement `IConnectionPlatform`, select the transport in its preparation methods, register it in `ProjectScope` and add its menu mode
- Add an item type: register its network prefab and add a server-side choice in `ItemSpawner`. The current spawner uses one cube prefab

#### Networking

I chose owner-authoritative movement so local controls respond immediately. The host checks position and speed, keeps the last accepted position and corrects invalid moves. Remote players use NGO interpolation

The host runs all item physics. Client bodies are kinematic and use interpolated transforms. Holding an item transfers ownership to the player, but the host still controls its physics

- Spawn: the host checks cooldown, free space, world bounds and the limit of `48` items
- Grab: the host checks the sender, distance, line of sight and item revision. The first valid grab changes the revision, so another request for the old state fails. Each player can hold one item
- Drop, throw and delete: the host checks the holder and revision again. A throw uses a fixed impulse and a checked direction. Drop and throw return ownership to the host
- Disconnect: the held item is released and falls. Items survive their creator leaving

Network tick is `30 Hz`. Players use reliable transform updates, items use unreliable deltas. Aim is sent on change, at most `10 Hz`, with a refresh every `0.5 s`. Held items have a small visual offset to reduce lag, limited by obstacle checks

Approval checks version, duplicate identity and player limit. The host tracks scene loading and starts everyone after the shared load finishes. Disconnected or timed-out players do not hold up the others

Late-join gets the current scene, items, holders and colours. Existing players keep playing. If the host leaves, clients return to the menu with a reason

Steam lobby data follows the session's player count and join state. Rich Presence shows lobby/game status and clears on exit

#### Limits

- Movement checks are basic, without server collision checks or prediction
- Player identity is declared in the connection payload. SteamID is not checked against the transport peer. That binding would be needed for authentication
- Ping is an application round trip, including RPC and game scheduling. Traffic counters and direct/relay route are shown as `N/A`
- Stats use a logging service behind `IPlayerStatistics`. Only host-confirmed throws count, on the thrower's client. It flushes every `10 s` and on exit. No persistent stats, host migration or automatic reconnect

EditMode tests are in `Assets/Code/Core/Tests` and `Assets/Code/Networking/Tests`. Run them from Unity's Test Runner
