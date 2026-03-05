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
Option A (most lightweight):
- STT: `Vosk` (offline, low resource).
- Intent: deterministic parser/rules.
- TTS: `Piper`.
- LLM: none in MVP.

Option B (better STT quality, still local):
- STT: `whisper.cpp` (`tiny` or `base` model).
- Intent: deterministic parser/rules.
- TTS: `Piper`.
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

