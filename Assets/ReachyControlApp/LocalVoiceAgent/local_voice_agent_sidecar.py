#!/usr/bin/env python3
"""
Local Reachy voice-agent sidecar.

Endpoints used by Unity:
- GET  /intent
- POST /speak
- POST /help

Testing endpoints:
- GET  /health
- GET  /logs
- POST /inject_transcript
- POST /inject_intent
- POST /listening
"""

from __future__ import annotations

import argparse
import json
import logging
import subprocess
import queue
import re
import sys
import threading
import time
from collections import deque
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse


DEFAULT_POSES = [
    "Neutral Arms",
    "T-Pose",
    "Hello Pose A",
    "Hello Pose B",
    "Hello Pose C",
    "Hello Pose D",
]

DEFAULT_JOINTS = [
    "r_shoulder_pitch",
    "r_shoulder_roll",
    "r_arm_yaw",
    "r_elbow_pitch",
    "r_forearm_yaw",
    "r_wrist_pitch",
    "r_wrist_roll",
    "r_gripper",
    "l_shoulder_pitch",
    "l_shoulder_roll",
    "l_arm_yaw",
    "l_elbow_pitch",
    "l_forearm_yaw",
    "l_wrist_pitch",
    "l_wrist_roll",
    "l_gripper",
]

DEFAULT_SHOW_MOVEMENT_SYNONYMS = [
    "show movement",
    "show motion",
    "do something",
    "do anything",
    "move",
    "move around",
    "make a move",
    "do a movement",
    "do a motion",
    "show me movement",
    "show me motion",
    "perform movement",
    "perform a movement",
    "perform motion",
    "make it move",
    "move a bit",
    "start moving",
    "do random movement",
    "do random motion",
    "surprise me",
]

DEFAULT_HELP_SYNONYMS = [
    "help",
    "help me",
    "i need help",
    "need help",
    "can you help",
    "please help",
    "give me help",
    "voice help",
    "show commands",
    "show me commands",
    "list commands",
    "list voice commands",
    "what can i say",
    "what commands are available",
    "what can you do",
    "how do i use this",
    "how to use this",
    "give instructions",
    "usage instructions",
    "guide me",
    "i need guidance",
    "show help",
    "open help",
    "display help",
    "need instructions",
    "how does this work",
    "teach me",
    "walk me through commands",
    "command list",
    "available commands",
    "help with commands",
]

DEFAULT_HELLO_SYNONYMS = [
    "hello",
    "hello there",
    "hi",
    "hi there",
    "hey",
    "hey there",
    "hey robot",
    "hello robot",
    "hi robot",
    "hey reachy",
    "hello reachy",
    "hi reachy",
    "greetings",
    "greetings robot",
    "good morning",
    "good afternoon",
    "good evening",
    "say hello",
    "greet me",
    "do a greeting",
]

DEFAULT_WHO_ARE_YOU_SYNONYMS = [
    "who are you",
    "what are you",
    "what are you exactly",
    "what is this assistant",
    "what is this agent",
    "who am i talking to",
    "identify yourself",
    "tell me who you are",
    "tell me what you are",
    "what is your name",
    "whats your name",
    "who is this",
    "are you a robot",
    "are you an assistant",
    "are you ai",
    "what kind of assistant are you",
    "what kind of ai are you",
    "who is speaking",
    "introduce yourself",
    "tell me about yourself",
]

DEFAULT_STOP_MOTION_SYNONYMS = [
    "stop",
    "stop now",
    "stop motion",
    "stop moving",
    "halt",
    "halt now",
    "halt motion",
    "halt movement",
    "abort",
    "abort command",
    "cancel",
    "cancel motion",
    "cancel command",
    "emergency stop",
    "hard stop",
    "freeze",
    "freeze motion",
    "cease movement",
    "terminate motion",
    "end movement",
    "stop immediately",
]

DEFAULT_CONFIRM_PENDING_SYNONYMS = [
    "confirm",
    "confirm it",
    "confirm command",
    "yes",
    "yes do it",
    "yes execute",
    "go ahead",
    "go for it",
    "proceed",
    "approved",
    "approve it",
    "execute it",
    "run it",
    "do it",
    "sounds good",
    "looks good",
    "ok do it",
    "okay proceed",
    "affirmative",
    "that is right",
]

DEFAULT_REJECT_PENDING_SYNONYMS = [
    "reject",
    "reject it",
    "decline",
    "decline it",
    "deny",
    "deny it",
    "no",
    "no thanks",
    "do not",
    "do not do it",
    "do not proceed",
    "do not execute",
    "not now",
    "negative",
    "skip it",
    "dismiss that",
    "forget it",
    "hold off",
    "never mind",
    "i reject that",
]

DEFAULT_DISCONNECT_ROBOT_SYNONYMS = [
    "disconnect",
    "disconnect robot",
    "disconnect now",
    "go offline",
    "switch offline",
    "take robot offline",
    "end connection",
    "close connection",
    "drop connection",
    "cut connection",
    "disconnect from robot",
    "shut down connection",
    "terminate connection",
    "leave session",
    "end session",
    "disconnect session",
    "unpair robot",
    "go disconnected",
    "disconnect please",
    "disconnect link",
]

DEFAULT_CONNECT_ROBOT_SYNONYMS = [
    "connect",
    "connect robot",
    "connect now",
    "reconnect",
    "reconnect robot",
    "go online",
    "come online",
    "bring robot online",
    "start connection",
    "open connection",
    "establish connection",
    "pair robot",
    "link robot",
    "join session",
    "connect please",
    "resume connection",
    "restore connection",
    "reconnect now",
    "connect to robot",
    "start robot connection",
]

DEFAULT_STATUS_SYNONYMS = [
    "status",
    "robot status",
    "connection status",
    "show status",
    "check status",
    "status report",
    "what is status",
    "what is the status",
    "are you connected",
    "is robot connected",
    "connection state",
    "health status",
    "system status",
    "current status",
    "tell me status",
    "get status",
    "show connection",
    "check connection",
    "am i connected",
    "are we connected",
]

DEFAULT_MOVE_JOINT_TRIGGER_SYNONYMS = [
    "move",
    "set",
    "turn",
    "rotate",
    "bend",
    "joint",
    "adjust",
    "tilt",
    "twist",
    "lift",
    "raise",
    "lower",
    "drop",
    "flex",
    "extend",
    "position",
    "point",
    "pitch",
    "roll",
    "yaw",
    "swing",
    "angle",
]

DEFAULT_SET_POSE_TRIGGER_SYNONYMS = [
    "set pose",
    "change pose",
    "switch pose",
    "use pose",
    "apply pose",
    "go to pose",
    "activate pose",
    "load pose",
    "run pose",
    "do pose",
    "pick pose",
    "select pose",
    "choose pose",
    "make pose",
    "show pose",
    "assume pose",
    "strike pose",
    "set posture",
    "change posture",
    "set position",
]

SHORT_LONE_HELP_FALLBACK_MAX_CHARS = 8


def normalize(text: str) -> str:
    return "".join(ch.lower() for ch in (text or "") if ch.isalnum())


def tokenize(text: str) -> list[str]:
    return [tok for tok in re.split(r"[^a-zA-Z0-9_]+", (text or "").lower()) if tok]


def contains_word_sequence(words: list[str], sequence: list[str]) -> bool:
    if not words or not sequence or len(words) < len(sequence):
        return False

    limit = len(words) - len(sequence) + 1
    for idx in range(limit):
        if words[idx:idx + len(sequence)] == sequence:
            return True
    return False


def contains_any_word_sequence(words: list[str], sequences: list[list[str]]) -> bool:
    if not words or not sequences:
        return False
    for sequence in sequences:
        if contains_word_sequence(words, sequence):
            return True
    return False


def build_token_sets_from_phrases(phrases: list[str]) -> list[list[str]]:
    token_sets: list[list[str]] = []
    for phrase in phrases:
        tokens = tokenize(str(phrase))
        if tokens and tokens not in token_sets:
            token_sets.append(tokens)
    return token_sets


STOP_MOTION_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_STOP_MOTION_SYNONYMS)
CONFIRM_PENDING_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_CONFIRM_PENDING_SYNONYMS)
REJECT_PENDING_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_REJECT_PENDING_SYNONYMS)
DISCONNECT_ROBOT_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_DISCONNECT_ROBOT_SYNONYMS)
CONNECT_ROBOT_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_CONNECT_ROBOT_SYNONYMS)
STATUS_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_STATUS_SYNONYMS)
MOVE_JOINT_TRIGGER_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_MOVE_JOINT_TRIGGER_SYNONYMS)
SET_POSE_TRIGGER_TOKEN_SETS = build_token_sets_from_phrases(DEFAULT_SET_POSE_TRIGGER_SYNONYMS)


def transcript_mentions_known_joint(compact_transcript: str, known_joints: list[str]) -> bool:
    if not compact_transcript or not known_joints:
        return False
    for joint in known_joints:
        token = normalize(str(joint))
        if token and token in compact_transcript:
            return True
    return False


def looks_like_joint_specific_command(
    transcript: str,
    words: list[str],
    compact_transcript: str,
    config: dict,
) -> bool:
    has_joint_verb = contains_any_word_sequence(words, MOVE_JOINT_TRIGGER_TOKEN_SETS)
    if not has_joint_verb:
        return False

    known_joints = config.get("known_joints", [])
    mentions_known_joint = transcript_mentions_known_joint(compact_transcript, known_joints)
    has_joint_descriptor = any(
        w in words
        for w in (
            "joint",
            "degree",
            "degrees",
            "deg",
            "shoulder",
            "elbow",
            "wrist",
            "gripper",
            "arm",
            "neck",
            "head",
        )
    )
    has_target_angle = extract_joint_number(
        transcript,
        safe_mode=parse_bool(config.get("safe_numeric_parsing"), True),
        require_target_token=parse_bool(config.get("require_target_token_for_joint"), False),
    ) is not None

    return mentions_known_joint or has_joint_descriptor or has_target_angle


def is_short_confirm_or_reject(text: str, words: list[str]) -> bool:
    trimmed = (text or "").strip().lower()
    if trimmed in ("yes", "no"):
        return True
    return len(words) == 1 and words[0] in ("yes", "no")


def is_short_lone_word_help_fallback_candidate(text: str, words: list[str]) -> bool:
    if len(words) != 1:
        return False
    if is_short_confirm_or_reject(text, words):
        return False
    token = str(words[0] or "").strip()
    return 0 < len(token) <= SHORT_LONE_HELP_FALLBACK_MAX_CHARS


def _clean_numeric_token(token: str) -> str:
    if not token:
        return ""
    return token.replace(",", ".").strip()


def extract_joint_number(text: str, safe_mode: bool, require_target_token: bool) -> float | None:
    if not text:
        return None

    parts = re.findall(r"[-+]?\d+(?:[.,]\d+)?|[A-Za-z_]+", text)
    if not parts:
        return None

    for idx, token in enumerate(parts):
        if not re.fullmatch(r"[-+]?\d+(?:[.,]\d+)?", token):
            continue

        previous = parts[idx - 1].lower() if idx > 0 else ""
        next_token = parts[idx + 1].lower() if idx + 1 < len(parts) else ""
        has_target_token = previous in ("to", "at", "around")
        has_degree_token = previous in ("degree", "degrees", "deg") or next_token in ("degree", "degrees", "deg")

        if safe_mode:
            if require_target_token and not has_target_token:
                continue
            if not has_target_token and not has_degree_token:
                continue

        cleaned = _clean_numeric_token(token)
        if not cleaned:
            continue

        try:
            return float(cleaned)
        except ValueError:
            continue

    return None


def parse_bool(value, default: bool) -> bool:
    if isinstance(value, bool):
        return value
    if value is None:
        return default
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        normalized = value.strip().lower()
        if normalized in ("1", "true", "yes", "on", "enabled"):
            return True
        if normalized in ("0", "false", "no", "off", "disabled"):
            return False
    return default


def normalize_phrase_list(value, fallback: list[str]) -> list[str]:
    source = value if isinstance(value, list) else []
    normalized: list[str] = []

    for item in source:
        phrase = " ".join(tokenize(str(item)))
        if phrase and phrase not in normalized:
            normalized.append(phrase)

    if normalized:
        return normalized

    fallback_normalized: list[str] = []
    for item in fallback:
        phrase = " ".join(tokenize(str(item)))
        if phrase and phrase not in fallback_normalized:
            fallback_normalized.append(phrase)
    return fallback_normalized


def load_config(path: Path) -> dict:
    config = {
        "bind_host": "127.0.0.1",
        "bind_port": 8099,
        "stt_backend": "vosk",
        "stt_model_path": "../../../.local_voice_models/vosk-model-small-en-us-0.15",
        "stt_sample_rate_hz": 16000,
        "transcript_default_confidence": 0.85,
        "intent_confidence_threshold": 0.78,
        "tts_backend": "pyttsx3",
        "tts_rate": 175,
        "tts_voice_name": "",
        "known_poses": list(DEFAULT_POSES),
        "known_joints": list(DEFAULT_JOINTS),
        "show_movement_synonyms": list(DEFAULT_SHOW_MOVEMENT_SYNONYMS),
        "help_synonyms": list(DEFAULT_HELP_SYNONYMS),
        "hello_synonyms": list(DEFAULT_HELLO_SYNONYMS),
        "who_are_you_synonyms": list(DEFAULT_WHO_ARE_YOU_SYNONYMS),
        "log_history_size": 200,
        "event_queue_size": 120,
        "help_context": "Reachy Unity app guidance only.",
        "help_model_backend": "rule_based",
        "help_model_path": "",
        "help_model_max_tokens": 96,
        "help_model_temperature": 0.2,
        "help_max_answer_chars": 360,
        "audio_input_device_name": "",
        "prefer_non_virtual_input_device": True,
        "start_listening_enabled": True,
        "min_transcript_chars": 4,
        "min_transcript_words": 1,
        "safe_numeric_parsing": True,
        "require_target_token_for_joint": False,
        "reject_out_of_range_joint_commands": True,
        "joint_min_degrees": -180.0,
        "joint_max_degrees": 180.0,
    }
    config["_config_dir"] = str(path.parent.resolve())

    if not path.exists():
        return config

    loaded = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(loaded, dict):
        raise RuntimeError(f"Config '{path}' must be a JSON object.")

    for key, value in loaded.items():
        if key in config:
            config[key] = value

    config["bind_host"] = str(config.get("bind_host", "127.0.0.1")).strip() or "127.0.0.1"
    config["bind_port"] = max(1, min(65535, int(config.get("bind_port", 8099))))
    config["stt_backend"] = str(config.get("stt_backend", "none")).strip().lower()
    config["tts_backend"] = str(config.get("tts_backend", "none")).strip().lower()
    config["stt_sample_rate_hz"] = max(8000, int(config.get("stt_sample_rate_hz", 16000)))
    config["transcript_default_confidence"] = max(0.0, min(1.0, float(config.get("transcript_default_confidence", 0.85))))
    config["intent_confidence_threshold"] = max(0.0, min(1.0, float(config.get("intent_confidence_threshold", 0.78))))
    config["tts_rate"] = max(60, min(320, int(config.get("tts_rate", 175))))
    config["log_history_size"] = max(20, min(1000, int(config.get("log_history_size", 200))))
    config["event_queue_size"] = max(10, min(1000, int(config.get("event_queue_size", 120))))

    poses = [str(x).strip() for x in config.get("known_poses", []) if str(x).strip()]
    joints = [str(x).strip() for x in config.get("known_joints", []) if str(x).strip()]
    config["known_poses"] = poses if poses else list(DEFAULT_POSES)
    config["known_joints"] = joints if joints else list(DEFAULT_JOINTS)
    config["show_movement_synonyms"] = normalize_phrase_list(
        config.get("show_movement_synonyms", []),
        DEFAULT_SHOW_MOVEMENT_SYNONYMS,
    )
    config["help_synonyms"] = normalize_phrase_list(
        config.get("help_synonyms", []),
        DEFAULT_HELP_SYNONYMS,
    )
    config["hello_synonyms"] = normalize_phrase_list(
        config.get("hello_synonyms", []),
        DEFAULT_HELLO_SYNONYMS,
    )
    config["who_are_you_synonyms"] = normalize_phrase_list(
        config.get("who_are_you_synonyms", []),
        DEFAULT_WHO_ARE_YOU_SYNONYMS,
    )
    config["help_context"] = str(config.get("help_context", "")).strip() or "Reachy Unity app guidance only."
    config["help_model_backend"] = str(config.get("help_model_backend", "rule_based")).strip().lower()
    if config["help_model_backend"] not in ("rule_based", "llama_cpp"):
        config["help_model_backend"] = "rule_based"
    config["help_model_path"] = str(config.get("help_model_path", "")).strip()
    config["help_model_max_tokens"] = max(16, min(512, int(config.get("help_model_max_tokens", 96))))
    config["help_model_temperature"] = max(
        0.0, min(1.5, float(config.get("help_model_temperature", 0.2))))
    config["help_max_answer_chars"] = max(80, min(1200, int(config.get("help_max_answer_chars", 360))))
    config["audio_input_device_name"] = str(config.get("audio_input_device_name", "")).strip()
    config["prefer_non_virtual_input_device"] = parse_bool(
        config.get("prefer_non_virtual_input_device"),
        True)
    config["start_listening_enabled"] = parse_bool(config.get("start_listening_enabled"), True)
    config["min_transcript_chars"] = max(0, int(config.get("min_transcript_chars", 4)))
    config["min_transcript_words"] = max(0, int(config.get("min_transcript_words", 1)))
    config["safe_numeric_parsing"] = parse_bool(config.get("safe_numeric_parsing"), True)
    config["require_target_token_for_joint"] = parse_bool(config.get("require_target_token_for_joint"), False)
    config["reject_out_of_range_joint_commands"] = parse_bool(
        config.get("reject_out_of_range_joint_commands"),
        True)
    config["joint_min_degrees"] = float(config.get("joint_min_degrees", -180.0))
    config["joint_max_degrees"] = float(config.get("joint_max_degrees", 180.0))
    if config["joint_min_degrees"] > config["joint_max_degrees"]:
        low = config["joint_max_degrees"]
        high = config["joint_min_degrees"]
        config["joint_min_degrees"] = low
        config["joint_max_degrees"] = high
    return config


def resolve_config_relative_path(config: dict, raw_path: str) -> Path:
    text = str(raw_path or "").strip()
    candidate = Path(text)
    if candidate.is_absolute():
        return candidate

    search_roots: list[Path] = []
    config_dir = str(config.get("_config_dir", "")).strip()
    if config_dir:
        search_roots.append(Path(config_dir))
    search_roots.append(Path.cwd())
    search_roots.append(Path(__file__).resolve().parent)

    # In packaged/runtime scenarios, config location may differ from project/model location.
    # Walk a few parent levels so relative model paths still resolve when possible.
    expanded_roots: list[Path] = []
    for root in search_roots:
        try:
            cursor = root.resolve()
        except Exception:
            cursor = root
        for _ in range(0, 7):
            if cursor in expanded_roots:
                break
            expanded_roots.append(cursor)
            parent = cursor.parent
            if parent == cursor:
                break
            cursor = parent

    candidates: list[Path] = []
    for root in expanded_roots:
        resolved = (root / candidate).resolve()
        if resolved not in candidates:
            candidates.append(resolved)

    for path in candidates:
        if path.exists():
            return path

    if config_dir:
        return (Path(config_dir) / candidate).resolve()
    return (Path.cwd() / candidate).resolve()


class Parser:
    def __init__(self, config: dict) -> None:
        self.config = config
        self.help_phrase_tokens = self._build_help_phrase_tokens()
        self.hello_phrase_tokens = self._build_hello_phrase_tokens()
        self.who_are_you_phrase_tokens = self._build_who_are_you_phrase_tokens()
        self.show_movement_phrase_tokens = self._build_show_movement_phrase_tokens()

    def parse(self, transcript: str, confidence: float) -> tuple[dict | None, str]:
        text = (transcript or "").strip()
        words = tokenize(transcript)
        if not words:
            return None, "Transcript is empty."

        confidence = max(0.0, min(1.0, confidence if confidence > 0 else 0.85))

        def fallback_to_help(reason: str) -> tuple[dict | None, str]:
            return self._intent("help", transcript, confidence, False), reason

        short_confirm_or_reject = is_short_confirm_or_reject(text, words)
        short_lone_help_fallback = is_short_lone_word_help_fallback_candidate(text, words)
        min_chars = max(0, int(self.config.get("min_transcript_chars", 4)))
        if len(text) < min_chars and not short_confirm_or_reject and not short_lone_help_fallback:
            return fallback_to_help(f"help_fallback_shorter_than_{min_chars}_chars")

        min_words = max(0, int(self.config.get("min_transcript_words", 1)))
        if len(words) < min_words and not short_confirm_or_reject and not short_lone_help_fallback:
            return fallback_to_help(f"help_fallback_fewer_than_{min_words}_words")
        compact = normalize(transcript)

        if self._is_stop_motion_phrase(words):
            return self._intent("stop_motion", transcript, confidence, False), "stop_motion"
        if self._is_reject_pending_phrase(words):
            return self._intent("reject_pending", transcript, confidence, False), "reject_pending"
        if self._is_confirm_pending_phrase(words):
            return self._intent("confirm_pending", transcript, confidence, False), "confirm_pending"
        if self._is_disconnect_robot_phrase(words):
            return self._intent("disconnect_robot", transcript, confidence, False), "disconnect_robot"
        if self._is_connect_robot_phrase(words):
            return self._intent("connect_robot", transcript, confidence, False), "connect_robot"
        if self._is_status_phrase(words):
            return self._intent("status", transcript, confidence, False), "status"
        if self._is_who_are_you_phrase(words):
            return self._intent("who_are_you", transcript, confidence, False), "who_are_you"
        if self._is_help_phrase(words):
            return self._intent("help", transcript, confidence, False), "help"
        show_movement_phrase = self._is_show_movement_phrase(transcript, words, compact)
        if show_movement_phrase:
            return self._intent("show_movement", transcript, confidence, True), "show_movement"

        likely_pose = self._is_likely_set_pose_phrase(words)
        best_pose = ""
        best_len = 0
        for pose in self.config["known_poses"]:
            token = normalize(pose)
            if token and token in compact and len(token) > best_len:
                best_pose = pose
                best_len = len(token)
        if best_pose:
            intent = self._intent("set_pose", transcript, confidence, True)
            intent["pose_name"] = best_pose
            return intent, "set_pose"

        likely_joint = self._is_likely_move_joint_phrase(words)
        if likely_joint:
            number = extract_joint_number(
                transcript,
                safe_mode=parse_bool(self.config.get("safe_numeric_parsing"), True),
                require_target_token=parse_bool(self.config.get("require_target_token_for_joint"), False))
            if number is None:
                if parse_bool(self.config.get("safe_numeric_parsing"), True):
                    return fallback_to_help("help_fallback_move_joint_missing_numeric_target")
            if number is None:
                return fallback_to_help("help_fallback_move_joint_no_numeric_target")

            if parse_bool(self.config.get("reject_out_of_range_joint_commands"), True):
                joint_min = float(self.config.get("joint_min_degrees", -180.0))
                joint_max = float(self.config.get("joint_max_degrees", 180.0))
                if number < joint_min or number > joint_max:
                    return fallback_to_help(
                        f"help_fallback_move_joint_target_outside_{joint_min:.1f}_{joint_max:.1f}"
                    )

            best_joint = ""
            best_len = 0
            for joint in self.config["known_joints"]:
                token = normalize(joint)
                if token and token in compact and len(token) > best_len:
                    best_joint = joint
                    best_len = len(token)
            if best_joint:
                intent = self._intent("move_joint", transcript, confidence, True)
                intent["joint_name"] = best_joint
                intent["joint_degrees"] = float(number)
                return intent, "move_joint"

        if likely_pose:
            return fallback_to_help("help_fallback_pose_name_not_found")

        if self._is_hello_phrase(words):
            return self._intent("hello", transcript, confidence, False), "hello"

        if short_lone_help_fallback:
            return self._intent("help", transcript, confidence, False), "help_fallback_short_lone_word"

        return fallback_to_help("help_fallback_unrecognized_transcript")

    def _build_help_phrase_tokens(self) -> list[list[str]]:
        phrases = normalize_phrase_list(
            self.config.get("help_synonyms", []),
            DEFAULT_HELP_SYNONYMS,
        )
        token_sets: list[list[str]] = []
        for phrase in phrases:
            tokens = tokenize(phrase)
            if tokens and tokens not in token_sets:
                token_sets.append(tokens)
        return token_sets

    def _is_help_phrase(self, words: list[str]) -> bool:
        if contains_any_word_sequence(words, self.help_phrase_tokens):
            return True
        return ("how" in words and ("use" in words or "do" in words)) or ("what" in words and "say" in words)

    def _build_hello_phrase_tokens(self) -> list[list[str]]:
        phrases = normalize_phrase_list(
            self.config.get("hello_synonyms", []),
            DEFAULT_HELLO_SYNONYMS,
        )
        token_sets: list[list[str]] = []
        for phrase in phrases:
            tokens = tokenize(phrase)
            if tokens and tokens not in token_sets:
                token_sets.append(tokens)
        return token_sets

    def _is_hello_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, self.hello_phrase_tokens)

    def _build_who_are_you_phrase_tokens(self) -> list[list[str]]:
        phrases = normalize_phrase_list(
            self.config.get("who_are_you_synonyms", []),
            DEFAULT_WHO_ARE_YOU_SYNONYMS,
        )
        token_sets: list[list[str]] = []
        for phrase in phrases:
            tokens = tokenize(phrase)
            if tokens and tokens not in token_sets:
                token_sets.append(tokens)
        return token_sets

    def _is_who_are_you_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, self.who_are_you_phrase_tokens)

    def _is_stop_motion_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, STOP_MOTION_TOKEN_SETS)

    def _is_confirm_pending_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, CONFIRM_PENDING_TOKEN_SETS)

    def _is_reject_pending_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, REJECT_PENDING_TOKEN_SETS)

    def _is_disconnect_robot_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, DISCONNECT_ROBOT_TOKEN_SETS)

    def _is_connect_robot_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, CONNECT_ROBOT_TOKEN_SETS)

    def _is_status_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, STATUS_TOKEN_SETS)

    def _is_likely_move_joint_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, MOVE_JOINT_TRIGGER_TOKEN_SETS)

    def _is_likely_set_pose_phrase(self, words: list[str]) -> bool:
        return contains_any_word_sequence(words, SET_POSE_TRIGGER_TOKEN_SETS)

    def _build_show_movement_phrase_tokens(self) -> list[list[str]]:
        phrases = normalize_phrase_list(
            self.config.get("show_movement_synonyms", []),
            DEFAULT_SHOW_MOVEMENT_SYNONYMS,
        )
        token_sets: list[list[str]] = []
        for phrase in phrases:
            tokens = tokenize(phrase)
            if tokens and tokens not in token_sets:
                token_sets.append(tokens)
        return token_sets

    def _is_show_movement_phrase(self, transcript: str, words: list[str], compact: str) -> bool:
        if not contains_any_word_sequence(words, self.show_movement_phrase_tokens):
            return False
        return not looks_like_joint_specific_command(transcript, words, compact, self.config)

    def _intent(self, name: str, spoken: str, confidence: float, requires_confirmation: bool) -> dict:
        if name in ("set_pose", "move_joint", "show_movement") and confidence < float(self.config["intent_confidence_threshold"]):
            requires_confirmation = True
        return {
            "type": "robot_command",
            "intent": name,
            "pose_name": "",
            "joint_name": "",
            "joint_degrees": 0.0,
            "confidence": confidence,
            "requires_confirmation": requires_confirmation,
            "spoken_text": spoken,
            "transcript_is_final": True,
        }


class State:
    def __init__(self, config: dict) -> None:
        self.config = config
        self.lock = threading.Lock()
        self.events = deque(maxlen=int(config["event_queue_size"]))
        self.logs = deque(maxlen=int(config["log_history_size"]))
        self.pending_partial = None
        self.started_at = time.time()
        self.mic_active = False
        self.listening = False
        self.accept_listening = parse_bool(config.get("start_listening_enabled"), True)
        self.stt_backend = str(config["stt_backend"])
        self.last_transcript = ""
        self.last_transcript_confidence = 0.0
        self.last_transcript_is_final = True
        self.last_message = "Sidecar started."
        self.last_error = ""
        self.last_help_answer = ""
        self.tts_speaking = False
        self.selected_input_device_name = ""
        self.selected_input_device_index = -1

    def log(self, level: str, message: str) -> None:
        msg = (message or "").strip() or "n/a"
        lvl = (level or "info").lower().strip() or "info"
        row = {
            "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "level": lvl,
            "message": msg,
        }
        with self.lock:
            self.logs.append(row)
            self.last_message = msg
            if lvl in ("error", "critical"):
                self.last_error = msg

    def set_runtime(self, mic_active: bool, listening: bool, backend: str | None = None) -> None:
        with self.lock:
            self.mic_active = bool(mic_active)
            self.listening = bool(listening)
            if backend is not None:
                self.stt_backend = backend

    def set_listening_enabled(self, enabled: bool) -> None:
        with self.lock:
            self.accept_listening = bool(enabled)
            if not enabled:
                self.listening = False
                self.pending_partial = None
            elif self.mic_active:
                self.listening = True

    def set_selected_input_device(self, device_index: int, device_name: str) -> None:
        with self.lock:
            self.selected_input_device_index = int(device_index)
            self.selected_input_device_name = str(device_name or "").strip()

    def set_tts_speaking(self, speaking: bool) -> None:
        with self.lock:
            self.tts_speaking = bool(speaking)

    def is_tts_speaking(self) -> bool:
        with self.lock:
            return bool(self.tts_speaking)

    def is_listening_enabled(self) -> bool:
        with self.lock:
            return bool(self.accept_listening)

    def clear_pending_partial(self) -> None:
        with self.lock:
            self.pending_partial = None

    def process_transcript(self, transcript: str, confidence: float, is_final: bool, parser: Parser) -> dict:
        text = (transcript or "").strip()
        if not text:
            return {"ok": False, "message": "Transcript is empty."}

        confidence = max(0.0, min(1.0, float(confidence)))

        with self.lock:
            if not self.accept_listening:
                return {"ok": False, "message": "Listening is disabled."}
            self.last_transcript = text
            self.last_transcript_confidence = confidence
            self.last_transcript_is_final = bool(is_final)
            if not is_final:
                self.pending_partial = {
                    "has_intent": False,
                    "transcript": text,
                    "confidence": confidence,
                    "transcript_is_final": False,
                    "message": "Partial transcript.",
                }
                return {"ok": True, "message": "Stored partial transcript.", "intent_name": ""}

        parsed, parse_message = parser.parse(text, confidence)
        event = {
            "has_intent": parsed is not None,
            "transcript": text,
            "confidence": confidence,
            "transcript_is_final": True,
            "message": "Parsed transcript into intent." if parsed is not None else parse_message,
        }
        if parsed is not None:
            event["intent"] = parsed

        with self.lock:
            self.events.append(event)
            self.pending_partial = None

        return {
            "ok": True,
            "message": event["message"],
            "intent_name": parsed["intent"] if parsed is not None else "",
            "has_intent": parsed is not None,
        }

    def inject_intent(self, payload: dict) -> dict:
        intent_name = str(payload.get("intent", "")).strip()
        if not intent_name:
            return {"ok": False, "message": "Intent name is required."}

        intent = {
            "type": str(payload.get("type", "robot_command")),
            "intent": intent_name,
            "pose_name": str(payload.get("pose_name", "")).strip(),
            "joint_name": str(payload.get("joint_name", "")).strip(),
            "joint_degrees": float(payload.get("joint_degrees", 0.0) or 0.0),
            "confidence": max(0.0, min(1.0, float(payload.get("confidence", 0.95) or 0.95))),
            "requires_confirmation": bool(payload.get("requires_confirmation", False)),
            "spoken_text": str(payload.get("spoken_text", "")).strip(),
            "transcript_is_final": bool(payload.get("transcript_is_final", True)),
        }

        with self.lock:
            self.events.append(
                {
                    "has_intent": True,
                    "intent": intent,
                    "transcript": intent["spoken_text"],
                    "confidence": intent["confidence"],
                    "transcript_is_final": intent["transcript_is_final"],
                    "message": "Intent enqueued via inject_intent.",
                }
            )
        return {"ok": True, "message": "Intent enqueued.", "intent_name": intent_name}

    def poll(self) -> dict:
        with self.lock:
            base = {
                "mic_active": self.mic_active,
                "listening": self.listening,
                "stt_backend": self.stt_backend,
                "has_intent": False,
                "message": "Voice bridge poll OK (idle).",
            }
            if self.events:
                item = self.events.popleft()
                data = dict(base)
                data.update(item)
                return data
            if self.pending_partial is not None:
                data = dict(base)
                data.update(self.pending_partial)
                return data
            return base

    def health(self) -> dict:
        with self.lock:
            return {
                "ok": True,
                "uptime_seconds": int(max(0, time.time() - self.started_at)),
                "event_queue_size": len(self.events),
                "mic_active": self.mic_active,
                "listening": self.listening,
                "tts_speaking": self.tts_speaking,
                "accept_listening": self.accept_listening,
                "stt_backend": self.stt_backend,
                "help_model_backend": str(self.config.get("help_model_backend", "rule_based")),
                "help_model_path_set": bool(str(self.config.get("help_model_path", "")).strip()),
                "last_transcript": self.last_transcript,
                "last_transcript_confidence": self.last_transcript_confidence,
                "last_transcript_is_final": self.last_transcript_is_final,
                "selected_input_device_index": self.selected_input_device_index,
                "selected_input_device_name": self.selected_input_device_name,
                "last_help_answer": self.last_help_answer,
                "last_message": self.last_message,
                "last_error": self.last_error,
            }

    def log_rows(self, count: int = 60) -> dict:
        with self.lock:
            rows = list(self.logs)[-max(1, min(500, int(count))):]
            return {"ok": True, "count": len(rows), "entries": rows}


class TTS:
    def __init__(self, config: dict, state: State) -> None:
        self.config = config
        self.state = state
        self.enabled = str(config["tts_backend"]) == "pyttsx3"
        self.queue = queue.Queue(maxsize=200)
        self.stop_event = threading.Event()
        self.interrupt_event = threading.Event()
        self._active_process = None
        self._active_process_lock = threading.Lock()
        self.thread = None
        self._pyttsx3_module = None

    def start(self) -> None:
        if not self.enabled:
            self.state.log("info", "TTS disabled (set tts_backend to 'pyttsx3' to enable).")
            return
        self.thread = threading.Thread(target=self._worker, daemon=True, name="tts-worker")
        self.thread.start()

    def stop(self) -> None:
        self.stop_event.set()
        self.state.set_tts_speaking(False)
        self.interrupt_event.set()
        self._terminate_active_process()
        try:
            self.queue.put_nowait(("", False))
        except queue.Full:
            pass
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=1.0)

    def speak(self, text: str, interrupt: bool) -> tuple[bool, str]:
        text = (text or "").strip()
        if not text:
            return False, "Speech text is empty."
        if not self.enabled:
            self.state.log("info", f"TTS stub: {text}")
            return True, "TTS backend disabled; accepted as no-op."

        try:
            if interrupt:
                self.interrupt_event.set()
                self._clear_queue_nonblocking()
                self._terminate_active_process()
            self.queue.put_nowait((text, interrupt))
            return True, "Speech queued."
        except queue.Full:
            return False, "TTS queue is full."

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

    @staticmethod
    def _rate_to_windows_sapi_scale(tts_rate: int) -> int:
        # pyttsx3 defaults around ~175 wpm; map to SAPI's [-10, +10] scale.
        normalized = int(round((int(tts_rate) - 175) / 12.5))
        return max(-10, min(10, normalized))

    def _build_windows_sapi_script(self, text: str) -> str:
        rate = self._rate_to_windows_sapi_scale(int(self.config.get("tts_rate", 175)))
        voice_name = str(self.config.get("tts_voice_name", "")).strip()
        escaped_text = (text or "").replace("'", "''")
        escaped_voice = voice_name.replace("'", "''")
        return (
            "Add-Type -AssemblyName System.Speech; "
            "$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
            f"$synth.Rate = {rate}; "
            f"$voiceToken = '{escaped_voice}'.ToLowerInvariant(); "
            "if ($voiceToken) { "
            "  foreach ($voice in $synth.GetInstalledVoices()) { "
            "    $name = $voice.VoiceInfo.Name; "
            "    if ($name.ToLowerInvariant().Contains($voiceToken)) { "
            "      $synth.SelectVoice($name); "
            "      break; "
            "    } "
            "  } "
            "} "
            f"$synth.Speak('{escaped_text}'); "
            "$synth.Dispose();"
        )

    def _speak_with_subprocess(self, text: str) -> tuple[bool, str]:
        timeout_seconds = max(5.0, min(60.0, float(self.config.get("tts_timeout_seconds", 20.0))))
        script = self._build_windows_sapi_script(text)
        cmd = [
            "powershell",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            script,
        ]

        creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        try:
            proc = subprocess.Popen(
                cmd,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=creationflags,
                text=True,
            )
        except Exception as exc:
            return False, f"TTS subprocess start failed: {exc}"

        self._set_active_process(proc)
        started = time.time()
        while True:
            if self.stop_event.is_set():
                self._terminate_active_process()
                return False, "TTS stopped."
            if self.interrupt_event.is_set():
                self.interrupt_event.clear()
                self._terminate_active_process()
                return False, "TTS interrupted."

            return_code = proc.poll()
            if return_code is not None:
                break

            if (time.time() - started) > timeout_seconds:
                self._terminate_active_process()
                return False, f"TTS subprocess timed out after {timeout_seconds:.1f}s."

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
            return False, f"TTS subprocess exited with code {return_code}.{detail}"

        return True, "TTS spoke."

    def _speak_with_fresh_engine(self, text: str) -> tuple[bool, str]:
        if self._pyttsx3_module is None:
            return False, "pyttsx3 is not loaded."

        try:
            engine = self._pyttsx3_module.init()
            engine.setProperty("rate", int(self.config.get("tts_rate", 175)))
            target_voice = str(self.config.get("tts_voice_name", "")).strip().lower()
            if target_voice:
                for voice in engine.getProperty("voices"):
                    if target_voice in str(voice.name).lower():
                        engine.setProperty("voice", voice.id)
                        break
            engine.say(text)
            engine.runAndWait()
            engine.stop()
            return True, "TTS spoke."
        except Exception as exc:
            return False, f"TTS playback failed: {exc}"

    def _worker(self) -> None:
        if sys.platform.startswith("win"):
            self.state.log("info", "TTS backend windows_sapi initialized.")
        else:
            try:
                import pyttsx3  # type: ignore
                self._pyttsx3_module = pyttsx3
                self.state.log("info", "TTS backend pyttsx3 initialized.")
            except Exception as exc:
                self.enabled = False
                self.state.log("error", f"pyttsx3 unavailable; TTS disabled: {exc}")
                return

        while not self.stop_event.is_set():
            try:
                text, _interrupt = self.queue.get(timeout=0.2)
            except queue.Empty:
                continue
            if not text:
                continue
            try:
                if self.interrupt_event.is_set():
                    self.interrupt_event.clear()
                self.state.set_tts_speaking(True)
                if sys.platform.startswith("win"):
                    ok, message = self._speak_with_subprocess(text)
                else:
                    ok, message = self._speak_with_fresh_engine(text)
                if ok:
                    self.state.log("info", f"TTS spoke: {text}")
                else:
                    self.state.log("error", message)
            finally:
                self.state.set_tts_speaking(False)


class VoskSTT:
    def __init__(self, config: dict, state: State, parser: Parser) -> None:
        self.config = config
        self.state = state
        self.parser = parser
        self.enabled = str(config["stt_backend"]) == "vosk"
        self.stop_event = threading.Event()
        self.thread = None

    def start(self) -> None:
        if not self.enabled:
            self.state.set_runtime(False, False, str(self.config["stt_backend"]))
            self.state.log("info", "STT disabled (set stt_backend to 'vosk' to enable).")
            return
        self.thread = threading.Thread(target=self._run, daemon=True, name="vosk-stt")
        self.thread.start()

    def stop(self) -> None:
        self.stop_event.set()
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=1.0)

    @staticmethod
    def _drain_audio_queue(audio_q: "queue.Queue[bytes]") -> int:
        drained = 0
        while True:
            try:
                audio_q.get_nowait()
                drained += 1
            except queue.Empty:
                return drained

    @staticmethod
    def _reset_recognizer(recognizer) -> None:
        try:
            reset_fn = getattr(recognizer, "Reset", None)
            if callable(reset_fn):
                reset_fn()
        except Exception:
            # Reset support is recognizer-version dependent; ignore failures.
            pass

    @staticmethod
    def _is_virtual_input_name(name: str) -> bool:
        lowered = (name or "").strip().lower()
        if not lowered:
            return False

        virtual_tokens = (
            "virtual",
            "stereo mix",
            "vb-audio",
            "voicemeeter",
            "cable output",
            "cable input",
            "loopback",
            "what u hear",
            "wave out",
            "wave out mix",
            "monitor",
            "obs",
            "ndi",
            "blackhole",
            "soundflower",
            "sunflower",
        )
        return any(tok in lowered for tok in virtual_tokens)

    def _score_input_device(
        self,
        device_name: str,
        preferred_name: str,
        prefer_non_virtual: bool,
    ) -> int:
        lowered = (device_name or "").strip().lower()
        score = 0
        is_virtual = self._is_virtual_input_name(lowered)
        if prefer_non_virtual:
            score += -220 if is_virtual else 120
        elif is_virtual:
            score -= 40

        preferred = (preferred_name or "").strip().lower()
        if preferred:
            if lowered == preferred:
                score += 900
            elif preferred in lowered or lowered in preferred:
                score += 320

        if "headset" in lowered:
            score += 30
        if "microphone" in lowered or "mic" in lowered:
            score += 20
        if "array" in lowered:
            score += 8
        return score

    def _select_input_device(self, sd_module) -> tuple[int | None, str, str]:
        try:
            devices = sd_module.query_devices()
        except Exception as exc:
            return None, "", f"Audio device query failed: {exc}"

        candidates: list[tuple[int, str]] = []
        for idx, device in enumerate(devices):
            try:
                max_input = int(device.get("max_input_channels", 0))
            except Exception:
                max_input = 0
            if max_input <= 0:
                continue
            name = str(device.get("name", "")).strip() or f"input_{idx}"
            candidates.append((idx, name))

        if not candidates:
            return None, "", "No audio input devices available."

        preferred_name = str(self.config.get("audio_input_device_name", "")).strip()
        prefer_non_virtual = parse_bool(
            self.config.get("prefer_non_virtual_input_device"),
            True)

        best_index = None
        best_name = ""
        best_score = -10**9
        for idx, name in candidates:
            score = self._score_input_device(name, preferred_name, prefer_non_virtual)
            if best_index is None or score > best_score:
                best_index = idx
                best_name = name
                best_score = score

        return best_index, best_name, ""

    def _run(self) -> None:
        try:
            import vosk  # type: ignore
            import sounddevice as sd  # type: ignore
        except Exception as exc:
            self.state.set_runtime(False, False, "none")
            self.state.log("error", f"Vosk/sounddevice unavailable; STT disabled: {exc}")
            return

        model_path = resolve_config_relative_path(self.config, self.config.get("stt_model_path", ""))
        if not model_path.exists():
            self.state.set_runtime(False, False, "none")
            self.state.log("error", f"Vosk model path does not exist: {model_path}")
            return

        try:
            model = vosk.Model(str(model_path))
            recognizer = vosk.KaldiRecognizer(model, int(self.config["stt_sample_rate_hz"]))
        except Exception as exc:
            self.state.set_runtime(False, False, "none")
            self.state.log("error", f"Failed to initialize Vosk model: {exc}")
            return

        selected_device_index, selected_device_name, select_err = self._select_input_device(sd)
        if selected_device_index is None:
            self.state.set_runtime(False, False, "none")
            self.state.log("error", f"Failed to select input device: {select_err}")
            return
        self.state.set_selected_input_device(selected_device_index, selected_device_name)

        audio_q = queue.Queue(maxsize=80)

        def callback(indata, _frames, _time_info, status):
            if status:
                self.state.log("warn", f"Audio callback status: {status}")
            try:
                audio_q.put_nowait(bytes(indata))
            except queue.Full:
                pass

        self.state.set_runtime(True, self.state.is_listening_enabled(), "vosk")
        self.state.log(
            "info",
            f"Vosk STT worker started (device [{selected_device_index}] {selected_device_name}).")

        try:
            with sd.RawInputStream(
                device=selected_device_index,
                samplerate=int(self.config["stt_sample_rate_hz"]),
                blocksize=4000,
                dtype="int16",
                channels=1,
                callback=callback,
            ):
                last_listening_state = None
                tts_gate_active = False
                while not self.stop_event.is_set():
                    try:
                        chunk = audio_q.get(timeout=0.25)
                    except queue.Empty:
                        listening_enabled = self.state.is_listening_enabled() and not self.state.is_tts_speaking()
                        if listening_enabled != last_listening_state:
                            self.state.set_runtime(True, listening_enabled, "vosk")
                            last_listening_state = listening_enabled
                        continue

                    tts_speaking = self.state.is_tts_speaking()
                    if tts_speaking:
                        if last_listening_state is not False:
                            self.state.set_runtime(True, False, "vosk")
                            last_listening_state = False
                        if not tts_gate_active:
                            tts_gate_active = True
                            self.state.clear_pending_partial()
                            self._reset_recognizer(recognizer)
                            self.state.log("info", "STT input gated while TTS is speaking.")
                        continue
                    if tts_gate_active:
                        tts_gate_active = False
                        self._drain_audio_queue(audio_q)
                        self._reset_recognizer(recognizer)
                        self.state.log("info", "STT input resumed after TTS completed.")

                    listening_enabled = self.state.is_listening_enabled()
                    if listening_enabled != last_listening_state:
                        self.state.set_runtime(True, listening_enabled, "vosk")
                        last_listening_state = listening_enabled

                    if not listening_enabled:
                        continue

                    if recognizer.AcceptWaveform(chunk):
                        payload = json.loads(recognizer.Result())
                        text = str(payload.get("text", "")).strip()
                        if text:
                            self.state.process_transcript(
                                text,
                                float(self.config["transcript_default_confidence"]),
                                True,
                                self.parser,
                            )
                    else:
                        payload = json.loads(recognizer.PartialResult())
                        partial = str(payload.get("partial", "")).strip()
                        if partial:
                            self.state.process_transcript(
                                partial,
                                float(self.config["transcript_default_confidence"]),
                                False,
                                self.parser,
                            )
        except Exception as exc:
            self.state.log("error", f"Vosk STT runtime failed: {exc}")
        finally:
            self.state.set_runtime(False, False, "vosk")
            self.state.log("warn", "Vosk STT worker stopped.")


class App:
    def __init__(self, config: dict) -> None:
        self.config = config
        self.state = State(config)
        self.parser = Parser(config)
        self.tts = TTS(config, self.state)
        self.stt = VoskSTT(config, self.state, self.parser)
        self.help_responder = LocalHelpResponder(config, self.state)

    def start(self) -> None:
        self.tts.start()
        self.stt.start()
        self.state.log(
            "info",
            f"Local sidecar ready on {self.config['bind_host']}:{self.config['bind_port']} "
            f"(stt={self.config['stt_backend']}, tts={self.config['tts_backend']}, "
            f"help={self.help_responder.backend}).",
        )

    def stop(self) -> None:
        self.stt.stop()
        self.tts.stop()

    def help_answer(self, query: str, context: str) -> str:
        return self.help_responder.answer(query, context)


class LocalHelpResponder:
    def __init__(self, config: dict, state: State) -> None:
        self.config = config
        self.state = state
        self.backend = str(config.get("help_model_backend", "rule_based")).strip().lower()
        if self.backend not in ("rule_based", "llama_cpp"):
            self.backend = "rule_based"
        self._llama = None
        self._llama_load_attempted = False
        self._llama_load_error = ""
        self._llama_lock = threading.Lock()
        self._fallback_logged = False

    def answer(self, query: str, context: str) -> str:
        if self.backend == "llama_cpp":
            generated = self._answer_with_llama_cpp(query, context)
            if generated:
                return generated
            if not self._fallback_logged:
                self.state.log("warn", "Falling back to rule-based help response.")
                self._fallback_logged = True

        return self._answer_rule_based(query, context)

    def _answer_rule_based(self, query: str, context: str) -> str:
        words = tokenize(query)
        if "connect" in words:
            return "Say 'connect robot' to connect, then verify Unity status before movement commands."
        if "disconnect" in words:
            return "Say 'disconnect robot' to close the current robot session."
        if "pose" in words:
            return "Try: 'set neutral arms pose' or 'set hello pose b'. Movement requires confirmation in Unity."
        if "hello" in words or "hi" in words or "greeting" in words:
            return "Try: 'hello' to say hello, then trigger 'Hello Pose C' without extra confirmation."
        if "who" in words and "you" in words:
            return "Try: 'who are you' to hear the local agent identity response."
        if "movement" in words or "motion" in words:
            return "Try: 'show movement' to run 3 random preset poses with 4-second spacing after confirmation in Unity."
        if "joint" in words or "degree" in words:
            return "Try: 'move r_shoulder_pitch to 10 degrees'."
        if "stop" in words or "cancel" in words:
            return "Emergency cancel phrases: 'stop', 'stop now', or 'cancel'."
        if "confirm" in words or "approve" in words or "yes" in words:
            return "Say 'confirm' or 'yes execute' to run the pending voice action."
        if "reject" in words or "decline" in words or "no" in words:
            return "Say 'reject' or 'no' to cancel the pending voice action."
        if "status" in words:
            return "Say 'status' to ask Unity for connection and bridge status."

        default_help = (
            "Supported intents: hello, who are you, help, status, connect robot, disconnect robot, set pose, move joint, show movement, stop motion, confirm pending, reject pending."
        )
        final_context = (context or "").strip() or str(self.config.get("help_context", ""))
        return f"{default_help} Context: {final_context}"

    def _answer_with_llama_cpp(self, query: str, context: str) -> str:
        if not self._ensure_llama_cpp_loaded():
            return ""

        model = self._llama
        if model is None:
            return ""

        final_context = (context or "").strip() or str(self.config.get("help_context", ""))
        question = (query or "").strip() or "How do I use the Reachy Unity app safely?"
        prompt = (
            "You are a local Reachy Unity help assistant.\n"
            "Rules:\n"
            "- Provide concise guidance for using the app safely.\n"
            "- Do not execute commands or claim actions were executed.\n"
            "- Mention confirmation for movement commands when relevant.\n"
            "- Keep answer plain text and practical.\n"
            f"Context: {final_context}\n"
            f"Question: {question}\n"
            "Answer:"
        )

        max_tokens = int(self.config.get("help_model_max_tokens", 96))
        temperature = float(self.config.get("help_model_temperature", 0.2))
        try:
            with self._llama_lock:
                response = model.create_completion(
                    prompt=prompt,
                    max_tokens=max_tokens,
                    temperature=temperature,
                    top_p=0.9,
                    stop=["\nQuestion:", "\nUser:", "\nContext:"],
                )
        except Exception as ex:
            self.state.log("error", f"llama_cpp help generation failed: {ex}")
            return ""

        text = ""
        if isinstance(response, dict):
            choices = response.get("choices")
            if isinstance(choices, list) and choices:
                first = choices[0]
                if isinstance(first, dict):
                    text = str(first.get("text", ""))

        cleaned = re.sub(r"\s+", " ", (text or "")).strip()
        if not cleaned:
            return ""

        max_chars = int(self.config.get("help_max_answer_chars", 360))
        if len(cleaned) > max_chars:
            cleaned = cleaned[:max_chars].rstrip()
        return cleaned

    def _ensure_llama_cpp_loaded(self) -> bool:
        if self._llama is not None:
            return True
        if self._llama_load_attempted:
            return False

        self._llama_load_attempted = True
        model_path_raw = str(self.config.get("help_model_path", "")).strip()
        if not model_path_raw:
            self._llama_load_error = "help_model_path is empty."
            self.state.log("warn", f"llama_cpp disabled: {self._llama_load_error}")
            return False

        model_file = resolve_config_relative_path(self.config, model_path_raw)
        if not model_file.exists():
            self._llama_load_error = f"model not found: {model_file}"
            self.state.log("warn", f"llama_cpp disabled: {self._llama_load_error}")
            return False

        try:
            from llama_cpp import Llama  # type: ignore
        except Exception as ex:
            self._llama_load_error = f"llama_cpp import failed: {ex}"
            self.state.log("warn", self._llama_load_error)
            return False

        try:
            self._llama = Llama(
                model_path=str(model_file),
                n_ctx=1024,
                verbose=False,
            )
            self.state.log("info", f"Loaded llama_cpp help model: {model_file}")
            return True
        except Exception as ex:
            self._llama_load_error = f"llama_cpp model load failed: {ex}"
            self.state.log("error", self._llama_load_error)
            self._llama = None
            return False


class Handler(BaseHTTPRequestHandler):
    server_version = "ReachyLocalVoiceAgent/0.2"

    @property
    def app(self) -> App:
        return self.server.app  # type: ignore[attr-defined]

    def do_GET(self):
        path = urlparse(self.path).path
        if path == "/intent":
            self._send(HTTPStatus.OK, self.app.state.poll())
            return
        if path == "/health":
            self._send(HTTPStatus.OK, self.app.state.health())
            return
        if path == "/logs":
            self._send(HTTPStatus.OK, self.app.state.log_rows())
            return
        self._send(HTTPStatus.NOT_FOUND, {"ok": False, "message": "Unknown GET endpoint."})

    def do_POST(self):
        path = urlparse(self.path).path
        payload = self._read_json()
        if payload is None:
            return

        if path == "/speak":
            ok, msg = self.app.tts.speak(str(payload.get("text", "")), bool(payload.get("interrupt", False)))
            self.app.state.log("info" if ok else "warn", f"/speak -> {msg}")
            self._send(HTTPStatus.OK if ok else HTTPStatus.BAD_REQUEST, {"ok": ok, "message": msg})
            return

        if path == "/help":
            query = str(payload.get("query", "")).strip()
            context = str(payload.get("context", "")).strip()
            answer = self.app.help_answer(query, context)
            self.app.state.last_help_answer = answer
            self.app.state.log(
                "info",
                f"/help backend={self.app.help_responder.backend} query='{query[:80]}'")
            self._send(HTTPStatus.OK, {"ok": True, "answer": answer, "message": "Local help answer generated."})
            return

        if path == "/inject_transcript":
            text = str(payload.get("text", "")).strip()
            final = bool(payload.get("final", True))
            try:
                conf = float(payload.get("confidence", self.app.config["transcript_default_confidence"]))
            except Exception:
                conf = float(self.app.config["transcript_default_confidence"])
            result = self.app.state.process_transcript(text, conf, final, self.app.parser)
            self.app.state.log("info" if result.get("ok") else "warn", f"/inject_transcript -> {result.get('message')}" )
            self._send(HTTPStatus.OK if result.get("ok") else HTTPStatus.BAD_REQUEST, result)
            return

        if path == "/inject_intent":
            result = self.app.state.inject_intent(payload)
            self.app.state.log("info" if result.get("ok") else "warn", f"/inject_intent -> {result.get('message')}" )
            self._send(HTTPStatus.OK if result.get("ok") else HTTPStatus.BAD_REQUEST, result)
            return

        if path == "/listening":
            enabled = parse_bool(payload.get("enabled", True), True)
            self.app.state.set_listening_enabled(enabled)
            self.app.state.log("info", f"/listening -> {'enabled' if enabled else 'disabled'}")
            self._send(HTTPStatus.OK, {"ok": True, "enabled": enabled})
            return

        self._send(HTTPStatus.NOT_FOUND, {"ok": False, "message": "Unknown POST endpoint."})

    def log_message(self, fmt, *args):
        logging.debug("%s - %s", self.address_string(), fmt % args)

    def _read_json(self) -> dict | None:
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            self._send(HTTPStatus.BAD_REQUEST, {"ok": False, "message": "Invalid Content-Length header."})
            return None

        raw = self.rfile.read(max(0, length)) if length > 0 else b""
        if not raw:
            return {}

        try:
            payload = json.loads(raw.decode("utf-8"))
        except Exception:
            self._send(HTTPStatus.BAD_REQUEST, {"ok": False, "message": "Request body must be JSON."})
            return None

        if not isinstance(payload, dict):
            self._send(HTTPStatus.BAD_REQUEST, {"ok": False, "message": "JSON body must be an object."})
            return None

        return payload

    def _send(self, code: int, payload: dict):
        data = json.dumps(payload, ensure_ascii=True).encode("utf-8")
        self.send_response(int(code))
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run local Reachy voice-agent sidecar.")
    parser.add_argument(
        "--config",
        type=str,
        default=str(Path(__file__).with_name("local_voice_agent_sidecar_config.json")),
        help="Path to sidecar JSON config.",
    )
    parser.add_argument(
        "--log-level",
        type=str,
        default="info",
        choices=["debug", "info", "warning", "error"],
        help="Python logging verbosity.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    logging.basicConfig(level=getattr(logging, str(args.log_level).upper()), format="%(asctime)s %(levelname)s %(message)s")

    config_path = Path(args.config).expanduser().resolve()
    try:
        config = load_config(config_path)
    except Exception as exc:
        logging.error("Failed to load config '%s': %s", config_path, exc)
        return 2

    app = App(config)
    app.start()

    server = ThreadingHTTPServer((str(config["bind_host"]), int(config["bind_port"])), Handler)
    server.daemon_threads = True
    server.app = app  # type: ignore[attr-defined]

    logging.info("Serving local voice sidecar at http://%s:%s", config["bind_host"], config["bind_port"])
    try:
        server.serve_forever(poll_interval=0.2)
    except KeyboardInterrupt:
        logging.info("Shutdown requested by keyboard interrupt.")
    finally:
        server.shutdown()
        server.server_close()
        app.stop()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
