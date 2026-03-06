# Local Voice Agent Construction Guide (Future Work)

## 1) Goal
Build a small, offline-capable voice agent that runs locally with the Unity app and can:
- recognize spoken commands,
- explain how to use the robot,
- trigger safe robot poses/commands from speech.

Primary target: light laptop (CPU-first, no cloud dependency).

## 2) Scope and Non-Goals
In scope:
- speech-to-text (STT),
- intent extraction for robot control,
- text-to-speech (TTS) responses,
- Unity integration and safety gating.

Out of scope for MVP:
- full conversational autonomy,
- fully natural-language free-form robot programming,
- cloud services required for core operation.

## 3) Recommended Architecture
Use a sidecar process (separate executable/service) and keep Unity focused on UI + robot control.

Flow:
1. Microphone audio -> local STT engine.
2. STT text -> intent parser (command grammar first).
3. Intent -> safety validator.
4. Valid command -> Unity bridge (localhost IPC).
5. Unity -> existing `ReachyGrpcClient` methods.
6. Agent response text -> local TTS -> speaker output.

Why sidecar:
- isolates AI/audio dependencies from Unity runtime,
- easier restarts/debugging,
- simpler profiling and model swapping.

## 4) Lightweight Stack Options
Option A (most lightweight, current project baseline):
- STT: `Vosk` (offline, low resource).
- Intent: deterministic parser/rules.
- TTS: `pyttsx3` (Windows path uses local SAPI subprocess for reliability).
- LLM: optional local `llama_cpp` for help text only.

Option B (better STT quality, still local):
- STT: `whisper.cpp` (`tiny` or `base` model).
- Intent: deterministic parser/rules.
- TTS: `Piper` or existing `pyttsx3` path.
- Optional small local LLM for help text only.

Guideline:
- start with Option A for lowest hardware risk,
- switch STT to Option B only if accuracy is insufficient.

## 5) Unity Integration Plan
Add bridge scripts:
- `Assets/ReachyControlApp/Scripts/VoiceAgentBridge.cs`
- `Assets/ReachyControlApp/Scripts/VoiceCommandRouter.cs`
- `Assets/ReachyControlApp/Scripts/VoiceAgentStatusPanel.cs`

Responsibilities:
- `VoiceAgentBridge`: IPC client (HTTP or WebSocket), heartbeats, reconnect.
- `VoiceCommandRouter`: map intent payloads to existing control actions (`SendPresetPose`, joint commands, status updates).
- `VoiceAgentStatusPanel`: show mic/listening state, transcript, parsed intent, safety/confirmation state.

Keep command execution on main thread in Unity.

## 6) IPC Contract (Suggested)
Transport:
- local loopback only (`127.0.0.1`),
- simple HTTP JSON for MVP (WebSocket optional later).

Example command payload from agent:
```json
{
  "type": "robot_command",
  "intent": "set_pose",
  "pose_name": "Neutral Arms",
  "confidence": 0.92,
  "requires_confirmation": true,
  "spoken_text": "set neutral arms pose"
}
```

Example Unity response:
```json
{
  "ok": true,
  "result": "Preset 'Neutral Arms' sent (16 joints).",
  "timestamp_utc": "2026-03-05T10:00:00Z"
}
```

## 7) Command Strategy (Important)
Use strict intent grammar first, not free-form generation.

Core intents:
- `help`
- `set_pose`
- `move_joint`
- `stop_motion`
- `connect_robot`
- `disconnect_robot`
- `status`
- `confirm_pending`
- `reject_pending`

Require confirmation for:
- any command that can move hardware,
- commands with low confidence,
- ambiguous pose/joint names.

Example confirmation phrase:
- "I heard: Hello Pose B. Should I execute it?"

## 8) Safety and Operational Rules
Mandatory safety rules:
- never execute motion commands when robot connection is unhealthy,
- require explicit confirmation before non-trivial movement,
- support immediate voice cancel (`"cancel"`, `"stop now"`),
- apply angle/range limits before sending joint commands,
- log all recognized commands and final executed actions.

Optional but recommended:
- push-to-talk mode for noisy environments,
- "simulation-only mode" switch for demos.

## 9) Performance Budget for Light Laptop
Targets:
- STT partial result latency: < 500 ms,
- final command parse latency: < 1.5 s,
- memory overhead of agent process: as low as possible,
- steady CPU usage low enough to keep Unity render/control responsive.

Practical defaults:
- mono 16 kHz audio input,
- short audio chunking (streaming),
- fixed-size model choice (`tiny`/small offline model),
- no heavy LLM in real-time command path.

## 10) Phased Build Plan
Phase 1: Bridge and fake agent
- Implement Unity IPC client.
- Send mocked intents from local test server.
- Verify command routing and safety prompts.

Phase 2: STT + intent parser
- Integrate STT in sidecar.
- Map transcript to strict intents/slots.
- Add confidence thresholding.

Phase 3: TTS feedback
- Add spoken confirmations/status.
- Tune pacing and interruption behavior.

Phase 4: Robustness
- Add reconnect/heartbeat.
- Add timeout handling, retries, and degraded modes.
- Add structured logs.

Phase 5 (optional): local LLM for help
- Keep LLM out of direct motion command execution path.
- Use only for user guidance text.

Status note:
- Phases 1-5 are implemented in current project scope (with iterative polish after Phase 5).
- Next recommended active phase is Phase 6 in section 16.

## 11) Suggested Config File
Create: `Assets/ReachyControlApp/voice_agent_config.json`

Include:
- selected STT backend,
- model path,
- confidence thresholds,
- confirmation policy,
- IPC address/port,
- push-to-talk enabled/disabled.

## 12) MVP Acceptance Checklist
- voice command can trigger at least 3 preset poses reliably,
- false positives do not execute motion without confirmation,
- disconnection state is handled gracefully (no silent failures),
- command round-trip (speech -> action -> spoken response) is stable for 30+ minutes,
- runs acceptably on target light laptop without cloud services.

## 13) Implementation Notes for This Project
- Reuse existing connection and pose APIs in `ReachyRuntimeControlUI` / `ReachyGrpcClient`.
- Keep automation behavior (auto-reconnect etc.) separate from voice logic.
- Voice layer should request actions; robot control layer remains source of truth.

## 14) Current Project Progress Snapshot (as of 2026-03-06)
Implemented in this repository:
- Unity-side phases (bridge/router/status panel, transcript parsing, TTS wiring, robustness controls, optional help-model UI path).
- Local sidecar reference implementation:
  - `Assets/ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py`
  - `Assets/ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json`
  - `Assets/ReachyControlApp/LocalVoiceAgent/README.md`
- Push-to-talk/listening-control wiring:
  - Unity bridge + UI support for `/listening`
  - Sidecar listening toggle endpoint integrated with STT runtime state
- Sidecar lifecycle wiring:
  - Unity can auto-start the local sidecar process when AI is enabled
  - AI bridge enable is gated until sidecar endpoint is reachable
  - Unity can auto-stop sidecar on AI disable (default true) and runs shutdown cleanup on play-stop/app-exit
- Safety/robustness polish:
  - Simulation-only voice mode toggle for blocking real-robot voice motion/connect actions
  - Duplicate command suppression window for STT burst protection
- Parser hardening:
  - Safe numeric parsing controls for `move_joint` extraction
  - Configurable joint degree range reject policy (Unity parser + sidecar parser)
- STT quality gating:
  - Minimum transcript chars/words thresholds to reduce short-noise false positives
  - Parser fallback routes unrecognized final transcripts to `help` (including short lone-word safety fallback)
  - Sidecar STT now ignores input while TTS is speaking, then flushes queued audio and resets recognizer on resume
- Confirmation UX:
  - Hands-free confirmation/rejection intents (`confirm_pending` / `reject_pending`) in Unity parser + sidecar parser
- Config persistence UX:
  - Local AI panel can now load and save `voice_agent_config.json` directly for tuning persistence
  - Local AI panel can sync shared parser/listening/help settings into `local_voice_agent_sidecar_config.json`
  - Local AI panel can load shared parser/listening/help settings from `local_voice_agent_sidecar_config.json`
  - Optional auto-sync-on-start toggle ensures sidecar picks up current shared UI config at startup
- Optional local LLM help path:
  - Sidecar `/help` supports both `rule_based` and `llama_cpp` backends with safe fallback
  - Unity panel exposes help backend/model tuning fields and syncs them to sidecar config
  - Unity startup now auto-loads `voice_agent_config.json` defaults/overrides at `Awake()`
  - Sidecar sync/load now converts help model paths between Unity-project-relative and sidecar-config-relative forms
  - Sidecar startup prioritizes bundled `.venv` Python when local `llama_cpp` help is enabled (to avoid missing module in system Python)
- Hands-free defaults:
  - Voice/sidecar defaults tuned so enabling Local AI can run spoken help flow directly (`vosk` + `pyttsx3` + local help enabled)
  - Vosk model storage moved outside Unity `Assets/` (`.local_voice_models`) to avoid Unity importer conflicts with model binary files
  - Current default help backend/path is set for local `llama_cpp` with a small GGUF model path under `.local_voice_models/llm/`

## 15) Remaining Gaps (Priority)
P0:
- Add automated regression tests for:
  - parser mapping/fallback (`help`, confirm/reject, joints/range),
  - bridge state transitions (healthy/degraded/timeouts),
  - sidecar lifecycle (auto-start, auto-stop, quit cleanup, stale process cleanup).
- Add explicit runtime diagnostics in Unity panel for help backend health:
  - model load success/failure reason,
  - active Python executable used to launch sidecar.

P1:
- Tighten local LLM safety and relevance:
  - stronger prompt constraints to Reachy-only guidance,
  - response post-filter (ban out-of-domain advice),
  - optional retrieval from local app-specific docs/intents.
- Add benchmark scripts for target laptop:
  - STT latency, help latency, CPU/RAM over 30+ minutes.

P2:
- Packaging/self-contained setup:
  - one-click bootstrap for sidecar `.venv` + optional dependencies,
  - model presence checks + guided download helper.
- Audio quality improvements:
  - optional noise suppression / AEC path,
  - optional wake-word mode if hands-free false positives remain.

## 16) Suggested Next Implementation Phase
Phase 6: Reliability + diagnostics hardening
- Build lightweight test harnesses for sidecar endpoints and parser behavior.
- Surface sidecar launch executable, help backend status, and model-load errors directly in Unity UI.
- Run and record a 30+ minute stability/performance soak on target hardware with current defaults.
