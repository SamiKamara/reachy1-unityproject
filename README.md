# Reachy Controller and Simulator

Unity `2021.3.24f1` project for Reachy 1 that combines the simulator package, an in-Play-Mode runtime control app, and an optional local voice-agent sidecar.

## What is in this repository

- `Packages/ReachySimulator`: Reachy simulator package and gRPC-facing robot services
- `Assets/ReachyControlApp`: runtime UI for simulation and real robot control
- `Assets/Scenes/SampleScene.unity` and `Assets/Scenes/OfficeScene.unity`: included scenes
- `Build/Reachy controller & simulator.exe`: current Windows build output

The runtime control UI is auto-created in Play Mode by `ReachyControlBootstrap`, so no manual scene wiring is required.

## Current features

- Simulation and Real Robot connection modes
- Auto-connect on Play, health watchdog, auto-reconnect, fallback host/port retries, and optional restart-signal recovery for real robots
- Optional DNS resolution and TCP reachability prechecks before real-robot gRPC connects
- Left/right eye camera preview over the Reachy camera service
- Preset poses: `Neutral Arms`, `T-Pose`, `Tray Holding`, `Hello Pose A`, `Hello Pose B`, `Hello Pose C`, `Hello Pose D`
- Looping animations and acted sequences, including `Speech A`, `Reachy introduction`, and `bender sleep`
- Manual single-joint commands and full-pose commands at runtime
- Xbox controller support for base driving, arm/head/gripper control, camera eye switching, and triggering acted sequences
- Mobile-base velocity commands when the connected endpoint exposes mobility services
- Window controls in the runtime UI: `Windowed`, `Fullscreen`, and `Exit`
- Optional runtime file logging

## Local AI / Voice features

The `ReachyControlApp` now includes a local AI panel backed by `Assets/ReachyControlApp/LocalVoiceAgent`.

- Local sidecar endpoints for `/intent`, `/speak`, `/help`, and `/listening`
- Transcript parsing for `help`, `status`, `connect_robot`, `disconnect_robot`, `set_pose`, `move_joint`, `stop_motion`, `confirm_pending`, `reject_pending`, `show_movement`, `hello`, and `who_are_you`
- Safety controls for confirmation flow, duplicate suppression, transcript gating, safe numeric parsing, and joint range rejection
- Push-to-talk or always-listening modes
- Microphone selection plus hold-to-record mic test playback
- Local TTS feedback with optional mirroring to a small robot-speaker HTTP service on the Reachy computer, with runtime probing/logging and a `8099` TTS-only fallback if a voice sidecar is running on the robot
- Local help responses through a rule-based backend or optional `llama_cpp`
- UI actions to load/save Unity voice config and sync/load the sidecar config
- Optional sidecar auto-start, auto-stop, health probing, and bridge diagnostics

Default local model/config paths point outside `Assets/` under `.local_voice_models/` so Unity does not try to import model files.

## Quick start

1. Open the project in Unity `2021.3.24f1`.
2. Open `Assets/Scenes/SampleScene.unity` or `Assets/Scenes/OfficeScene.unity`.
3. Press Play. The runtime UI appears automatically.
4. Use the `Connections` view to connect to Simulation or Real Robot mode.
5. Use `Animations & Poses`, `Manual Control`, and `AI` for runtime operation.

## Default endpoints

- Simulation joint service: `localhost:50055`
- Real robot joint service: `192.168.1.109:50055`
- Real robot fallback ports: `3972`
- Real robot restart-signal port: `50059`
- Camera service: `50057`
- Local voice sidecar base URL: `http://127.0.0.1:8099`
- Robot speaker mirror port: `8101` (custom helper service, not a stock Reachy 2021 port)
- Remote helper SSH defaults for stock robots: user `reachy`, password `reachy` (editable in UI/helper script)

## Notes

- The `Teleoperation` top-bar view exists, but it is still a placeholder and does not yet expose a dedicated runtime workflow.
- Real-hardware validation is still required. Motion commands, acted sequences, and voice-confirmed actions can move physical hardware.
- For detailed app-specific setup, see:
  - `Assets/ReachyControlApp/README.md`
  - `Assets/ReachyControlApp/LocalVoiceAgent/README.md`
  - `Assets/ReachyControlApp/LOCAL_VOICE_AGENT_CONSTRUCTION_GUIDE.md`

## License

Creative Commons BY-NC-SA 4.0:

- https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode
