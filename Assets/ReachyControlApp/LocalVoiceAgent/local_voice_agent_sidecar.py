#!/usr/bin/env python3
"""
Local Reachy voice-agent sidecar.

Endpoints used by Unity:
- GET  /intent
- POST /speak
- POST /stop
- POST /help

Testing endpoints:
- GET  /health
- GET  /logs
- POST /inject_transcript
- POST /inject_intent
- POST /listening
- POST /microphone-source
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import logging
import os
import queue
import re
import shlex
import shutil
import subprocess
import sys
import threading
import time
import tempfile
import unicodedata
import wave
from collections import deque
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib import error as urllib_error
from urllib import request as urllib_request
from urllib.parse import urlparse


DEFAULT_POSES = [
    "Neutral Arms",
    "T-Pose",
    "Tray Holding",
    "Hello Pose A",
    "Hello Pose B",
    "Hello Pose C",
    "Hello Pose D",
    "Left Hand Up",
    "Left Hand Wave",
    "Right Hand Up",
    "Right Hand Wave",
    "Hands Up",
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
    "neck_roll",
    "neck_pitch",
    "neck_yaw",
]

ONLINE_ALLOWED_INTENTS = (
    "none",
    "help",
    "status",
    "connect_robot",
    "disconnect_robot",
    "set_pose",
    "set_custom_pose",
    "move_joint",
    "play_motion_sequence",
    "show_movement",
    "stop_motion",
)
DEFAULT_ONLINE_AI_MODEL = "gpt-5.4"
DEFAULT_ONLINE_AI_TRANSCRIBE_MODEL = "gpt-4o-mini-transcribe"
DEFAULT_ONLINE_AI_BASE_URL = "https://api.openai.com/v1"
DEFAULT_ONLINE_AI_API_KEY_ENV_VAR = "OPENAI_API_KEY"
DEFAULT_TTS_MODE = "online"
DEFAULT_ONLINE_TTS_MODEL = "gpt-4o-mini-tts"
DEFAULT_ONLINE_TTS_VOICE = "alloy"
DEFAULT_ONLINE_AI_MAX_OUTPUT_TOKENS = 480
DEFAULT_OPENAI_TRANSCRIBE_LANGUAGE_HINTS = ["en", "fi"]
DEFAULT_ONLINE_CUSTOM_POSE_MAX_JOINTS = len(DEFAULT_JOINTS)
DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_STEPS = 8
DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_JOINTS_PER_STEP = len(DEFAULT_JOINTS)
DEFAULT_ONLINE_MOTION_SEQUENCE_MIN_HOLD_SECONDS = 0.15
DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_HOLD_SECONDS = 4.0
DEFAULT_AUDIO_SOURCE_MODE = "blend"
DEFAULT_REACHY_MIC_SSH_PORT = 22
DEFAULT_REACHY_MIC_SSH_USER = "reachy"
DEFAULT_REACHY_MIC_SSH_PASSWORD = "reachy"
DEFAULT_REACHY_MIC_DEVICE_SPEC = "plughw:1,0"
DEFAULT_REACHY_MIC_DEVICE_LABEL = "Reachy ReSpeaker"
SEMANTIC_ASSIST_POSE_NAMES = [
    "Left Hand Up",
    "Left Hand Wave",
    "Right Hand Up",
    "Right Hand Wave",
    "Hands Up",
]
SEMANTIC_LEFT_SIDE_HINTS = [
    "left",
    "vasen",
    "vasenta",
    "vasemman",
    "vasemmalla",
]
SEMANTIC_RIGHT_SIDE_HINTS = [
    "right",
    "oikea",
    "oikean",
    "oikeaa",
    "oikealla",
]
SEMANTIC_BOTH_SIDE_HINTS = [
    "both",
    "molemmat",
    "molempia",
    "kummatkin",
    "kahdella",
]
SEMANTIC_SINGULAR_LIMB_HINTS = [
    "hand",
    "arm",
    "kasi",
    "kaden",
]
SEMANTIC_PLURAL_LIMB_HINTS = [
    "hands",
    "arms",
    "kadet",
    "kasia",
]
SEMANTIC_RAISE_TOKEN_HINTS = [
    "raise",
    "lift",
    "up",
    "hold",
    "nosta",
    "ylos",
]
SEMANTIC_RAISE_PHRASE_HINTS = [
    "raise hand",
    "raise arm",
    "raise your hand",
    "raise your arm",
    "lift hand",
    "lift arm",
    "hand up",
    "arm up",
    "hands up",
    "arms up",
    "put your hand up",
    "hold your hand up",
    "nosta kasi",
    "nosta kasi ylos",
    "nosta kadet",
    "nosta kadet ylos",
    "kasi ylos",
    "kadet ylos",
]
SEMANTIC_WAVE_TOKEN_HINTS = [
    "wave",
    "waving",
    "heiluta",
    "vilkuta",
    "moikkaa",
    "tervehdi",
    "greet",
]
SEMANTIC_WAVE_PHRASE_HINTS = [
    "wave hand",
    "wave your hand",
    "wave arm",
    "wave to me",
    "heiluta kasi",
]
SEMANTIC_QUESTION_TOKEN_HINTS = [
    "what",
    "how",
    "why",
    "which",
    "who",
    "when",
    "where",
    "mika",
    "mita",
    "miten",
    "miksi",
    "kuka",
    "milloin",
    "missa",
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
    decomposed = unicodedata.normalize("NFKD", text or "")
    return "".join(
        ch.lower()
        for ch in decomposed
        if not unicodedata.combining(ch) and ch.isalnum()
    )


def tokenize(text: str) -> list[str]:
    return [tok for tok in re.split(r"[^\w]+", (text or "").lower(), flags=re.UNICODE) if tok]


def build_normalized_hint_set(values: list[str]) -> set[str]:
    normalized: set[str] = set()
    for value in values:
        token = normalize(str(value))
        if token:
            normalized.add(token)
    return normalized


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
SEMANTIC_LEFT_SIDE_HINT_SET = build_normalized_hint_set(SEMANTIC_LEFT_SIDE_HINTS)
SEMANTIC_RIGHT_SIDE_HINT_SET = build_normalized_hint_set(SEMANTIC_RIGHT_SIDE_HINTS)
SEMANTIC_BOTH_SIDE_HINT_SET = build_normalized_hint_set(SEMANTIC_BOTH_SIDE_HINTS)
SEMANTIC_SINGULAR_LIMB_HINT_SET = build_normalized_hint_set(SEMANTIC_SINGULAR_LIMB_HINTS)
SEMANTIC_PLURAL_LIMB_HINT_SET = build_normalized_hint_set(SEMANTIC_PLURAL_LIMB_HINTS)
SEMANTIC_RAISE_TOKEN_HINT_SET = build_normalized_hint_set(SEMANTIC_RAISE_TOKEN_HINTS)
SEMANTIC_RAISE_PHRASE_HINT_SET = build_normalized_hint_set(SEMANTIC_RAISE_PHRASE_HINTS)
SEMANTIC_WAVE_TOKEN_HINT_SET = build_normalized_hint_set(SEMANTIC_WAVE_TOKEN_HINTS)
SEMANTIC_WAVE_PHRASE_HINT_SET = build_normalized_hint_set(SEMANTIC_WAVE_PHRASE_HINTS)
SEMANTIC_QUESTION_TOKEN_HINT_SET = build_normalized_hint_set(SEMANTIC_QUESTION_TOKEN_HINTS)


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


def normalize_audio_source_mode(value) -> str:
    normalized = str(value or "").strip().lower()
    if normalized in ("pc", "pc_only", "local", "local_only", "computer"):
        return "pc"
    if normalized in ("reachy", "robot", "reachy_only", "robot_only"):
        return "reachy"
    if normalized in ("blend", "both", "mixed", "mix"):
        return "blend"
    return DEFAULT_AUDIO_SOURCE_MODE


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
        "ai_mode": "local",
        "stt_backend": "auto",
        "stt_model_path": "../../../.local_voice_models/vosk-model-small-en-us-0.15",
        "stt_sample_rate_hz": 16000,
        "transcript_default_confidence": 0.85,
        "intent_confidence_threshold": 0.78,
        "tts_backend": "pyttsx3",
        "tts_mode": DEFAULT_TTS_MODE,
        "tts_rate": 175,
        "tts_timeout_seconds": 30.0,
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
        "audio_source_mode": DEFAULT_AUDIO_SOURCE_MODE,
        "reachy_mic_robot_connected": False,
        "reachy_mic_ssh_host": "",
        "reachy_mic_ssh_port": DEFAULT_REACHY_MIC_SSH_PORT,
        "reachy_mic_ssh_user": DEFAULT_REACHY_MIC_SSH_USER,
        "reachy_mic_ssh_password": DEFAULT_REACHY_MIC_SSH_PASSWORD,
        "reachy_mic_device_spec": "",
        "start_listening_enabled": True,
        "min_transcript_chars": 4,
        "min_transcript_words": 1,
        "simulation_only_mode": False,
        "block_motion_when_bridge_unhealthy": True,
        "safe_numeric_parsing": True,
        "require_target_token_for_joint": False,
        "reject_out_of_range_joint_commands": True,
        "joint_min_degrees": -180.0,
        "joint_max_degrees": 180.0,
        "online_ai_enabled": False,
        "online_ai_model": DEFAULT_ONLINE_AI_MODEL,
        "online_ai_transcription_model": DEFAULT_ONLINE_AI_TRANSCRIBE_MODEL,
        "online_tts_model": DEFAULT_ONLINE_TTS_MODEL,
        "online_tts_voice": DEFAULT_ONLINE_TTS_VOICE,
        "online_tts_stream_partial_replies": True,
        "online_ai_api_key_env_var": DEFAULT_ONLINE_AI_API_KEY_ENV_VAR,
        "online_ai_api_key_file_path": "",
        "online_ai_base_url": DEFAULT_ONLINE_AI_BASE_URL,
        "online_ai_timeout_seconds": 15.0,
        "openai_transcribe_language_hints": list(DEFAULT_OPENAI_TRANSCRIBE_LANGUAGE_HINTS),
        "online_ai_temperature": 0.2,
        "online_ai_max_output_tokens": DEFAULT_ONLINE_AI_MAX_OUTPUT_TOKENS,
        "online_ai_system_prompt": "You are Reachy's online conversational AI.",
        "online_ai_allow_direct_joint_commands": True,
        "online_ai_require_motion_confirmation": False,
        "online_ai_allow_custom_pose_commands": True,
        "online_ai_allow_motion_sequence_commands": True,
        "online_ai_custom_pose_max_joints": DEFAULT_ONLINE_CUSTOM_POSE_MAX_JOINTS,
        "online_ai_motion_sequence_max_steps": DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_STEPS,
        "online_ai_motion_sequence_max_joints_per_step": DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_JOINTS_PER_STEP,
        "online_ai_motion_sequence_min_hold_seconds": DEFAULT_ONLINE_MOTION_SEQUENCE_MIN_HOLD_SECONDS,
        "online_ai_motion_sequence_max_hold_seconds": DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_HOLD_SECONDS,
        "online_ai_show_api_key_help_on_first_open": True,
        "online_ai_last_api_key_check_ok": False,
        "openai_transcribe_min_clip_seconds": 0.55,
        "openai_transcribe_max_clip_seconds": 8.0,
        "openai_transcribe_silence_seconds": 0.8,
        "openai_transcribe_pre_roll_seconds": 0.25,
        "openai_transcribe_rms_threshold": 520,
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
    config["ai_mode"] = str(config.get("ai_mode", "local")).strip().lower() or "local"
    if config["ai_mode"] not in ("local", "online"):
        config["ai_mode"] = "local"
    config["stt_backend"] = normalize_stt_backend(config.get("stt_backend", "auto"))
    config["tts_backend"] = str(config.get("tts_backend", "none")).strip().lower()
    config["tts_mode"] = normalize_tts_mode(config.get("tts_mode", DEFAULT_TTS_MODE))
    config["stt_sample_rate_hz"] = max(8000, int(config.get("stt_sample_rate_hz", 16000)))
    config["transcript_default_confidence"] = max(0.0, min(1.0, float(config.get("transcript_default_confidence", 0.85))))
    config["intent_confidence_threshold"] = max(0.0, min(1.0, float(config.get("intent_confidence_threshold", 0.78))))
    config["tts_rate"] = max(60, min(320, int(config.get("tts_rate", 175))))
    config["tts_timeout_seconds"] = max(5.0, min(600.0, float(config.get("tts_timeout_seconds", 30.0))))
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
    config["audio_source_mode"] = normalize_audio_source_mode(
        config.get("audio_source_mode", DEFAULT_AUDIO_SOURCE_MODE))
    config["reachy_mic_robot_connected"] = parse_bool(
        config.get("reachy_mic_robot_connected"),
        False)
    config["reachy_mic_ssh_host"] = str(config.get("reachy_mic_ssh_host", "")).strip()
    config["reachy_mic_ssh_port"] = max(
        1,
        min(65535, int(config.get("reachy_mic_ssh_port", DEFAULT_REACHY_MIC_SSH_PORT))))
    config["reachy_mic_ssh_user"] = (
        str(config.get("reachy_mic_ssh_user", DEFAULT_REACHY_MIC_SSH_USER)).strip()
        or DEFAULT_REACHY_MIC_SSH_USER)
    config["reachy_mic_ssh_password"] = (
        str(config.get("reachy_mic_ssh_password", DEFAULT_REACHY_MIC_SSH_PASSWORD)).strip()
        or DEFAULT_REACHY_MIC_SSH_PASSWORD)
    config["reachy_mic_device_spec"] = str(config.get("reachy_mic_device_spec", "")).strip()
    config["start_listening_enabled"] = parse_bool(config.get("start_listening_enabled"), True)
    config["min_transcript_chars"] = max(0, int(config.get("min_transcript_chars", 4)))
    config["min_transcript_words"] = max(0, int(config.get("min_transcript_words", 1)))
    config["simulation_only_mode"] = parse_bool(config.get("simulation_only_mode"), False)
    config["block_motion_when_bridge_unhealthy"] = parse_bool(
        config.get("block_motion_when_bridge_unhealthy"),
        True)
    config["safe_numeric_parsing"] = parse_bool(config.get("safe_numeric_parsing"), True)
    config["require_target_token_for_joint"] = parse_bool(config.get("require_target_token_for_joint"), False)
    config["reject_out_of_range_joint_commands"] = parse_bool(
        config.get("reject_out_of_range_joint_commands"),
        True)
    config["joint_min_degrees"] = float(config.get("joint_min_degrees", -180.0))
    config["joint_max_degrees"] = float(config.get("joint_max_degrees", 180.0))
    config["online_ai_enabled"] = parse_bool(config.get("online_ai_enabled"), False)
    config["online_ai_model"] = (
        str(config.get("online_ai_model", DEFAULT_ONLINE_AI_MODEL)).strip() or DEFAULT_ONLINE_AI_MODEL)
    config["online_ai_transcription_model"] = (
        str(config.get("online_ai_transcription_model", DEFAULT_ONLINE_AI_TRANSCRIBE_MODEL)).strip()
        or DEFAULT_ONLINE_AI_TRANSCRIBE_MODEL)
    config["online_tts_model"] = (
        str(config.get("online_tts_model", DEFAULT_ONLINE_TTS_MODEL)).strip()
        or DEFAULT_ONLINE_TTS_MODEL)
    config["online_tts_voice"] = (
        str(config.get("online_tts_voice", DEFAULT_ONLINE_TTS_VOICE)).strip()
        or DEFAULT_ONLINE_TTS_VOICE)
    config["online_tts_stream_partial_replies"] = parse_bool(
        config.get("online_tts_stream_partial_replies"),
        True)
    config["online_ai_api_key_env_var"] = (
        str(config.get("online_ai_api_key_env_var", DEFAULT_ONLINE_AI_API_KEY_ENV_VAR)).strip()
        or DEFAULT_ONLINE_AI_API_KEY_ENV_VAR)
    config["online_ai_api_key_file_path"] = (
        str(config.get("online_ai_api_key_file_path", "") or "").strip())
    config["online_ai_base_url"] = (
        str(config.get("online_ai_base_url", DEFAULT_ONLINE_AI_BASE_URL)).strip()
        or DEFAULT_ONLINE_AI_BASE_URL)
    config["online_ai_timeout_seconds"] = max(
        3.0,
        min(120.0, float(config.get("online_ai_timeout_seconds", 15.0))))
    raw_language_hints = config.get(
        "openai_transcribe_language_hints",
        DEFAULT_OPENAI_TRANSCRIBE_LANGUAGE_HINTS,
    )
    if not isinstance(raw_language_hints, list):
        raw_language_hints = DEFAULT_OPENAI_TRANSCRIBE_LANGUAGE_HINTS
    normalized_language_hints: list[str] = []
    for item in raw_language_hints:
        normalized = str(item or "").strip().lower()
        if not normalized or normalized in normalized_language_hints:
            continue
        normalized_language_hints.append(normalized)
    config["openai_transcribe_language_hints"] = (
        normalized_language_hints
        if normalized_language_hints
        else list(DEFAULT_OPENAI_TRANSCRIBE_LANGUAGE_HINTS)
    )
    config["online_ai_temperature"] = max(
        0.0,
        min(2.0, float(config.get("online_ai_temperature", 0.2))))
    config["online_ai_max_output_tokens"] = max(
        32,
        min(2048, int(config.get("online_ai_max_output_tokens", DEFAULT_ONLINE_AI_MAX_OUTPUT_TOKENS))))
    config["online_ai_system_prompt"] = (
        str(config.get("online_ai_system_prompt", "You are Reachy's online conversational AI.")).strip()
        or "You are Reachy's online conversational AI.")
    config["online_ai_allow_direct_joint_commands"] = parse_bool(
        config.get("online_ai_allow_direct_joint_commands"),
        True)
    config["online_ai_require_motion_confirmation"] = parse_bool(
        config.get("online_ai_require_motion_confirmation"),
        False)
    config["online_ai_allow_custom_pose_commands"] = parse_bool(
        config.get("online_ai_allow_custom_pose_commands"),
        True)
    config["online_ai_allow_motion_sequence_commands"] = parse_bool(
        config.get("online_ai_allow_motion_sequence_commands"),
        True)
    config["online_ai_custom_pose_max_joints"] = max(
        1,
        min(len(config["known_joints"]), int(config.get("online_ai_custom_pose_max_joints", DEFAULT_ONLINE_CUSTOM_POSE_MAX_JOINTS))))
    config["online_ai_motion_sequence_max_steps"] = max(
        1,
        min(32, int(config.get("online_ai_motion_sequence_max_steps", DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_STEPS))))
    config["online_ai_motion_sequence_max_joints_per_step"] = max(
        1,
        min(len(config["known_joints"]), int(config.get("online_ai_motion_sequence_max_joints_per_step", DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_JOINTS_PER_STEP))))
    config["online_ai_motion_sequence_min_hold_seconds"] = max(
        0.05,
        min(5.0, float(config.get("online_ai_motion_sequence_min_hold_seconds", DEFAULT_ONLINE_MOTION_SEQUENCE_MIN_HOLD_SECONDS))))
    config["online_ai_motion_sequence_max_hold_seconds"] = max(
        config["online_ai_motion_sequence_min_hold_seconds"],
        min(10.0, float(config.get("online_ai_motion_sequence_max_hold_seconds", DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_HOLD_SECONDS))))
    config["online_ai_show_api_key_help_on_first_open"] = parse_bool(
        config.get("online_ai_show_api_key_help_on_first_open"),
        True)
    config["online_ai_last_api_key_check_ok"] = parse_bool(
        config.get("online_ai_last_api_key_check_ok"),
        False)
    config["openai_transcribe_min_clip_seconds"] = max(
        0.2,
        min(6.0, float(config.get("openai_transcribe_min_clip_seconds", 0.55))))
    config["openai_transcribe_max_clip_seconds"] = max(
        config["openai_transcribe_min_clip_seconds"],
        min(20.0, float(config.get("openai_transcribe_max_clip_seconds", 8.0))))
    config["openai_transcribe_silence_seconds"] = max(
        0.2,
        min(4.0, float(config.get("openai_transcribe_silence_seconds", 0.8))))
    config["openai_transcribe_pre_roll_seconds"] = max(
        0.0,
        min(2.0, float(config.get("openai_transcribe_pre_roll_seconds", 0.25))))
    config["openai_transcribe_rms_threshold"] = max(
        80,
        min(5000, int(config.get("openai_transcribe_rms_threshold", 520))))
    if config["joint_min_degrees"] > config["joint_max_degrees"]:
        low = config["joint_max_degrees"]
        high = config["joint_min_degrees"]
        config["joint_min_degrees"] = low
        config["joint_max_degrees"] = high
    return config


def normalize_stt_backend(value) -> str:
    normalized = str(value or "").strip().lower()
    if normalized in ("", "default"):
        return "auto"
    if normalized in ("auto", "vosk", "openai_transcribe", "none"):
        return normalized
    return "auto"


def normalize_tts_mode(value) -> str:
    normalized = str(value or "").strip().lower()
    if normalized in ("", "default"):
        return DEFAULT_TTS_MODE
    if normalized in ("online", "local"):
        return normalized
    return DEFAULT_TTS_MODE


def resolve_effective_stt_backend(config: dict, api_key_available: bool) -> str:
    requested = normalize_stt_backend(config.get("stt_backend", "auto"))
    if requested in ("none", "vosk", "openai_transcribe"):
        return requested

    use_online_mode = str(config.get("ai_mode", "local")).strip().lower() == "online"
    online_enabled = parse_bool(config.get("online_ai_enabled"), False)
    if use_online_mode and online_enabled and api_key_available:
        return "openai_transcribe"
    return "vosk"


def build_openai_audio_transcriptions_endpoint(base_url: str) -> str:
    trimmed = str(base_url or "").strip().rstrip("/")
    if not trimmed:
        trimmed = DEFAULT_ONLINE_AI_BASE_URL
    if trimmed.endswith("/audio/transcriptions"):
        return trimmed
    if trimmed.endswith("/v1"):
        return f"{trimmed}/audio/transcriptions"
    return f"{trimmed}/v1/audio/transcriptions"


def build_openai_audio_speech_endpoint(base_url: str) -> str:
    trimmed = str(base_url or "").strip().rstrip("/")
    if not trimmed:
        trimmed = DEFAULT_ONLINE_AI_BASE_URL
    if trimmed.endswith("/audio/speech"):
        return trimmed
    if trimmed.endswith("/v1"):
        return f"{trimmed}/audio/speech"
    return f"{trimmed}/v1/audio/speech"


def pcm16_rms(chunk: bytes) -> float:
    if not chunk:
        return 0.0

    try:
        samples = memoryview(chunk).cast("h")
    except Exception:
        return 0.0

    count = len(samples)
    if count <= 0:
        return 0.0

    total = 0.0
    for sample in samples:
        value = int(sample)
        total += float(value * value)
    return (total / float(count)) ** 0.5


def apply_microphone_routing_payload(config: dict, payload: dict) -> dict:
    config["audio_source_mode"] = normalize_audio_source_mode(
        payload.get("audio_source_mode", config.get("audio_source_mode", DEFAULT_AUDIO_SOURCE_MODE))
    )
    config["audio_input_device_name"] = str(
        payload.get("audio_input_device_name", config.get("audio_input_device_name", ""))
    ).strip()
    config["prefer_non_virtual_input_device"] = parse_bool(
        payload.get("prefer_non_virtual_input_device", config.get("prefer_non_virtual_input_device", True)),
        True,
    )
    config["reachy_mic_robot_connected"] = parse_bool(
        payload.get("reachy_mic_robot_connected", config.get("reachy_mic_robot_connected", False)),
        False,
    )
    config["reachy_mic_ssh_host"] = str(
        payload.get("reachy_mic_ssh_host", config.get("reachy_mic_ssh_host", ""))
    ).strip()
    config["reachy_mic_ssh_port"] = max(
        1,
        min(65535, int(payload.get("reachy_mic_ssh_port", config.get("reachy_mic_ssh_port", DEFAULT_REACHY_MIC_SSH_PORT)))),
    )
    config["reachy_mic_ssh_user"] = (
        str(payload.get("reachy_mic_ssh_user", config.get("reachy_mic_ssh_user", DEFAULT_REACHY_MIC_SSH_USER))).strip()
        or DEFAULT_REACHY_MIC_SSH_USER
    )
    config["reachy_mic_ssh_password"] = (
        str(payload.get("reachy_mic_ssh_password", config.get("reachy_mic_ssh_password", DEFAULT_REACHY_MIC_SSH_PASSWORD))).strip()
        or DEFAULT_REACHY_MIC_SSH_PASSWORD
    )
    config["reachy_mic_device_spec"] = str(
        payload.get("reachy_mic_device_spec", config.get("reachy_mic_device_spec", ""))
    ).strip()

    requested_mode = normalize_audio_source_mode(config.get("audio_source_mode", DEFAULT_AUDIO_SOURCE_MODE))
    reachy_connected = parse_bool(config.get("reachy_mic_robot_connected"), False) and bool(
        str(config.get("reachy_mic_ssh_host", "")).strip()
    )
    if not reachy_connected:
        effective_mode = "pc"
        pc_enabled = True
        reachy_enabled = False
    elif requested_mode == "pc":
        effective_mode = "pc"
        pc_enabled = True
        reachy_enabled = False
    elif requested_mode == "reachy":
        effective_mode = "reachy"
        pc_enabled = False
        reachy_enabled = True
    else:
        effective_mode = "blend"
        pc_enabled = True
        reachy_enabled = True

    message = f"Microphone routing updated: requested={requested_mode}, effective={effective_mode}."
    if not reachy_connected and requested_mode != "pc":
        message += " No real robot connection is active, so the PC mic stays active."

    return {
        "ok": True,
        "message": message,
        "requested_mode": requested_mode,
        "effective_mode": effective_mode,
        "pc_enabled": pc_enabled,
        "reachy_enabled": reachy_enabled,
        "reachy_host": str(config.get("reachy_mic_ssh_host", "")).strip(),
    }


def apply_tts_mode_payload(config: dict, payload: dict) -> dict:
    requested_mode = normalize_tts_mode(payload.get("tts_mode", config.get("tts_mode", DEFAULT_TTS_MODE)))
    config["tts_mode"] = requested_mode
    return {
        "ok": True,
        "message": f"TTS mode updated: requested={requested_mode}, effective={requested_mode}.",
        "requested_mode": requested_mode,
        "effective_mode": requested_mode,
    }


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


def discover_project_root(start_path: str) -> Path | None:
    text = str(start_path or "").strip()
    if not text:
        return None

    try:
        cursor = Path(text).resolve()
    except Exception:
        cursor = Path(text)

    for _ in range(0, 8):
        if (cursor / "Assets" / "ReachyControlApp").exists():
            return cursor
        parent = cursor.parent
        if parent == cursor:
            break
        cursor = parent

    return None


def build_default_online_api_key_store_paths(config: dict) -> list[Path]:
    candidates: list[Path] = []
    seen: set[str] = set()

    def add_candidate(path: Path | None) -> None:
        if path is None:
            return
        normalized = str(path).strip()
        if not normalized:
            return
        key = normalized.lower()
        if key in seen:
            return
        seen.add(key)
        candidates.append(path)

    local_app_data = str(os.environ.get("LOCALAPPDATA", "") or "").strip()
    if local_app_data:
        add_candidate(Path(local_app_data) / "ReachyControlApp" / "Secrets" / "online_ai_api_key.json")

    config_dir = str(config.get("_config_dir", "")).strip()
    project_root = discover_project_root(config_dir)
    if project_root is not None:
        add_candidate(project_root / "UserSettings" / "ReachyControlApp" / "Secrets" / "online_ai_api_key.json")

    add_candidate(Path.home() / ".reachy_control_app" / "secrets" / "online_ai_api_key.json")
    return candidates


def resolve_online_api_key_store_paths(config: dict) -> list[Path]:
    candidates: list[Path] = []
    seen: set[str] = set()

    def add_candidate(path: Path | None) -> None:
        if path is None:
            return
        normalized = str(path).strip()
        if not normalized:
            return
        key = normalized.lower()
        if key in seen:
            return
        seen.add(key)
        candidates.append(path)

    raw_path = str(config.get("online_ai_api_key_file_path", "") or "").strip()
    if raw_path:
        add_candidate(resolve_config_relative_path(config, raw_path))

    for candidate in build_default_online_api_key_store_paths(config):
        add_candidate(candidate)

    return candidates


def resolve_online_api_key_store_path(config: dict) -> Path:
    candidates = resolve_online_api_key_store_paths(config)
    if candidates:
        return candidates[0]
    return Path.home() / ".reachy_control_app" / "secrets" / "online_ai_api_key.json"


def load_online_api_key_from_store(config: dict, env_var: str) -> str:
    for path in resolve_online_api_key_store_paths(config):
        if not path.exists():
            continue

        try:
            payload = json.loads(path.read_text(encoding="utf-8-sig"))
        except Exception:
            continue

        if not isinstance(payload, dict):
            continue

        stored_env_var = str(payload.get("env_var", "") or "").strip() or env_var
        if stored_env_var != env_var:
            continue

        secret = str(payload.get("api_key", "") or "").strip()
        if secret:
            return secret

    return ""


def build_missing_online_api_key_message(env_var: str) -> str:
    return (
        "OpenAI API key is missing. "
        f"Save it in the app's local secret store or set env var '{env_var}'."
    )


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
            "speed_scale": 0.0,
            "joint_targets": [],
            "motion_steps": [],
            "confidence": confidence,
            "requires_confirmation": requires_confirmation,
            "reply_text": "",
            "spoken_text": spoken,
            "source_backend": "local_parser",
            "source_mode": "local",
            "validation_status": "local_parser",
            "validation_message": "",
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
        self.audio_source_mode = normalize_audio_source_mode(
            config.get("audio_source_mode", DEFAULT_AUDIO_SOURCE_MODE))
        self.audio_source_effective = "pc"
        self.local_input_device_name = ""
        self.local_input_device_index = -1
        self.reachy_input_device_name = ""
        self.reachy_mic_connected = False
        self.reachy_mic_available = False
        self.reachy_mic_last_error = ""
        self.ai_mode = str(config.get("ai_mode", "local")).strip().lower() or "local"
        self.online_ai_enabled = parse_bool(config.get("online_ai_enabled"), False)
        self.online_model = str(config.get("online_ai_model", DEFAULT_ONLINE_AI_MODEL)).strip() or DEFAULT_ONLINE_AI_MODEL
        self.online_api_key_env_var = (
            str(config.get("online_ai_api_key_env_var", DEFAULT_ONLINE_AI_API_KEY_ENV_VAR)).strip()
            or DEFAULT_ONLINE_AI_API_KEY_ENV_VAR)
        self.online_api_key_found = False
        self.online_last_key_check_utc = ""
        self.online_last_request_utc = ""
        self.online_last_response_summary = ""
        self.online_last_reply_text = ""
        self.online_last_validation_result = "idle"
        self.online_last_validation_failure = ""
        self.online_last_http_error = ""
        self.online_last_latency_ms = -1.0
        self.online_last_connection_test_result = "Not tested."
        self.online_last_connection_test_ok = False
        self.online_source_backend = "openai_responses"

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

    @staticmethod
    def _utc_now_text() -> str:
        return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())

    def refresh_online_key_status(self) -> tuple[bool, str]:
        env_var = (
            str(self.config.get("online_ai_api_key_env_var", self.online_api_key_env_var)).strip()
            or DEFAULT_ONLINE_AI_API_KEY_ENV_VAR)
        key_value = str(os.environ.get(env_var, "") or "").strip()
        if not key_value:
            key_value = load_online_api_key_from_store(self.config, env_var)
            if key_value:
                os.environ[env_var] = key_value
        found = bool(str(key_value or "").strip())
        with self.lock:
            self.online_api_key_env_var = env_var
            self.online_api_key_found = found
            self.online_last_key_check_utc = self._utc_now_text()
            self.online_ai_enabled = parse_bool(self.config.get("online_ai_enabled"), False)
            self.ai_mode = str(self.config.get("ai_mode", self.ai_mode)).strip().lower() or "local"
            self.online_model = str(self.config.get("online_ai_model", self.online_model)).strip()
        return found, env_var

    def record_online_request_started(self, model: str) -> None:
        with self.lock:
            self.online_model = str(model or self.online_model).strip()
            self.online_last_request_utc = self._utc_now_text()
            self.online_last_http_error = ""

    def record_online_response(
        self,
        *,
        reply_text: str,
        validation_result: str,
        validation_failure: str,
        response_summary: str,
        http_error: str,
        latency_ms: float,
        source_backend: str,
    ) -> None:
        with self.lock:
            self.online_last_reply_text = str(reply_text or "").strip()
            self.online_last_validation_result = str(validation_result or "").strip() or "unknown"
            self.online_last_validation_failure = str(validation_failure or "").strip()
            self.online_last_response_summary = str(response_summary or "").strip()
            self.online_last_http_error = str(http_error or "").strip()
            self.online_last_latency_ms = float(latency_ms)
            self.online_source_backend = str(source_backend or "openai_responses").strip() or "openai_responses"

    def record_online_connection_test(
        self,
        ok: bool,
        message: str,
        *,
        latency_ms: float = -1.0,
        http_error: str = "",
        model: str = "",
    ) -> None:
        with self.lock:
            self.online_last_connection_test_ok = bool(ok)
            self.online_last_connection_test_result = str(message or "").strip() or "No result."
            self.online_last_latency_ms = float(latency_ms)
            self.online_last_http_error = str(http_error or "").strip()
            if model:
                self.online_model = str(model).strip()

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

    def set_audio_source_status(
        self,
        *,
        requested_mode: str,
        effective_mode: str,
        display_name: str,
        local_device_index: int,
        local_device_name: str,
        reachy_device_name: str,
        reachy_connected: bool,
        reachy_available: bool,
        reachy_error: str,
    ) -> None:
        with self.lock:
            self.audio_source_mode = normalize_audio_source_mode(requested_mode)
            self.audio_source_effective = normalize_audio_source_mode(effective_mode)
            self.selected_input_device_index = int(local_device_index)
            self.selected_input_device_name = str(display_name or "").strip()
            self.local_input_device_index = int(local_device_index)
            self.local_input_device_name = str(local_device_name or "").strip()
            self.reachy_input_device_name = str(reachy_device_name or "").strip()
            self.reachy_mic_connected = bool(reachy_connected)
            self.reachy_mic_available = bool(reachy_available)
            self.reachy_mic_last_error = str(reachy_error or "").strip()

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

    def process_transcript(
        self,
        transcript: str,
        confidence: float,
        is_final: bool,
        parser: Parser,
        online_orchestrator=None,
    ) -> dict:
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

        use_online_mode = str(self.config.get("ai_mode", "local")).strip().lower() == "online"
        if use_online_mode and online_orchestrator is not None:
            parsed, parse_message = online_orchestrator.handle_transcript(text, confidence)
        else:
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
            "speed_scale": float(payload.get("speed_scale", 0.0) or 0.0),
            "joint_targets": payload.get("joint_targets", []) if isinstance(payload.get("joint_targets", []), list) else [],
            "motion_steps": payload.get("motion_steps", []) if isinstance(payload.get("motion_steps", []), list) else [],
            "confidence": max(0.0, min(1.0, float(payload.get("confidence", 0.95) or 0.95))),
            "requires_confirmation": bool(payload.get("requires_confirmation", False)),
            "reply_text": str(payload.get("reply_text", "")).strip(),
            "spoken_text": str(payload.get("spoken_text", "")).strip(),
            "source_backend": str(payload.get("source_backend", "")).strip(),
            "source_mode": str(payload.get("source_mode", "")).strip(),
            "validation_status": str(payload.get("validation_status", "")).strip(),
            "validation_message": str(payload.get("validation_message", "")).strip(),
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
                "audio_source_mode": self.audio_source_mode,
                "audio_source_effective": self.audio_source_effective,
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
        api_key_found, env_var = self.refresh_online_key_status()
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
                "audio_source_mode": self.audio_source_mode,
                "audio_source_effective": self.audio_source_effective,
                "local_input_device_index": self.local_input_device_index,
                "local_input_device_name": self.local_input_device_name,
                "reachy_input_device_name": self.reachy_input_device_name,
                "reachy_mic_connected": self.reachy_mic_connected,
                "reachy_mic_available": self.reachy_mic_available,
                "reachy_mic_last_error": self.reachy_mic_last_error,
                "last_help_answer": self.last_help_answer,
                "ai_mode": self.ai_mode,
                "simulation_only_mode": parse_bool(self.config.get("simulation_only_mode"), False),
                "block_motion_when_bridge_unhealthy": parse_bool(
                    self.config.get("block_motion_when_bridge_unhealthy"),
                    True),
                "online_ai_enabled": self.online_ai_enabled,
                "online_ai_model": self.online_model,
                "online_ai_api_key_env_var": env_var,
                "online_ai_api_key_found": api_key_found,
                "online_ai_last_key_check_utc": self.online_last_key_check_utc,
                "online_ai_last_request_utc": self.online_last_request_utc,
                "online_ai_last_response_summary": self.online_last_response_summary,
                "online_ai_last_reply_text": self.online_last_reply_text,
                "online_ai_last_validation_result": self.online_last_validation_result,
                "online_ai_last_validation_failure": self.online_last_validation_failure,
                "online_ai_last_http_error": self.online_last_http_error,
                "online_ai_last_latency_ms": self.online_last_latency_ms,
                "online_ai_last_connection_test_result": self.online_last_connection_test_result,
                "online_ai_last_connection_test_ok": self.online_last_connection_test_ok,
                "online_ai_source_backend": self.online_source_backend,
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
        self.local_backend_enabled = str(config.get("tts_backend", "none")) == "pyttsx3"
        self.enabled = self.local_backend_enabled or (
            normalize_tts_mode(config.get("tts_mode", DEFAULT_TTS_MODE)) == "online")
        self.queue = queue.Queue(maxsize=200)
        self.stop_event = threading.Event()
        self.interrupt_event = threading.Event()
        self._active_process = None
        self._active_process_lock = threading.Lock()
        self._active_temp_audio_path = ""
        self._active_temp_audio_path_lock = threading.Lock()
        self.thread = None
        self._pyttsx3_module = None

    def start(self) -> None:
        if not self.enabled:
            self.state.log("info", "TTS disabled (no local backend and online TTS is not selected).")
            return
        self.thread = threading.Thread(target=self._worker, daemon=True, name="tts-worker")
        self.thread.start()

    def stop(self) -> None:
        self.stop_event.set()
        self.state.set_tts_speaking(False)
        self.interrupt_event.set()
        self._terminate_active_process()
        self._clear_queue_nonblocking("TTS worker stopped before playback.")
        try:
            self.queue.put_nowait(("", False, None))
        except queue.Full:
            pass
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=1.0)

    def speak(
        self,
        text: str,
        interrupt: bool,
        wait_for_completion: bool = False,
        tts_mode_override: str = "",
    ) -> tuple[bool, str]:
        text = (text or "").strip()
        if not text:
            return False, "Speech text is empty."
        if not self.enabled:
            self.state.log("info", f"TTS stub: {text}")
            return True, "TTS backend disabled; accepted as no-op."

        try:
            completion_queue = queue.Queue(maxsize=1) if wait_for_completion else None
            if interrupt:
                self.interrupt_event.set()
                self._clear_queue_nonblocking("TTS request cleared by interrupt.")
                self._terminate_active_process()
            resolved_tts_mode = normalize_tts_mode(
                tts_mode_override if str(tts_mode_override or "").strip() else self.config.get("tts_mode", DEFAULT_TTS_MODE)
            )
            self.queue.put_nowait((text, interrupt, completion_queue, resolved_tts_mode))
            if not wait_for_completion or completion_queue is None:
                return True, "Speech queued."

            wait_timeout = self._estimate_timeout_seconds(text) + 2.0
            try:
                ok, message = completion_queue.get(timeout=wait_timeout)
                return bool(ok), str(message or "").strip() or "Speech completed."
            except queue.Empty:
                return False, f"TTS completion wait timed out after {wait_timeout:.1f}s."
        except queue.Full:
            return False, "TTS queue is full."

    def interrupt(self) -> tuple[bool, str]:
        self.interrupt_event.set()
        self._clear_queue_nonblocking("TTS request cleared by interrupt.")
        self._terminate_active_process()
        self.state.set_tts_speaking(False)
        return True, "TTS playback interrupted."

    def should_stream_online_reply_audio(self) -> bool:
        if normalize_tts_mode(self.config.get("tts_mode", DEFAULT_TTS_MODE)) != "online":
            return False
        if not parse_bool(self.config.get("online_tts_stream_partial_replies"), True):
            return False
        api_key, _env_var = self._read_openai_api_key()
        return bool(api_key)

    def _clear_queue_nonblocking(self, cleared_message: str = "TTS request cleared before playback.") -> None:
        while True:
            try:
                queued_item = self.queue.get_nowait()
            except queue.Empty:
                return
            if not isinstance(queued_item, tuple):
                continue
            if len(queued_item) >= 4:
                text, _interrupt, completion_queue, _tts_mode_override = queued_item
            elif len(queued_item) >= 3:
                text, _interrupt, completion_queue = queued_item
            else:
                continue
            if not text or completion_queue is None:
                continue
            try:
                completion_queue.put_nowait((False, cleared_message))
            except Exception:
                pass

    def _set_active_process(self, proc) -> None:
        with self._active_process_lock:
            self._active_process = proc

    def _set_active_temp_audio_path(self, path: str) -> None:
        with self._active_temp_audio_path_lock:
            self._active_temp_audio_path = str(path or "").strip()

    def _cleanup_active_temp_audio_path(self) -> None:
        path = ""
        with self._active_temp_audio_path_lock:
            path = self._active_temp_audio_path
            self._active_temp_audio_path = ""
        if not path:
            return
        try:
            if os.path.exists(path):
                os.remove(path)
        except Exception:
            pass

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
            self._cleanup_active_temp_audio_path()

    @staticmethod
    def _rate_to_windows_sapi_scale(tts_rate: int) -> int:
        # pyttsx3 defaults around ~175 wpm; map to SAPI's [-10, +10] scale.
        normalized = int(round((int(tts_rate) - 175) / 12.5))
        return max(-10, min(10, normalized))

    def _build_windows_sapi_script(self, text: str) -> str:
        rate = self._rate_to_windows_sapi_scale(int(self.config.get("tts_rate", 175)))
        voice_name = str(self.config.get("tts_voice_name", "")).strip()
        encoded_text = base64.b64encode((text or "").encode("utf-8")).decode("ascii")
        encoded_voice = base64.b64encode(voice_name.encode("utf-8")).decode("ascii")
        return (
            "$ErrorActionPreference = 'Stop'; "
            "Add-Type -AssemblyName System.Speech; "
            "$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
            f"$synth.Rate = {rate}; "
            f"$voiceTokenBytes = [System.Convert]::FromBase64String('{encoded_voice}'); "
            "$voiceToken = [System.Text.Encoding]::UTF8.GetString($voiceTokenBytes).ToLowerInvariant(); "
            "if ($voiceToken) { "
            "  foreach ($voice in $synth.GetInstalledVoices()) { "
            "    $name = $voice.VoiceInfo.Name; "
            "    if ($name.ToLowerInvariant().Contains($voiceToken)) { "
            "      $synth.SelectVoice($name); "
            "      break; "
            "    } "
            "  } "
            "} "
            f"$speechBytes = [System.Convert]::FromBase64String('{encoded_text}'); "
            "$speechText = [System.Text.Encoding]::UTF8.GetString($speechBytes); "
            "$synth.Speak($speechText); "
            "$synth.Dispose();"
        )

    def _estimate_timeout_seconds(self, text: str) -> float:
        configured_timeout = max(5.0, min(600.0, float(self.config.get("tts_timeout_seconds", 30.0))))
        trimmed = (text or "").strip()
        if not trimmed:
            return configured_timeout

        words = re.findall(r"\S+", trimmed)
        word_count = max(1, len(words))
        rate_wpm = max(60, min(320, int(self.config.get("tts_rate", 175))))
        words_per_second = max(0.75, rate_wpm / 60.0)
        punctuation_pauses = sum(trimmed.count(mark) for mark in ".!?;:") * 0.25
        estimated_timeout = (word_count / words_per_second) + punctuation_pauses + 6.0
        return max(configured_timeout, min(600.0, estimated_timeout))

    def _speak_with_subprocess(self, text: str) -> tuple[bool, str]:
        timeout_seconds = self._estimate_timeout_seconds(text)
        script = self._build_windows_sapi_script(text)
        encoded_script = base64.b64encode(script.encode("utf-16le")).decode("ascii")
        cmd = [
            "powershell",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-EncodedCommand",
            encoded_script,
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

    def _read_openai_api_key(self) -> tuple[str, str]:
        api_key_found, env_var = self.state.refresh_online_key_status()
        if not api_key_found:
            return "", env_var
        return os.environ.get(env_var, "").strip(), env_var

    def _speak_local(self, text: str) -> tuple[bool, str]:
        if not self.local_backend_enabled:
            self.state.log("info", f"TTS stub: {text}")
            return True, "TTS backend disabled; accepted as no-op."
        if sys.platform.startswith("win"):
            return self._speak_with_subprocess(text)
        return self._speak_with_fresh_engine(text)

    def _build_online_tts_request_payload(self, text: str) -> dict:
        return {
            "model": (
                str(self.config.get("online_tts_model", DEFAULT_ONLINE_TTS_MODEL)).strip()
                or DEFAULT_ONLINE_TTS_MODEL
            ),
            "voice": (
                str(self.config.get("online_tts_voice", DEFAULT_ONLINE_TTS_VOICE)).strip()
                or DEFAULT_ONLINE_TTS_VOICE
            ),
            "input": str(text or "").strip(),
            "response_format": "wav",
        }

    def _request_online_audio_bytes(self, text: str) -> tuple[bool, bytes, str]:
        api_key, env_var = self._read_openai_api_key()
        if not api_key:
            return False, b"", build_missing_online_api_key_message(env_var)

        endpoint = build_openai_audio_speech_endpoint(
            str(self.config.get("online_ai_base_url", DEFAULT_ONLINE_AI_BASE_URL)))
        timeout_seconds = float(self.config.get("online_ai_timeout_seconds", 15.0))
        body = json.dumps(
            self._build_online_tts_request_payload(text),
            ensure_ascii=False).encode("utf-8")
        request = urllib_request.Request(
            endpoint,
            data=body,
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json",
                "Accept": "audio/wav",
            },
            method="POST",
        )

        try:
            with urllib_request.urlopen(request, timeout=timeout_seconds) as response:
                audio_bytes = response.read()
        except urllib_error.HTTPError as exc:
            error_body = ""
            try:
                error_body = exc.read().decode("utf-8", errors="replace")
            except Exception:
                error_body = ""
            detail = error_body.strip() or str(exc)
            return False, b"", f"OpenAI TTS HTTP {exc.code}: {detail}"
        except urllib_error.URLError as exc:
            return False, b"", f"OpenAI TTS network error: {exc.reason}"
        except Exception as exc:
            return False, b"", f"OpenAI TTS request failed: {exc}"

        if not audio_bytes:
            return False, b"", "OpenAI TTS returned no audio."
        return True, audio_bytes, "OpenAI TTS audio received."

    @staticmethod
    def _build_windows_wav_player_script(audio_path: str) -> str:
        encoded_path = base64.b64encode(str(audio_path or "").encode("utf-8")).decode("ascii")
        return (
            "$ErrorActionPreference = 'Stop'; "
            "Add-Type -AssemblyName System; "
            f"$pathBytes = [System.Convert]::FromBase64String('{encoded_path}'); "
            "$path = [System.Text.Encoding]::UTF8.GetString($pathBytes); "
            "$player = New-Object System.Media.SoundPlayer; "
            "$player.SoundLocation = $path; "
            "$player.Load(); "
            "$player.PlaySync(); "
            "$player.Dispose();"
        )

    def _build_audio_player_command(self, audio_path: str) -> list[str]:
        if sys.platform.startswith("win"):
            script = self._build_windows_wav_player_script(audio_path)
            encoded_script = base64.b64encode(script.encode("utf-16le")).decode("ascii")
            return [
                "powershell",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-EncodedCommand",
                encoded_script,
            ]
        if shutil.which("ffplay"):
            return ["ffplay", "-nodisp", "-autoexit", "-loglevel", "error", audio_path]
        if shutil.which("afplay"):
            return ["afplay", audio_path]
        if shutil.which("aplay"):
            return ["aplay", audio_path]
        if shutil.which("paplay"):
            return ["paplay", audio_path]
        return []

    def _play_wav_bytes_with_subprocess(self, audio_bytes: bytes, text: str) -> tuple[bool, str]:
        temp_path = ""
        try:
            with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as handle:
                handle.write(audio_bytes)
                handle.flush()
                temp_path = handle.name
        except Exception as exc:
            return False, f"Online TTS temp-file creation failed: {exc}"

        cmd = self._build_audio_player_command(temp_path)
        if not cmd:
            try:
                os.remove(temp_path)
            except Exception:
                pass
            return False, "No system audio player is available for online TTS playback."

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
            try:
                os.remove(temp_path)
            except Exception:
                pass
            return False, f"Online TTS playback start failed: {exc}"

        self._set_active_temp_audio_path(temp_path)
        self._set_active_process(proc)
        timeout_seconds = self._estimate_timeout_seconds(text)
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
                return False, f"Online TTS playback timed out after {timeout_seconds:.1f}s."

            time.sleep(0.05)

        self._set_active_process(None)
        self._cleanup_active_temp_audio_path()
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
            return False, f"Online TTS playback exited with code {return_code}.{detail}"

        return True, "OpenAI TTS spoke."

    @staticmethod
    def _normalize_wav_bytes(audio_bytes: bytes) -> bytes:
        raw_bytes = bytes(audio_bytes or b"")
        if not raw_bytes:
            return raw_bytes

        try:
            with wave.open(io.BytesIO(raw_bytes), "rb") as src:
                channel_count = src.getnchannels()
                sample_width = src.getsampwidth()
                sample_rate = src.getframerate()
                frames = src.readframes(10**9)

            with io.BytesIO() as buffer:
                with wave.open(buffer, "wb") as dst:
                    dst.setnchannels(channel_count)
                    dst.setsampwidth(sample_width)
                    dst.setframerate(sample_rate)
                    dst.writeframes(frames)
                return buffer.getvalue()
        except Exception:
            return raw_bytes

    def _speak_online(self, text: str) -> tuple[bool, str]:
        ok, audio_bytes, message = self._request_online_audio_bytes(text)
        if not ok:
            return False, message
        audio_bytes = self._normalize_wav_bytes(audio_bytes)
        if self.stop_event.is_set():
            return False, "TTS stopped."
        if self.interrupt_event.is_set():
            self.interrupt_event.clear()
            return False, "TTS interrupted."
        return self._play_wav_bytes_with_subprocess(audio_bytes, text)

    def _speak_with_selected_provider(self, text: str, tts_mode_override: str = "") -> tuple[bool, str]:
        preferred_mode = normalize_tts_mode(
            tts_mode_override if str(tts_mode_override or "").strip() else self.config.get("tts_mode", DEFAULT_TTS_MODE)
        )
        if preferred_mode != "online":
            return self._speak_local(text)

        ok, message = self._speak_online(text)
        if ok:
            return True, message

        if not self.local_backend_enabled:
            return False, message

        self.state.log("warn", f"OpenAI TTS unavailable, falling back to local TTS. Reason: {message}")
        local_ok, local_message = self._speak_local(text)
        if local_ok:
            return True, f"OpenAI TTS unavailable; local fallback spoke. Reason: {message}"
        return False, f"OpenAI TTS failed: {message} Local fallback failed: {local_message}"

    def _worker(self) -> None:
        if self.local_backend_enabled and sys.platform.startswith("win"):
            self.state.log("info", "TTS backend windows_sapi initialized.")
        elif self.local_backend_enabled:
            try:
                import pyttsx3  # type: ignore
                self._pyttsx3_module = pyttsx3
                self.state.log("info", "TTS backend pyttsx3 initialized.")
            except Exception as exc:
                self.local_backend_enabled = False
                if normalize_tts_mode(self.config.get("tts_mode", DEFAULT_TTS_MODE)) != "online":
                    self.enabled = False
                    self.state.log("error", f"pyttsx3 unavailable; TTS disabled: {exc}")
                    return
                self.state.log("warn", f"pyttsx3 unavailable; local TTS fallback disabled: {exc}")

        while not self.stop_event.is_set():
            try:
                queued_item = self.queue.get(timeout=0.2)
            except queue.Empty:
                continue
            if isinstance(queued_item, tuple):
                if len(queued_item) >= 4:
                    text, _interrupt, completion_queue, tts_mode_override = queued_item
                elif len(queued_item) >= 3:
                    text, _interrupt, completion_queue = queued_item
                    tts_mode_override = ""
                elif len(queued_item) == 2:
                    text, _interrupt = queued_item
                    completion_queue = None
                    tts_mode_override = ""
                else:
                    continue
            else:
                continue
            if not text:
                continue
            ok = False
            message = "TTS worker did not process the request."
            try:
                if self.interrupt_event.is_set():
                    self.interrupt_event.clear()
                self.state.set_tts_speaking(True)
                ok, message = self._speak_with_selected_provider(text, tts_mode_override)
                if ok:
                    self.state.log("info", f"TTS spoke: {text}")
                else:
                    self.state.log("error", message)
            finally:
                self.state.set_tts_speaking(False)
                if completion_queue is not None:
                    try:
                        completion_queue.put_nowait((ok, message))
                    except Exception:
                        pass


class DisabledSTT:
    def __init__(self, config: dict, state: State, requested_backend: str) -> None:
        self.config = config
        self.state = state
        self.requested_backend = requested_backend
        self.backend_name = "none"

    def start(self) -> None:
        self.state.set_runtime(False, False, self.backend_name)
        if self.requested_backend == "none":
            self.state.log("info", "STT disabled (stt_backend='none').")
        else:
            self.state.log("warn", f"STT backend '{self.requested_backend}' resolved to 'none'.")

    def stop(self) -> None:
        pass


class MicrophoneSTTBase:
    def __init__(
        self,
        config: dict,
        state: State,
        parser: Parser,
        online_orchestrator: "OnlineAIOrchestrator",
        *,
        backend_name: str,
        thread_name: str,
    ) -> None:
        self.config = config
        self.state = state
        self.parser = parser
        self.online_orchestrator = online_orchestrator
        self.backend_name = backend_name
        self.thread_name = thread_name
        self.stop_event = threading.Event()
        self.thread = None

    def start(self) -> None:
        self.thread = threading.Thread(target=self._run, daemon=True, name=self.thread_name)
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
        raise NotImplementedError


class ReachyRemoteMicrophoneStream:
    def __init__(self, config: dict, state: State, sample_rate_hz: int, blocksize: int) -> None:
        self.config = config
        self.state = state
        self.sample_rate_hz = max(8000, int(sample_rate_hz))
        self.blocksize = max(160, int(blocksize))
        self.bytes_per_chunk = self.blocksize * 2
        self.audio_q: "queue.Queue[bytes]" = queue.Queue(maxsize=120)
        self.stop_event = threading.Event()
        self.thread = None
        self._signature = ""
        self._ssh_host = ""
        self._ssh_port = DEFAULT_REACHY_MIC_SSH_PORT
        self._ssh_user = DEFAULT_REACHY_MIC_SSH_USER
        self._ssh_password = DEFAULT_REACHY_MIC_SSH_PASSWORD
        self._device_spec_override = ""
        self._process = None
        self._process_lock = threading.Lock()
        self._paramiko_client = None
        self._paramiko_channel = None
        self._device_name = ""
        self._last_error = ""
        self._available = False

    @property
    def device_name(self) -> str:
        return self._device_name

    @property
    def last_error(self) -> str:
        return self._last_error

    @property
    def available(self) -> bool:
        return self._available

    def ensure_running(
        self,
        enabled: bool,
        *,
        ssh_host: str,
        ssh_port: int,
        ssh_user: str,
        ssh_password: str,
        device_spec_override: str,
    ) -> None:
        if not enabled:
            self.stop()
            self._available = False
            self._device_name = ""
            return

        signature = "|".join(
            (
                str(ssh_host or "").strip(),
                str(max(1, int(ssh_port))),
                str(ssh_user or "").strip(),
                str(device_spec_override or "").strip(),
            )
        )
        if self.thread is not None and self.thread.is_alive() and signature == self._signature:
            return

        self.stop()
        self._signature = signature
        self._ssh_host = str(ssh_host or "").strip()
        self._ssh_port = max(1, int(ssh_port))
        self._ssh_user = str(ssh_user or "").strip() or DEFAULT_REACHY_MIC_SSH_USER
        self._ssh_password = str(ssh_password or "").strip()
        self._device_spec_override = str(device_spec_override or "").strip()
        self.stop_event = threading.Event()
        self.thread = threading.Thread(
            target=self._run,
            daemon=True,
            name="reachy-remote-mic",
        )
        self.thread.start()

    def stop(self) -> None:
        self.stop_event.set()
        self._terminate_active_capture()
        self._clear_queue_nonblocking()
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=1.0)
        self.thread = None
        self._available = False

    def read_chunk(self, timeout: float) -> bytes | None:
        try:
            return self.audio_q.get(timeout=max(0.0, float(timeout)))
        except queue.Empty:
            return None

    def _clear_queue_nonblocking(self) -> None:
        while True:
            try:
                self.audio_q.get_nowait()
            except queue.Empty:
                return

    def _set_last_error(self, message: str) -> None:
        detail = str(message or "").strip()
        if detail == self._last_error:
            return
        self._last_error = detail
        if detail:
            self.state.log("warn", f"Reachy microphone: {detail}")

    def _set_active_process(self, proc) -> None:
        with self._process_lock:
            self._process = proc

    def _terminate_active_capture(self) -> None:
        try:
            if self._paramiko_channel is not None:
                try:
                    self._paramiko_channel.close()
                except Exception:
                    pass
                self._paramiko_channel = None

            if self._paramiko_client is not None:
                try:
                    self._paramiko_client.close()
                except Exception:
                    pass
                self._paramiko_client = None

            proc = None
            with self._process_lock:
                proc = self._process
                self._process = None
            if proc is not None:
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
            self._available = False

    def _run(self) -> None:
        while not self.stop_event.is_set():
            if not self._ssh_host:
                self._set_last_error("robot host is empty.")
                break

            try:
                self._capture_until_disconnect()
                if not self.stop_event.is_set():
                    self._set_last_error("remote audio capture ended unexpectedly.")
            except Exception as exc:
                self._set_last_error(str(exc))
            finally:
                self._terminate_active_capture()

            if self.stop_event.is_set():
                break
            time.sleep(2.0)

    def _capture_until_disconnect(self) -> None:
        backend_name = self._select_transport_backend()
        listing = self._query_device_listing(backend_name)
        device_spec, device_name = self._resolve_device_spec(listing)
        self._device_name = device_name
        self._last_error = ""
        self.state.log(
            "info",
            f"Reachy microphone capture starting on {self._ssh_user}@{self._ssh_host}:{self._ssh_port} ({device_spec}).")

        if backend_name == "paramiko":
            self._start_paramiko_capture(device_spec)
            self._drain_paramiko_channel()
            return

        self._start_subprocess_capture(device_spec, backend_name)
        self._drain_subprocess_stdout()

    def _select_transport_backend(self) -> str:
        try:
            import paramiko  # type: ignore  # noqa: F401
            return "paramiko"
        except Exception:
            pass

        if shutil.which("plink"):
            return "plink"

        if shutil.which("ssh"):
            if self._ssh_password:
                raise RuntimeError(
                    "password-based Reachy mic capture needs paramiko or plink in PATH; native ssh fallback is key-based only."
                )
            return "ssh"

        raise RuntimeError("no SSH transport is available for Reachy mic capture (install paramiko or plink/ssh).")

    def _query_device_listing(self, backend_name: str) -> str:
        command_text = "arecord -l 2>/dev/null || true"
        if backend_name == "paramiko":
            return self._run_paramiko_query(command_text)
        return self._run_subprocess_query(command_text, backend_name)

    def _resolve_device_spec(self, listing: str) -> tuple[str, str]:
        configured = self._device_spec_override or str(self.config.get("reachy_mic_device_spec", "")).strip()
        fallback_spec = configured or DEFAULT_REACHY_MIC_DEVICE_SPEC
        fallback_name = DEFAULT_REACHY_MIC_DEVICE_LABEL if not configured else configured
        pattern = re.compile(
            r"card\s+(\d+):\s*([^\[]+)\[([^\]]+)\],\s*device\s+(\d+):\s*([^\[]+)\[([^\]]+)\]",
            re.IGNORECASE,
        )
        best_score = -10**9
        best_spec = fallback_spec
        best_name = fallback_name

        for raw_line in str(listing or "").splitlines():
            match = pattern.search(raw_line)
            if not match:
                continue
            card_number = match.group(1)
            device_number = match.group(4)
            labels = " ".join(
                (
                    match.group(2).strip(),
                    match.group(3).strip(),
                    match.group(5).strip(),
                    match.group(6).strip(),
                )
            ).strip()
            lowered = labels.lower()
            score = 0
            if "respeaker" in lowered:
                score += 1000
            if "mic array" in lowered:
                score += 240
            if "usb audio" in lowered:
                score += 60
            if device_number == "0":
                score += 12
            if score <= best_score:
                continue
            best_score = score
            best_spec = f"plughw:{card_number},{device_number}"
            best_name = labels or f"card {card_number}, device {device_number}"

        return best_spec, best_name

    def _run_paramiko_query(self, command_text: str) -> str:
        try:
            import paramiko  # type: ignore
        except Exception as exc:
            raise RuntimeError(f"paramiko is unavailable: {exc}") from exc

        timeout_seconds = 6.0
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        try:
            client.connect(
                hostname=self._ssh_host,
                port=self._ssh_port,
                username=self._ssh_user,
                password=self._ssh_password or None,
                timeout=timeout_seconds,
                banner_timeout=timeout_seconds,
                auth_timeout=timeout_seconds,
                look_for_keys=True,
                allow_agent=True,
            )
            _stdin, stdout, stderr = client.exec_command(command_text, timeout=timeout_seconds)
            output = stdout.read().decode("utf-8", errors="replace")
            error_text = stderr.read().decode("utf-8", errors="replace").strip()
            exit_status = stdout.channel.recv_exit_status()
            if exit_status != 0 and error_text:
                raise RuntimeError(error_text)
            return output
        finally:
            try:
                client.close()
            except Exception:
                pass

    def _run_subprocess_query(self, command_text: str, backend_name: str) -> str:
        cmd = self._build_subprocess_command(command_text, backend_name)
        try:
            completed = subprocess.run(
                cmd,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                timeout=6.0,
                check=False,
                text=True,
            )
        except Exception as exc:
            raise RuntimeError(f"{backend_name} device query failed: {exc}") from exc

        if completed.returncode != 0:
            stderr_text = (completed.stderr or "").strip()
            if stderr_text:
                raise RuntimeError(stderr_text)
        return completed.stdout or ""

    def _start_paramiko_capture(self, device_spec: str) -> None:
        try:
            import paramiko  # type: ignore
        except Exception as exc:
            raise RuntimeError(f"paramiko is unavailable: {exc}") from exc

        timeout_seconds = 8.0
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(
            hostname=self._ssh_host,
            port=self._ssh_port,
            username=self._ssh_user,
            password=self._ssh_password or None,
            timeout=timeout_seconds,
            banner_timeout=timeout_seconds,
            auth_timeout=timeout_seconds,
            look_for_keys=True,
            allow_agent=True,
        )

        command_text = self._build_arecord_command_text(device_spec)
        transport = client.get_transport()
        if transport is None:
            client.close()
            raise RuntimeError("paramiko transport was not created.")

        channel = transport.open_session()
        channel.exec_command(command_text)
        self._paramiko_client = client
        self._paramiko_channel = channel
        self._available = True

    def _drain_paramiko_channel(self) -> None:
        channel = self._paramiko_channel
        if channel is None:
            raise RuntimeError("paramiko capture channel is not open.")

        pending = bytearray()
        while not self.stop_event.is_set():
            if channel.recv_ready():
                chunk = channel.recv(max(512, self.bytes_per_chunk))
                if not chunk:
                    break
                self._available = True
                pending.extend(chunk)
                self._flush_capture_chunks(pending)
                continue

            if channel.exit_status_ready():
                stderr_text = b""
                try:
                    if channel.recv_stderr_ready():
                        stderr_text = channel.recv_stderr(4096)
                except Exception:
                    stderr_text = b""
                detail = stderr_text.decode("utf-8", errors="replace").strip()
                raise RuntimeError(detail or "remote capture process exited.")

            time.sleep(0.01)

    def _start_subprocess_capture(self, device_spec: str, backend_name: str) -> None:
        cmd = self._build_subprocess_command(
            self._build_arecord_command_text(device_spec),
            backend_name,
        )
        creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
        try:
            proc = subprocess.Popen(
                cmd,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                creationflags=creationflags,
            )
        except Exception as exc:
            raise RuntimeError(f"{backend_name} capture start failed: {exc}") from exc

        self._set_active_process(proc)
        self._available = True

    def _drain_subprocess_stdout(self) -> None:
        proc = None
        with self._process_lock:
            proc = self._process
        if proc is None or proc.stdout is None:
            raise RuntimeError("remote capture process did not provide stdout.")

        pending = bytearray()
        while not self.stop_event.is_set():
            chunk = proc.stdout.read(max(512, self.bytes_per_chunk))
            if not chunk:
                break
            self._available = True
            pending.extend(chunk)
            self._flush_capture_chunks(pending)

        stderr_text = ""
        try:
            if proc.stderr is not None:
                stderr_text = proc.stderr.read().decode("utf-8", errors="replace").strip()
        except Exception:
            stderr_text = ""

        if self.stop_event.is_set():
            return
        raise RuntimeError(stderr_text or "remote capture process ended.")

    def _flush_capture_chunks(self, pending: bytearray) -> None:
        while len(pending) >= self.bytes_per_chunk:
            frame = bytes(pending[: self.bytes_per_chunk])
            del pending[: self.bytes_per_chunk]
            try:
                self.audio_q.put_nowait(frame)
            except queue.Full:
                try:
                    self.audio_q.get_nowait()
                except queue.Empty:
                    pass
                try:
                    self.audio_q.put_nowait(frame)
                except queue.Full:
                    pass

    def _build_arecord_command_text(self, device_spec: str) -> str:
        quoted_spec = shlex.quote(device_spec or DEFAULT_REACHY_MIC_DEVICE_SPEC)
        return (
            "exec arecord -q "
            f"-D {quoted_spec} "
            "-f S16_LE "
            f"-r {self.sample_rate_hz} "
            "-c 1 "
            "-t raw -"
        )

    def _build_subprocess_command(self, command_text: str, backend_name: str) -> list[str]:
        target = f"{self._ssh_user}@{self._ssh_host}"
        if backend_name == "plink":
            cmd = ["plink", "-batch", "-P", str(self._ssh_port)]
            if self._ssh_password:
                cmd.extend(["-pw", self._ssh_password])
            cmd.extend([target, command_text])
            return cmd

        return [
            "ssh",
            "-o",
            "BatchMode=yes",
            "-p",
            str(self._ssh_port),
            target,
            command_text,
        ]


class HybridAudioInput:
    def __init__(
        self,
        owner: MicrophoneSTTBase,
        sd_module,
        sample_rate_hz: int,
        blocksize: int,
    ) -> None:
        self.owner = owner
        self.config = owner.config
        self.state = owner.state
        self.sd = sd_module
        self.sample_rate_hz = max(8000, int(sample_rate_hz))
        self.blocksize = max(160, int(blocksize))
        self.local_q: "queue.Queue[bytes]" = queue.Queue(maxsize=120)
        self.local_stream = None
        self.local_device_index = -1
        self.local_device_name = ""
        self.local_error = ""
        self.last_route_signature = ""
        self.requested_mode = DEFAULT_AUDIO_SOURCE_MODE
        self.effective_mode = "pc"
        self.local_enabled = False
        self.reachy_enabled = False
        self.reachy_connected = False
        self.remote_stream = ReachyRemoteMicrophoneStream(self.config, self.state, self.sample_rate_hz, self.blocksize)

    def close(self) -> None:
        self._close_local_stream()
        self.remote_stream.stop()
        self._update_state()

    def clear_buffers(self) -> None:
        while True:
            try:
                self.local_q.get_nowait()
            except queue.Empty:
                break
        self.remote_stream._clear_queue_nonblocking()

    def has_active_source(self) -> bool:
        return self.local_stream is not None or self.remote_stream.available

    def describe_route(self) -> str:
        display_name = self.state.selected_input_device_name
        return display_name or self.effective_mode

    def read_chunk(self, timeout: float) -> bytes | None:
        self._sync_sources()
        deadline = time.time() + max(0.02, float(timeout))
        local_chunk = None
        reachy_chunk = None

        while time.time() < deadline:
            if local_chunk is None and self.local_stream is not None:
                local_chunk = self._try_get_queue_item(self.local_q, 0.01)
            if reachy_chunk is None and self.reachy_enabled:
                reachy_chunk = self.remote_stream.read_chunk(0.01)

            if local_chunk is not None or reachy_chunk is not None:
                if local_chunk is None and self.local_stream is not None:
                    local_chunk = self._try_get_queue_item(self.local_q, 0.015)
                if reachy_chunk is None and self.reachy_enabled:
                    reachy_chunk = self.remote_stream.read_chunk(0.015)
                break

        if local_chunk is None and reachy_chunk is None:
            self._update_state()
            return None

        mixed = self._mix_chunks(local_chunk, reachy_chunk)
        self._update_state()
        return mixed

    @staticmethod
    def _try_get_queue_item(audio_q: "queue.Queue[bytes]", timeout: float) -> bytes | None:
        try:
            return audio_q.get(timeout=max(0.0, float(timeout)))
        except queue.Empty:
            return None

    def _sync_sources(self) -> None:
        requested_mode = normalize_audio_source_mode(
            self.config.get("audio_source_mode", DEFAULT_AUDIO_SOURCE_MODE))
        reachy_host = str(self.config.get("reachy_mic_ssh_host", "")).strip()
        reachy_connected = parse_bool(self.config.get("reachy_mic_robot_connected"), False) and bool(reachy_host)
        if not reachy_connected:
            effective_mode = "pc"
            local_enabled = True
            reachy_enabled = False
        elif requested_mode == "pc":
            effective_mode = "pc"
            local_enabled = True
            reachy_enabled = False
        elif requested_mode == "reachy":
            effective_mode = "reachy"
            local_enabled = False
            reachy_enabled = True
        else:
            effective_mode = "blend"
            local_enabled = True
            reachy_enabled = True

        self.requested_mode = requested_mode
        self.effective_mode = effective_mode
        self.local_enabled = local_enabled
        self.reachy_enabled = reachy_enabled
        self.reachy_connected = reachy_connected

        route_signature = "|".join(
            (
                requested_mode,
                effective_mode,
                "1" if local_enabled else "0",
                "1" if reachy_enabled else "0",
                reachy_host,
                str(self.config.get("audio_input_device_name", "")).strip(),
                "1" if parse_bool(self.config.get("prefer_non_virtual_input_device"), True) else "0",
            )
        )
        if route_signature == self.last_route_signature:
            return

        self.last_route_signature = route_signature

        if local_enabled:
            self._ensure_local_stream()
        else:
            self._close_local_stream()

        self.remote_stream.ensure_running(
            reachy_enabled,
            ssh_host=reachy_host,
            ssh_port=int(self.config.get("reachy_mic_ssh_port", DEFAULT_REACHY_MIC_SSH_PORT)),
            ssh_user=str(self.config.get("reachy_mic_ssh_user", DEFAULT_REACHY_MIC_SSH_USER)).strip(),
            ssh_password=str(self.config.get("reachy_mic_ssh_password", DEFAULT_REACHY_MIC_SSH_PASSWORD)).strip(),
            device_spec_override=str(self.config.get("reachy_mic_device_spec", "")).strip(),
        )
        self._update_state()

    def _ensure_local_stream(self) -> None:
        if self.sd is None:
            if self.local_error != "sounddevice unavailable":
                self.local_error = "sounddevice unavailable"
                self.state.log("warn", "PC microphone is unavailable because sounddevice is not installed.")
            self._close_local_stream()
            return

        selected_device_index, selected_device_name, select_err = self.owner._select_input_device(self.sd)
        if selected_device_index is None:
            if select_err and select_err != self.local_error:
                self.local_error = select_err
                self.state.log("warn", f"PC microphone selection failed: {select_err}")
            self._close_local_stream()
            return

        if (
            self.local_stream is not None and
            self.local_device_index == selected_device_index and
            self.local_device_name == selected_device_name
        ):
            self.local_error = ""
            return

        self._close_local_stream()

        def callback(indata, _frames, _time_info, status):
            if status:
                self.state.log("warn", f"Audio callback status: {status}")
            try:
                self.local_q.put_nowait(bytes(indata))
            except queue.Full:
                pass

        try:
            stream = self.sd.RawInputStream(
                device=selected_device_index,
                samplerate=self.sample_rate_hz,
                blocksize=self.blocksize,
                dtype="int16",
                channels=1,
                callback=callback,
            )
            stream.start()
        except Exception as exc:
            self.local_error = str(exc)
            self.state.log("warn", f"PC microphone open failed: {exc}")
            self._close_local_stream()
            return

        self.local_stream = stream
        self.local_device_index = selected_device_index
        self.local_device_name = selected_device_name
        self.local_error = ""

    def _close_local_stream(self) -> None:
        if self.local_stream is not None:
            try:
                self.local_stream.stop()
            except Exception:
                pass
            try:
                self.local_stream.close()
            except Exception:
                pass
            self.local_stream = None
        self.local_device_index = -1
        self.local_device_name = ""
        while True:
            try:
                self.local_q.get_nowait()
            except queue.Empty:
                break

    def _update_state(self) -> None:
        display_parts: list[str] = []
        selected_index = self.local_device_index if self.local_stream is not None else -1

        if self.local_stream is not None and self.local_device_name:
            display_parts.append(f"PC [{self.local_device_index}] {self.local_device_name}")
        elif self.local_enabled and self.local_error and self.requested_mode == "pc":
            display_parts.append("PC microphone unavailable")

        if self.reachy_enabled:
            if self.remote_stream.device_name:
                display_parts.append(f"Reachy {self.remote_stream.device_name}")
            elif self.remote_stream.last_error:
                display_parts.append("Reachy microphone unavailable")

        if not display_parts:
            if self.effective_mode == "reachy":
                display_name = "Reachy microphone unavailable"
            else:
                display_name = "No microphone source active"
        elif len(display_parts) == 1:
            display_name = display_parts[0]
        else:
            display_name = "Blend: " + " + ".join(display_parts)

        self.state.set_audio_source_status(
            requested_mode=self.requested_mode,
            effective_mode=self.effective_mode,
            display_name=display_name,
            local_device_index=selected_index,
            local_device_name=self.local_device_name,
            reachy_device_name=self.remote_stream.device_name,
            reachy_connected=self.reachy_connected,
            reachy_available=self.remote_stream.available,
            reachy_error=self.remote_stream.last_error,
        )

    @staticmethod
    def _mix_chunks(first: bytes | None, second: bytes | None) -> bytes | None:
        if first and not second:
            return first
        if second and not first:
            return second
        if not first or not second:
            return None

        sample_bytes = min(len(first), len(second))
        sample_bytes -= sample_bytes % 2
        if sample_bytes <= 0:
            return first or second

        first_view = memoryview(first[:sample_bytes]).cast("h")
        second_view = memoryview(second[:sample_bytes]).cast("h")
        mixed = bytearray(sample_bytes)
        mixed_view = memoryview(mixed).cast("h")
        for index in range(len(mixed_view)):
            value = int((int(first_view[index]) + int(second_view[index])) / 2)
            if value > 32767:
                value = 32767
            elif value < -32768:
                value = -32768
            mixed_view[index] = value
        return bytes(mixed)
class VoskSTT(MicrophoneSTTBase):
    def __init__(self, config: dict, state: State, parser: Parser, online_orchestrator: OnlineAIOrchestrator) -> None:
        super().__init__(
            config,
            state,
            parser,
            online_orchestrator,
            backend_name="vosk",
            thread_name="vosk-stt",
        )

    @staticmethod
    def _reset_recognizer(recognizer) -> None:
        try:
            reset_fn = getattr(recognizer, "Reset", None)
            if callable(reset_fn):
                reset_fn()
        except Exception:
            # Reset support is recognizer-version dependent; ignore failures.
            pass

    def _run(self) -> None:
        try:
            import vosk  # type: ignore
        except Exception as exc:
            self.state.set_runtime(False, False, "none")
            self.state.log("error", f"Vosk unavailable; STT disabled: {exc}")
            return

        try:
            import sounddevice as sd  # type: ignore
        except Exception:
            sd = None

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

        audio_input = None
        audio_input = HybridAudioInput(
            self,
            sd,
            int(self.config["stt_sample_rate_hz"]),
            4000,
        )
        self.state.set_runtime(audio_input.has_active_source(), self.state.is_listening_enabled(), "vosk")
        self.state.log("info", f"Vosk STT worker started ({audio_input.describe_route()}).")

        try:
            last_listening_state = None
            tts_gate_active = False
            while not self.stop_event.is_set():
                chunk = audio_input.read_chunk(0.25)
                if chunk is None:
                    listening_enabled = self.state.is_listening_enabled() and not self.state.is_tts_speaking()
                    if listening_enabled != last_listening_state:
                        self.state.set_runtime(audio_input.has_active_source(), listening_enabled, "vosk")
                        last_listening_state = listening_enabled
                    continue

                tts_speaking = self.state.is_tts_speaking()
                if tts_speaking:
                    if last_listening_state is not False:
                        self.state.set_runtime(audio_input.has_active_source(), False, "vosk")
                        last_listening_state = False
                    if not tts_gate_active:
                        tts_gate_active = True
                        self.state.clear_pending_partial()
                        self._reset_recognizer(recognizer)
                        self.state.log("info", "STT input gated while TTS is speaking.")
                    continue
                if tts_gate_active:
                    tts_gate_active = False
                    self._reset_recognizer(recognizer)
                    self.state.log("info", "STT input resumed after TTS completed.")

                listening_enabled = self.state.is_listening_enabled()
                if listening_enabled != last_listening_state:
                    self.state.set_runtime(audio_input.has_active_source(), listening_enabled, "vosk")
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
                            self.online_orchestrator,
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
                            self.online_orchestrator,
                        )
        except Exception as exc:
            self.state.log("error", f"Vosk STT runtime failed: {exc}")
        finally:
            if audio_input is not None:
                try:
                    audio_input.close()
                except Exception:
                    pass
            self.state.set_runtime(False, False, "vosk")
            self.state.log("warn", "Vosk STT worker stopped.")


class OpenAITranscribeSTT(MicrophoneSTTBase):
    def __init__(self, config: dict, state: State, parser: Parser, online_orchestrator: "OnlineAIOrchestrator") -> None:
        super().__init__(
            config,
            state,
            parser,
            online_orchestrator,
            backend_name="openai_transcribe",
            thread_name="openai-transcribe-stt",
        )

    def _run(self) -> None:
        try:
            import sounddevice as sd  # type: ignore
        except Exception:
            sd = None

        api_key, env_var = self._read_openai_api_key()
        if not api_key:
            self.state.set_runtime(False, False, "none")
            self.state.log(
                "error",
                f"OpenAI STT requires an API key in the local secret store or env var '{env_var}'.",
            )
            return

        sample_rate = int(self.config.get("stt_sample_rate_hz", 16000))
        blocksize = 1600
        chunk_duration_seconds = float(blocksize) / float(sample_rate)
        silence_seconds = float(self.config.get("openai_transcribe_silence_seconds", 0.8))
        min_clip_seconds = float(self.config.get("openai_transcribe_min_clip_seconds", 0.55))
        max_clip_seconds = float(self.config.get("openai_transcribe_max_clip_seconds", 8.0))
        rms_threshold = float(self.config.get("openai_transcribe_rms_threshold", 520))
        pre_roll_seconds = float(self.config.get("openai_transcribe_pre_roll_seconds", 0.25))
        pre_roll_chunk_count = max(0, int(round(pre_roll_seconds / max(chunk_duration_seconds, 0.001))))
        pre_roll_chunks = deque(maxlen=max(1, pre_roll_chunk_count or 1))
        active_chunks: list[bytes] = []
        active_duration_seconds = 0.0
        trailing_silence_seconds = 0.0
        speech_active = False

        def reset_segment() -> None:
            nonlocal active_chunks, active_duration_seconds, trailing_silence_seconds, speech_active
            active_chunks = []
            active_duration_seconds = 0.0
            trailing_silence_seconds = 0.0
            speech_active = False
            pre_roll_chunks.clear()

        audio_input = None
        audio_input = HybridAudioInput(self, sd, sample_rate, blocksize)
        self.state.set_runtime(audio_input.has_active_source(), self.state.is_listening_enabled(), self.backend_name)
        self.state.log("info", f"OpenAI STT worker started ({audio_input.describe_route()}).")

        try:
            last_listening_state = None
            tts_gate_active = False
            while not self.stop_event.is_set():
                chunk = audio_input.read_chunk(0.25)
                if chunk is None:
                    listening_enabled = self.state.is_listening_enabled() and not self.state.is_tts_speaking()
                    if listening_enabled != last_listening_state:
                        self.state.set_runtime(audio_input.has_active_source(), listening_enabled, self.backend_name)
                        last_listening_state = listening_enabled
                    continue

                tts_speaking = self.state.is_tts_speaking()
                if tts_speaking:
                    if last_listening_state is not False:
                        self.state.set_runtime(audio_input.has_active_source(), False, self.backend_name)
                        last_listening_state = False
                    if not tts_gate_active:
                        tts_gate_active = True
                        self.state.clear_pending_partial()
                        reset_segment()
                        audio_input.clear_buffers()
                        self.state.log("info", "STT input gated while TTS is speaking.")
                    continue
                if tts_gate_active:
                    tts_gate_active = False
                    reset_segment()
                    audio_input.clear_buffers()
                    self.state.log("info", "STT input resumed after TTS completed.")

                listening_enabled = self.state.is_listening_enabled()
                if listening_enabled != last_listening_state:
                    self.state.set_runtime(audio_input.has_active_source(), listening_enabled, self.backend_name)
                    last_listening_state = listening_enabled

                if not listening_enabled:
                    reset_segment()
                    continue

                rms = pcm16_rms(chunk)
                is_speech_chunk = rms >= rms_threshold

                if speech_active:
                    active_chunks.append(chunk)
                    active_duration_seconds += chunk_duration_seconds
                    trailing_silence_seconds = 0.0 if is_speech_chunk else (
                        trailing_silence_seconds + chunk_duration_seconds)
                else:
                    if pre_roll_chunk_count > 0:
                        pre_roll_chunks.append(chunk)
                    if not is_speech_chunk:
                        continue
                    speech_active = True
                    active_chunks = list(pre_roll_chunks) if pre_roll_chunk_count > 0 else []
                    active_chunks.append(chunk)
                    active_duration_seconds = chunk_duration_seconds * float(len(active_chunks))
                    trailing_silence_seconds = 0.0

                clip_ready = (
                    active_duration_seconds >= max_clip_seconds or
                    trailing_silence_seconds >= silence_seconds
                )
                if not clip_ready:
                    continue

                clip_duration_seconds = active_duration_seconds
                clip_audio = b"".join(active_chunks)
                reset_segment()

                if clip_duration_seconds < min_clip_seconds or not clip_audio:
                    continue

                try:
                    transcript = self._transcribe_clip(clip_audio, sample_rate)
                except Exception as exc:
                    error_message = str(exc).strip() or "Unknown OpenAI transcription error."
                    friendly = OnlineAIOrchestrator._summarize_online_request_error(error_message)
                    self.state.log("error", f"OpenAI transcription failed: {friendly}")
                    continue

                if not transcript:
                    continue

                self.state.process_transcript(
                    transcript,
                    float(self.config["transcript_default_confidence"]),
                    True,
                    self.parser,
                    self.online_orchestrator,
                )
        except Exception as exc:
            self.state.log("error", f"OpenAI transcription runtime failed: {exc}")
        finally:
            if audio_input is not None:
                try:
                    audio_input.close()
                except Exception:
                    pass
            self.state.set_runtime(False, False, self.backend_name)
            self.state.log("warn", "OpenAI STT worker stopped.")

    def _read_openai_api_key(self) -> tuple[str, str]:
        api_key_found, env_var = self.state.refresh_online_key_status()
        if not api_key_found:
            return "", env_var
        return os.environ.get(env_var, "").strip(), env_var

    def _transcribe_clip(self, pcm_audio: bytes, sample_rate_hz: int) -> str:
        api_key, env_var = self._read_openai_api_key()
        if not api_key:
            raise RuntimeError(build_missing_online_api_key_message(env_var))

        wav_audio = self._encode_wav_bytes(pcm_audio, sample_rate_hz)
        endpoint = build_openai_audio_transcriptions_endpoint(
            str(self.config.get("online_ai_base_url", DEFAULT_ONLINE_AI_BASE_URL)))
        model = (
            str(
                self.config.get(
                    "online_ai_transcription_model",
                    DEFAULT_ONLINE_AI_TRANSCRIBE_MODEL,
                )
            ).strip()
            or DEFAULT_ONLINE_AI_TRANSCRIBE_MODEL
        )
        prompt = self._build_transcription_prompt()
        request_fields = {
            "model": model,
            "response_format": "json",
        }
        if prompt:
            request_fields["prompt"] = prompt

        body, boundary = self._build_multipart_body(
            request_fields,
            file_field_name="file",
            file_name="reachy_mic.wav",
            file_bytes=wav_audio,
            file_content_type="audio/wav",
        )
        timeout_seconds = float(self.config.get("online_ai_timeout_seconds", 15.0))
        request = urllib_request.Request(
            endpoint,
            data=body,
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": f"multipart/form-data; boundary={boundary}",
            },
            method="POST",
        )

        try:
            with urllib_request.urlopen(request, timeout=timeout_seconds) as response:
                raw = response.read().decode("utf-8")
        except urllib_error.HTTPError as exc:
            error_body = ""
            try:
                error_body = exc.read().decode("utf-8")
            except Exception:
                error_body = ""
            detail = error_body.strip() or str(exc)
            raise RuntimeError(f"HTTP {exc.code}: {detail}")
        except urllib_error.URLError as exc:
            raise RuntimeError(f"Network error: {exc.reason}")

        if not raw.strip():
            return ""

        try:
            payload = json.loads(raw)
        except Exception:
            return raw.strip()

        return str(payload.get("text", "") or "").strip()

    def _build_transcription_prompt(self) -> str:
        language_hints = [
            str(item).strip().lower()
            for item in self.config.get("openai_transcribe_language_hints", [])
            if str(item).strip()
        ]
        known_poses = [str(item).strip() for item in self.config.get("known_poses", []) if str(item).strip()]
        known_joints = [str(item).strip() for item in self.config.get("known_joints", []) if str(item).strip()]
        if not language_hints and not known_poses and not known_joints:
            return ""

        pose_text = ", ".join(known_poses[:12])
        joint_text = ", ".join(known_joints[:24])
        parts = ["Transcribe Reachy robot control speech exactly."]
        if language_hints:
            language_names: list[str] = []
            for code in language_hints[:4]:
                if code == "en":
                    language_names.append("English")
                elif code == "fi":
                    language_names.append("Finnish")
                else:
                    language_names.append(code)
            languages_text = ", ".join(language_names)
            parts.append(
                f"The spoken language is most likely {languages_text}. "
                "Prefer those languages when the audio is ambiguous and do not translate."
            )
        if pose_text:
            parts.append(f"Known pose names: {pose_text}.")
        if joint_text:
            parts.append(f"Known joint names: {joint_text}.")
        return " ".join(parts)

    @staticmethod
    def _encode_wav_bytes(pcm_audio: bytes, sample_rate_hz: int) -> bytes:
        with io.BytesIO() as buffer:
            with wave.open(buffer, "wb") as wav_file:
                wav_file.setnchannels(1)
                wav_file.setsampwidth(2)
                wav_file.setframerate(sample_rate_hz)
                wav_file.writeframes(pcm_audio)
            return buffer.getvalue()

    @staticmethod
    def _build_multipart_body(
        fields: dict[str, str],
        *,
        file_field_name: str,
        file_name: str,
        file_bytes: bytes,
        file_content_type: str,
    ) -> tuple[bytes, str]:
        boundary = f"----ReachyBoundary{int(time.time() * 1000)}"
        body = bytearray()

        for key, value in fields.items():
            body.extend(f"--{boundary}\r\n".encode("utf-8"))
            body.extend(f'Content-Disposition: form-data; name="{key}"\r\n\r\n'.encode("utf-8"))
            body.extend(str(value).encode("utf-8"))
            body.extend(b"\r\n")

        body.extend(f"--{boundary}\r\n".encode("utf-8"))
        body.extend(
            f'Content-Disposition: form-data; name="{file_field_name}"; filename="{file_name}"\r\n'.encode("utf-8")
        )
        body.extend(f"Content-Type: {file_content_type}\r\n\r\n".encode("utf-8"))
        body.extend(file_bytes)
        body.extend(b"\r\n")
        body.extend(f"--{boundary}--\r\n".encode("utf-8"))
        return bytes(body), boundary


def build_stt_runtime(
    config: dict,
    state: State,
    parser: Parser,
    online_orchestrator: "OnlineAIOrchestrator",
):
    requested_backend = normalize_stt_backend(config.get("stt_backend", "auto"))
    api_key_available, env_var = state.refresh_online_key_status()
    effective_backend = resolve_effective_stt_backend(config, api_key_available)
    state.set_runtime(False, False, effective_backend)

    if requested_backend == "auto":
        resolution_reason = (
            f"online AI is active and API key '{env_var}' is available"
            if effective_backend == "openai_transcribe"
            else "local fallback is active"
        )
        state.log("info", f"STT backend auto-resolved to '{effective_backend}' because {resolution_reason}.")

    if effective_backend == "vosk":
        return VoskSTT(config, state, parser, online_orchestrator)
    if effective_backend == "openai_transcribe":
        return OpenAITranscribeSTT(config, state, parser, online_orchestrator)
    return DisabledSTT(config, state, requested_backend)


class OnlineReplySpeechStreamer:
    def __init__(self, tts: TTS) -> None:
        self.tts = tts
        self._preface_buffer = ""
        self._capturing_reply = False
        self._reply_complete = False
        self._escape_active = False
        self._unicode_digits: str | None = None
        self._pending_segment_buffer = ""
        self.reply_already_spoken = False
        self.failure_message = ""

    def consume_delta(self, delta_text: str) -> None:
        if self.failure_message or self._reply_complete:
            return

        text = str(delta_text or "")
        if not text:
            return

        if not self._capturing_reply:
            self._preface_buffer += text
            match = re.search(r'"reply_text"\s*:\s*"', self._preface_buffer)
            if not match:
                if len(self._preface_buffer) > 512:
                    self._preface_buffer = self._preface_buffer[-512:]
                return

            trailing = self._preface_buffer[match.end():]
            self._preface_buffer = ""
            self._capturing_reply = True
            self._consume_reply_chars(trailing)
            return

        self._consume_reply_chars(text)

    def finish(self) -> None:
        if self.failure_message:
            return
        self._flush_pending_segments(final=True)

    def _consume_reply_chars(self, text: str) -> None:
        for ch in text:
            if self._reply_complete:
                return

            if self._unicode_digits is not None:
                self._unicode_digits += ch
                if len(self._unicode_digits) >= 4:
                    try:
                        decoded = chr(int(self._unicode_digits[:4], 16))
                    except Exception:
                        decoded = ""
                    self._unicode_digits = None
                    self._append_decoded_reply_text(decoded)
                continue

            if self._escape_active:
                self._escape_active = False
                if ch == "u":
                    self._unicode_digits = ""
                    continue
                escape_map = {
                    '"': '"',
                    "\\": "\\",
                    "/": "/",
                    "b": "\b",
                    "f": "\f",
                    "n": "\n",
                    "r": "\r",
                    "t": "\t",
                }
                self._append_decoded_reply_text(escape_map.get(ch, ch))
                continue

            if ch == "\\":
                self._escape_active = True
                continue

            if ch == '"':
                self._capturing_reply = False
                self._reply_complete = True
                self._flush_pending_segments(final=True)
                return

            self._append_decoded_reply_text(ch)

    def _append_decoded_reply_text(self, text: str) -> None:
        if not text or self.failure_message:
            return
        self._pending_segment_buffer += text
        self._flush_pending_segments(final=False)

    def _flush_pending_segments(self, final: bool) -> None:
        if self.failure_message:
            return

        while True:
            segment, remainder = self._extract_ready_segment(self._pending_segment_buffer, final)
            if not segment:
                self._pending_segment_buffer = remainder
                return

            ok, message = self.tts.speak(segment, interrupt=False, wait_for_completion=False)
            self._pending_segment_buffer = remainder
            if ok:
                self.reply_already_spoken = True
                continue

            self.failure_message = str(message or "").strip() or "Streaming TTS segment enqueue failed."
            return

    @staticmethod
    def _extract_ready_segment(buffer: str, final: bool) -> tuple[str, str]:
        text = str(buffer or "")
        if not text:
            return "", ""

        if final:
            return text.strip(), ""

        candidate_break = -1
        for idx, ch in enumerate(text):
            if ch == "\n":
                candidate_break = idx + 1
            elif ch in ".!?":
                if idx + 1 >= len(text) or text[idx + 1].isspace():
                    candidate_break = idx + 1
            elif ch in ";:" and idx >= 18:
                candidate_break = idx + 1
            elif ch == "," and idx >= 64:
                candidate_break = idx + 1

        if candidate_break > 0:
            return text[:candidate_break].strip(), text[candidate_break:].lstrip()

        if len(text) >= 120:
            split_at = text.rfind(" ", 40, 120)
            if split_at > 0:
                return text[:split_at].strip(), text[split_at + 1:].lstrip()

        return "", text


class OnlineAIOrchestrator:
    def __init__(self, config: dict, state: State, tts: TTS | None = None) -> None:
        self.config = config
        self.state = state
        self.tts = tts
        self.source_backend = "openai_responses"
        self._streaming_online_reply_ready = False

    def is_selected_mode(self) -> bool:
        return str(self.config.get("ai_mode", "local")).strip().lower() == "online"

    def _build_reply_streamer(self, *, connection_test: bool) -> OnlineReplySpeechStreamer | None:
        if connection_test or self.tts is None:
            return None
        if not self._streaming_online_reply_ready:
            return None
        if not self.tts.should_stream_online_reply_audio():
            return None
        return OnlineReplySpeechStreamer(self.tts)

    def handle_transcript(self, transcript: str, confidence: float) -> tuple[dict | None, str]:
        resolved_confidence = max(0.0, min(1.0, confidence if confidence > 0.0 else 0.85))
        if not self.is_selected_mode():
            return None, "online_mode_inactive"

        if not parse_bool(self.config.get("online_ai_enabled"), False):
            message = "Online AI mode is selected, but the online backend is disabled."
            self.state.record_online_response(
                reply_text="Online AI is currently disabled. Switch back to Local AI or enable the online backend.",
                validation_result="disabled",
                validation_failure=message,
                response_summary=message,
                http_error="",
                latency_ms=-1.0,
                source_backend=self.source_backend,
            )
            return self._build_safe_reply_intent(
                transcript,
                resolved_confidence,
                "Online AI is currently disabled. Switch back to Local AI or enable the online backend.",
                validation_status="disabled",
                validation_message=message,
            ), message

        model = str(self.config.get("online_ai_model", DEFAULT_ONLINE_AI_MODEL)).strip() or DEFAULT_ONLINE_AI_MODEL
        if not model:
            message = "Online AI model is empty."
            self.state.record_online_response(
                reply_text="Online AI is not configured yet. Set an OpenAI model name before testing it.",
                validation_result="config_error",
                validation_failure=message,
                response_summary=message,
                http_error="",
                latency_ms=-1.0,
                source_backend=self.source_backend,
            )
            return self._build_safe_reply_intent(
                transcript,
                resolved_confidence,
                "Online AI is not configured yet. Set an OpenAI model name before testing it.",
                validation_status="config_error",
                validation_message=message,
            ), message

        api_key_found, env_var = self.state.refresh_online_key_status()
        api_key = os.environ.get(env_var, "").strip()
        if not api_key_found or not api_key:
            message = build_missing_online_api_key_message(env_var)
            self.state.record_online_response(
                reply_text="I cannot use online AI until the OpenAI API key is available in the app's local secret store or configured environment variable.",
                validation_result="missing_api_key",
                validation_failure=message,
                response_summary=message,
                http_error="",
                latency_ms=-1.0,
                source_backend=self.source_backend,
            )
            return self._build_safe_reply_intent(
                transcript,
                resolved_confidence,
                "I cannot use online AI until the OpenAI API key is available in the app's local secret store or configured environment variable.",
                validation_status="missing_api_key",
                validation_message=message,
            ), message

        reply_streamer = self._build_reply_streamer(connection_test=False)
        self.state.record_online_request_started(model)
        request_started = time.perf_counter()
        try:
            payload = self._request_online_turn(
                api_key,
                transcript,
                reply_streamer=reply_streamer,
            )
            latency_ms = max(0.0, (time.perf_counter() - request_started) * 1000.0)
        except Exception as exc:
            latency_ms = max(0.0, (time.perf_counter() - request_started) * 1000.0)
            error_message = str(exc).strip() or "Unknown online AI request error."
            reply_text = self._build_online_failure_reply(error_message)
            self.state.record_online_response(
                reply_text=reply_text,
                validation_result="http_error",
                validation_failure=error_message,
                response_summary=reply_text,
                http_error=error_message,
                latency_ms=latency_ms,
                source_backend=self.source_backend,
            )
            return self._build_safe_reply_intent(
                transcript,
                resolved_confidence,
                reply_text,
                validation_status="http_error",
                validation_message=error_message,
                reply_already_spoken=bool(reply_streamer and reply_streamer.reply_already_spoken),
            ), f"online_http_error: {error_message}"

        if reply_streamer is not None and reply_streamer.failure_message:
            self.state.log("warn", f"Online reply speech streaming degraded: {reply_streamer.failure_message}")

        normalized_intent, message = self._validate_and_normalize(transcript, resolved_confidence, payload)
        semantic_override, semantic_message = self._try_build_semantic_motion_intent(
            transcript,
            resolved_confidence,
        )
        if semantic_override is not None and self._should_override_with_semantic_motion(normalized_intent):
            normalized_intent = semantic_override
            message = semantic_message

        normalized_intent["source_backend"] = (
            str(normalized_intent.get("source_backend", "")).strip() or self.source_backend)
        normalized_intent["source_mode"] = "online"
        normalized_intent["reply_already_spoken"] = bool(
            reply_streamer is not None and reply_streamer.reply_already_spoken)
        self.state.record_online_response(
            reply_text=normalized_intent.get("reply_text", ""),
            validation_result=normalized_intent.get("validation_status", "validated"),
            validation_failure=normalized_intent.get("validation_message", ""),
            response_summary=message,
            http_error="",
            latency_ms=latency_ms,
            source_backend=normalized_intent.get("source_backend", self.source_backend),
        )
        self._streaming_online_reply_ready = True
        return normalized_intent, message

    def test_connection(self) -> dict:
        model = str(self.config.get("online_ai_model", DEFAULT_ONLINE_AI_MODEL)).strip() or DEFAULT_ONLINE_AI_MODEL
        api_key_found, env_var = self.state.refresh_online_key_status()
        api_key = os.environ.get(env_var, "").strip()
        if not parse_bool(self.config.get("online_ai_enabled"), False):
            message = "Online AI is disabled."
            self.state.record_online_connection_test(False, message, model=model)
            return {
                "ok": False,
                "message": message,
                "model": model,
                "api_key_found": api_key_found,
                "api_key_env_var": env_var,
            }
        if not model:
            message = "Online AI model is empty."
            self.state.record_online_connection_test(False, message, model=model)
            return {
                "ok": False,
                "message": message,
                "model": model,
                "api_key_found": api_key_found,
                "api_key_env_var": env_var,
            }

        if not api_key_found or not api_key:
            message = build_missing_online_api_key_message(env_var)
            self.state.record_online_connection_test(False, message, model=model)
            return {
                "ok": False,
                "message": message,
                "model": model,
                "api_key_found": False,
                "api_key_env_var": env_var,
            }

        self.state.record_online_request_started(model)
        started = time.perf_counter()
        try:
            payload = self._request_online_turn(
                api_key,
                "Connection test. Reply briefly and set action.intent to none.",
                connection_test=True,
            )
            latency_ms = max(0.0, (time.perf_counter() - started) * 1000.0)
            normalized_intent, validation_message = self._validate_and_normalize(
                "Connection test.",
                1.0,
                payload)
            reply_text = normalized_intent.get("reply_text", "")
            result_message = (
                f"Online AI connection OK. Model={model}. "
                f"{validation_message}. Reply='{reply_text}'."
            )
            self.state.record_online_response(
                reply_text=reply_text,
                validation_result=normalized_intent.get("validation_status", "validated"),
                validation_failure=normalized_intent.get("validation_message", ""),
                response_summary=validation_message,
                http_error="",
                latency_ms=latency_ms,
                source_backend=self.source_backend,
            )
            self.state.record_online_connection_test(
                True,
                result_message,
                latency_ms=latency_ms,
                http_error="",
                model=model,
            )
            self._streaming_online_reply_ready = True
            return {
                "ok": True,
                "message": result_message,
                "model": model,
                "api_key_found": True,
                "api_key_env_var": env_var,
                "latency_ms": latency_ms,
                "reply_text": reply_text,
            }
        except Exception as exc:
            latency_ms = max(0.0, (time.perf_counter() - started) * 1000.0)
            error_message = str(exc).strip() or "Unknown online AI request error."
            friendly_reason = self._summarize_online_request_error(error_message)
            self.state.record_online_connection_test(
                False,
                f"Online AI connection failed: {friendly_reason}",
                latency_ms=latency_ms,
                http_error=error_message,
                model=model,
            )
            return {
                "ok": False,
                "message": f"Online AI connection failed: {friendly_reason}",
                "model": model,
                "api_key_found": True,
                "api_key_env_var": env_var,
                "latency_ms": latency_ms,
                "http_error": error_message,
            }

    def _build_online_failure_reply(self, error_message: str) -> str:
        return f"Online AI request failed. {self._summarize_online_request_error(error_message)}"

    @staticmethod
    def _summarize_online_request_error(error_message: str) -> str:
        status_code, error_code, _error_type, server_message = (
            OnlineAIOrchestrator._extract_online_error_details(error_message))
        cleaned_server_message = OnlineAIOrchestrator._clean_online_error_message(server_message)
        lowered_message = cleaned_server_message.lower()
        error_code = str(error_code or "").strip().lower()

        if error_code == "insufficient_quota" or "exceeded your current quota" in lowered_message:
            return (
                "The OpenAI API project is out of quota or billing is not active. "
                "Add credits, enable billing, or raise the project spend limit, then try again.")

        if error_code == "rate_limit_exceeded":
            return "The OpenAI API rate limit was reached. Wait a moment, then try again."

        if error_code in ("invalid_api_key", "incorrect_api_key_provided") or status_code == 401:
            return (
                "The OpenAI API key was rejected. Save a valid API key to the local secret store or configured env var, "
                "restart the sidecar, and try again.")

        if error_code == "model_not_found" or "does not exist" in lowered_message or "not available" in lowered_message:
            return "The selected OpenAI model is not available for this API project. Choose a supported model and try again."

        if error_code == "invalid_json_schema":
            return "The app sent an invalid structured-output schema. Restart the sidecar after updating the app."

        if "hit max_output_tokens and was truncated" in lowered_message:
            return (
                "The structured online reply was cut off by the output token limit. "
                "The sidecar now retries with a larger budget, but if this keeps happening, "
                "raise online_ai_max_output_tokens or keep improvised sequences shorter.")

        raw_text = str(error_message or "").strip().lower()
        if raw_text.startswith("network error:") or "name or service not known" in raw_text or "timed out" in raw_text:
            return "The request could not reach OpenAI. Check internet access, the base URL, and any proxy settings, then try again."

        if status_code is not None and status_code >= 500:
            return "OpenAI returned a server error. Try again in a moment."

        if cleaned_server_message:
            return f"OpenAI rejected the request: {cleaned_server_message}"

        if status_code is not None:
            return f"OpenAI returned HTTP {status_code}. Check the online AI settings and try again."

        return "Check the API key, selected model, and network connection, then try again."

    @staticmethod
    def _extract_online_error_details(error_message: str) -> tuple[int | None, str, str, str]:
        text = str(error_message or "").strip()
        if not text:
            return None, "", "", ""

        status_code = None
        body_text = text
        match = re.match(r"HTTP\s+(\d+):\s*(.*)", text, re.IGNORECASE | re.DOTALL)
        if match:
            try:
                status_code = int(match.group(1))
            except Exception:
                status_code = None
            body_text = match.group(2).strip()

        if not body_text.startswith("{"):
            return status_code, "", "", body_text

        try:
            payload = json.loads(body_text)
        except Exception:
            return status_code, "", "", body_text

        error_payload = payload.get("error")
        if not isinstance(error_payload, dict):
            return status_code, "", "", body_text

        return (
            status_code,
            str(error_payload.get("code", "") or "").strip(),
            str(error_payload.get("type", "") or "").strip(),
            str(error_payload.get("message", "") or "").strip(),
        )

    @staticmethod
    def _clean_online_error_message(message: str) -> str:
        cleaned = str(message or "").replace("\r", " ").replace("\n", " ").strip()
        if not cleaned:
            return ""

        cleaned = cleaned.split("For more information", 1)[0].strip()
        cleaned = re.sub(r"https?://\S+", "", cleaned)
        cleaned = re.sub(r"\s+", " ", cleaned).strip()
        return cleaned.rstrip(" .")

    def _semantic_motion_assist_enabled(self) -> bool:
        return parse_bool(self.config.get("online_ai_enable_motion_assist"), True)

    def _get_online_known_pose_names(self) -> list[str]:
        known_poses = [str(item).strip() for item in self.config.get("known_poses", []) if str(item).strip()]
        normalized_known = {normalize(item) for item in known_poses}
        for pose_name in SEMANTIC_ASSIST_POSE_NAMES:
            if normalize(pose_name) not in normalized_known:
                known_poses.append(pose_name)
                normalized_known.add(normalize(pose_name))
        return known_poses if known_poses else list(DEFAULT_POSES)

    @staticmethod
    def _compact_contains_any(compact_text: str, compact_hints: set[str]) -> bool:
        if not compact_text or not compact_hints:
            return False
        for hint in compact_hints:
            if hint and hint in compact_text:
                return True
        return False

    def _build_semantic_motion_assist_catalog(self) -> list[dict]:
        return [
            {
                "request_family": "raise hand or arm",
                "examples": ["raise your hand", "raise left hand", "nosta kasi ylos"],
                "default_pose": "Right Hand Up",
                "left_pose": "Left Hand Up",
                "right_pose": "Right Hand Up",
                "both_pose": "Hands Up",
                "default_side_if_unspecified": "right",
            },
            {
                "request_family": "wave or greeting gesture",
                "examples": ["wave", "wave your hand", "heiluta kasi"],
                "default_pose": "Right Hand Wave",
                "left_pose": "Left Hand Wave",
                "right_pose": "Right Hand Wave",
                "default_side_if_unspecified": "right",
            },
        ]

    @staticmethod
    def _should_override_with_semantic_motion(intent: dict) -> bool:
        if not isinstance(intent, dict):
            return True

        intent_name = str(intent.get("intent", "")).strip().lower()
        validation_status = str(intent.get("validation_status", "")).strip().lower()
        if intent_name in ("none", "move_joint"):
            return True
        if validation_status and validation_status != "validated":
            return True
        return False

    def _try_build_semantic_motion_intent(
        self,
        transcript: str,
        confidence: float,
    ) -> tuple[dict | None, str]:
        if not self._semantic_motion_assist_enabled():
            return None, "semantic_motion_assist_disabled"

        text = str(transcript or "").strip()
        if not text:
            return None, "semantic_motion_assist_empty_transcript"

        words = tokenize(text)
        if not words:
            return None, "semantic_motion_assist_no_tokens"

        compact = normalize(text)
        normalized_tokens = {normalize(word) for word in words if normalize(word)}
        if normalized_tokens & SEMANTIC_QUESTION_TOKEN_HINT_SET:
            return None, "semantic_motion_assist_question_context"

        if transcript_mentions_known_joint(compact, list(self.config.get("known_joints", []))):
            return None, "semantic_motion_assist_explicit_joint_name"

        if extract_joint_number(
            text,
            safe_mode=parse_bool(self.config.get("safe_numeric_parsing"), True),
            require_target_token=parse_bool(self.config.get("require_target_token_for_joint"), False),
        ) is not None:
            return None, "semantic_motion_assist_numeric_target"

        if normalized_tokens & {"joint", "motor", "moottori", "servo", "degree", "degrees", "deg"}:
            return None, "semantic_motion_assist_joint_specific"

        mentions_left = bool(normalized_tokens & SEMANTIC_LEFT_SIDE_HINT_SET)
        mentions_right = bool(normalized_tokens & SEMANTIC_RIGHT_SIDE_HINT_SET)
        mentions_both = bool(normalized_tokens & SEMANTIC_BOTH_SIDE_HINT_SET)
        mentions_singular_limb = bool(normalized_tokens & SEMANTIC_SINGULAR_LIMB_HINT_SET)
        mentions_plural_limbs = bool(normalized_tokens & SEMANTIC_PLURAL_LIMB_HINT_SET)
        mentions_limb = mentions_singular_limb or mentions_plural_limbs

        has_wave = bool(normalized_tokens & SEMANTIC_WAVE_TOKEN_HINT_SET) or self._compact_contains_any(
            compact,
            SEMANTIC_WAVE_PHRASE_HINT_SET,
        )
        has_raise = bool(normalized_tokens & SEMANTIC_RAISE_TOKEN_HINT_SET) or self._compact_contains_any(
            compact,
            SEMANTIC_RAISE_PHRASE_HINT_SET,
        )

        family = ""
        if has_wave:
            family = "wave"
        elif has_raise and mentions_limb:
            family = "raise"

        if not family:
            return None, "semantic_motion_assist_no_match"

        if family == "wave" and mentions_both:
            return None, "semantic_motion_assist_wave_both_ambiguous"

        side = "right"
        if mentions_both or (family == "raise" and mentions_plural_limbs):
            side = "both"
        elif mentions_left and not mentions_right:
            side = "left"
        elif mentions_right and not mentions_left:
            side = "right"
        elif family == "raise" and mentions_plural_limbs:
            side = "both"

        pose_name = ""
        reply_text = ""
        if family == "wave":
            if side == "left":
                pose_name = "Left Hand Wave"
                reply_text = "Waving with the left hand."
            else:
                pose_name = "Right Hand Wave"
                reply_text = "Waving with the right hand."
        elif family == "raise":
            if side == "both":
                pose_name = "Hands Up"
                reply_text = "Raising both hands."
            elif side == "left":
                pose_name = "Left Hand Up"
                reply_text = "Raising the left hand."
            else:
                pose_name = "Right Hand Up"
                reply_text = "Raising the right hand."

        resolved_pose = self._resolve_name(pose_name, self._get_online_known_pose_names())
        if not resolved_pose:
            return None, f"semantic_motion_assist_pose_not_found:{pose_name}"

        intent = {
            "type": "robot_command",
            "intent": "set_pose",
            "pose_name": resolved_pose,
            "joint_name": "",
            "joint_degrees": 0.0,
            "confidence": max(0.0, min(1.0, confidence if confidence > 0.0 else 0.85)),
            "requires_confirmation": parse_bool(
                self.config.get("online_ai_require_motion_confirmation"),
                False,
            ),
            "reply_text": reply_text,
            "spoken_text": text,
            "source_backend": "semantic_motion_assist",
            "source_mode": "online",
            "validation_status": "semantic_motion_assist",
            "validation_message": (
                f"Mapped an underspecified {family} request to preset pose '{resolved_pose}' "
                f"using side='{side}'."
            ),
            "reply_already_spoken": False,
            "transcript_is_final": True,
        }
        return intent, (
            f"Semantic motion assist mapped '{text}' to preset pose '{resolved_pose}' "
            f"(family={family}, side={side})."
        )

    def _build_online_turn_request_payload(self, transcript: str, connection_test: bool) -> dict:
        system_prompt = self._build_system_prompt()
        user_payload = {
            "user_transcript": str(transcript or "").strip(),
            "ai_mode": str(self.config.get("ai_mode", "local")).strip().lower() or "local",
            "connection_test": bool(connection_test),
            "require_confirmation_for_motion": parse_bool(
                self.config.get("online_ai_require_motion_confirmation"),
                False),
            "simulation_only_mode": parse_bool(self.config.get("simulation_only_mode"), False),
            "block_motion_when_bridge_unhealthy": parse_bool(
                self.config.get("block_motion_when_bridge_unhealthy"),
                True),
            "allow_direct_joint_commands": parse_bool(
                self.config.get("online_ai_allow_direct_joint_commands"),
                True),
            "known_poses": self._get_online_known_pose_names(),
            "known_joints": list(self.config.get("known_joints", [])),
            "joint_degree_limits": {
                "default_min": float(self.config.get("joint_min_degrees", -180.0)),
                "default_max": float(self.config.get("joint_max_degrees", 180.0)),
            },
            "allowed_action_intents": list(ONLINE_ALLOWED_INTENTS),
            "semantic_motion_assist": {
                "enabled": self._semantic_motion_assist_enabled(),
                "prefer_preset_pose_for_vague_arm_or_hand_requests": True,
                "default_single_side_if_unspecified": "right",
                "catalog": self._build_semantic_motion_assist_catalog(),
            },
            "custom_motion_capabilities": {
                "allow_custom_pose_commands": parse_bool(
                    self.config.get("online_ai_allow_custom_pose_commands"),
                    True,
                ),
                "allow_motion_sequence_commands": parse_bool(
                    self.config.get("online_ai_allow_motion_sequence_commands"),
                    True,
                ),
                "custom_pose_max_joints": int(self.config.get("online_ai_custom_pose_max_joints", DEFAULT_ONLINE_CUSTOM_POSE_MAX_JOINTS)),
                "motion_sequence_max_steps": int(self.config.get("online_ai_motion_sequence_max_steps", DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_STEPS)),
                "motion_sequence_max_joints_per_step": int(self.config.get("online_ai_motion_sequence_max_joints_per_step", DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_JOINTS_PER_STEP)),
                "motion_sequence_hold_seconds_range": {
                    "min": float(self.config.get("online_ai_motion_sequence_min_hold_seconds", DEFAULT_ONLINE_MOTION_SEQUENCE_MIN_HOLD_SECONDS)),
                    "max": float(self.config.get("online_ai_motion_sequence_max_hold_seconds", DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_HOLD_SECONDS)),
                },
                "recommended_use_cases": [
                    "dance",
                    "celebrate",
                    "point around",
                    "improvise a short motion",
                ],
            },
        }
        return {
            "model": str(self.config.get("online_ai_model", DEFAULT_ONLINE_AI_MODEL)).strip() or DEFAULT_ONLINE_AI_MODEL,
            "temperature": float(self.config.get("online_ai_temperature", 0.2)),
            "max_output_tokens": int(self.config.get("online_ai_max_output_tokens", DEFAULT_ONLINE_AI_MAX_OUTPUT_TOKENS)),
            "input": [
                {
                    "role": "system",
                    "content": [
                        {
                            "type": "input_text",
                            "text": system_prompt,
                        }
                    ],
                },
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "input_text",
                            "text": json.dumps(user_payload, ensure_ascii=True),
                        }
                    ],
                },
            ],
            "text": {
                "format": {
                    "type": "json_schema",
                    "name": "reachy_online_turn",
                    "strict": True,
                    "schema": self._build_response_schema(),
                }
            },
        }

    def _request_online_turn(
        self,
        api_key: str,
        transcript: str,
        connection_test: bool = False,
        reply_streamer: OnlineReplySpeechStreamer | None = None,
    ) -> dict:
        request_payload = self._build_online_turn_request_payload(transcript, connection_test)
        endpoint = self._build_responses_endpoint()
        timeout_seconds = float(self.config.get("online_ai_timeout_seconds", 15.0))
        if reply_streamer is not None:
            response_payload: dict | None = None
            response_text = ""
            try:
                response_text, response_payload = self._post_online_streaming_request(
                    endpoint,
                    api_key,
                    timeout_seconds,
                    request_payload,
                    reply_streamer,
                )
            finally:
                reply_streamer.finish()

            if not response_text and response_payload is not None:
                response_text = self._extract_response_text(response_payload)

            if not response_text:
                if not reply_streamer.reply_already_spoken:
                    self.state.log(
                        "warn",
                        "Streaming online response returned no structured text; retrying once without streaming.",
                    )
                    return self._request_online_turn(
                        api_key,
                        transcript,
                        connection_test=connection_test,
                        reply_streamer=None,
                    )
                raise RuntimeError("Online AI response did not contain structured text output.")

            try:
                return json.loads(response_text)
            except Exception as exc:
                if not reply_streamer.reply_already_spoken:
                    self.state.log(
                        "warn",
                        f"Streaming structured online response parse failed ({exc}); retrying once without streaming.",
                    )
                    return self._request_online_turn(
                        api_key,
                        transcript,
                        connection_test=connection_test,
                        reply_streamer=None,
                    )

                if self._response_hit_max_output_tokens(response_payload) or self._looks_like_truncated_json_output(response_text):
                    raise RuntimeError(
                        "Structured online response hit max_output_tokens and was truncated. "
                        f"Current limit={int(request_payload['max_output_tokens'])}.")
                raise RuntimeError(f"Structured online response was not valid JSON: {exc}")

        max_output_tokens = int(request_payload["max_output_tokens"])
        response_payload: dict | None = None
        response_text = ""
        for attempt_index in range(2):
            request_payload["max_output_tokens"] = max_output_tokens
            raw = self._post_online_request(endpoint, api_key, timeout_seconds, request_payload)
            response_payload = json.loads(raw)
            response_text = self._extract_response_text(response_payload)
            if not response_text:
                if attempt_index == 0 and self._should_retry_structured_output(
                    response_payload,
                    response_text,
                    None,
                    max_output_tokens,
                ):
                    retry_tokens = self._get_retry_max_output_tokens(max_output_tokens)
                    if retry_tokens > max_output_tokens:
                        self.state.log(
                            "warn",
                            f"Online AI structured output was incomplete at {max_output_tokens} output tokens; retrying with {retry_tokens}.",
                        )
                        max_output_tokens = retry_tokens
                        continue
                raise RuntimeError("Online AI response did not contain structured text output.")

            try:
                return json.loads(response_text)
            except Exception as exc:
                if attempt_index == 0 and self._should_retry_structured_output(
                    response_payload,
                    response_text,
                    exc,
                    max_output_tokens,
                ):
                    retry_tokens = self._get_retry_max_output_tokens(max_output_tokens)
                    if retry_tokens > max_output_tokens:
                        self.state.log(
                            "warn",
                            f"Online AI structured output looked truncated at {max_output_tokens} output tokens; retrying with {retry_tokens}.",
                        )
                        max_output_tokens = retry_tokens
                        continue

                if self._response_hit_max_output_tokens(response_payload) or self._looks_like_truncated_json_output(response_text):
                    raise RuntimeError(
                        "Structured online response hit max_output_tokens and was truncated. "
                        f"Current limit={max_output_tokens}.")
                raise RuntimeError(f"Structured online response was not valid JSON: {exc}")

        raise RuntimeError(
            "Structured online response could not be completed within the configured token budget.")

    @staticmethod
    def _post_online_streaming_request(
        endpoint: str,
        api_key: str,
        timeout_seconds: float,
        request_payload: dict,
        reply_streamer: OnlineReplySpeechStreamer,
    ) -> tuple[str, dict | None]:
        streaming_payload = dict(request_payload)
        streaming_payload["stream"] = True
        body = json.dumps(streaming_payload, ensure_ascii=True).encode("utf-8")
        request = urllib_request.Request(
            endpoint,
            data=body,
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json",
                "Accept": "text/event-stream",
            },
            method="POST",
        )

        response_text_fragments: list[str] = []
        final_response_payload: dict | None = None

        def flush_event(event_name: str, data_lines: list[str]) -> tuple[bool, dict | None]:
            return OnlineAIOrchestrator._process_online_stream_event(
                event_name,
                data_lines,
                response_text_fragments,
                reply_streamer,
                final_response_payload,
            )

        try:
            with urllib_request.urlopen(request, timeout=timeout_seconds) as response:
                event_name = ""
                data_lines: list[str] = []
                while True:
                    raw_line = response.readline()
                    if not raw_line:
                        break
                    line = raw_line.decode("utf-8", errors="replace").rstrip("\r\n")
                    if not line:
                        done, final_response_payload = flush_event(event_name, data_lines)
                        event_name = ""
                        data_lines = []
                        if done:
                            break
                        continue
                    if line.startswith(":"):
                        continue
                    if line.startswith("event:"):
                        event_name = line[6:].strip()
                        continue
                    if line.startswith("data:"):
                        data_lines.append(line[5:].lstrip())

                if data_lines or event_name:
                    _done, final_response_payload = flush_event(event_name, data_lines)
        except urllib_error.HTTPError as exc:
            error_body = ""
            try:
                error_body = exc.read().decode("utf-8")
            except Exception:
                error_body = ""
            detail = error_body.strip() or str(exc)
            raise RuntimeError(f"HTTP {exc.code}: {detail}")
        except urllib_error.URLError as exc:
            raise RuntimeError(f"Network error: {exc.reason}")

        response_text = "".join(fragment for fragment in response_text_fragments if fragment).strip()
        return response_text, final_response_payload

    @staticmethod
    def _process_online_stream_event(
        event_name: str,
        data_lines: list[str],
        response_text_fragments: list[str],
        reply_streamer: OnlineReplySpeechStreamer,
        existing_response_payload: dict | None,
    ) -> tuple[bool, dict | None]:
        if not data_lines and not event_name:
            return False, existing_response_payload

        data_text = "\n".join(data_lines).strip()
        if not data_text:
            return False, existing_response_payload
        if data_text == "[DONE]":
            return True, existing_response_payload

        try:
            payload = json.loads(data_text)
        except Exception as exc:
            raise RuntimeError(f"Streaming online response contained invalid JSON event data: {exc}")

        resolved_event_name = str(event_name or payload.get("type", "")).strip()
        if resolved_event_name == "response.output_text.delta":
            delta = str(payload.get("delta", "") or "")
            if delta:
                response_text_fragments.append(delta)
                reply_streamer.consume_delta(delta)
            return False, existing_response_payload

        if resolved_event_name == "response.output_text.done":
            text = payload.get("text")
            if isinstance(text, str) and text.strip() and not response_text_fragments:
                response_text_fragments.append(text.strip())
            return False, existing_response_payload

        if resolved_event_name in ("response.completed", "response.incomplete"):
            response_payload = payload.get("response")
            if isinstance(response_payload, dict):
                return resolved_event_name == "response.incomplete", response_payload
            if isinstance(payload, dict):
                return resolved_event_name == "response.incomplete", payload

        if resolved_event_name in ("response.failed", "error"):
            raise RuntimeError(OnlineAIOrchestrator._format_online_stream_error(payload))

        return False, existing_response_payload

    @staticmethod
    def _format_online_stream_error(payload: dict) -> str:
        if not isinstance(payload, dict):
            return "Streaming online response failed."

        error_payload = payload.get("error")
        if isinstance(error_payload, dict):
            message = str(error_payload.get("message", "") or "").strip()
            code = str(error_payload.get("code", "") or "").strip()
            if message and code:
                return f"{code}: {message}"
            if message:
                return message
            if code:
                return code

        message = str(payload.get("message", "") or "").strip()
        if message:
            return message
        return "Streaming online response failed."

    @staticmethod
    def _post_online_request(endpoint: str, api_key: str, timeout_seconds: float, request_payload: dict) -> str:
        body = json.dumps(request_payload, ensure_ascii=True).encode("utf-8")
        request = urllib_request.Request(
            endpoint,
            data=body,
            headers={
                "Authorization": f"Bearer {api_key}",
                "Content-Type": "application/json",
            },
            method="POST",
        )

        try:
            with urllib_request.urlopen(request, timeout=timeout_seconds) as response:
                return response.read().decode("utf-8")
        except urllib_error.HTTPError as exc:
            error_body = ""
            try:
                error_body = exc.read().decode("utf-8")
            except Exception:
                error_body = ""
            detail = error_body.strip() or str(exc)
            raise RuntimeError(f"HTTP {exc.code}: {detail}")
        except urllib_error.URLError as exc:
            raise RuntimeError(f"Network error: {exc.reason}")

    def _build_system_prompt(self) -> str:
        operator_prompt = (
            str(self.config.get("online_ai_system_prompt", "You are Reachy's online conversational AI.")).strip()
            or "You are Reachy's online conversational AI.")
        return (
            f"{operator_prompt}\n"
            "You are producing one JSON object for a Reachy robot controller.\n"
            "Rules:\n"
            "- Return valid JSON only.\n"
            "- Keep spoken output in reply_text.\n"
            "- Put one robot action in action.intent.\n"
            "- Always include action.pose_name, action.joint_name, action.joint_degrees, action.speed_scale, action.joint_targets, and action.motion_steps; use null or [] when a field does not apply.\n"
            "- Use intent 'none' when no robot action is needed.\n"
            "- Never invent pose names or joint names.\n"
            "- Never emit actions outside the provided allowlists.\n"
            "- Prefer set_pose for vague full-arm or hand gestures; use move_joint only when the operator names a specific joint or exact degree target.\n"
            "- Do not ask whether the operator means a pose or a motor unless the command is genuinely unsafe or conflicting.\n"
            "- When a single hand or arm is requested without a side, default to the right side.\n"
            "- For natural requests like 'raise your hand', 'raise left hand', 'wave', or 'hands up', choose the closest matching preset pose yourself.\n"
            "- Useful preset mappings include Left Hand Up, Right Hand Up, Hands Up, Left Hand Wave, and Right Hand Wave.\n"
            "- Use intent 'set_custom_pose' when you need a one-off pose that combines multiple joints and no preset pose fits.\n"
            "- Use intent 'play_motion_sequence' for open-ended motions like dancing, celebrating, or improvised gesturing.\n"
            "- For play_motion_sequence, keep reply_text to a short acknowledgement such as 'Okay, I'll do that.' instead of narrating the movement.\n"
            "- For dances or improvised motions, default to 3 or 4 steps with about 2 to 4 joint targets per step unless the operator explicitly asks for a longer routine.\n"
            "- Keep motion step labels extremely short, or set them to null when they do not add value.\n"
            "- Keep custom poses and sequences compact and purposeful instead of returning extremely long choreographies.\n"
            "- If unsure, use intent 'none' and ask a clarifying question in reply_text.\n"
            "- If direct joint commands are not allowed, do not emit move_joint.\n"
            "- Keep reply_text concise and operator-facing.\n"
        )

    @staticmethod
    def _build_response_schema() -> dict:
        joint_target_schema = {
            "type": "object",
            "additionalProperties": False,
            "properties": {
                "joint_name": {"type": "string"},
                "joint_degrees": {"type": "number"},
            },
            "required": ["joint_name", "joint_degrees"],
        }
        motion_step_schema = {
            "type": "object",
            "additionalProperties": False,
            "properties": {
                "label": {"type": ["string", "null"]},
                "hold_seconds": {"type": ["number", "null"]},
                "speed_scale": {"type": ["number", "null"]},
                "joint_targets": {
                    "type": "array",
                    "items": joint_target_schema,
                },
            },
            "required": ["label", "hold_seconds", "speed_scale", "joint_targets"],
        }
        return {
            "type": "object",
            "additionalProperties": False,
            "properties": {
                "reply_text": {"type": "string"},
                "confidence": {"type": "number"},
                "action": {
                    "type": "object",
                    "additionalProperties": False,
                    "properties": {
                        "intent": {
                            "type": "string",
                            "enum": list(ONLINE_ALLOWED_INTENTS),
                        },
                        "pose_name": {"type": ["string", "null"]},
                        "joint_name": {"type": ["string", "null"]},
                        "joint_degrees": {"type": ["number", "null"]},
                        "speed_scale": {"type": ["number", "null"]},
                        "joint_targets": {
                            "type": ["array", "null"],
                            "items": joint_target_schema,
                        },
                        "motion_steps": {
                            "type": ["array", "null"],
                            "items": motion_step_schema,
                        },
                    },
                    "required": [
                        "intent",
                        "pose_name",
                        "joint_name",
                        "joint_degrees",
                        "speed_scale",
                        "joint_targets",
                        "motion_steps",
                    ],
                },
            },
            "required": ["reply_text", "confidence", "action"],
        }

    @staticmethod
    def _get_retry_max_output_tokens(current_max_output_tokens: int) -> int:
        current = max(32, int(current_max_output_tokens or DEFAULT_ONLINE_AI_MAX_OUTPUT_TOKENS))
        return min(2048, max(current * 2, current + 192, DEFAULT_ONLINE_AI_MAX_OUTPUT_TOKENS))

    @staticmethod
    def _response_hit_max_output_tokens(response_payload: dict | None) -> bool:
        if not isinstance(response_payload, dict):
            return False

        incomplete_details = response_payload.get("incomplete_details")
        reason = ""
        if isinstance(incomplete_details, dict):
            reason = str(incomplete_details.get("reason", "") or "").strip().lower()

        status = str(response_payload.get("status", "") or "").strip().lower()
        if "max_output_tokens" in reason:
            return True
        return status == "incomplete" and not reason

    @staticmethod
    def _looks_like_truncated_json_output(response_text: str) -> bool:
        text = str(response_text or "").rstrip()
        if not text:
            return False

        if text.count("{") > text.count("}"):
            return True
        if text.count("[") > text.count("]"):
            return True
        if text.endswith((",", ":", "\\", "\"")):
            return True
        if text.startswith("{") and not text.endswith("}"):
            return True
        if text.startswith("[") and not text.endswith("]"):
            return True
        return False

    def _should_retry_structured_output(
        self,
        response_payload: dict | None,
        response_text: str,
        parse_error: Exception | None,
        current_max_output_tokens: int,
    ) -> bool:
        if self._get_retry_max_output_tokens(current_max_output_tokens) <= current_max_output_tokens:
            return False

        if self._response_hit_max_output_tokens(response_payload):
            return True

        if parse_error is None:
            return False

        error_text = str(parse_error or "").strip().lower()
        if "unterminated string" in error_text or "unexpected end" in error_text:
            return self._looks_like_truncated_json_output(response_text)
        return False

    def _build_responses_endpoint(self) -> str:
        base_url = str(self.config.get("online_ai_base_url", DEFAULT_ONLINE_AI_BASE_URL)).strip()
        if not base_url:
            return f"{DEFAULT_ONLINE_AI_BASE_URL}/responses"

        trimmed = base_url.rstrip("/")
        if trimmed.endswith("/responses"):
            return trimmed
        if trimmed.endswith("/v1"):
            return f"{trimmed}/responses"
        return f"{trimmed}/v1/responses"

    @staticmethod
    def _extract_response_text(response_payload: dict) -> str:
        if not isinstance(response_payload, dict):
            return ""

        top_level_text = response_payload.get("output_text")
        if isinstance(top_level_text, str) and top_level_text.strip():
            return top_level_text.strip()

        output_items = response_payload.get("output")
        if not isinstance(output_items, list):
            return ""

        fragments: list[str] = []
        for item in output_items:
            if not isinstance(item, dict):
                continue
            content_items = item.get("content")
            if not isinstance(content_items, list):
                continue
            for content in content_items:
                if not isinstance(content, dict):
                    continue
                text_value = content.get("text")
                if isinstance(text_value, str) and text_value.strip():
                    fragments.append(text_value.strip())
                    continue
                if isinstance(text_value, dict):
                    nested = text_value.get("value")
                    if isinstance(nested, str) and nested.strip():
                        fragments.append(nested.strip())
                        continue
                if isinstance(content.get("output_text"), str) and content["output_text"].strip():
                    fragments.append(content["output_text"].strip())

        return "\n".join(fragment for fragment in fragments if fragment).strip()

    def _normalize_speed_scale(self, value, *, allow_zero_default: bool) -> tuple[float | None, str]:
        if value is None:
            return (0.0 if allow_zero_default else None), ""

        try:
            speed_scale = float(value)
        except Exception:
            return None, "speed_scale was not numeric."

        if allow_zero_default and speed_scale <= 0.0:
            return 0.0, ""

        if speed_scale < 0.05 or speed_scale > 2.0:
            return None, "speed_scale must stay within [0.05, 2.0]."

        return speed_scale, ""

    def _normalize_joint_targets(
        self,
        targets_value,
        *,
        max_targets: int,
        validation_prefix: str,
    ) -> tuple[list[dict] | None, str]:
        if not isinstance(targets_value, list):
            return None, f"{validation_prefix} must be an array."

        if len(targets_value) <= 0:
            return None, f"{validation_prefix} must contain at least one joint target."

        if len(targets_value) > max_targets:
            return None, f"{validation_prefix} exceeds the configured limit of {max_targets} targets."

        joint_min = float(self.config.get("joint_min_degrees", -180.0))
        joint_max = float(self.config.get("joint_max_degrees", 180.0))
        ordered_keys: list[str] = []
        normalized_by_key: dict[str, dict] = {}
        for idx, item in enumerate(targets_value):
            if not isinstance(item, dict):
                return None, f"{validation_prefix}[{idx}] must be an object."

            joint_name = self._coerce_optional_text(item.get("joint_name"))
            resolved_joint = self._resolve_name(joint_name, self.config.get("known_joints", []))
            if not resolved_joint:
                return None, f"{validation_prefix}[{idx}] uses unknown joint '{joint_name}'."

            try:
                joint_degrees = float(item.get("joint_degrees"))
            except Exception:
                return None, f"{validation_prefix}[{idx}].joint_degrees was not numeric."

            if joint_degrees < joint_min or joint_degrees > joint_max:
                return None, (
                    f"{validation_prefix}[{idx}] target {joint_degrees:.1f} is outside "
                    f"[{joint_min:.1f}, {joint_max:.1f}] deg.")

            key = normalize(resolved_joint)
            if key not in normalized_by_key:
                ordered_keys.append(key)
            normalized_by_key[key] = {
                "joint_name": resolved_joint,
                "joint_degrees": float(joint_degrees),
            }

        normalized_targets = [normalized_by_key[key] for key in ordered_keys]
        if len(normalized_targets) <= 0:
            return None, f"{validation_prefix} did not contain any valid joint targets."

        return normalized_targets, ""

    def _normalize_motion_steps(
        self,
        steps_value,
        *,
        default_speed_scale: float,
    ) -> tuple[list[dict] | None, str]:
        if not isinstance(steps_value, list):
            return None, "motion_steps must be an array."

        if len(steps_value) <= 0:
            return None, "motion_steps must contain at least one step."

        max_steps = int(self.config.get("online_ai_motion_sequence_max_steps", DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_STEPS))
        if len(steps_value) > max_steps:
            return None, f"motion_steps exceeds the configured limit of {max_steps} steps."

        max_joints_per_step = int(
            self.config.get(
                "online_ai_motion_sequence_max_joints_per_step",
                DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_JOINTS_PER_STEP,
            )
        )
        min_hold = float(
            self.config.get(
                "online_ai_motion_sequence_min_hold_seconds",
                DEFAULT_ONLINE_MOTION_SEQUENCE_MIN_HOLD_SECONDS,
            )
        )
        max_hold = float(
            self.config.get(
                "online_ai_motion_sequence_max_hold_seconds",
                DEFAULT_ONLINE_MOTION_SEQUENCE_MAX_HOLD_SECONDS,
            )
        )

        normalized_steps: list[dict] = []
        for idx, item in enumerate(steps_value):
            if not isinstance(item, dict):
                return None, f"motion_steps[{idx}] must be an object."

            joint_targets, error_message = self._normalize_joint_targets(
                item.get("joint_targets"),
                max_targets=max_joints_per_step,
                validation_prefix=f"motion_steps[{idx}].joint_targets",
            )
            if joint_targets is None:
                return None, error_message

            raw_speed_scale = item.get("speed_scale", default_speed_scale)
            if raw_speed_scale is None:
                raw_speed_scale = default_speed_scale

            speed_scale, error_message = self._normalize_speed_scale(
                raw_speed_scale,
                allow_zero_default=True,
            )
            if speed_scale is None:
                return None, f"motion_steps[{idx}].{error_message}"

            hold_seconds_value = item.get("hold_seconds", None)
            if hold_seconds_value is None:
                hold_seconds = max(min_hold, 0.45)
            else:
                try:
                    hold_seconds = float(hold_seconds_value)
                except Exception:
                    return None, f"motion_steps[{idx}].hold_seconds was not numeric."

            if hold_seconds < min_hold or hold_seconds > max_hold:
                return None, (
                    f"motion_steps[{idx}].hold_seconds={hold_seconds:.2f} is outside "
                    f"[{min_hold:.2f}, {max_hold:.2f}] seconds.")

            normalized_steps.append({
                "label": self._coerce_optional_text(item.get("label")),
                "hold_seconds": float(hold_seconds),
                "speed_scale": float(speed_scale),
                "joint_targets": joint_targets,
            })

        return normalized_steps, ""

    @staticmethod
    def _normalize_online_reply_text(intent_name: str, reply_text: str) -> str:
        normalized_intent = str(intent_name or "").strip().lower()
        normalized_reply = str(reply_text or "").strip()
        if normalized_intent == "play_motion_sequence":
            return "Okay, I'll do that."

        if normalized_reply:
            return normalized_reply
        return "I am ready."

    def _validate_and_normalize(
        self,
        transcript: str,
        confidence: float,
        payload: dict,
    ) -> tuple[dict, str]:
        if not isinstance(payload, dict):
            return self._build_safe_reply_intent(
                transcript,
                confidence,
                "I could not understand the online AI response safely, so I did not move.",
                validation_status="invalid_schema",
                validation_message="Top-level online response is not a JSON object.",
            ), "Online AI response rejected: top-level payload is not an object."

        reply_text_value = payload.get("reply_text", "")
        reply_text = "" if reply_text_value is None else str(reply_text_value).strip()
        if not reply_text:
            reply_text = "I am ready."

        action = payload.get("action")
        if not isinstance(action, dict):
            return self._build_safe_reply_intent(
                transcript,
                confidence,
                "I could not validate the requested online action, so I did not move.",
                validation_status="invalid_schema",
                validation_message="action must be an object with an intent field.",
            ), "Online AI response rejected: action field was invalid."

        intent_value = action.get("intent", "")
        intent_name = "" if intent_value is None else str(intent_value).strip().lower()
        intent_name = intent_name or "none"
        if intent_name not in ONLINE_ALLOWED_INTENTS:
            return self._build_safe_reply_intent(
                transcript,
                confidence,
                "I could not validate the requested online action, so I did not move.",
                validation_status="unsupported_intent",
                validation_message=f"Unsupported action intent '{intent_name}'.",
            ), f"Online AI response rejected: unsupported action intent '{intent_name}'."

        reply_text = self._normalize_online_reply_text(intent_name, reply_text)

        normalized = {
            "type": "robot_command",
            "intent": intent_name,
            "pose_name": "",
            "joint_name": "",
            "joint_degrees": 0.0,
            "speed_scale": 0.0,
            "joint_targets": [],
            "motion_steps": [],
            "confidence": max(0.0, min(1.0, float(payload.get("confidence", confidence) or confidence))),
            "requires_confirmation": False,
            "reply_text": reply_text,
            "spoken_text": str(transcript or "").strip(),
            "source_backend": self.source_backend,
            "source_mode": "online",
            "validation_status": "validated",
            "validation_message": "",
            "transcript_is_final": True,
        }

        if intent_name == "set_pose":
            pose_name = self._coerce_optional_text(action.get("pose_name"))
            resolved_pose = self._resolve_name(pose_name, self._get_online_known_pose_names())
            if not resolved_pose:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "I could not validate that pose name safely, so I did not move.",
                    validation_status="rejected_pose",
                    validation_message=f"Unknown pose '{pose_name}'.",
                ), f"Online AI response rejected: unknown pose '{pose_name}'."
            normalized["pose_name"] = resolved_pose
            normalized["requires_confirmation"] = parse_bool(
                self.config.get("online_ai_require_motion_confirmation"),
                False)

        if intent_name == "move_joint":
            if not parse_bool(self.config.get("online_ai_allow_direct_joint_commands"), True):
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "Direct online joint commands are disabled, so I did not move.",
                    validation_status="joint_commands_disabled",
                    validation_message="online_ai_allow_direct_joint_commands is false.",
                ), "Online AI response rejected: direct joint commands are disabled."

            joint_name = self._coerce_optional_text(action.get("joint_name"))
            resolved_joint = self._resolve_name(joint_name, self.config.get("known_joints", []))
            if not resolved_joint:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "I could not validate that joint name safely, so I did not move.",
                    validation_status="rejected_joint",
                    validation_message=f"Unknown joint '{joint_name}'.",
                ), f"Online AI response rejected: unknown joint '{joint_name}'."

            try:
                joint_degrees_value = action.get("joint_degrees", 0.0)
                if joint_degrees_value is None:
                    raise ValueError("joint_degrees was null")
                joint_degrees = float(joint_degrees_value)
            except Exception:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "I could not validate that joint target safely, so I did not move.",
                    validation_status="invalid_joint_value",
                    validation_message="joint_degrees was not numeric.",
                ), "Online AI response rejected: joint_degrees was not numeric."

            joint_min = float(self.config.get("joint_min_degrees", -180.0))
            joint_max = float(self.config.get("joint_max_degrees", 180.0))
            if joint_degrees < joint_min or joint_degrees > joint_max:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "That joint target is outside the allowed range, so I did not move.",
                    validation_status="joint_out_of_range",
                    validation_message=(
                        f"Target {joint_degrees:.1f} is outside [{joint_min:.1f}, {joint_max:.1f}] deg."),
                ), (
                    f"Online AI response rejected: target {joint_degrees:.1f} "
                    f"is outside [{joint_min:.1f}, {joint_max:.1f}] deg.")

            normalized["joint_name"] = resolved_joint
            normalized["joint_degrees"] = joint_degrees
            normalized["requires_confirmation"] = parse_bool(
                self.config.get("online_ai_require_motion_confirmation"),
                False)

        if intent_name == "set_custom_pose":
            if not parse_bool(self.config.get("online_ai_allow_direct_joint_commands"), True):
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "Direct online joint commands are disabled, so I did not move.",
                    validation_status="joint_commands_disabled",
                    validation_message="online_ai_allow_direct_joint_commands is false.",
                ), "Online AI response rejected: direct joint commands are disabled."

            if not parse_bool(self.config.get("online_ai_allow_custom_pose_commands"), True):
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "Online AI custom poses are disabled, so I did not move.",
                    validation_status="custom_pose_disabled",
                    validation_message="online_ai_allow_custom_pose_commands is false.",
                ), "Online AI response rejected: custom poses are disabled."

            joint_targets, error_message = self._normalize_joint_targets(
                action.get("joint_targets"),
                max_targets=int(self.config.get("online_ai_custom_pose_max_joints", DEFAULT_ONLINE_CUSTOM_POSE_MAX_JOINTS)),
                validation_prefix="joint_targets",
            )
            if joint_targets is None:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "I could not validate that custom pose safely, so I did not move.",
                    validation_status="invalid_custom_pose",
                    validation_message=error_message,
                ), f"Online AI response rejected: {error_message}"

            speed_scale, error_message = self._normalize_speed_scale(
                action.get("speed_scale"),
                allow_zero_default=True,
            )
            if speed_scale is None:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "I could not validate the custom pose speed safely, so I did not move.",
                    validation_status="invalid_custom_pose_speed",
                    validation_message=error_message,
                ), f"Online AI response rejected: {error_message}"

            normalized["joint_targets"] = joint_targets
            normalized["speed_scale"] = float(speed_scale)
            normalized["requires_confirmation"] = parse_bool(
                self.config.get("online_ai_require_motion_confirmation"),
                False)

        if intent_name == "play_motion_sequence":
            if not parse_bool(self.config.get("online_ai_allow_direct_joint_commands"), True):
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "Direct online joint commands are disabled, so I did not move.",
                    validation_status="joint_commands_disabled",
                    validation_message="online_ai_allow_direct_joint_commands is false.",
                ), "Online AI response rejected: direct joint commands are disabled."

            if not parse_bool(self.config.get("online_ai_allow_motion_sequence_commands"), True):
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "Online AI motion sequences are disabled, so I did not move.",
                    validation_status="motion_sequence_disabled",
                    validation_message="online_ai_allow_motion_sequence_commands is false.",
                ), "Online AI response rejected: motion sequences are disabled."

            default_speed_scale, error_message = self._normalize_speed_scale(
                action.get("speed_scale"),
                allow_zero_default=True,
            )
            if default_speed_scale is None:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "I could not validate the motion sequence speed safely, so I did not move.",
                    validation_status="invalid_motion_sequence_speed",
                    validation_message=error_message,
                ), f"Online AI response rejected: {error_message}"

            motion_steps, error_message = self._normalize_motion_steps(
                action.get("motion_steps"),
                default_speed_scale=float(default_speed_scale),
            )
            if motion_steps is None:
                return self._build_safe_reply_intent(
                    transcript,
                    confidence,
                    "I could not validate that motion sequence safely, so I did not move.",
                    validation_status="invalid_motion_sequence",
                    validation_message=error_message,
                ), f"Online AI response rejected: {error_message}"

            normalized["speed_scale"] = float(default_speed_scale)
            normalized["motion_steps"] = motion_steps
            normalized["requires_confirmation"] = parse_bool(
                self.config.get("online_ai_require_motion_confirmation"),
                False)

        if intent_name == "show_movement":
            normalized["requires_confirmation"] = parse_bool(
                self.config.get("online_ai_require_motion_confirmation"),
                False)

        if intent_name == "stop_motion":
            normalized["requires_confirmation"] = False

        return normalized, f"Online AI response validated for intent '{intent_name}'."

    @staticmethod
    def _coerce_optional_text(value) -> str:
        return "" if value is None else str(value).strip()

    @staticmethod
    def _resolve_name(requested_name: str, candidates) -> str:
        requested = str(requested_name or "").strip()
        if not requested:
            return ""

        if not isinstance(candidates, list):
            candidates = list(candidates or [])

        for candidate in candidates:
            candidate_text = str(candidate or "").strip()
            if candidate_text and candidate_text.lower() == requested.lower():
                return candidate_text

        normalized_requested = normalize(requested)
        for candidate in candidates:
            candidate_text = str(candidate or "").strip()
            if candidate_text and normalize(candidate_text) == normalized_requested:
                return candidate_text

        return ""

    def _build_safe_reply_intent(
        self,
        transcript: str,
        confidence: float,
        reply_text: str,
        *,
        validation_status: str,
        validation_message: str,
        reply_already_spoken: bool = False,
    ) -> dict:
        return {
            "type": "robot_command",
            "intent": "none",
            "pose_name": "",
            "joint_name": "",
            "joint_degrees": 0.0,
            "speed_scale": 0.0,
            "joint_targets": [],
            "motion_steps": [],
            "confidence": max(0.0, min(1.0, confidence if confidence > 0.0 else 0.85)),
            "requires_confirmation": False,
            "reply_text": str(reply_text or "").strip(),
            "spoken_text": str(transcript or "").strip(),
            "source_backend": self.source_backend,
            "source_mode": "online",
            "validation_status": str(validation_status or "").strip(),
            "validation_message": str(validation_message or "").strip(),
            "reply_already_spoken": bool(reply_already_spoken),
            "transcript_is_final": True,
        }


class App:
    def __init__(self, config: dict) -> None:
        self.config = config
        self.state = State(config)
        self.parser = Parser(config)
        self.tts = TTS(config, self.state)
        self.online = OnlineAIOrchestrator(config, self.state, self.tts)
        self.stt = build_stt_runtime(config, self.state, self.parser, self.online)
        self.help_responder = LocalHelpResponder(config, self.state)

    def start(self) -> None:
        self.tts.start()
        self.stt.start()
        self.state.log(
            "info",
            f"Local sidecar ready on {self.config['bind_host']}:{self.config['bind_port']} "
            f"(stt={self.state.stt_backend}, requested_stt={self.config['stt_backend']}, "
            f"tts_mode={self.config.get('tts_mode', DEFAULT_TTS_MODE)}, tts_local_backend={self.config['tts_backend']}, "
            f"help={self.help_responder.backend}, ai_mode={self.config.get('ai_mode', 'local')}).",
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
            return "Try: 'set tray holding pose' or 'set hello pose b'. Movement requires confirmation in Unity."
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
            ok, msg = self.app.tts.speak(
                str(payload.get("text", "")),
                bool(payload.get("interrupt", False)),
                bool(payload.get("wait_for_completion", False)),
                str(payload.get("tts_mode", "")).strip())
            self.app.state.log("info" if ok else "warn", f"/speak -> {msg}")
            self._send(HTTPStatus.OK if ok else HTTPStatus.BAD_REQUEST, {"ok": ok, "message": msg})
            return

        if path == "/stop":
            stop_reason = str(payload.get("reason", "")).strip()
            ok, msg = self.app.tts.interrupt()
            if stop_reason:
                self.app.state.log("info", f"/stop reason: {stop_reason}")
            self.app.state.log("info" if ok else "warn", f"/stop -> {msg}")
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
            result = self.app.state.process_transcript(
                text,
                conf,
                final,
                self.app.parser,
                self.app.online,
            )
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

        if path == "/microphone-source":
            result = apply_microphone_routing_payload(self.app.config, payload)
            self.app.state.log(
                "info",
                f"/microphone-source -> requested={result.get('requested_mode')} effective={result.get('effective_mode')}",
            )
            self._send(HTTPStatus.OK, result)
            return

        if path == "/tts-mode":
            result = apply_tts_mode_payload(self.app.config, payload)
            self.app.state.log(
                "info",
                f"/tts-mode -> requested={result.get('requested_mode')} effective={result.get('effective_mode')}",
            )
            self._send(HTTPStatus.OK, result)
            return

        if path == "/online-test":
            result = self.app.online.test_connection()
            self.app.state.log(
                "info" if result.get("ok") else "warn",
                f"/online-test -> {result.get('message', '')}")
            self._send(HTTPStatus.OK if result.get("ok") else HTTPStatus.BAD_REQUEST, result)
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
