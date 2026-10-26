# 6. The app's UI

The same look as LUNA EVO Mission Control (modelled on NOMM): Material 3 colors from one accent, dark by default,
JetBrains Mono, a left rail with section titles and pill selection, and pages of rounded groups of rows (name, muted
description, control on the right). `App.axaml`, `ThemeManager.cs` and `Ui.cs` came from Mission Control; keep new UI
on their classes (`headline`, `sublabel`, `label`, `groupTitle`, `fab`, `text`, `nav`, `row`, `swatch`, `statusDot`).

## 6.1 The rail

| Section | Page | Class |
|---|---|---|
| Fly | Agents | `AgentsPage` (chapter 4) |
| | KSC tower | `TowerPage` (chapter 2) |
| Knowledge | Skills, Planes, Explored map, Lessons | `SkillsPage`, `PlanesPage`, `MapPage`, `LessonsPage` (chapter 3) |
| Records | Sessions | `SessionsPage` (chapter 4) |
| Setup | Install, Settings | `SetupPage`, `SettingsPage` (chapter 5) |

The footer: the tower's dot and port, then Flight Lab's version, Wingman's, the agents running and the server's folder
name. Flight Lab opens on Install while Wingman isn't installed, on Agents after.

## 6.2 How pages work

- Each page is a class with a `View` (built once) and a `Refresh()` (rebuilds what changes). `MainWindow.AddNav`
  registers it under a key (`MainWindow.AgentsKey`...).
- **Refreshing**: the tower and the agents call `MainWindow.Changed()` (posted to the UI thread) as things happen; a
  2 s timer refreshes the page on screen if anything changed (`RefreshIfChanged`). Pages never poll on their own,
  except the agents' status (every 15 s, the status tool, no model).
- **The tower and the agents are shared**: `MainWindow.Tower` and `MainWindow.Agents` live as long as the window; every
  page reads them. Closing the window asks the agents to quit and stops the tower.
- **Knowledge** is read through `MainWindow.ReadKnowledge`: live from the running tower, or from its file with the
  tower off.
- Controls carry AutomationIds (`nav/<key>`, `agents/agent/<call sign>/order`, `planes/<craft>/limits`,
  `settings/server`...): the UI tests find everything by them.
- `Ui.Open` only opens web links and existing folders (anything else could start a program).

## 6.3 Tested

`tests/KspFlightLab.Tests`: headless Avalonia (xUnit v3). `UiHarness` opens the real window over throwaway
folders with a fake HTTP client and the tower off; `Pump()` runs the dispatcher and the 2 s refresh (headless timers
don't tick); `Find<T>(id)`, `Click`, `Type` (text and Enter), `Description(id)` (a row's sublabel), `WaitFor` and
`Await` (never block on `.GetResult()` in a UI test). `FakeAgent` stands in for a Wingman console.
`PageTests.Every_page_opens_and_the_footer_says_what_runs` opens every page; new pages go in its list.
