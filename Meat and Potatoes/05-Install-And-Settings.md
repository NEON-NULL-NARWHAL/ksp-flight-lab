# 5. Install and settings

**Setup → Install** gets everything the agents need, in order; **Setup → Settings** keeps Flight Lab's own choices.
From Mission Control's "Tools (for AI pilots)" (its chapter 29), made a page of its own.

## 5.1 The pieces

| File | What |
|---|---|
| `Core/Install/GitHubReleases.cs` | A repo's releases read defensively (`ValueKind` checked, no throwing reads), `Digest` (`sha256:<64 hex>`), `ParseVersion`. |
| `Core/Install/VerifiedDownload.cs` | Downloads an asset: GitHub hosts over HTTPS only (after redirects too), the size GitHub gave (and a ceiling), and GitHub's SHA-256 required and checked (`DownloadVerificationException` otherwise). `SafeFileName`. |
| `Core/Install/KspFolder.cs` | A folder is a KSP when it has `KSP_x64.exe` (Windows) or `KSP.x86_64` (Linux) and `GameData`; `IsRunning` (mods only go in with KSP closed). |
| `Core/Wingman/WingmanTools.cs` | Wingman's newest release with a checksum (`LatestAsync`), the careful unzip into a staging folder (`Unpack`), `Installed`, its mod into a KSP (`Bridge`, `InstallBridge`), `HasKrpc`, Ollama's version and models (`OllamaAsync`), a model download (`PullAsync`, streamed). `DefaultModel` is gemma4:e4b. |
| `Core/LabSettings.cs` | `LabSettings` (a record: folders, models, model server, tower port, start with Flight Lab, talk on chat, accent, theme) and `LabSettingsStore` (load, save through a `.tmp`, `KnowledgePath`, `RadioPath`). |
| `SetupPage.cs` | Four rows: 1 KSP Wingman (Install / Check for updates), 2 Wingman in your KSP (the mod, kRPC), 3 the local model (Ollama: ready, or Download), 4 the server (the knowledge's place, the server plugin). |
| `SettingsPage.cs` | Folders (type and Enter, or Browse), models, the tower, appearance, and where everything is kept. |

## 5.2 Rules

- **Downloads**: only a release asset named `KspWingman-<version>.zip` with a GitHub SHA-256; hashed again before
  unpacking. Mission Control's security rules (its chapter 14) all apply: GitHub hosts only, size limits, no throwing
  JSON reads, scans skip reparse points.
- **Unzip**: only `KspWingman(.exe)`, `KspWingman.Mcp(.exe)`, `GameData/KspWingman/**`, README, LICENSE, CHANGELOG; at
  most 200 entries and 400 MB; nothing outside the folder; into a staging folder first, so a bad zip leaves nothing.
- **The mod** is copied into `GameData/KspWingman` only when the folder is a KSP and that KSP is closed.
- **kRPC** isn't installed by Flight Lab: the row says to use CKAN (Mission Control's CKAN page does it safely).
- **The server plugin** (the tower's radio) is installed by Mission Control (Map → server plugin); Flight Lab only
  checks `<server>/Plugins/LunaEvo.ServerPlugin.dll`.
- **Ollama** is checked when the page opens and again when that's over 15 s old (it may have been started after Flight
  Lab), or with "Check again". The models wanted are Settings' pilot and planner.

## 5.3 Settings

- A bad settings file is kept as `settings.json.bad` and Flight Lab starts on its defaults. Saves go through a `.tmp`.
- The command line wins for that run (`--server`, `--ksp`, `--tower [port]`, `--tower-chat`), without being saved.
- Changing the **server folder** moves the agents' knowledge: the tower restarts on the new file at once, unless agents
  are flying (then on its next start, and the page says so). A folder without `Server.exe` is saved with a note.
- A **KSP folder** must pass `IsKspFolder`. The model server must be an http(s) address; anything invalid keeps the old
  value and says so.
- The tower **port** is used the next time the tower starts. "Talk on LMP chat" needs a server folder.
- No secrets live in the settings (no passwords, no webhooks).

## 5.4 Tested

`WingmanTests` (verified install, refused unverified/tampered/escaping zips, the bridge, Ollama, a pull), `LabTests`
(settings round trip and a bad file, knowledge and radio paths, KSP folders), `FuzzTests.Settings_files` and
`GitHub_digests_and_versions`, and in the UI `Install_page_installs_wingman_its_mod_and_downloads_the_model` and
`Settings_change_the_folders_models_tower_and_look`.
