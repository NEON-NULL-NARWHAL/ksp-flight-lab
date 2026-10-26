# Changelog

## Unreleased

- A logo of its own, "Flight path": a plane climbing away from the runway on a dashed track (the window, the exe and
  the taskbar; a thicker version at 32 px and below). Before, it borrowed Mission Control's.

## 0.1.0 (2026-10-25)

The first version: everything LUNA EVO Mission Control 2.18 did for AI pilots, in an app of its own.

- **Fly → Agents**: KSP Wingman agents, one per KSP, with their cards (phase, last flight, status, events, orders,
  free flight, training that switches mid-training). Models default from Settings (gemma4:e4b).
- **Fly → KSC tower**: Mission Control's tower, unchanged (approaches, the short visual, holds over the runway,
  departures, cleared to land, go-arounds, the radio). Starts with Flight Lab (Settings) or `--tower`.
- **Knowledge**: pages of their own for the skill tree, a card per plane (tests, final approach limits, what's left to
  learn, notes, last finals), the explored map, and the lessons. They read the tower's file when the tower is off.
- **Records → Sessions**: each agent's day from its recorded log.
- **Updates itself** from its GitHub releases (Settings → Updates): checked at startup, verified against GitHub's
  SHA-256, swapped in and restarted. One file, `KspFlightLab.exe`, nothing to install.
- **Setup → Install**: KSP Wingman (verified against GitHub's checksum), its mod, kRPC check, Ollama and the models,
  the server plugin check. **Setup → Settings**: folders, models, the tower, the look.
- The agents' knowledge stays in `<server>/LunaEvo/agents/knowledge.json`: the same file Mission Control kept.
- Mission Control's command-line flags work (`--wingman-agent`, `--wingman-planner`, `--wingman-free`,
  `--wingman-train`), plus short ones (`--agent`, `--planner`, `--free`, `--train`), `--server`, `--ksp`, `--settings`.
- The session log reader skips a line with a broken character instead of throwing (found by the new fuzz tests).
