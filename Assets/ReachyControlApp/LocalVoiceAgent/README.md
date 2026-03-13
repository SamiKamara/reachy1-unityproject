# Local Voice Agent Sidecar

This folder contains a lightweight local sidecar service that Unity can connect to through:
- `GET /intent`
- `POST /speak`
- `POST /help`

The service is local-only by default (`127.0.0.1:8099`) and is compatible with `VoiceAgentBridge` defaults.

## Files
- `local_voice_agent_sidecar.py`: sidecar server
- `local_voice_agent_sidecar_config.json`: runtime config
- `run_local_voice_agent_sidecar.ps1`: Windows helper launcher
- `requirements-optional.txt`: optional STT/TTS dependencies

## Quick start (Windows PowerShell)
1. Open PowerShell in this folder.
2. Run:
```powershell
./run_local_voice_agent_sidecar.ps1
```

Alternative:
```powershell
python ./local_voice_agent_sidecar.py --config ./local_voice_agent_sidecar_config.json --log-level info
```

If `.venv/Scripts/python.exe` exists in this folder, Unity auto-start and `run_local_voice_agent_sidecar.ps1`
prefer it automatically (self-contained dependency setup).

## Optional dependencies
The sidecar runs without extra packages.

To enable microphone STT (`openai_transcribe` or local `vosk`) and local TTS (`pyttsx3`), install optional packages:
```powershell
pip install -r ./requirements-optional.txt
```

On Windows, install `llama-cpp-python` from prebuilt CPU wheels (recommended) to avoid local C/C++ build toolchain issues:
```powershell
pip install --upgrade --prefer-binary llama-cpp-python --extra-index-url https://abetlen.github.io/llama-cpp-python/whl/cpu
```

Default config is now hands-free oriented:
- `stt_backend` defaults to `auto`
- `tts_backend` defaults to `pyttsx3`
- Unity default voice config enables local help model

`auto` resolves like this:
- `online` AI mode + OpenAI API key available: use `openai_transcribe`
- otherwise: use local `vosk`

With `sounddevice` installed, online mode can use OpenAI Transcribe without the Vosk model. With `sounddevice` + the Vosk model available, local/offline STT also works.

Then set config values:
- `stt_backend`: `"auto"`, `"openai_transcribe"`, or `"vosk"`
- `stt_model_path`: path to your Vosk model directory (default: `../../../.local_voice_models/vosk-model-small-en-us-0.15`)
- `online_ai_transcription_model`: OpenAI transcription model (default: `gpt-4o-mini-transcribe`)
- `tts_backend`: `"pyttsx3"`
- `start_listening_enabled`: `true/false` (default listening mode before Unity toggles `/listening`)
- `min_transcript_chars` / `min_transcript_words`: ignore very short final transcripts
- `safe_numeric_parsing`: `true/false` (for safer `move_joint` number extraction)
- `require_target_token_for_joint`: require `to/at` before numeric target when parsing joints
- `reject_out_of_range_joint_commands`: reject numeric targets outside configured range
- `joint_min_degrees` / `joint_max_degrees`: numeric bounds used by sidecar parser
- `help_model_backend`: `"rule_based"` (default) or `"llama_cpp"` (optional local LLM for `/help` only)
- `help_model_path`: path to local `.gguf` model file when using `llama_cpp`
- `help_model_max_tokens`: max output tokens for generated help responses
- `help_model_temperature`: sampling temperature for generated help responses
- `help_max_answer_chars`: trims long generated answers to keep UI speech concise
- `audio_input_device_name`: optional preferred input device name (empty = auto)
- `prefer_non_virtual_input_device`: prefer physical mic-like inputs over virtual loopback devices

For optional local LLM help mode:
- install `llama-cpp-python` (already listed in `requirements-optional.txt`)
- on Windows prefer the prebuilt wheel command above
- set `help_model_backend` to `"llama_cpp"`
- set a valid `help_model_path`
- if model load fails, sidecar automatically falls back to rule-based help

Important:
- Keep STT model files outside Unity `Assets/` so Unity does not try to import Vosk binary files (for example `final.mat`).

## Manual endpoint testing
Check health:
```powershell
Invoke-RestMethod -Method Get -Uri http://127.0.0.1:8099/health
```

Inject a transcript and let sidecar parse to intent:
```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8099/inject_transcript -ContentType 'application/json' -Body '{"text":"set neutral arms pose","final":true,"confidence":0.92}'
```

Poll intent (what Unity does):
```powershell
Invoke-RestMethod -Method Get -Uri http://127.0.0.1:8099/intent
```

Request local help response:
```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8099/help -ContentType 'application/json' -Body '{"query":"how do i set a pose?","context":"Reachy Unity app"}'
```

Test local speak endpoint:
```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8099/speak -ContentType 'application/json' -Body '{"text":"Voice sidecar online","interrupt":false}'
```

## Reachy robot speaker mirror
Reachy 2021 documentation lists torso speakers and a ReSpeaker audio path, and the official `hello-world`
application plays sound directly on the robot computer. This project uses the same general model for mirrored
robot audio: keep local desktop TTS as-is, and optionally send the same text to a small HTTP speaker service
running on the Reachy computer.

Important: stock Reachy 2021 software does not expose an HTTP speaker mirror endpoint by default. The `8101`
port used by this Unity project is the default port for the custom helper server in this repository, not a
built-in Reachy service.

Run this on the robot computer:
```bash
cd ~/reachy1-unityproject/Assets/ReachyControlApp/LocalVoiceAgent
python3 ./reachy_robot_speaker_server.py --bind-host 0.0.0.0 --bind-port 8101 --tts-backend auto
```

Or from Windows, use the helper script in this folder to copy/start the server remotely over SSH:
```powershell
./start_reachy_robot_speaker_server.ps1 -ReachyHost 192.168.1.109
```

The helper script defaults to the common factory credentials:
- user: `reachy`
- password: `reachy`

If the password was changed, either pass `-ReachyPassword <newPassword>` or use `-PromptForPassword`.
The script writes a local run log to `start_reachy_robot_speaker_server_last_run.log`.

If `auto` cannot find a backend on the robot:
- install `espeak`, or
- install `pyttsx3` in the robot Python environment

Unity now has a `Mirror to robot speaker` toggle in the Local AI panel. When enabled in `Real Robot` mode,
Unity now probes the robot and logs what it finds:
- `http://<robotHost>:8101/health` for the bundled `reachy_robot_speaker_server.py`
- `http://<robotHost>:8099/health` as a fallback if a full voice sidecar is running on the robot

Resolution rules:
- `8101` reachable with `reachy_robot_speaker_server.py`: TTS mirroring and mirrored audio clips are both available
- `8099` reachable with `local_voice_agent_sidecar.py`: TTS-only mirroring is available
- neither reachable: Unity skips mirroring and writes a diagnostic runtime log entry instead of repeatedly failing silently

Unity UI also exposes:
- SSH user/password fields for helper actions
- `Start spk srv` button to launch the remote helper from the app on Windows

The local desktop/device audio still plays through the existing sidecar at `http://127.0.0.1:8099/speak`.

Useful runtime log events:
- `robot-speaker probe-ok`
- `robot-speaker probe-failed`
- `robot-speaker tts-mirror-accepted`
- `robot-speaker tts-mirror-failed`
- `robot-speaker audio-mirror-started`
- `robot-speaker audio-mirror-failed`

Toggle sidecar listening state (used by Unity push-to-talk):
```powershell
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8099/listening -ContentType 'application/json' -Body '{"enabled":false}'
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8099/listening -ContentType 'application/json' -Body '{"enabled":true}'
```

## Unity wiring
In Unity Local AI Agent panel keep these defaults (or set explicitly):
- Agent endpoint: `http://127.0.0.1:8099/intent`
- TTS endpoint: `http://127.0.0.1:8099/speak`
- Help endpoint: `http://127.0.0.1:8099/help`
- Listening endpoint: `http://127.0.0.1:8099/listening`
- Use `Load cfg` / `Save cfg` in the panel to persist Unity-side voice settings in `Assets/ReachyControlApp/voice_agent_config.json`.
- Use `Sync sidecar` in the panel to push shared parser/listening/help settings into `Assets/ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json`.
- Use `Load sidecar` in the panel to pull shared parser/listening/help settings back from `local_voice_agent_sidecar_config.json` into Unity UI.
- Optional toggle `Sync sidecar config from UI before sidecar start` (default on) auto-applies shared settings when sidecar is started.
- Help panel fields now include sidecar help backend/model tuning values that are included in sidecar sync/load.
- Mic tools in Unity panel:
  - `Prefer physical mic` + `Auto mic` heuristics for default selection
  - microphone selector dropdown to choose a specific device
  - hold-to-record mic test button that plays captured audio on release

If `Auto-start sidecar` is enabled in Unity, pressing `Enable local AI agent` starts the sidecar first and only enables bridge polling after the sidecar is reachable.

Build/runtime path note:
- In Editor, sidecar files are resolved from `Assets/ReachyControlApp/LocalVoiceAgent/...`.
- In standalone builds, Unity now probes multiple locations (build data folder, build folder, parent folders, current directory, persistent data) and also checks project-root style `Assets/...` paths.
- For portable build usage outside the Unity project tree, keep `ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py` available next to the executable folder (or provide absolute sidecar paths in config).
- If Unity reports `STT backend is inactive`, check Local AI status text: it now appends sidecar `last_error` details when available.

Current sidecar parser intents include:
- `help`, `status`, `connect_robot`, `disconnect_robot`
- `set_pose`, `move_joint`, `stop_motion`
- `confirm_pending`, `reject_pending` (for voice confirmation workflow)

Parser fallback behavior:
- final transcripts that do not resolve to a supported command are automatically routed to `help`
- this lets local LLM help answer general/unrecognized spoken inputs
