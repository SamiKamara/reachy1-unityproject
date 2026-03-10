# Reachy Control App (Runtime UI)

This module adds a runtime Unity UI for controlling Reachy 1 through gRPC in two modes:

- `Simulation`: connect to `localhost` (default `50055`)
- `Real Robot`: connect to robot IP from your PC

The UI appears automatically in Play mode.

## Files

- `Scripts/ReachyControlMode.cs`
- `Scripts/ReachyGrpcClient.cs`
- `Scripts/ReachyRuntimeControlUI.cs`
- `Scripts/ReachyControlBootstrap.cs`

## How to use

1. Open project in Unity `2021.3.24f1`.
2. Press Play.
3. In panel:
   - Select mode (`Simulation` or `Real Robot`)
   - Set `Host` and `Port`
   - Configure `Automation` options (recommended for real robot), including `Pose speed %` (default 60)
   - Click `Connect` (or let auto-connect run)
4. Click `Refresh Joints` to fetch names.
5. Send commands:
   - `Single Joint Command` by joint name and goal degree
   - `Preset Poses` with one click (`Neutral Arms`, `T-Pose`, `Tray Holding`, `Hello Pose A`, `Hello Pose B`, `Hello Pose C`, `Hello Pose D`)

## Recovery automation

The runtime panel now includes automatic connection recovery:

- Auto-connect on Play start.
- Background health watchdog (`Ping` on interval).
- Auto-reconnect when connection is lost.
- Multi-attempt connect per host.
- Optional gRPC restart-signal between retries (`Real Robot` mode only).
- Optional fallback hosts list (comma-separated, supports `host:port`).
- Preset pose library with one-click pose buttons for both simulation and real robot.

Typical real robot setup:

1. Mode: `Real Robot`.
2. Set primary robot `Host` (`192.168.1.118`) and `Port` (`50055`) or your robot endpoint.
3. Keep `Auto-connect` and `Auto-reconnect` enabled.
4. Set `Attempts/host` to `3` to `5`.
5. Keep `Fallback ports` including `3972` as backup if your robot exposes that endpoint.
6. Add fallback hosts if you use multiple network names/IPs.

## Notes

- Default Reachy 1 joint service port is `50055`.
- For simulator mode, ensure the simulator gRPC server is active in scene or external simulator process.
- For real robot mode, ensure PC and robot are on the same network and firewall allows outbound gRPC.
- Restart-signal recovery depends on robot-side support for `reachy.sdk.restart.RestartService`.
  If unavailable, retries continue without restart assistance.
- Joint goals entered in the UI are in degrees and converted to radians before gRPC send.
- Motion commands automatically request `Compliant = false` for the addressed joints.
  When joint state RPC is available, the app first latches compliant joints to their present position before sending the target move.
- Commands can move real hardware. Test with small values first.
