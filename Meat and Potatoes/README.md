# 🥩🥔 Meat and Potatoes

**The maintainer's guide to KSP Flight Lab.** The main [README](../README.md) says *what* Flight Lab does; this
folder says *how*, *where* and *why*. It's a sibling of
[Mission Control's](https://github.com/NEON-NULL-NARWHAL/luna-evo-mission-control/tree/main/Meat%20and%20Potatoes) and
[KSP Wingman's](https://github.com/NEON-NULL-NARWHAL/ksp-wingman/tree/main/Meat%20and%20Potatoes) guides: Wingman's
covers the pilot itself (flying, landing, the console, learning); this one covers the app around it.

## 🗺️ Chapters

| # | Chapter | Read it when you want to... |
|---|---|---|
| 01 | [Architecture](01-Architecture.md) | see the big picture: what talks to what, the projects, where things are kept, build and test |
| 02 | [The KSC tower](02-Tower.md) | change the tower's rules, its channel's protocol, departures, holds, the radio |
| 03 | [What the agents know](03-Knowledge.md) | change the skill tree, lessons, plane cards, flight tests and limits, the explored map |
| 04 | [Agents](04-Agents.md) | change how agents are started and watched, the console's `--ipc` lines, free flight and training, sessions |
| 05 | [Install and settings](05-Install-And-Settings.md) | change how Wingman, its mod and models are installed, or Flight Lab's settings |
| 06 | [The app's UI](06-App-UI.md) | add a page, change the rail or the look, write UI tests |

## 📁 File map

Symbols: 🗼 the tower · 🧠 knowledge · 🧑‍✈️ agents · 📦 install and settings · 🖥️ app shell · 🧪 tests

| | File | What it is | Chapter |
|---|---|---|---|
| 🗼 | `src/KspFlightLab.Core/Tower/Tower.cs` | The tower's rules (pure): planes, states, the channel, KSC's runway frame, approaches, holds, departures | [02](02-Tower.md) |
| 🗼 | `src/KspFlightLab.Core/Tower/TowerServer.cs` | The channel over HTTP on localhost: requests checked, answered, logged, the radio file | [02](02-Tower.md) |
| 🗼 | `src/KspFlightLab/TowerPage.cs` | Fly → KSC tower: start/stop, planes, the channel, the radio | [02](02-Tower.md) |
| 🧠 | `src/KspFlightLab.Core/Tower/Knowledge.cs` | Skills, grades, flights, lessons, plane profiles, tests and limits, places | [03](03-Knowledge.md) |
| 🧠 | `src/KspFlightLab/SkillsPage.cs` | Knowledge → Skills: the tree, open or locked, everyone's numbers, top lessons | [03](03-Knowledge.md) |
| 🧠 | `src/KspFlightLab/PlanesPage.cs` | Knowledge → Planes: a card per plane | [03](03-Knowledge.md) |
| 🧠 | `src/KspFlightLab/MapPage.cs` | Knowledge → Explored map: `ExploredMap` and the places list | [03](03-Knowledge.md) |
| 🧠 | `src/KspFlightLab/LessonsPage.cs` | Knowledge → Lessons, with Delete | [03](03-Knowledge.md) |
| 🧑‍✈️ | `src/KspFlightLab.Core/Wingman/AgentHost.cs` | Agents' processes, their `--ipc` lines, `WingmanAgent`, session recording | [04](04-Agents.md) |
| 🧑‍✈️ | `src/KspFlightLab.Core/SessionLog.cs` | Reads the recorded sessions back as stories | [04](04-Agents.md) |
| 🧑‍✈️ | `src/KspFlightLab/AgentsPage.cs` | Fly → Agents: start agents, a card each (`AgentCard`) | [04](04-Agents.md) |
| 🧑‍✈️ | `src/KspFlightLab/SessionsPage.cs` | Records → Sessions | [04](04-Agents.md) |
| 📦 | `src/KspFlightLab.Core/Wingman/WingmanTools.cs` | Wingman's release, unzip, its mod, kRPC, Ollama | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab.Core/Install/GitHubReleases.cs` | GitHub releases read defensively, digests, versions | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab.Core/Install/VerifiedDownload.cs` | Downloads checked: GitHub hosts, size, SHA-256 | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab.Core/Install/KspFolder.cs` | Is it a KSP, is it running | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab.Core/LabSettings.cs` | The settings record and store, knowledge and radio paths | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab.Core/Install/AppUpdater.cs` | Flight Lab updating itself: check, verified download, the exe swap | [05](05-Install-And-Settings.md) |
| 📦 | `publish.ps1`, `release.ps1` | The single-file exe; a GitHub release with it | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab.Core/LabHttp.cs` | The one `HttpClient`, and `ErrorLog` | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab/SetupPage.cs` | Setup → Install | [05](05-Install-And-Settings.md) |
| 📦 | `src/KspFlightLab/SettingsPage.cs` | Setup → Settings | [05](05-Install-And-Settings.md) |
| 🖥️ | `src/KspFlightLab/Program.cs` | Entry point, the command line (`StartupOptions`), error hooks | [06](06-App-UI.md) |
| 🖥️ | `src/KspFlightLab/MainWindow.cs` | The rail, the pages, the shared tower and agents, refreshing, knowledge reads | [06](06-App-UI.md) |
| 🖥️ | `src/KspFlightLab/App.axaml(.cs)` | Theme and styles; opens the window | [06](06-App-UI.md) |
| 🖥️ | `src/KspFlightLab/ThemeManager.cs` | One accent → the light and dark palettes, applied live | [06](06-App-UI.md) |
| 🖥️ | `src/KspFlightLab/Ui.cs` | Helpers: text, buttons, groups, rows, `WithId`, `Open` | [06](06-App-UI.md) |
| 🧪 | `tests/KspFlightLab.Core.Tests/TowerTests.cs` | The tower's sequences and channel | [02](02-Tower.md) |
| 🧪 | `tests/KspFlightLab.Core.Tests/KnowledgeTests.cs` | The knowledge store and its endpoints | [03](03-Knowledge.md) |
| 🧪 | `tests/KspFlightLab.Core.Tests/WingmanTests.cs` | Wingman's install, the bridge, Ollama, agents | [04](04-Agents.md) |
| 🧪 | `tests/KspFlightLab.Core.Tests/AppUpdaterTests.cs` | Updating itself: picking, verifying, swapping | [05](05-Install-And-Settings.md) |
| 🧪 | `tests/KspFlightLab.Core.Tests/LabTests.cs` | Settings, paths, sessions, KSP folders, the radio file | [05](05-Install-And-Settings.md) |
| 🧪 | `tests/KspFlightLab.Core.Tests/FuzzTests.cs` | Every outside-text parser, fuzzed | [01](01-Architecture.md) |
| 🧪 | `tests/KspFlightLab.Core.Tests/FakeHttp.cs`, `TestFolders.cs` | No network; throwaway folders, a fake KSP | [01](01-Architecture.md) |
| 🧪 | `tests/KspFlightLab.Tests/UiHarness.cs` | The headless window, `FakeAgent` | [06](06-App-UI.md) |
| 🧪 | `tests/KspFlightLab.Tests/PageTests.cs` | Every page, driven like a user | [06](06-App-UI.md) |
