#!/usr/bin/env python3
"""Check a trusted Reachy 2023 over wired SSH and recover its SDK if needed."""

import ipaddress
import json
import pathlib
import socket
import sys


# Use the same ROS environment as the Reachy 2023 SDK launcher. A gRPC response
# timestamp alone is insufficient: the SDK stamps cached positions at reply time.
REMOTE_PROBE = r'''#!/bin/bash
unit=reachy_sdk_server.service
setup="$HOME/reachy_ws/src/reachy_2023/reachy_utils/.reachy.bashrc"
if [ ! -r "$setup" ]; then echo unsupported; exit 0; fi
if [ "$(systemctl --user show "$unit" -p LoadState --value)" != loaded ]; then
    echo unsupported; exit 0
fi
state=$(systemctl --user show "$unit" -p ActiveState --value)
case "$state" in
    activating|deactivating|reloading) echo starting; exit 0;;
    active|failed|inactive) ;;
    *) echo inconclusive; exit 0;;
esac
group=$(systemctl --user show "$unit" -p ControlGroup --value)
controller_running() {
    [ -n "$group" ] || return 1
    for pid in $(pgrep -f '^/opt/ros/humble/lib/controller_manager/ros2_control_node([[:space:]]|$)'); do
        grep -Fq ":$group" "/proc/$pid/cgroup" 2>/dev/null && return 0
    done
    return 1
}
if controller_running; then
    sample=$(source "$setup" >/dev/null 2>&1; timeout 7s ros2 topic echo --once \
        --qos-reliability best_effort /joint_states sensor_msgs/msg/JointState 2>/dev/null) || {
        echo no_sample; exit 0;
    }
    printf '%s\n' "$sample" | /usr/bin/python3 -c '
import math, sys, time, yaml
try:
    sample = next(yaml.safe_load_all(sys.stdin))
    names, positions = sample["name"], sample["position"]
    stamp = sample["header"]["stamp"]
    age = time.time() - (stamp["sec"] + stamp["nanosec"] * 1e-9)
    valid = (len(names) == len(positions) and len(set(names)) == len(names)
             and {"neck_yaw", "l_shoulder_pitch", "r_shoulder_pitch"}.issubset(names)
             and all(math.isfinite(p) for p in positions) and -1 <= age <= 4)
    print(f"ready {len(names)}" if valid else "no_sample")
except (StopIteration, KeyError, TypeError, ValueError, yaml.YAMLError):
    print("no_sample")'
    exit 0
fi
read -r uptime unused < /proc/uptime
uptime=${uptime%%.*}
clock_synced=$(timedatectl show -p NTPSynchronized --value 2>/dev/null)
if [ "$uptime" -lt 60 ] || { [ "$clock_synced" != yes ] && [ "$uptime" -lt 120 ]; }; then
    echo starting; exit 0
fi
# An active launch unit can still be bringing up its hardware. Recheck the
# controller immediately before changing anything.
if [ "$state" = active ]; then
    started=$(systemctl --user show "$unit" -p ActiveEnterTimestampMonotonic --value)
    if [ "$((uptime - started / 1000000))" -lt 20 ]; then echo starting; exit 0; fi
fi
if [ "${1:-probe}" = recover ] && ! controller_running &&
    [ "$(systemctl --user show "$unit" -p ActiveState --value)" = "$state" ]; then
    if systemctl --user restart --no-block "$unit"; then echo restarted; else echo restart_failed; fi
else
    echo controller_missing
fi
'''


def run(request):
    import paramiko

    host = str(ipaddress.IPv4Address(request["host"]))
    source = str(ipaddress.IPv4Address(request["source"]))
    trusted = paramiko.HostKeys()
    file = pathlib.Path(request["knownHostsPath"])
    if file.is_file():
        trusted.load(str(file))
    keys = trusted.lookup(host)
    if not keys:
        return {"status": "untrusted"}

    # Bind to the interface selected by the application. Never authenticate to
    # an unknown or changed key, and never put the password on the command line.
    try:
        with socket.create_connection((host, 22), timeout=2.5, source_address=(source, 0)) as sock:
            with paramiko.Transport(sock) as ssh:
                ssh.banner_timeout = 3
                ssh.auth_timeout = 3
                ssh.start_client(timeout=3)
                key = ssh.get_remote_server_key()
                if keys.get(key.get_name()) != key:
                    return {"status": "key_mismatch"}
                ssh.auth_password(request["user"], request["password"], fallback=False)
                with ssh.open_session(timeout=3) as session:
                    session.settimeout(12)
                    session.exec_command("/bin/bash -s -- " + ("recover" if request.get("recover") is True else "probe"))
                    session.sendall(REMOTE_PROBE.encode("utf-8"))
                    session.shutdown_write()
                    session.set_combine_stderr(True)
                    with session.makefile("rb") as stdout:
                        reply = stdout.read(1024).decode("utf-8", "replace").strip().split()
                    if not reply or session.recv_exit_status() != 0:
                        return {"status": "inconclusive"}
                    if len(reply) == 2 and reply[0] == "ready" and reply[1].isdigit():
                        return {"status": "ready", "jointCount": int(reply[1])}
                    if len(reply) == 1 and reply[0] in {
                        "starting", "controller_missing", "restarted", "restart_failed",
                        "unsupported", "inconclusive", "no_sample",
                    }:
                        return {"status": reply[0]}
    except paramiko.AuthenticationException:
        return {"status": "auth_failed"}
    except (OSError, paramiko.SSHException, EOFError):
        return {"status": "ssh_unavailable"}
    return {"status": "inconclusive"}


if __name__ == "__main__":
    try:
        result = run(json.load(sys.stdin))
    except ImportError:
        result = {"status": "dependency_missing"}
    except Exception:
        result = {"status": "inconclusive"}
    print(json.dumps(result))
