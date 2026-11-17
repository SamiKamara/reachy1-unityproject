#!/usr/bin/env python3
"""Check a trusted Reachy 2023 over wired SSH and recover its SDK if needed."""

import ipaddress
import base64
import concurrent.futures
import hashlib
import json
import pathlib
import socket
import sys


# Find the exact interface that accepted SSH. This also works when only IPv6
# is available and the address learned by the Windows app is out of date.
REMOTE_NETWORK = r'''
import ipaddress, json, os, subprocess, sys

def run(*args):
    return subprocess.run(args, text=True, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=4)

def addresses():
    return json.loads(run("ip", "-j", "address", "show").stdout)

local = ipaddress.ip_address(os.environ["SSH_CONNECTION"].split()[2].split("%", 1)[0])
matches = [item for item in addresses() if any(ipaddress.ip_address(a["local"]) == local
            for a in item.get("addr_info", []) if a.get("family") in ("inet", "inet6"))]
if len(matches) != 1 or matches[0].get("link_type") != "ether":
    print("{}"); sys.exit(0)
interface = matches[0]
device = interface["ifname"]
info = interface.get("addr_info", [])
v4 = [a["local"] for a in info if a["family"] == "inet" and a["scope"] != "host"]
v6 = [a["local"] for a in info if a["family"] == "inet6" and a["scope"] == "link"]
network = {"addresses": v4, "linkLocal": next(iter(v6), ""), "hostname": os.uname().nodename + ".local"}
if not v4 and local.is_link_local and sys.argv[1] in ("direct-probe", "network"):
    profile = "Reachy direct Ethernet"
    fields = "connection.uuid,connection.interface-name,ipv4.method,ipv4.addresses,ipv4.never-default"
    properties = run("nmcli", "-g", fields, "connection", "show", "id", profile)
    values = properties.stdout.strip().splitlines()
    import re
    if (properties.returncode == 0 and len(values) == 5 and re.fullmatch(r"[0-9a-f-]{36}", values[0])
            and values[1:] == [device, "manual", "169.254.42.1/16", "yes"]):
        uptime = float(open("/proc/uptime").read().split()[0])
        if uptime >= 65 and sys.argv[1] == "network":
            # Recheck IPv4 before selecting the already configured profile. No
            # edits, system reboot or changes to the Wi-Fi interface.
            current = next((i for i in addresses() if i["ifname"] == device), {})
            if not any(a["family"] == "inet" for a in current.get("addr_info", [])):
                activation = ("/usr/bin/nmcli", "--wait", "0", "connection", "up", "uuid", values[0], "ifname", device)
                result = run(*activation)
                if result.returncode != 0:
                    # A robot can explicitly authorize just this exact profile
                    # and interface. Never prompt for sudo or send its password.
                    result = run("/usr/bin/sudo", "-n", "--", *activation)
                network["status"] = "network_requested" if result.returncode == 0 else "network_denied"
        elif uptime >= 65:
            network["status"] = "network_missing"
        else:
            network["status"] = "network_starting"
print(json.dumps(network))
'''


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


def endpoint(raw):
    address = ipaddress.ip_address(raw)
    if address.version == 4:
        if address.is_loopback or address.is_multicast or address.is_unspecified:
            raise ValueError("Not an Ethernet peer")
        return socket.AF_INET, (str(address), 0)
    if not address.is_link_local or not address.scope_id or not address.scope_id.isdecimal():
        raise ValueError("Expected an IPv6 link-local address with interface index")
    return socket.AF_INET6, (raw.split("%", 1)[0], 0, 0, int(address.scope_id))


def probe_key(route, trusted, paramiko):
    sock = transport = None
    try:
        family, host = endpoint(route["host"])
        source_family, source = endpoint(route["source"])
        if family != source_family or (family == socket.AF_INET6 and host[3] != source[3]):
            return "inconclusive", None, None
        sock = socket.socket(family, socket.SOCK_STREAM)
        sock.settimeout(1.8)
        sock.bind(source)
        sock.connect((host[0], 22, *host[2:]))
        transport = paramiko.Transport(sock)
        # Ask for an enrolled key type first: the server can have several keys.
        options = transport.get_security_options()
        pinned = {key.get_name() for key in trusted.values()}
        options.key_types = tuple(sorted(options.key_types, key=lambda name: not (
            name in pinned or name.startswith("rsa-sha2-") and "ssh-rsa" in pinned)))
        transport.banner_timeout = 2.5
        transport.auth_timeout = 3
        transport.start_client(timeout=3)
        key = transport.get_remote_server_key()
        identity = base64.b64encode(hashlib.sha256(key.asbytes()).digest()).decode("ascii").rstrip("=")
        if identity not in trusted:
            return "key_mismatch" if route.get("previouslyKnown") else "untrusted", None, None
        selected, transport, sock = transport, None, None
        return "matched", identity, selected
    except (OSError, paramiko.SSHException, EOFError, ValueError):
        return "ssh_unavailable", None, None
    finally:
        if transport is not None:
            transport.close()
        if sock is not None:
            sock.close()


def read_command(ssh, command, payload):
    with ssh.open_session(timeout=3) as session:
        session.settimeout(12)
        session.set_combine_stderr(True)
        session.exec_command(command)
        session.sendall(payload.encode("utf-8"))
        session.shutdown_write()
        with session.makefile("rb") as stdout:
            reply = stdout.read(4096).decode("utf-8", "replace").strip()
        return reply if session.recv_exit_status() == 0 else ""


def run(request):
    import paramiko

    known = paramiko.HostKeys()
    file = pathlib.Path(request["knownHostsPath"])
    if file.is_file():
        known.load(str(file))
    trusted = {base64.b64encode(hashlib.sha256(key.asbytes()).digest()).decode("ascii").rstrip("="): key
               for name in known for key in known[name].values()}
    if not trusted:
        return {"status": "untrusted"}

    routes = request.get("routes", [])[:12]
    # Keep the prior one-address request format usable for local tooling.
    if not routes and request.get("host") and request.get("source"):
        routes = [{"host": request["host"], "source": request["source"]}]
    for route in routes:
        route["previouslyKnown"] = known.lookup(route["host"]) is not None
    if not routes:
        return {"status": "no_management_route"}

    # No credentials are sent while discovering which endpoint owns the pinned key.
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        probes = list(pool.map(lambda route: probe_key(route, trusted, paramiko), routes))
    matches = [(route, identity, ssh) for route, (state, identity, ssh) in zip(routes, probes) if state == "matched"]
    try:
        if not matches:
            status = next((name for name in ("key_mismatch", "untrusted") if any(p[0] == name for p in probes)),
                          "ssh_unavailable")
            return {"status": status}
        expected = request.get("identity")
        if expected:
            matches_for_robot = [item for item in matches if item[1] == expected]
            if not matches_for_robot:
                return {"status": "trusted_robot_unavailable"}
        elif len({item[1] for item in matches}) > 1:
            return {"status": "ambiguous"}
        else:
            matches_for_robot = matches
        # Prefer IPv4 when both routes reach the same robot. Keep the scoped
        # IPv6 connection for discovery and repair when IPv4 is not available.
        route, identity, ssh = min(matches_for_robot, key=lambda item: ":" in item[0]["host"])
        ssh.auth_password(request["user"], request["password"], fallback=False)
        result = {"identity": identity}
        action = request.get("action", "probe") if expected else "probe"
        direct = route.get("directFallback") is True and ":" in route["host"]
        network_action = "network" if direct and action == "network" else "direct-probe" if direct else "probe"
        try:
            data = read_command(ssh, "/usr/bin/python3 - " + network_action, REMOTE_NETWORK)
            if data:
                result.update(json.loads(data))
        except (OSError, ValueError, paramiko.SSHException):
            # IPv4 profile activation can close the IPv6 session before a reply.
            if network_action == "network":
                return {**result, "status": "network_denied"}
        if result.get("status", "").startswith("network_"):
            return result
        command = "/bin/bash -s -- " + ("recover" if action == "sdk" else "probe")
        reply = read_command(ssh, command, REMOTE_PROBE).split()
        if len(reply) == 2 and reply[0] == "ready" and reply[1].isdigit():
            return {**result, "status": "ready", "jointCount": int(reply[1])}
        if len(reply) == 1 and reply[0] in {
            "starting", "controller_missing", "restarted", "restart_failed",
            "unsupported", "inconclusive", "no_sample",
        }:
            return {**result, "status": reply[0]}
        return {**result, "status": "inconclusive"}
    except paramiko.AuthenticationException:
        return {"status": "auth_failed"}
    except (OSError, paramiko.SSHException, EOFError):
        return {"status": "ssh_unavailable"}
    finally:
        for _, _, ssh in matches:
            ssh.close()


if __name__ == "__main__":
    try:
        result = run(json.load(sys.stdin))
    except ImportError:
        result = {"status": "dependency_missing"}
    except Exception:
        result = {"status": "inconclusive"}
    print(json.dumps(result))
