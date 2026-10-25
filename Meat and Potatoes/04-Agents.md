# 4. Agents

[KSP Wingman](https://github.com/NEON-NULL-NARWHAL/ksp-wingman) agents (a local model flies a plane through kRPC),
started and watched from **Fly → Agents**, their days told on **Records → Sessions**. From Mission Control's Wingman page
(its chapter 29, 2.12-2.18), moved here in Flight Lab 0.1 with the same console protocol. Installing Wingman, its mod
and a model is chapter 5.

## 4.1 The pieces

| File | What |
|---|---|
| `Core/Wingman/AgentHost.cs` | Agents: KSP Wingman's console started with `--ipc`, one per kRPC port; `AgentEvent.Parse` reads its lines; `WingmanAgent` keeps the phase, status, last flight, the training running and the last 500 events; orders and quit go in as JSON lines. Each agent's lines are recorded to `logs/wingman-<call sign>-<date>.jsonl`. `IAgentProcess` is faked in tests. |
| `AgentsPage.cs` | Start an agent (call sign, kRPC port, pilot model, model server, planner, use the tower; the models default from Settings), and a card per agent (`AgentCard`): phase, kRPC port, model, last flight, the plane's status, its events, an order box, Free flight, the Train menu (switches while training), Status, Stop/Remove. |
| `Core/SessionLog.cs` | Reads the recorded sessions: one per agent and day, its story (flights, tests, free flight and training lines, debriefs, lessons, places, errors; not status lines or tool calls), 2,000 lines and 25 MB at most per file. |
| `SessionsPage.cs` | Pick an agent and day (newest first): flights landed and crashed, tests, debriefs, places explored, and the story (the last 400 lines). |
| `Program.cs` | `--agent CallSign:port[:model]` (or `--wingman-agent`; any number) starts agents at startup; `--planner` (`--wingman-planner`), `--free minutes` (`--wingman-free`), `--train a,b` (`--wingman-train`: each agent in order trains that). The `--wingman-*` spellings are Mission Control's, so the rig's scripts work with either. |

## 4.2 The console's --ipc mode (KSP Wingman 0.6)

One JSON object a line each way, UTF-8:

- **In** (Flight Lab → console): `{"type":"order","text":"land at KSC","from":"Commander"}`, `{"type":"status"}`,
  `{"type":"forget"}`, `{"type":"quit"}`. Plain text is an order. Orders are cut to 1,000 characters.
- **Out**: `{"type":"event","time":...,"kind":...,"text":...,"data":...}`. Kinds: `wingman` (the console's own
  lines), `order`, `chat`, `call` and `result` (tool calls), `answer` (the model), `status` (the answer to a status
  request, kept apart from tool results), `phase` (`data.from`, `data.to`), `flight` (an outcome: `data.result`
  landed/crashed/splashed/..., `minutes`, `goArounds`, `approach`, `landing` with `downRunway`, `offCenterline`,
  `headingError`), `tower` (the tower's messages, `data.kind`), `error`.

`phase`, `flight` and `tower` come from Wingman's MCP server (its `events` tool, which the console polls every 2 s);
they're the same events whether a model is talking or not.

Anything the console prints that isn't such a line (a .NET crash) is shown as an `error` line, cut to 4,000
characters; lines over 64 KB are dropped (`FuzzTests.Wingman_agent_lines`).

## 4.2b Free flight, the planner and the debrief (2.13, Wingman 0.7)

- **The planner** is a second model that thinks (Qwen3 4B with thinking, through Ollama's own `/api/chat`: its OpenAI
  endpoint ignores thinking switches). It picks free-flight goals and writes debriefs, never flies. The page's "Planner
  (thinks)" box (Settings → Models → Planner); empty = the pilot's model. Since Wingman 0.10, gemma4:e4b is the tested pilot and planner.
- **Free flight** (the card's button, 30 minutes; `{"type":"free"}`): the planner picks a goal from a numbered menu
  (land on RNAV 09 NORTH, SOUTH, STRAIGHT-IN, or the short visual) with its skill's numbers, preferring the weakest;
  code flies it (recover, a new plane, take off, `land` with that approach, which asks the tower for it). Code keeps it
  in a box (25 km of KSC, under 3,000 m: outside, sent back to land), stops after two crashes in a row or when a launch
  is refused, drops a goal that failed twice without a lesson, and any order or Stop free flight stops it.
- **The debrief**: after each recorded flight (the tower's knowledge event), the planner reads the record and writes
  one lesson; code refuses reports ("Landed 25 m off...": it must be advice, starting with a verb) and anything not
  for a skill the flight used, then `learn` saves it citing the flight (chapter 3).
- **Sessions are recorded**: every line an agent says goes to `logs/wingman-<call sign>-<date>.jsonl` next to the
  settings file (20 MB a day at most). Flight Lab reads them back on the Sessions page.
- **KSP's dialogs** that kRPC can't answer: the bridge (0.7) closes the Mission Summary after a recovery, and `launch`
  refuses with a reason when no kerbal is free (otherwise KSP stops on "No Control!" and the launch never returns;
  seen in the rig with every kerbal in a parked plane).

## 4.2c Training (2.15, Wingman 0.9)

The owner's "ask it to train": `/train <what> [minutes]`, `{"type":"train","focus":...}`, the card's Train menu, or in
plain words to the pilot (it has a `train` tool of the console's own). Free flight on one focus until it's learned (3
good flights in a row) or the time's up. Focuses: landings (RNAV and visual), fast (220 m/s legs, then land), high
(climb to 6,000 m in legs over KSC, the box's ceiling raised to 8,000 for that drill only, then land), planes (launch a
plane from the SPH it hasn't flown this session; one that won't take off is skipped), plans (the planner builds a
flight plan from the tower's table, code checks it with the tower's rules, files it, once more with the reason if it's
refused). Free flight without a focus offers all of them.

## 4.2d Switching training, the free-will drills (2.18, Wingman 0.12)

- `WingmanAgent.Training` is the training running, read from the console's "Free flight for N minutes: training
  <skills> until..." line (`TrainingOf`); the card shows it ("· training: explore"), the menu follows it, and picking
  another one while it trains switches at once (the owner found the menu did nothing mid-training). The console lands
  a plane in the air before a "new plane" training launches one.
- Two more trainings: **Riskier approaches** (`approaches`) and **Explore** (`explore`). Their side in Wingman:
  its Meat and Potatoes chapter 07 (Explore and fuel). What they find is on the Explored map (chapter 3).
- When an agent's process ends, `AgentHost.AgentExited` → `TowerServer.Leaving`: the tower takes it out of the sequence.

Wingman's own maintainer guide (the pilot, landing, tests, learning) is its
[Meat and Potatoes](https://github.com/NEON-NULL-NARWHAL/ksp-wingman/tree/main/Meat%20and%20Potatoes).

## 4.3 Rules

Installing (downloads, unzip, the mod) is chapter 5.

- **Agents**: one per kRPC port and per call sign, at most 8; call signs are 1-20 letters, digits, `-` or `_`; the model
  server must be an http(s) address. Started with an argument list (no shell), no window, from Wingman's own folder.
  The tower is passed as `WINGMAN_TOWER_URL` only when it's running (localhost).
- **Status** is asked every 15 s while Flight Lab is open (the status tool, no model), but never queued behind an
  unanswered one (the console answers between orders).
- **Closing Flight Lab** asks every agent to quit, waits 3 s, then kills what's left (with its MCP server). The planes keep
  their last orders (Wingman's autopilot stays on SAS), like quitting the console.

## 4.4 Tested

- Core: `WingmanTests` (an agent's events, orders, status and stop, one agent per KSP, sessions), `TowerTests`, the fuzz
  tests (`Wingman_agent_lines`, `Session_log_lines`) and `LabTests.Sessions_are_read_from_the_agents_logs_newest_first`.
- UI: `PageTests.Agents_show_their_flight_take_orders_and_train_and_the_knowledge_pages_follow` (fake consoles: the
  card, an order, training and switching mid-training, the Knowledge pages filling in from the tower, Stop leaving the
  tower's sequence, Remove) and `Sessions_tell_each_agents_day`.
- In the rig: Mission Control's results (its chapter 29, 2026-10-23 to 25: landings 1 m off the centerline, steep 4.5°
  and fast finals, explore with 12 places) are the bar Flight Lab's own rig runs have to match or beat.
- **Flight Lab's first rig runs (2026-10-25)**, the same flags as Mission Control's last (two agents on gemma4:e4b, the
  tower on chat, explore and approaches training): 7 landings, all 0-4 m off the centerline (four of them 0 m), the
  approaches training learned (steep, fast and slow finals), 18 places explored. Two crashes, both explore flying into
  mountains: KSP Wingman 0.12.1 fixed it (it checks the ground along the leg and 12 km ahead), checked in the rig (a
  leg raised to 1,839 m, a turn back from a 2,496 m ridge, landed 1 m off). Flight Lab itself: no errors, and closing
  it stopped both agents cleanly.
- Seen there, still open: some debrief lessons say nothing useful ("stop closer than 4 m" after a 4 m landing,
  "extend the flight past 5.8 minutes" after a crash), and a crashed explore flight's record carried the approach of
  the flight before it.

## 4.5 Next

A live map of each agent's plane, outcomes summed up across sessions (for model comparisons), the heartbeat page (the
plane with its hotspots, deferred: the owner's "heartbeat to next"), and the grid lab for testing plans.
