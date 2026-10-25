# 2. The KSC tower

A traffic controller for AI pilots, the owner's idea: [KSP Wingman](https://github.com/NEON-NULL-NARWHAL/ksp-wingman)
agents ask it for an approach, it asks their intentions, clears one plane at a time for a published RNAV approach to
KSC's runway 09 with the whole route, holds the next, and tells a landed plane to vacate. It lives in Flight Lab (rail:
Fly → KSC tower), not in KSP: the conversation is on its own channel, not LMP's chat. It was born in LUNA EVO Mission
Control (2.12-2.18, its chapter 28) and moved here unchanged in Flight Lab 0.1; the rules and the protocol are the same,
so every Wingman since 0.6 talks to either.

## 2.1 The pieces

| File | What |
|---|---|
| `Core/Tower/Tower.cs` | The rules (pure: no network, no clock): planes, their states, the channel, KSC's runway frame and approaches (`Ksc`). |
| `Core/Tower/TowerServer.cs` | The channel over HTTP on **localhost only** (`http://localhost:8920/`, Settings → KSC tower → Port): `POST /v1/report` and `POST /v1/say`, and the knowledge endpoints (chapter 3). `Answer()` is the whole request handling, tested without a socket. |
| `TowerPage.cs` | Start/Stop, the planes (state, number, approach, height, speed, departures, cleared to land), the channel (newest last, 200 lines), the radio checkbox, and the channel log's folder. |
| `MainWindow.cs` | Starts it with Flight Lab (Settings → "Start with Flight Lab", on by default) or from `--tower [port]`; stops it on close. The footer shows it. |

Wingman's side is `TowerClient.cs` in KSP Wingman: with `WINGMAN_TOWER_URL` set (localhost only), it reports every 3 s,
answers "say intentions" from what its pilot is doing, and flies what it's told (a route, a hold, vacating).

## 2.2 The protocol

Both endpoints take a small JSON object and answer `{"messages": [...]}`: the tower's messages for that plane that it
hasn't had yet. A message has `id`, `time`, `from`, `to`, `kind`, `text`, and for some kinds a `route` (`name`,
`runway`, `fixes`: `name`, `latitude`, `longitude`, `altitude`, `speed`) or a `hold` (`fix`, `latitude`, `longitude`,
`altitude`, `radius`, `number`).

| Request | Fields |
|---|---|
| `/v1/report` | `callSign`, `latitude`, `longitude`, `altitude`, `heading`, `speed`, `situation` (KSP's: Flying, Landed...), `phase` |
| `/v1/say` | `callSign`, `kind` (`intent`, `request-approach`), `text` |

Kinds the tower sends: `ask-intent`, `info`, `clearance` (with the route), `hold` (with the hold), `cleared-to-land`,
`vacate`.

The whole channel is also kept as data: `logs/tower-<date>.jsonl` next to Flight Lab's settings file, one message per
line in the same JSON as above (`TowerServer.LogPath`; a failing disk loses the log, not the tower).

Checks, like every outside input (Mission Control's chapter 14): POST only, 16 KB at most, JSON object with a `callSign`, numbers must be
JSON numbers and finite, strings are cut (call sign 40, text 300) and control characters dropped; anything else is a
4xx answer, never an exception (`FuzzTests.Tower_channel_requests`). Localhost only: no LAN, no firewall prompt.

## 2.3 The rules

- A new call sign gets "say intentions". An intent with "land" in it, or a `request-approach`, asks for the runway;
  anything else is a local flight ("roger, report when you want to land").
- **One plane on the runway at a time.** The first to ask is cleared (`Ksc.Pick`: far out west and facing the runway,
  straight in; otherwise its side's pattern, north or south). Anyone asking while a plane is on approach or still on
  the runway holds: its side's hold point (4 km past touchdown, 7 km out, a 2 km circle), stacked 300 m apart from
  1,200 m.
- "Cleared to land" (2.14) when the plane calls "runway in sight" (Wingman 0.8: on final within 10 km and 300 m of the
  centerline), once, while nobody is on the runway (otherwise "continue approach, traffic on the runway"); a plane
  that never calls is cleared 2.5 km out (`Tower.SightFallback`). Stopped on the runway: "vacate".
  Wingman (0.6 and later) taxis off to the right and parks 90 m beside the runway. Once a landed plane is more than
  60 m off the centerline (`Ksc.VacatedCross`), or its report isn't Landed any more (recovered, back at the Space
  Center), the next holding plane is cleared from where it is.
- **Which approach** (2.13): alone (nobody holding, on approach or on the runway), a plane gets the one it asked for
  ("request RNAV 09 NORTH", "request visual"), or, within 12 km of the touchdown point, the **short visual**: the
  owner saw agents fly far out for an RNAV route. It's the RNAV pattern made smaller: KSC-V1 abeam the touchdown point
  3 km to the side, KSC-V2 8 km back, a 90° turn to KSC-V3 on the centerline, the final from F09V 5.5 km out (about 19
  km instead of 25). With a 4.5 km final (2.13.0) it swung across the centerline at the threshold and stopped 16-23 m
  off; at 5.5 km, 10-11 m (2.13.1). A first version (diagonal base into a 4 km final) overshot the centerline by 2 km three times in
  the rig: at 90 m/s the 30° turn is 1.4 km wide. With traffic, it's the published route for its side.
- A plane silent for 60 s is lost from the channel and gives up its place.

## 2.3a Holds over the runway, directs, and the agents' own flight plans (2.15)

- **The overhead hold** (the owner's idea): a holding plane circles KSC-OH over the middle of the runway (2 km circle),
  stacked 300 m apart from 1,500 m, above the short visual's downwind (650 m) and the RNAV patterns (900 m). When the
  runway frees it's right there (usually alone then, so the short visual).
- **Directs**: west of the runway, roughly facing it (within 90°), and able to join the final at 45° or less, a plane
  is cleared "DIRECT F09" (from beyond 10.5 km) or "DIRECT F09V" instead of a whole route (`Ksc.Direct`).
- **The table** (`Ksc.Table`, `POST /v1/procedures`): every fix (the RNAV and visual fixes, KSC-W12, the departure
  fixes KSC-DN/KSC-DS 5 km past the runway and 4 km to each side, the hold KSC-OH), the legs allowed between them (each
  approach's own order, departures and the hold into the patterns), and the final fixes F09 and F09V.
- **Filed plans** (`POST /v1/plan` with fix names): checked (known fixes, published legs, a final fix last, the first
  within 25 km, 12 at most); cleared as filed when the runway is free, held for traffic and flown when its turn comes
  (if it still checks out from there), "unable: why" otherwise. The plan is used once.

- **Go-arounds** (2.16, the owner's idea): an agent going around says `go-around`; with others holding, the next
  one is cleared first and the go-around joins the back of the overhead hold; alone, it's cleared again at once for
  the short visual. Holds and clearances carry **traffic information** (each other plane: on approach, holding over the
  runway, or on the runway, and its altitude).

## 2.3a2 Leaving, fast planes (2.17)

- A plane reporting `SpaceCenter`, `Destroyed`, `Splashed` or `Offline` (no LMP server) leaves the sequence at once
  (`Leave`: those holding behind it move up, the next is cleared); so does a `leaving` say, which Flight Lab
  sends itself when an agent's process ends (`AgentHost.AgentExited` → `TowerServer.Leaving`). It's asked its
  intentions again once it has a plane in the air.
- Reports may carry `approachSpeed` (the plane's own, from its flight test). Over `FastApproach` (105 m/s) the short
  visual isn't offered, even when asked for or after a go-around: its turns are too wide for it.
- **Departures**: a `departure` say gets `cleared-departure` (the runway is the plane's for `DepartureTime`, 2 minutes,
  or until it reports flying 100 m up, crashed, splashed or offline: not "SpaceCenter", it asks from there before its
  launch) or `hold-departure` (another plane is departing, or one is cleared to land). Wingman 0.12 asks before every
  runway launch and waits.
- After a go-around, a holding plane further than `NearRunway` (15 km) doesn't take the turn: the one that went
  around is cleared again first. A go-around repeated within `GoAroundRepeat` (30 s) gets no new answer.

## 2.3b The radio (talking on LMP chat)

"Talk on LMP chat" on the page or in Settings (or `--tower-chat`), with a server folder chosen: every channel message is also appended to
`<server>/LunaEvo/radio.jsonl`, and the LUNA EVO server plugin (Mission Control 1.2; Mission Control installs it) says each new line in the server's chat, so
players hear the traffic. Always as "KSC tower" (an agent's line reads "RigPilot → tower: request visual"): anything
on this PC can talk on the channel (audit 13, T4), so it can never speak as a player. One line a second, 200
characters, control characters dropped, what was in the file before the server started isn't said, the file stops at
1 MB. In the game, the LUNA EVO client patches (1.6) show the tower's chat lines on a strip at the top of the screen
for 45 s, the last three (the owner's radio bar; a toggle in the LUNA EVO window).

## 2.4 Tested

- Core: `TowerTests` (the whole two-plane sequence with a timeline of reports, taxiing off, a silent plane, the frame,
  the HTTP answers) and the fuzz test. UI: `PageTests.The_tower_page_shows_two_agents_one_cleared_and_one_holding` (two agents through `Answer`, the rows and channel lines, the footer, Stop). The radio file: `LabTests`.
- In the rig (2026-10-23, two KSPs and two Wingman agents, SupportTools' RigRun, then from Mission Control): RigPilot cleared RNAV 09 SOUTH, RigViewer held as
  number 2 at KSC-HS for six minutes, RigPilot cleared to land, landed and vacated, RigViewer cleared the same second.

## 2.5 Next

Runway 27 and other airports, a live map of the traffic on the tower page, and a tower an AI can speak for (it's plain
rules today).
