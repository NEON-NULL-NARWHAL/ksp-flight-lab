# KSP Flight Lab

**The app for AI pilots in Kerbal Space Program.** Flight Lab installs [KSP Wingman](https://github.com/NEON-NULL-NARWHAL/ksp-wingman),
starts its agents (a local model flies a plane in your KSP through kRPC), runs the KSC tower they talk to, and shows
everything they've learned: skills, plane cards, an explored map, lessons, and each agent's day.

A sibling of [KSP Wingman](https://github.com/NEON-NULL-NARWHAL/ksp-wingman) (the pilot) and [LUNA EVO Mission Control](https://github.com/NEON-NULL-NARWHAL/luna-evo-mission-control)
(the app for your [Luna Multiplayer](https://github.com/LunaMultiplayer/LunaMultiplayer) server). Mission Control
looks after the server; Flight Lab looks after the pilots.

> [!NOTE]
> **Version 0.1: everything the agents could do in Mission Control, in an app of its own.** The KSC tower, the agents,
> their knowledge and Wingman's install moved here from Mission Control 2.18, with pages that have room to show them.
> Next: matching (and beating) Mission Control's rig results with Flight Lab in charge, a bridge from Mission Control,
> Linux packages, and the heartbeat page (the agent's plane, live).

## What you need

- **KSP 1.12.5** for the agents (a copy, not the one you play), with **[kRPC 0.7.0](https://github.com/krpc/krpc/releases)**
  (CKAN installs it; Mission Control's CKAN page does it safely).
- **[Ollama](https://ollama.com)** (or any model server with the OpenAI chat API). Flight Lab downloads the model:
  gemma4:e4b is the tested pilot and planner.
- Optional: an **LMP server** folder, so the agents' knowledge lives with the server (the same file Mission Control
  kept), and the **LUNA EVO server plugin** (installed by Mission Control) so the tower talks on the server's chat.
- Windows (Linux packages are coming). Flight Lab, the agents, their KSPs and the models run on one PC; nothing listens
  on your network.

## Pages

| | Page | What it does |
|---|---|---|
| **Fly** | Agents | Start an agent per KSP (call sign, kRPC port, pilot and planner models). Each card shows its phase, last flight, plane status and events; give orders in plain words ("take off and fly east at 1000 m", "land at KSC", "go explore north"), start free flight, or pick a training (landings, fly fast, fly high, new planes, flight plans, riskier approaches, explore). Switch training at any time. |
| | KSC tower | The traffic controller: one plane on the runway at a time, published approaches and the short visual, holds over the runway, departures, cleared to land, go-arounds. The planes, the channel, and "Talk on LMP chat". |
| **Knowledge** | Skills | The skill tree: what's open, what each needs, everyone's numbers and each agent's, the best lessons. |
| | Planes | A card per plane: its flights, flight and ground tests, final approach limits (speeds, touchdown, steepest glide slope), what's still to learn, the agents' notes, its last finals. |
| | Explored map | Every place the agents explored around KSC, on a map with 10 km rings, colored by biome, with the ground's height. |
| | Lessons | What the agents learned, each from a real flight; delete any you don't like. |
| **Records** | Sessions | Each agent's day from its recorded log: flights landed and crashed, tests, debriefs, places, the story. |
| **Setup** | Install | KSP Wingman (from its GitHub releases, checked against GitHub's checksum), its mod in your KSP, kRPC, the model, the server's side. |
| | Settings | Server and KSP folders, models, the tower (port, start with Flight Lab, chat), colors and theme, where things are kept. |

The Knowledge pages work with the tower off too: they read its file.

## Getting started

1. Download `KspFlightLab.exe` from the [latest release](https://github.com/NEON-NULL-NARWHAL/ksp-flight-lab/releases/latest)
   (one file for Windows 10/11 x64, nothing to install; it isn't code-signed, so SmartScreen may ask the first time).
   Run it anywhere. It opens on **Install**.
2. **Settings**: choose the agents' KSP folder (and your LMP server folder, if you have one).
3. **Install**: KSP Wingman, then its mod in your KSP, then the model.
4. Start that KSP, start kRPC's server (its window: Start server), load a game or join your server.
5. **Agents**: Start the agent. Try "take off and fly a circuit", then "land at KSC", or Train → Landings.

## Updates

Flight Lab checks its GitHub releases when it starts (Settings → Updates → "Check when Flight Lab starts"),
and the footer says when a newer one is out. **Update and restart** downloads it, checks it against GitHub's
SHA-256, swaps it in for the running exe and restarts with the same options. KSP Wingman updates from Setup → Install.

## Command line

The same flags Mission Control's AI pilots took, so test scripts work with either:

| Flag | What |
|---|---|
| `--server <folder>`, `--ksp <folder>` | The server and KSP folders for this run |
| `--settings <file>` | Use this settings file (portable: logs and Wingman's installs go next to it) |
| `--tower [port]`, `--tower-chat` | Start the tower (and talk on LMP chat) |
| `--agent CallSign:port[:model]` | Start an agent (any number; also `--wingman-agent`) |
| `--planner <model>`, `--free <minutes>`, `--train a,b` | The planner, free flight, or a training per agent (also `--wingman-*`) |

## Where things are kept

Settings in `%APPDATA%\KspFlightLab\settings.json`, logs (the tower's channel, each agent's sessions, errors) in
`logs` next to it, Wingman's installs in `%LOCALAPPDATA%\KspFlightLab\Tools`. The agents' knowledge is
`<server>\LunaEvo\agents\knowledge.json`, or `agents\knowledge.json` next to the settings without a server.

## For developers

.NET 10 and Avalonia 12. `dotnet test KspFlightLab.slnx` runs the Core and headless UI tests (no network, no real
KSP). How it all works: [Meat and Potatoes](Meat%20and%20Potatoes/README.md).

## License

GPL-3.0, like KSP Wingman and Mission Control. JetBrains Mono is under the SIL Open Font License (`src/KspFlightLab/Assets/Fonts/OFL.txt`).
