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

## Automatic Ethernet connection

`Automatically use Reachy on Ethernet` is enabled by default in the Connection panel.
On Play, and when a cable is connected later, the app searches active wired IPv4 interfaces
in the background. It checks the configured robot/fallback addresses, `reachy.local`,
Windows neighbour entries, and subnets containing at most 254 hosts. It does not sweep
large networks such as the automatic `169.254.0.0/16` range.

The search also checks `169.254.42.1`, the optional direct-cable fallback address.
On the configured Reachy, a persistent `Reachy direct Ethernet` fallback profile uses
this address, so Windows can use its automatic `169.254.x.x/16` address without an
administrator change on the PC. The ordinary DHCP profile has higher priority; after
a 15-second DHCP attempt without a server, NetworkManager can use the direct-cable
profile. The robot keeps DHCP for ordinary wired networks. This address must
be configured on a robot before it can be used; discovery alone does not configure it.

The configured robot also has a NetworkManager dispatcher that recovers its already
running SDK service if it started before Ethernet IPv4 was ready and port `50055` is
absent. Original network settings and a rollback script are backed up on the robot at
`/var/backups/reachy-direct-ethernet-20261022/`.

Only a reachable Reachy 1 joint service with recognizable joint names is accepted;
an Ethernet link or an open SSH port is not enough. The OS route must use Ethernet.
When exactly one robot is found, the app selects `Real Robot`, fills `Host` and `Port`,
and connects automatically. Camera, microphone SSH, speaker and mobile-base routing use
that robot host through the existing connection logic. Existing service ports and SSH
credentials remain configurable. Discovery does not send motion or restart commands.

The first discovery completes before the usual simulation auto-connect. While using the
simulator, discovery continues every 15 seconds so plugging in a robot switches to it.
Manual Disconnect prevents automatic reconnection until Connect, Find Reachy on Ethernet,
or re-enabling automatic Ethernet discovery. Disable the toggle to keep using simulation
with a robot attached. Multiple Reachy endpoints require manual selection.

**One-time network setup can still be necessary.** Without DHCP, the PC and robot must
already have Ethernet IPv4 addresses in the same subnet, for example PC `192.168.1.10/24`
and robot `192.168.1.109/24` if that is the robot's actual Ethernet address. No gateway is
needed on the direct link. An IPv6 SSH response alone does not establish an IPv4 SDK
connection. The app reports a possible IPv4 setup problem when only an automatic
`169.254` address is available. It cannot infer an arbitrary robot static IP or change
Windows/robot network settings without the necessary privileges and robot access.
The robot SDK, camera and audio services must also be running. The discovery status is
shown in the Connection panel; `Find Reachy on Ethernet` triggers a fresh search.

Validation: run `Tests/run_ethernet_tests.ps1` with Unity 2021.3.24f1 installed, or pass
`-UnityEditorPath` for another installation. `-Live` also performs read-only discovery
on this computer's wired networks. Test binaries are written under ignored `Temp/`.

Release packaging can select a tested build directory, for example:

```powershell
./scripts/package-standalone-release.ps1 -BuildRoot ./Build/DirectEthernet -TrackedDefaults -ReleaseTag standalone-2026-10-22-1
```

`-TrackedDefaults` restores version-controlled configuration defaults in the release
staging folder, adapts bundled Python/model paths and excludes personal saved personas
and runtime logs. The original build remains unchanged. Archives include source-commit
and assembly-hash metadata in `ReachyControlApp/build-info.json` plus a `.zip.sha256` file.

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
2. Set primary robot `Host` (`192.168.1.109`) and `Port` (`50055`) or your robot endpoint.
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
