# Reachy Controller and Simulator (Unity 2021)

This project is no longer only a simulator package.  
It is now a Unity 2021 project that combines:

- Reachy1 simulator services (gRPC)
- an in-Play-Mode control app for simulation and real robot targets
- runtime tools for connection management, commanding joints, and preset poses

## Current State

As of March 5, 2026, this repo includes a runtime control UI (`ReachyControlApp`) that auto-loads in Play Mode and supports:

- Simulation mode and Real Robot mode switching
- mode-aware connect/disconnect behavior
- auto-connect on Play and auto-reconnect monitoring
- connection health checking and reconnect cooldown
- restart-signal recovery during retries (real robot mode)
- real-robot endpoint prechecks (optional DNS resolve + TCP reachability probe)
- preset poses with one click (`Neutral Arms`, `T-Pose`, `Hello Pose A`, `Hello Pose B`, `Hello Pose C`, `Hello Pose D`)

UI layout:

- left panel: connection + automation
- right panel: commands + poses + status
- right panel header: `Windowed`, `Fullscreen`, `Exit` controls
- configurable windowed resolution fields (default `1280 x 720`)

Build app name is set to:

- `Reachy controller & simulator`

## Quick Start

1. Open `reachy1-unityproject` in Unity 2021.
2. Open a scene (for example `Assets/Scenes/SampleScene.unity` or `Assets/Scenes/OfficeScene.unity`).
3. Press Play. The runtime control panel appears automatically.
4. Choose mode (Simulation or Real Robot), configure host/port, then connect.

## Connection Defaults

- Simulation default: `localhost:50055`
- Real robot default: `192.168.1.118:3972`
- Real robot fallback ports: `50055`
- Restart-signal port (real robot): `50059`

Notes:

- The connection logic was hardened by comparing against official Reachy Unity/SDK repositories.
- Real hardware was not available during this setup, so physical validation is still required.

## SDK Compatibility (Reachy 2021)

For Reachy 2021 Python SDK usage, use:

```python
from reachy_sdk import ReachySDK
reachy = ReachySDK(host="localhost")
```

Recommended SDK version for Reachy 2021:

```bash
pip install reachy-sdk==0.5.4
```

## License

Creative Commons BY-NC-SA 4.0:

- https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode
