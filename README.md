# Reachy 1 (2021) Controller and Simulator

⬇️ [Download](https://github.com/SamiKamara/reachy1-unityproject/releases) | 🖼️ [Gallery](#gallery) | ⭐ [Current Main Features](#current-main-features) | 🔮 [Future Features](#future-features)

<img width="700" height="400" alt="Reachy_and_UnitySim" src="https://github.com/user-attachments/assets/fe73c780-3d58-4d21-9f9d-991b2f57dcb6" />

## About

Research Authors: Sami Nikkanen, Mikaela Marie Punzalan, Faezeh Sayad Sijani, Donya Davoodi

Developers: Sami Nikkanen, Mikaela Marie Punzalan

This project was developed as part of the HTI.460 course, Social Robots: Design, Research and Interaction (March-May 2026). Reachy 1 (2021) by Pollen Robotics was the primary robot used in two workshops, serving as a platform to support and enhance young people's **robot and AI literacy**. The selected dimension within this literacy framework was **"envisioning robots in the future"**, a dimension introduced in 2025 by the course instructors, building on prior research work by [Ahtinen et al. (2025)](https://doi.org/10.1007/s12369-025-01277-8).

## Notes

- Any changes within the Unity project still require real-world robot validation for best practices.
- For detailed app-specific setup, see:
  - `Assets/ReachyControlApp/README.md`
  - `Assets/ReachyControlApp/LocalVoiceAgent/README.md`
  - `Assets/ReachyControlApp/LOCAL_VOICE_AGENT_CONSTRUCTION_GUIDE.md`
 
## Quick Start

1. Download the Unity Standalone App --OR-- open the project in Unity with version: `2021.3.24f1`.
3. Open `Assets/Scenes/SampleScene.unity` or `Assets/Scenes/OfficeScene.unity`.
4. Press Play. The runtime UI appears automatically.
5. Go to `Connections` view to connect to Simulation or Real Robot mode and check mic input settings.
6. Go to `AI` view to enable Local or Online AI (OpenAI API key with at least €5 is required for Online AI). If enabled successfully, Reachy should start saying something.
7. You are ready to go! It's recommended to start with these features if you are a beginner: `AI Modes`, `Animations & Poses`, `Chit Chat`.

## Default Endpoints

- Simulation joint service: `localhost:50055`
- Real robot joint service: `192.168.1.109:50055`
- Real robot fallback ports: `3972`
- Real robot restart-signal port: `50059`
- Camera service: `50057`
- Local voice sidecar base URL: `http://127.0.0.1:8099`
- Robot speaker mirror port: `8101` (custom helper service, not a stock Reachy 2021 port)
- Remote helper SSH defaults for stock robots: user `reachy`, password `reachy` (editable in UI/helper script)

## Main Files

- `Packages/ReachySimulator`: Reachy simulator package and gRPC-facing robot services
- `Assets/Scenes`: Sample Scene (mainly used) and Office Scene (containing random objects on the desk)
- `Assets/Scripts/ReachyControlApp`: Runtime UI for simulation and real robot control
- `Assets/StreamingAssets`: Many audio files to match certain animations.

The runtime control UI is auto-created in Play Mode by `ReachyControlBootstrap`, so no manual scene wiring is required.
The `ReachySimulator` gRPC/Protobuf Unity plugins are bundled under `Assets/Plugins`, so a clean GitHub clone should not
require running any separate `Install GRPC` step before the project compiles.

## Current Main Features

| Features by tab | Descriptions |
| ------------- | ------------- |
| General | Main dashboard for connecting to simulation or real robot mode, running automation, adjusting presets and joints, and viewing status plus the optional camera preview. |
| AI | AI input and parsing controls for selecting local or online AI, tuning transcript and motion-safety settings, managing microphone and TTS options, and checking bridge diagnostics. |
| AI Modes | AI persona and behavior mode configuration for Assistant, Fortune Teller, Emotion Reactions and Custom modes. It works for local or online AI. |
| Animation & Poses | Controls for pose speed, looping animations, preset poses, and acted sequences. |
| Animation Creator | Keyframe editor for custom animations, including live scene posing, draft and saved JSON animations, import or export, and optional mirroring to a connected real robot. |
| Chit Chat | Text chat view backed by the current AI pipeline, with topic prompts, conversation history, transcript save, and stop or clear controls. |
| Oz Mode | Speech-only operator mode that sends exact text to TTS, reuses saved lines, and loops the introduction animation while speaking. |
| Manual Control | Gamepad or keyboard-driven manual control with joint sliders, base driving, camera preview, and a live motion visualizer. |
| Teleoperation (VR) | ⚠️ Reserved tab with no dedicated workflow implemented yet. ⚠️ |
| Connections | Runtime connection and sidecar settings for robot host and port, microphone input, TTS mirroring, SSH helper setup, and recovery automation. |

## Gallery

More to come!

https://github.com/user-attachments/assets/da6c0bbd-f640-4b07-bc56-381bb12034a9

*Chit Chat*
<img width="700" height="400" alt="chit_chat_example" src="https://github.com/user-attachments/assets/32481418-37d2-4001-98e3-5dde95458d6b" />

*OZ Mode*
<img width="700" height="400" alt="oz_mode_example" src="https://github.com/user-attachments/assets/9c053166-5db7-4090-b358-bac653aacdb4" />

*Animation Creator*
<img width="700" height="400" alt="animation_creator_example" src="https://github.com/user-attachments/assets/3bb8ce72-a6da-4125-b92d-e17ae1e8f930" />

*Animations & Poses*
<img width="700" height="400" alt="animations_poses_example" src="https://github.com/user-attachments/assets/4838be80-3f42-4885-85da-f447a33c163b" />

## Future Features

More to come!

* The UX of the UI to make it more usable and human-centered
* Real-time animation and posing with MediaPipe and Yolo to allow body and gesture interaction
* Dance mode to make Reachy dance some cool moves
* Routing `Animation Creator` to `AI Modes` with whitelisting of animations
* 3D rotation XYZ control (gizmo / ball) to allow easier Live scene posing of Reachy's 3D avatar
* VR Teleoperation (possibly a Meta Quest headset)
* ⚠️ Testing for edge cases 👀

## License

Creative Commons BY-NC-SA 4.0:

- https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode
