#!/usr/bin/env python3
"""
Minimal HTTP TTS server for Reachy 2021 robot speakers.

Endpoints:
- GET  /health
- POST /speak
- POST /play-audio
- POST /stop

The Unity app can mirror local AI TTS to this server while keeping local desktop
audio active through the existing local voice-agent sidecar.
"""

from __future__ import annotations

import argparse
import base64
import json
import logging
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Optional
from urllib.parse import urlparse


class SpeakerState:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.started_at = time.time()
        self.backend = "uninitialized"
        self.audio_backend = "unavailable"
        self.speaking = False
        self.last_message = "Robot speaker server idle."
        self.last_error = ""

    def set_backend(self, backend: str) -> None:
        with self.lock:
            self.backend = backend

    def set_audio_backend(self, backend: str) -> None:
        with self.lock:
            self.audio_backend = str(backend or "unavailable").strip() or "unavailable"

    def set_speaking(self, speaking: bool) -> None:
        with self.lock:
            self.speaking = bool(speaking)

    def set_message(self, message: str) -> None:
        with self.lock:
            self.last_message = str(message or "").strip() or "Robot speaker server idle."
            self.last_error = ""

    def set_error(self, message: str) -> None:
        with self.lock:
            detail = str(message or "").strip() or "Unknown robot speaker error."
            self.last_error = detail
            self.last_message = detail

    def snapshot(self) -> dict:
        with self.lock:
            return {
                "ok": True,
                "backend": self.backend,
                "audio_backend": self.audio_backend,
                "speaking": self.speaking,
                "last_message": self.last_message,
                "last_error": self.last_error,
                "uptime_seconds": max(0.0, time.time() - self.started_at),
            }


class RobotSpeakerTTS:
    def __init__(self, backend: str, rate: int, voice_name: str, state: SpeakerState) -> None:
        self.backend_preference = (backend or "auto").strip().lower()
        self.rate = max(60, min(320, int(rate)))
        self.voice_name = (voice_name or "").strip()
        self.state = state
        self.backend_name = ""
        self.audio_backend_name = "unavailable"
        self._pyttsx3_module = None
        self._active_process = None
        self._active_process_lock = threading.Lock()
        self.queue: "queue.Queue[dict]" = queue.Queue(maxsize=100)
        self.stop_event = threading.Event()
        self.interrupt_event = threading.Event()
        self.thread: Optional[threading.Thread] = None

    def start(self) -> bool:
        try:
            self.backend_name = self._detect_backend()
            self.state.set_backend(self.backend_name)
        except Exception as exc:
            self.state.set_error(str(exc))
            logging.error("Robot speaker backend initialization failed: %s", exc)
            return False

        try:
            self.audio_backend_name = self._detect_audio_backend()
        except Exception as exc:
            self.audio_backend_name = "unavailable"
            logging.warning("Robot speaker audio backend initialization failed: %s", exc)

        self.state.set_audio_backend(self.audio_backend_name)
        if self.audio_backend_name == "unavailable":
            self.state.set_message(
                f"Robot speaker backend ready: {self.backend_name}. Audio mirror unavailable."
            )
        else:
            self.state.set_message(
                f"Robot speaker backend ready: {self.backend_name}. Audio backend: {self.audio_backend_name}."
            )

        self.thread = threading.Thread(
            target=self._worker,
            daemon=True,
            name="reachy-robot-speaker-worker",
        )
        self.thread.start()
        return True

    def stop(self) -> None:
        self.stop_event.set()
        self.interrupt_event.set()
        self._terminate_active_process()
        try:
            self.queue.put_nowait({"kind": "noop"})
        except queue.Full:
            pass
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=1.0)

    def speak(self, text: str, interrupt: bool) -> tuple[bool, str]:
        text = (text or "").strip()
        if not text:
            return False, "Speech text is empty."

        try:
            if interrupt:
                self.interrupt_event.set()
                self._clear_queue_nonblocking()
                self._terminate_active_process()

            self.queue.put_nowait(
                {
                    "kind": "tts",
                    "text": text,
                }
            )
            return True, f"Speech queued for robot speaker ({self.backend_name})."
        except queue.Full:
            return False, "Robot speaker queue is full."

    def play_audio(
        self,
        audio_bytes: bytes,
        audio_format: str,
        interrupt: bool,
        loop: bool,
        label: str,
    ) -> tuple[bool, str]:
        if not audio_bytes:
            return False, "Audio payload is empty."

        normalized_format = str(audio_format or "wav").strip().lower()
        if normalized_format not in ("wav", "wave"):
            return False, f"Unsupported audio format '{normalized_format}'."

        if self.audio_backend_name == "unavailable":
            return False, "No robot speaker audio playback backend is available."

        try:
            if interrupt:
                self.interrupt_event.set()
                self._clear_queue_nonblocking()
                self._terminate_active_process()

            self.queue.put_nowait(
                {
                    "kind": "audio",
                    "audio_bytes": bytes(audio_bytes),
                    "audio_format": normalized_format,
                    "loop": bool(loop),
                    "label": str(label or "").strip() or "audio",
                }
            )
            return True, f"Audio queued for robot speaker ({self.audio_backend_name})."
        except queue.Full:
            return False, "Robot speaker queue is full."

    def interrupt(self) -> tuple[bool, str]:
        self.interrupt_event.set()
        self._clear_queue_nonblocking()
        self._terminate_active_process()
        message = "Robot speaker playback interrupted."
        self.state.set_message(message)
        return True, message

    def _detect_backend(self) -> str:
        preference = self.backend_preference

        if preference not in ("auto", "espeak", "pyttsx3", "say"):
            raise RuntimeError(
                f"Unsupported backend '{preference}'. Use auto, espeak, pyttsx3, or say."
            )

        if preference in ("auto", "espeak") and shutil.which("espeak"):
            return "espeak"

        if preference in ("auto", "say") and sys.platform == "darwin" and shutil.which("say"):
            return "say"

        if preference in ("auto", "pyttsx3"):
            try:
                import pyttsx3  # type: ignore

                self._pyttsx3_module = pyttsx3
                return "pyttsx3"
            except Exception as exc:
                if preference == "pyttsx3":
                    raise RuntimeError(f"pyttsx3 is unavailable: {exc}") from exc

        raise RuntimeError(
            "No robot speaker TTS backend is available. Install 'espeak' or 'pyttsx3'."
        )

    def _detect_audio_backend(self) -> str:
        candidates = (
            ("ffplay", "ffplay"),
            ("paplay", "paplay"),
            ("aplay", "aplay"),
            ("afplay", "afplay"),
            ("play", "play"),
        )
        for backend_name, command_name in candidates:
            if shutil.which(command_name):
                return backend_name

        return "unavailable"

    def _clear_queue_nonblocking(self) -> None:
        while True:
            try:
                self.queue.get_nowait()
            except queue.Empty:
                return

    def _set_active_process(self, proc) -> None:
        with self._active_process_lock:
            self._active_process = proc

    def _get_active_process(self):
        with self._active_process_lock:
            return self._active_process

    def _terminate_active_process(self) -> None:
        proc = self._get_active_process()
        if proc is None:
            return

        try:
            if proc.poll() is None:
                proc.terminate()
                try:
                    proc.wait(timeout=0.6)
                except Exception:
                    try:
                        proc.kill()
                    except Exception:
                        pass
        except Exception:
            pass
        finally:
            self._set_active_process(None)

    def _speak_with_subprocess(self, command: list[str]) -> tuple[bool, str]:
        try:
            proc = subprocess.Popen(
                command,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
            )
        except Exception as exc:
            return False, f"Robot speaker subprocess start failed: {exc}"

        self._set_active_process(proc)
        timeout_seconds = 30.0
        started = time.time()

        while True:
            if self.stop_event.is_set():
                self._terminate_active_process()
                return False, "Robot speaker stopped."

            if self.interrupt_event.is_set():
                self.interrupt_event.clear()
                self._terminate_active_process()
                return False, "Robot speaker interrupted."

            return_code = proc.poll()
            if return_code is not None:
                break

            if (time.time() - started) > timeout_seconds:
                self._terminate_active_process()
                return False, f"Robot speaker timed out after {timeout_seconds:.1f}s."

            time.sleep(0.05)

        self._set_active_process(None)
        stdout_text = ""
        stderr_text = ""
        try:
            stdout_text, stderr_text = proc.communicate(timeout=0.2)
        except Exception:
            pass

        if return_code != 0:
            detail = (stderr_text or stdout_text or "").strip()
            if detail:
                detail = f" {detail}"
            return False, f"Robot speaker exited with code {return_code}.{detail}"

        return True, "Robot speaker spoke."

    def _speak_with_espeak(self, text: str) -> tuple[bool, str]:
        command = ["espeak", "-s", str(self.rate)]
        if self.voice_name:
            command.extend(["-v", self.voice_name])
        command.append(text)
        return self._speak_with_subprocess(command)

    def _speak_with_say(self, text: str) -> tuple[bool, str]:
        command = ["say", "-r", str(self.rate)]
        if self.voice_name:
            command.extend(["-v", self.voice_name])
        command.append(text)
        return self._speak_with_subprocess(command)

    def _speak_with_pyttsx3(self, text: str) -> tuple[bool, str]:
        if self._pyttsx3_module is None:
            return False, "pyttsx3 is not loaded."

        try:
            engine = self._pyttsx3_module.init()
            engine.setProperty("rate", self.rate)
            target_voice = self.voice_name.lower()
            if target_voice:
                for voice in engine.getProperty("voices"):
                    voice_name = str(getattr(voice, "name", "")).lower()
                    if target_voice in voice_name:
                        engine.setProperty("voice", voice.id)
                        break
            engine.say(text)
            engine.runAndWait()
            engine.stop()
            return True, "Robot speaker spoke."
        except Exception as exc:
            return False, f"Robot speaker playback failed: {exc}"

    def _build_audio_command(self, audio_path: str, loop: bool) -> list[str]:
        if self.audio_backend_name == "ffplay":
            command = ["ffplay", "-nodisp", "-autoexit", "-loglevel", "error"]
            if loop:
                command.extend(["-loop", "0"])
            command.append(audio_path)
            return command

        if self.audio_backend_name == "paplay":
            return ["paplay", audio_path]

        if self.audio_backend_name == "aplay":
            return ["aplay", "-q", audio_path]

        if self.audio_backend_name == "afplay":
            return ["afplay", audio_path]

        if self.audio_backend_name == "play":
            return ["play", "-q", audio_path]

        raise RuntimeError("No robot speaker audio playback backend is available.")

    def _play_audio_file(self, audio_path: str, loop: bool) -> tuple[bool, str]:
        if self.audio_backend_name == "unavailable":
            return False, "No robot speaker audio playback backend is available."

        if self.audio_backend_name == "ffplay":
            return self._speak_with_subprocess(self._build_audio_command(audio_path, loop))

        while True:
            ok, message = self._speak_with_subprocess(self._build_audio_command(audio_path, False))
            if not ok or not loop:
                if ok and not loop:
                    return True, "Robot speaker played mirrored audio."
                return ok, message

            if self.stop_event.is_set():
                return False, "Robot speaker stopped."

            if self.interrupt_event.is_set():
                self.interrupt_event.clear()
                return False, "Robot speaker interrupted."

    def _play_audio_bytes(
        self,
        audio_bytes: bytes,
        audio_format: str,
        loop: bool,
    ) -> tuple[bool, str]:
        normalized_format = str(audio_format or "wav").strip().lower()
        suffix = ".wav" if normalized_format in ("wav", "wave") else f".{normalized_format}"
        temp_path = ""
        try:
            with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as handle:
                handle.write(audio_bytes)
                temp_path = handle.name

            return self._play_audio_file(temp_path, loop)
        except Exception as exc:
            return False, f"Robot speaker audio playback failed: {exc}"
        finally:
            if temp_path:
                try:
                    os.remove(temp_path)
                except OSError:
                    pass

    @staticmethod
    def _is_expected_interrupt(message: str) -> bool:
        lowered = str(message or "").strip().lower()
        return "interrupted" in lowered or "stopped" in lowered

    def _worker(self) -> None:
        while not self.stop_event.is_set():
            try:
                request = self.queue.get(timeout=0.2)
            except queue.Empty:
                continue

            if not request or request.get("kind") == "noop":
                continue

            self.state.set_speaking(True)
            try:
                if self.interrupt_event.is_set():
                    self.interrupt_event.clear()

                request_kind = str(request.get("kind", "tts")).strip().lower()
                if request_kind == "audio":
                    label = str(request.get("label", "audio")).strip() or "audio"
                    ok, message = self._play_audio_bytes(
                        bytes(request.get("audio_bytes", b"")),
                        str(request.get("audio_format", "wav")),
                        bool(request.get("loop", False)),
                    )
                    if ok:
                        self.state.set_message(
                            f"{message} AudioBackend={self.audio_backend_name}."
                        )
                        logging.info("Robot speaker played audio: %s", label)
                    elif self._is_expected_interrupt(message):
                        self.state.set_message(message)
                        logging.info("%s", message)
                    else:
                        self.state.set_error(message)
                        logging.error("%s", message)
                    continue

                text = str(request.get("text", "")).strip()
                if not text:
                    continue

                if self.backend_name == "espeak":
                    ok, message = self._speak_with_espeak(text)
                elif self.backend_name == "say":
                    ok, message = self._speak_with_say(text)
                else:
                    ok, message = self._speak_with_pyttsx3(text)

                if ok:
                    self.state.set_message(f"{message} Backend={self.backend_name}.")
                    logging.info("Robot speaker spoke: %s", text)
                elif self._is_expected_interrupt(message):
                    self.state.set_message(message)
                    logging.info("%s", message)
                else:
                    self.state.set_error(message)
                    logging.error("%s", message)
            finally:
                self.state.set_speaking(False)


class RobotSpeakerApp:
    def __init__(self, backend: str, rate: int, voice_name: str) -> None:
        self.state = SpeakerState()
        self.tts = RobotSpeakerTTS(backend=backend, rate=rate, voice_name=voice_name, state=self.state)

    def start(self) -> bool:
        return self.tts.start()

    def stop(self) -> None:
        self.tts.stop()

    def health(self) -> dict:
        return self.state.snapshot()


class RobotSpeakerHttpServer(ThreadingHTTPServer):
    def __init__(self, server_address, request_handler_class, app: RobotSpeakerApp) -> None:
        super().__init__(server_address, request_handler_class)
        self.app = app


class RobotSpeakerHandler(BaseHTTPRequestHandler):
    server: RobotSpeakerHttpServer

    def do_GET(self) -> None:
        path = urlparse(self.path).path
        if path == "/health":
            self._write_json(HTTPStatus.OK, self.server.app.health())
            return

        self._write_json(HTTPStatus.NOT_FOUND, {"ok": False, "message": f"Unknown path: {path}"})

    def do_POST(self) -> None:
        path = urlparse(self.path).path
        payload = self._read_json_body()

        if path == "/speak":
            text = str(payload.get("text", ""))
            interrupt = bool(payload.get("interrupt", False))
            ok, message = self.server.app.tts.speak(text, interrupt)
            status = HTTPStatus.OK if ok else HTTPStatus.SERVICE_UNAVAILABLE
            self._write_json(
                status,
                {
                    "ok": ok,
                    "message": message,
                    "backend": self.server.app.tts.backend_name,
                    "audio_backend": self.server.app.tts.audio_backend_name,
                },
            )
            return

        if path == "/play-audio":
            encoded_audio = str(payload.get("audio_base64", "")).strip()
            if not encoded_audio:
                self._write_json(
                    HTTPStatus.BAD_REQUEST,
                    {"ok": False, "message": "audio_base64 is required."},
                )
                return

            try:
                audio_bytes = base64.b64decode(encoded_audio, validate=True)
            except Exception:
                self._write_json(
                    HTTPStatus.BAD_REQUEST,
                    {"ok": False, "message": "audio_base64 is not valid base64."},
                )
                return

            ok, message = self.server.app.tts.play_audio(
                audio_bytes=audio_bytes,
                audio_format=str(payload.get("format", "wav")),
                interrupt=bool(payload.get("interrupt", False)),
                loop=bool(payload.get("loop", False)),
                label=str(payload.get("label", "")),
            )
            status = HTTPStatus.OK if ok else HTTPStatus.SERVICE_UNAVAILABLE
            self._write_json(
                status,
                {
                    "ok": ok,
                    "message": message,
                    "backend": self.server.app.tts.backend_name,
                    "audio_backend": self.server.app.tts.audio_backend_name,
                },
            )
            return

        if path == "/stop":
            stop_reason = str(payload.get("reason", "")).strip()
            ok, message = self.server.app.tts.interrupt()
            if stop_reason:
                logging.info("Robot speaker stop requested: %s", stop_reason)
            self._write_json(
                HTTPStatus.OK if ok else HTTPStatus.SERVICE_UNAVAILABLE,
                {
                    "ok": ok,
                    "message": message,
                    "backend": self.server.app.tts.backend_name,
                    "audio_backend": self.server.app.tts.audio_backend_name,
                },
            )
            return

        self._write_json(HTTPStatus.NOT_FOUND, {"ok": False, "message": f"Unknown path: {path}"})

    def log_message(self, format: str, *args) -> None:
        logging.info("%s - %s", self.address_string(), format % args)

    def _read_json_body(self) -> dict:
        length = max(0, int(self.headers.get("Content-Length", "0")))
        if length <= 0:
            return {}

        raw = self.rfile.read(length)
        if not raw:
            return {}

        try:
            payload = json.loads(raw.decode("utf-8"))
            return payload if isinstance(payload, dict) else {}
        except Exception:
            return {}

    def _write_json(self, status: HTTPStatus, payload: dict) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(int(status))
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Reachy robot speaker HTTP server")
    parser.add_argument("--bind-host", default="0.0.0.0", help="Bind host. Default: 0.0.0.0")
    parser.add_argument("--bind-port", type=int, default=8101, help="Bind port. Default: 8101")
    parser.add_argument(
        "--tts-backend",
        default="auto",
        help="Backend: auto, espeak, pyttsx3, or say. Default: auto",
    )
    parser.add_argument("--tts-rate", type=int, default=175, help="Speech rate. Default: 175")
    parser.add_argument("--tts-voice-name", default="", help="Optional voice name filter")
    parser.add_argument(
        "--log-level",
        default="info",
        help="Log level: debug, info, warning, error. Default: info",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    logging.basicConfig(
        level=getattr(logging, str(args.log_level).strip().upper(), logging.INFO),
        format="[reachy-robot-speaker] %(levelname)s %(message)s",
    )

    app = RobotSpeakerApp(
        backend=args.tts_backend,
        rate=args.tts_rate,
        voice_name=args.tts_voice_name,
    )
    if not app.start():
        return 1

    server = RobotSpeakerHttpServer(
        (str(args.bind_host).strip(), max(1, min(65535, int(args.bind_port)))),
        RobotSpeakerHandler,
        app,
    )

    logging.info(
        "Reachy robot speaker server listening on http://%s:%s",
        args.bind_host,
        args.bind_port,
    )
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        logging.info("Stopping Reachy robot speaker server.")
    finally:
        server.server_close()
        app.stop()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
