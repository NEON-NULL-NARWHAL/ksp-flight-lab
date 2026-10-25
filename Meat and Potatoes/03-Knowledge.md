# 3. What the agents know (the knowledge store)

The owner's "tech tree", kept **on the LMP server** (their pick): KSP Wingman agents get skills graded from their real
flights and share short lessons, and every agent on the server reads them. No model changes: agents learn as checked
notes. It lives with the KSC tower (chapter 2) and has its own pages in Flight Lab: Knowledge → Skills, Planes, Explored map,
Lessons. From Mission Control's chapter 30 (2.12-2.18); the file and its format didn't change, so the knowledge the
agents built there is read here as it is.

## 3.1 The pieces

| File | What |
|---|---|
| `Core/Tower/Knowledge.cs` | The store (pure, plus Load/Save): the skill tree, `Grade` (a flight's phases and outcome → which skills it used, and how well), flights, lessons (`AddLesson` checks the evidence, merges the same lesson), `Of` (proficiency), `Unlocked`, `Expire`, `Crafts`, `FinalsOf`, places. |
| `Core/Tower/TowerServer.cs` | `POST /v1/flight`, `/v1/lesson`, `/v1/knowledge`, `/v1/test`, `/v1/craft`, `/v1/place` on the tower's channel (same checks as the rest: localhost, JSON, cut strings, finite numbers). Saves after each change to `KnowledgePath`. |
| `LabSettingsStore.KnowledgePath` | `<server>/LunaEvo/agents/knowledge.json` when a server folder is chosen (the same file Mission Control used), else `agents/knowledge.json` next to Flight Lab's settings. Read when the tower starts; changing the server folder restarts the tower on the new file (unless agents are flying). |
| `MainWindow.ReadKnowledge` | What the pages read: the running tower's knowledge (locked), or with the tower off, its file (cached by write time). `DeleteLesson` goes through the tower, or straight to the file. |
| `SkillsPage.cs` | The tree: each skill, open or 🔒 (with what it needs), everyone's numbers and each agent's, its top 3 lessons. |
| `PlanesPage.cs` | A card per plane: flights, the flight and ground tests, the final approach limits, what's still to learn (✓/○), the notes, the last finals. |
| `MapPage.cs` | The explored places around KSC (`ExploredMap`: rings every 10 km, north up, biome colors, dot size by visits, the highest ground labelled) and the list. |
| `LessonsPage.cs` | The lessons by skill, the notes about planes apart, with Delete. |

Wingman's side (0.7): the MCP server posts every finished flight (`TowerClient.RecordFlightAsync`, from the "outcome"
event, with the phases flown and the craft); the grades come back as a "knowledge" event. Tools `knowledge` (the menu)
and `learn` (a lesson citing a flight; hidden from the pilot model, used by the debrief). The console's debrief and
free flight are in KSP Wingman's guide (chapter 06) and chapter 4 here.

## 3.2 The tree and the grades

| Skill | Requires | A flight used it when | Good when |
|---|---|---|---|
| `takeoff` | | it went TakingOff → airborne | it got past the climb (or didn't crash) |
| `route` | takeoff | it flew a route (OnRoute) | it reached the final |
| `hold` | takeoff | it held (Holding) | it left the hold for the approach |
| `land-visual` | takeoff | it was on final, not on an RNAV approach | landed, no go-around, ≤ 15 m off the centerline |
| `land-rnav` | route, land-visual | on final from an RNAV approach | the same |
| `sequencing` | hold, land-rnav | it held, then landed on an RNAV approach | the same |
| `fly-fast` | takeoff | it flew over 180 m/s | over 200 m/s, and landed |
| `fly-high` | takeoff | it climbed over 4,000 m above the ground | over 5,000 m, and landed |
| `new-craft` | land-visual | it landed a plane it hadn't landed well before (not its first plane) | that landing was good |

Flight records carry `maxSpeed` and `maxHeight` (Wingman 0.9).

## 3.2b Planes (2.16)

`Knowledge.Crafts()`: a `CraftProfile` per plane flown (the owner's idea): flights, landed, crashed, good landings, the
best landing, the top speed and height reached, who flew it, its notes (lessons with `Craft` set), and `ToLearn`: take
off, land it, a good landing, its top speed (180 m/s reached), its ceiling (4,000 m), notes on how it handles, three
flights in a row without a crash, each with what's been found. `/v1/knowledge` returns them (`crafts`, with the items
still open), the `knowledge` tool shows them to agents, the Planes page shows them. A lesson with `aboutCraft: true`
is tied to the plane of the flights it cites (they must all be one plane), and only merges with that plane's notes.

A skill is **open** once everything it requires has 3 good flights by any agent on the server (shared). Proficiency
is good flights out of attempts, per agent and for everyone.

## 3.2c Flight tests (2.17)

KSP Wingman 0.11's `test_plane` measures a plane (ground: nose wheel °/s per full lock at 10 m/s, drift, braking; air:
pitch and roll °/s per full stick at 150 m/s, turn rate, slowest speed, slowing at idle, speeding up, cruise
throttle) and posts it to `/v1/test`: every number must be present and in range (ground ones optional), names cut to
60 characters, 200 tests kept (`CraftTest`, saved with the flights as `tests`). `/v1/craft` answers a plane's newest
test and its last five finals (`FinalNumbers`: centerline crossings, meters above the slope 1 km out, sent with each
flight as `finals`); Wingman works the pilot's setup out from them (its `Handling.cs`). The tower never computes a
setup: it keeps measurements only. A plane's profile has "A flight test (how it handles)" first on its list.

## 3.2d Riskier finals, explore, long flights, the map (2.18)

- Three skills: **approaches** (an `OnFinal` flight with a `style`, landed well), **explore** (`explored` places: good at
  3 and landed), **long-flight** (20 minutes or more: good when landed with `fuelLeft` ≥ 0.25). `FlightExtras` keeps
  the style, fuel used and left, and places explored with the flight (all optional; fractions 0-1, style cut to 40).
- `/v1/test` takes the plane's final approach limits (`finalMin`, `finalMax`, `touchdown`, `maxSlope`: all four, in
  range and in order, or none); `CraftTest.Describe` shows them. A plane's profile adds its endurance (minutes on full
  tanks, from its flights' fuel use) and "a final of its own choosing".
- `/v1/place` (latitude, longitude, ground, biome: letters, digits, spaces and dashes, 40 characters): `AddPlace`
  merges places within 1 km (a visit more), keeps 1,000. `/v1/knowledge` answers `places`: count, biomes, the highest
  ground, the last eight. The Explored map page draws them.

## 3.3 Lessons

- A lesson is 10-200 characters for one skill and **must cite a flight by the same agent that used that skill**, or
  it's refused (422 with why). So a lesson is always tied to something that happened.
- The same lesson again (word overlap ≥ 0.6) is merged: its evidence grows and it counts a confirmation. Paraphrases
  under 0.6 stay separate, on purpose: a wrong merge is worse than a near-duplicate (the host can delete).
- Unconfirmed lessons expire after 14 days. At most 20 per skill and 300 in all (the weakest, oldest goes).
- Agents read them as **facts, not orders** (the knowledge tool and the prompts say so). They're shown as plain text.

## 3.4 Limits

2,000 flights, 300 lessons, a 20 MB file; an unreadable file is kept as `.bad` and the store starts empty. Phases are
letters only (40 at most), numbers must be finite, strings are cut. Fuzzed with the rest of the channel
(`FuzzTests.Tower_channel_requests`).

## 3.5 Tested

`KnowledgeTests` (grading, evidence and merging, expiry, unlocking, save and load, the endpoints, the short visual and
named requests), `PageTests` (every Knowledge page, live and from the file, Delete with the tower on and off, the path on a server), and the rig (chapter 4).
