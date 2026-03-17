#!/usr/bin/env python3
"""
Deploy and start the Reachy robot speaker server over SSH.

This helper is intended to be launched from the existing PowerShell wrapper so
the Unity UI can keep using the same "Start spk srv" flow, while avoiding a
hard dependency on PuTTY tools for password-based SSH automation.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.request
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Deploy/start the Reachy robot speaker server over SSH.")
    parser.add_argument("--reachy-host", required=True)
    parser.add_argument("--reachy-user", default="reachy")
    parser.add_argument("--reachy-password", default="reachy")
    parser.add_argument("--remote-project-root", default="~/reachy1-unityproject")
    parser.add_argument("--python-command", default="python3")
    parser.add_argument("--port", type=int, default=8101)
    parser.add_argument("--tts-backend", default="auto")
    parser.add_argument("--bind-host", default="0.0.0.0")
    parser.add_argument("--remote-log-path", default="~/reachy_robot_speaker_server.log")
    parser.add_argument("--local-log-path", default="")
    parser.add_argument("--ensure-espeak", action="store_true")
    return parser.parse_args()


class Logger:
    def __init__(self, local_log_path: str) -> None:
        self.local_log_path = str(local_log_path or "").strip()
        if self.local_log_path:
            path = Path(self.local_log_path)
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("", encoding="utf-8")

    def write(self, message: str, level: str = "INFO") -> None:
        safe_message = str(message or "").strip()
        line = f"{time.strftime('%Y-%m-%dT%H:%M:%S')} [{level.upper()}] {safe_message}"
        print(line)
        if self.local_log_path:
            with open(self.local_log_path, "a", encoding="utf-8") as handle:
                handle.write(line + "\n")


def ssh_connect(host: str, user: str, password: str):
    try:
        import paramiko  # type: ignore
    except Exception as exc:
        raise RuntimeError(
            "paramiko is unavailable in the local helper environment. Install requirements-optional.txt first."
        ) from exc

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(
        hostname=host,
        username=user,
        password=password or None,
        port=22,
        timeout=10,
        banner_timeout=10,
        auth_timeout=10,
        look_for_keys=True,
        allow_agent=True,
    )
    return client


def run_remote_command(client, command_text: str, *, timeout: float = 120.0, sudo_password: str = "") -> tuple[int, str, str]:
    stdin, stdout, stderr = client.exec_command(command_text, timeout=timeout)
    if sudo_password:
        stdin.write(sudo_password + "\n")
        stdin.flush()
    output = stdout.read().decode("utf-8", errors="replace")
    error_text = stderr.read().decode("utf-8", errors="replace")
    exit_status = stdout.channel.recv_exit_status()
    return exit_status, output, error_text


def run_remote_checked(
    client,
    logger: Logger,
    command_text: str,
    *,
    step_name: str,
    timeout: float = 120.0,
    sudo_password: str = "",
) -> str:
    logger.write(f"{step_name} on remote host.")
    exit_status, output, error_text = run_remote_command(
        client,
        command_text,
        timeout=timeout,
        sudo_password=sudo_password,
    )
    if output.strip():
        logger.write(output.strip())
    if error_text.strip():
        logger.write(error_text.strip(), "WARN")
    if exit_status != 0:
        raise RuntimeError(f"{step_name} failed with exit code {exit_status}.")
    return output


def read_http_json(url: str, timeout_seconds: float) -> dict:
    with urllib.request.urlopen(url, timeout=timeout_seconds) as response:
        payload = response.read().decode("utf-8", errors="replace")
    return json.loads(payload)


def main() -> int:
    args = parse_args()
    logger = Logger(args.local_log_path)

    script_directory = Path(__file__).resolve().parent
    local_server_script = script_directory / "reachy_robot_speaker_server.py"
    if not local_server_script.exists():
        logger.write(f"Local speaker server script not found: {local_server_script}", "ERROR")
        return 2

    safe_port = max(1, min(65535, int(args.port)))
    remote_project_root = str(args.remote_project_root or "~/reachy1-unityproject").strip() or "~/reachy1-unityproject"
    remote_directory = f"{remote_project_root}/Assets/ReachyControlApp/LocalVoiceAgent"
    remote_script_path = f"{remote_directory}/reachy_robot_speaker_server.py"
    remote_log_path = str(args.remote_log_path or "~/reachy_robot_speaker_server.log").strip() or "~/reachy_robot_speaker_server.log"
    bind_host = str(args.bind_host or "0.0.0.0").strip() or "0.0.0.0"
    tts_backend = str(args.tts_backend or "auto").strip() or "auto"
    python_command = str(args.python_command or "python3").strip() or "python3"
    host = str(args.reachy_host or "").strip()
    user = str(args.reachy_user or "reachy").strip() or "reachy"
    password = str(args.reachy_password or "").strip()

    logger.write(f"Helper log path: {args.local_log_path}")
    logger.write(f"Connecting to {user}@{host}.")

    client = ssh_connect(host, user, password)
    try:
        run_remote_checked(
            client,
            logger,
            f"mkdir -p '{remote_directory}'",
            step_name="Create remote directory",
        )

        logger.write(f"Uploading {local_server_script} to {remote_script_path}.")
        sftp = client.open_sftp()
        try:
            sftp.put(str(local_server_script), remote_script_path)
        finally:
            sftp.close()

        if args.ensure_espeak:
            has_espeak = run_remote_checked(
                client,
                logger,
                "command -v espeak >/dev/null 2>&1 && echo present || echo missing",
                step_name="Check espeak",
            ).strip().lower()
            if has_espeak != "present":
                if not password:
                    raise RuntimeError("espeak is missing on the robot and no SSH password was provided for sudo install.")
                run_remote_checked(
                    client,
                    logger,
                    "sudo -S -p '' apt-get install -y espeak",
                    step_name="Install espeak",
                    timeout=900.0,
                    sudo_password=password,
                )

        stop_command = """python3 - <<'PY'
import os
import signal
import subprocess

needle = "reachy_robot_speaker_server.py"
for raw_line in subprocess.check_output(["ps", "-eo", "pid=,args="], text=True).splitlines():
    line = raw_line.strip()
    if not line:
        continue
    parts = line.split(None, 1)
    if len(parts) != 2:
        continue
    pid_text, args = parts
    if needle not in args:
        continue
    try:
        pid = int(pid_text)
    except ValueError:
        continue
    if pid == os.getpid():
        continue
    try:
        os.kill(pid, signal.SIGTERM)
        print(f"killed:{pid}")
    except ProcessLookupError:
        pass
PY"""
        logger.write("Stop existing speaker server on remote host.")
        stop_exit_status, stop_output, stop_error = run_remote_command(
            client,
            stop_command,
            timeout=120.0,
        )
        if stop_output.strip():
            logger.write(stop_output.strip())
        if stop_error.strip():
            logger.write(stop_error.strip(), "WARN")
        if stop_exit_status not in (0, -1):
            raise RuntimeError(f"Stop existing speaker server failed with exit code {stop_exit_status}.")
        stop_output = stop_output.strip()
        if not stop_output:
            logger.write("No existing robot speaker server process was running.")

        start_command = (
            "sh -lc " +
            "'" +
            f"nohup {python_command} {quote_for_shell(remote_script_path)} "
            f"--bind-host {quote_for_shell(bind_host)} "
            f"--bind-port {safe_port} "
            f"--tts-backend {quote_for_shell(tts_backend)} "
            f"> {quote_for_shell(remote_log_path)} 2>&1 < /dev/null &" +
            "'"
        )
        run_remote_checked(
            client,
            logger,
            start_command,
            step_name="Start robot speaker server",
        )

        health_url = f"http://{host}:{safe_port}/health"
        last_error: Exception | None = None
        for _ in range(20):
            time.sleep(1.0)
            try:
                payload = read_http_json(health_url, 3.0)
                logger.write(f"Health: {json.dumps(payload, ensure_ascii=True)}")
                logger.write(f"Robot speaker server is reachable at {health_url}.")
                return 0
            except Exception as exc:  # noqa: BLE001
                last_error = exc

        logger.write(f"Health probe failed on {health_url}: {last_error}", "ERROR")
        try:
            _, remote_log, remote_err = run_remote_command(
                client,
                f"cat {quote_for_shell(remote_log_path)} 2>/dev/null || true",
                timeout=20.0,
            )
            if remote_log.strip():
                logger.write("Remote log:")
                logger.write(remote_log.strip())
            if remote_err.strip():
                logger.write(remote_err.strip(), "WARN")
        except Exception as exc:  # noqa: BLE001
            logger.write(f"Reading remote log failed: {exc}", "WARN")
        return 1
    finally:
        client.close()


def quote_for_shell(value: str) -> str:
    text = str(value or "")
    return "'" + text.replace("'", "'\"'\"'") + "'"


if __name__ == "__main__":
    raise SystemExit(main())
