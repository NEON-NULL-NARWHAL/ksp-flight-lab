# 1. Architecture

KSP Flight Lab is the app for AI pilots in KSP: it installs [KSP Wingman](https://github.com/NEON-NULL-NARWHAL/ksp-wingman),
runs its agents, runs the KSC tower they talk to, and shows what they've learned. It's its own app on purpose (the
owner's call, 2026-10-25): the tower, the skills and the tests only matter while agents fly, so they moved out of LUNA
EVO Mission Control instead of making it bigger.

## 1.1 Who talks to whom

```
 Flight Lab (this app, one PC)
 ├─ KSC tower ── http://localhost:8920/ ──┐      the agents' channel: reports, says, flights, lessons, tests, places
 │    └─ knowledge.json (in <server>/LunaEvo/agents/, or next to the settings)
 │    └─ radio.jsonl (in <server>/LunaEvo/, only with "Talk on LMP chat")
 └─ Agents ── stdin/stdout JSON lines (--ipc) ──> KspWingman console ──> KspWingman.Mcp ──kRPC──> the agents' KSP
                                                      │                         │
                                                      └── Ollama (the models) ──┘ ──────> the tower (WINGMAN_TOWER_URL)

 LMP server  ── LUNA EVO server plugin reads radio.jsonl and says it in chat (installed by Mission Control)
```

- **Same PC.** Flight Lab, its agents, their KSPs, the model server and (usually) the LMP server run on one PC. Every
  port is localhost; nothing listens on the LAN.
- **Mission Control** stays the server's app (config, console, players, mods). It's not needed for Flight Lab to run,
  but it installs the server plugin the tower's radio needs. A bridge between the two (one-click install of Flight Lab
  from Mission Control) is planned.
- **The knowledge file is shared history.** With a server folder chosen, the tower uses
  `<server>/LunaEvo/agents/knowledge.json`: the very file Mission Control's tower kept, so nothing learned is lost in
  the move.

## 1.2 Projects

| Project | What |
|---|---|
| `src/KspFlightLab.Core` | No UI: the tower (`Tower/`), Wingman as a tool and its agents (`Wingman/`), installing (`Install/`: GitHub releases, verified downloads, KSP folders), the settings, the session logs, the one `HttpClient`. |
| `src/KspFlightLab` | The Avalonia 12 app: `MainWindow` (the rail and the shared tower and agents) and one class per page; every control is built in code, styled by `App.axaml` (the same styles as Mission Control). |
| `tests/KspFlightLab.Core.Tests` | xUnit 2: the tower, the knowledge, Wingman's install and agents, the settings and sessions, fuzz tests. |
| `tests/KspFlightLab.Tests` | Headless Avalonia UI tests (xUnit v3): every page driven like a user. |

.NET 10, Avalonia 12.1.4, Windows and Linux (the Linux package comes later; the code already uses `KSP.x86_64`,
`ProgramName` without `.exe`, and `~/.config` through `ApplicationData`).

## 1.3 Where things are kept

| What | Where |
|---|---|
| Settings | `%APPDATA%\KspFlightLab\settings.json` (Linux: `~/.config/KspFlightLab/`), or `--settings <file>` |
| Logs: the tower's channel, each agent's sessions | `logs/` next to the settings |
| Errors (anything unexpected, 5 MB at most) | `logs/errors.log` next to the settings |
| KSP Wingman's installs | `%LOCALAPPDATA%\KspFlightLab\Tools\KspWingman\<version>\` (with `--settings`: `Tools/` next to that file) |
| The agents' knowledge | `<server>/LunaEvo/agents/knowledge.json`, or `agents/knowledge.json` next to the settings |

## 1.4 Headers

Every source file starts with a header pointing back here:

```csharp
// Flight Lab · src/KspFlightLab/MapPage.cs
// [UI] The map the agents explored (KSP Wingman's explore): every place around KSC on a grid...
// Used by: MainWindow (rail: Knowledge → Explored map)
// Guide:   Meat and Potatoes/03-Knowledge.md
```

`[Core]`, `[UI]` or `[Test]`, what it is, who uses it, and its chapter. Keep them true when you move things.

## 1.5 Build and test

```
dotnet build KspFlightLab.slnx
dotnet test KspFlightLab.slnx
dotnet run --project src/KspFlightLab -- --settings D:\somewhere\settings.json
.\publish.ps1     # dist/KspFlightLab-<version>-win-x64/KspFlightLab.exe
.\release.ps1     # a GitHub release (chapter 5)
```

GitHub Actions (`.github/workflows/tests.yml`) runs every test on Windows for each push.

Tests never touch the network (`LabHttp.Client` is a `FakeHttp`), the real tools folder, or port 8920 (the tower
starts on a free port). Their folders go in `TEMP/FlightLabTests` and are cleaned up (`TestFolders`). Every new parser of
outside text goes in `FuzzTests`.
