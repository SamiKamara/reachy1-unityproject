import importlib.util
import json
from array import array
from collections import deque
from pathlib import Path
import tempfile
import threading
from types import SimpleNamespace
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from unittest.mock import patch


sidecar_path = (
    Path(__file__).resolve().parents[1]
    / "Assets/ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py"
)
spec = importlib.util.spec_from_file_location("reachy_voice_sidecar", sidecar_path)
sidecar = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sidecar)


class SilentSpeechRecognizer(sidecar.MicrophoneSTTBase):
    def __init__(self, config, state, parser, online, tts):
        backend = sidecar.resolve_effective_stt_backend(config, api_key_available=True)
        super().__init__(config, state, parser, online, tts, backend_name=backend, thread_name=backend)

    def start(self):
        self.state.set_runtime(True, self.state.is_listening_enabled(), self.backend_name)
        super().start()

    def _run(self):
        self.stop_event.wait()


class VoiceAgentModeTests(unittest.TestCase):
    def setUp(self):
        config_dir = self.enterContext(tempfile.TemporaryDirectory())
        config = sidecar.load_config(Path(config_dir) / "voice_config.json")
        self.enterContext(patch.object(sidecar, "build_stt_runtime", SilentSpeechRecognizer))
        self.enterContext(patch.object(sidecar.State, "refresh_online_key_status", return_value=(True, "TEST_KEY")))
        for method in ("start", "stop", "interrupt"):
            self.enterContext(patch.object(sidecar.TTS, method))

        self.app = sidecar.App(config, start_active=False)
        self.app.start()
        self.addCleanup(self.app.stop)
        self.server = sidecar.ThreadingHTTPServer(("127.0.0.1", 0), sidecar.Handler)
        self.server.app = self.app
        self.addCleanup(self.server.server_close)
        thread = threading.Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True)
        thread.start()
        self.addCleanup(self.server.shutdown)

    def request(self, path, payload=None):
        body = None if payload is None else json.dumps(payload).encode()
        req = Request(f"http://127.0.0.1:{self.server.server_port}{path}", data=body)
        try:
            response = urlopen(req, timeout=3)
        except HTTPError as error:
            response = error
        with response:
            return response.code, json.load(response)

    def set_mode(self, mode, *, active=True, listening=True, persona="assistant"):
        return self.request("/online-ai-mode", {
            "ai_active": active,
            "ai_mode": mode,
            "online_ai_enabled": active and mode == "online",
            "online_ai_persona_mode": persona,
            "listening_enabled": listening,
        })

    def test_switching_backend_replaces_recognizer_and_preserves_selected_persona(self):
        self.assertIsNone(self.app.stt)
        status, body = self.set_mode("online", persona="fortune_teller")
        self.assertEqual(status, 200)
        self.assertEqual(body["effective_mode"], "fortune_teller")
        first = self.app.stt
        self.assertEqual(first.backend_name, "openai_transcribe")

        self.assertEqual(self.set_mode("local")[0], 200)
        self.assertFalse(first.thread.is_alive())
        second = self.app.stt
        self.assertEqual(second.backend_name, "vosk")

        self.assertEqual(self.set_mode("online", persona="custom")[0], 200)
        self.assertFalse(second.thread.is_alive())
        third = self.app.stt
        self.assertEqual(third.backend_name, "openai_transcribe")
        self.assertEqual(self.app.config["online_ai_persona_mode"], "custom")
        self.assertEqual(self.set_mode("online", persona="emotion_reactions")[0], 200)
        self.assertIs(self.app.stt, third)

        self.assertEqual(self.set_mode("online", active=False)[0], 200)
        self.assertFalse(third.thread.is_alive())
        self.assertIsNone(self.app.stt)
        self.assertFalse(self.request("/health")[1]["ai_active"])

    def test_retries_switch_when_old_recognizer_has_not_stopped(self):
        self.assertEqual(self.set_mode("online")[0], 200)
        old_recognizer = self.app.stt
        with patch.object(old_recognizer, "stop", return_value=False):
            self.assertEqual(self.set_mode("local")[0], 503)
            self.assertIs(self.app.stt, old_recognizer)
            self.assertFalse(self.request("/health")[1]["ai_active"])

        self.assertEqual(self.set_mode("local")[0], 200)
        self.assertFalse(old_recognizer.thread.is_alive())
        self.assertEqual(self.app.stt.backend_name, "vosk")

    def test_typed_input_works_with_microphone_disabled_only_in_an_active_session(self):
        self.assertEqual(self.set_mode("local", listening=False)[0], 200)
        self.assertEqual(self.request("/inject_transcript", {"text": "hello"})[0], 400)
        self.assertEqual(self.request("/inject_transcript", {"text": "hello", "typed": True})[0], 200)
        self.assertTrue(self.request("/intent")[1]["has_intent"])

        self.assertEqual(self.request("/inject_transcript", {"text": "hello", "typed": True})[0], 200)
        self.assertEqual(self.set_mode("local", active=False)[0], 200)
        self.assertFalse(self.request("/intent")[1]["has_intent"])
        self.assertEqual(self.request("/inject_transcript", {"text": "hello", "typed": True})[0], 400)

    def test_previous_session_cannot_enqueue_a_late_intent(self):
        self.assertEqual(self.set_mode("online")[0], 200)
        started = threading.Event()
        release = threading.Event()

        def handle_transcript(_text, _confidence):
            started.set()
            release.wait(2)
            return {"intent": "move_joint"}, "Completed"

        with patch.object(self.app.online, "handle_transcript", side_effect=handle_transcript):
            results = []
            thread = threading.Thread(
                target=lambda: results.append(self.request("/inject_transcript", {"text": "test", "typed": True}))
            )
            thread.start()
            try:
                self.assertTrue(started.wait(1))
                self.assertEqual(self.set_mode("local")[0], 200)
            finally:
                release.set()
                thread.join(3)
        self.assertEqual(results[0][0], 400)
        self.assertFalse(self.request("/intent")[1]["has_intent"])

    def test_late_reachy_capture_updates_mic_and_listening_status(self):
        self.assertEqual(self.set_mode("online")[0], 200)
        state = self.app.state
        state.set_runtime(False, True, "openai_transcribe")
        self.assertFalse(self.request("/health")[1]["listening"])
        audio = sidecar.HybridAudioInput(
            SimpleNamespace(config=self.app.config, state=state), None, 16000, 1600)
        audio.remote_stream._available = True
        audio._update_state()
        self.assertTrue(self.request("/health")[1]["mic_active"])
        self.assertTrue(self.request("/health")[1]["listening"])
        state.set_tts_speaking(True)
        audio._update_state()
        self.assertFalse(self.request("/health")[1]["listening"])

    def test_ai_activation_applies_robot_audio_routing_before_starting(self):
        status, _ = self.request("/online-ai-mode", {
            "ai_active": True,
            "ai_mode": "online",
            "online_ai_enabled": True,
            "audio_routing": {
                "reachy_mic_robot_connected": True,
                "reachy_mic_ssh_host": "robot.test",
                "prefer_reachy_speaker": True,
            },
        })
        self.assertEqual(status, 200)
        self.assertEqual(self.app.stt.config["audio_source_mode"], "reachy")
        self.assertTrue(self.app.tts.prefers_reachy_speaker())

        with (
            patch.object(self.app.tts, "_play_wav_on_reachy", return_value=(True, "Reachy spoke")) as robot,
            patch.object(self.app.tts, "_play_wav_bytes_with_subprocess", return_value=(True, "PC spoke")) as pc,
        ):
            self.assertEqual(self.app.tts._play_speech_wav(b"sample", "test"), (True, "Reachy spoke"))
            robot.assert_called_once_with(b"sample")
            pc.assert_not_called()
            robot.return_value = False, "SSH unavailable"
            self.assertEqual(self.app.tts._play_speech_wav(b"sample", "test"), (True, "PC spoke"))
            pc.assert_called_once_with(b"sample", "test")

    def test_shared_robot_audio_ignores_ambiguous_and_echoed_interruptions(self):
        self.assertEqual(self.set_mode("online")[0], 200)
        self.app.config.update(prefer_reachy_speaker=True, reachy_mic_robot_connected=True,
                               reachy_mic_ssh_host="robot.test")
        self.app.state.reachy_mic_available = True
        self.app.state.set_tts_speaking(True)
        self.app.tts.interrupt.return_value = True, "Stopped"
        self.app.tts.active_utterance = (object(), "The stop phrase is Reachy, stop talking.")

        stt = self.app.stt
        self.assertFalse(stt._maybe_interrupt_tts_for_keyword("Eikun...", detector="openai_barge_clip"))
        self.assertFalse(stt._maybe_interrupt_tts_for_keyword("Reachy, stop talking.", detector="openai_barge_clip"))
        self.assertFalse(stt._maybe_interrupt_tts_for_keyword("Reachy, lopeta.", detector="vosk_keyword_partial"))
        self.app.tts.interrupt.assert_not_called()

        self.assertTrue(stt._maybe_interrupt_tts_for_keyword("Reachy, lopeta!", detector="openai_barge_clip"))
        self.app.tts.interrupt.assert_called_once()

    def test_separate_input_and_output_keep_existing_interrupt_phrases(self):
        self.assertEqual(self.set_mode("online")[0], 200)
        self.app.state.set_tts_speaking(True)
        self.app.tts.interrupt.return_value = True, "Stopped"
        self.assertTrue(self.app.stt._maybe_interrupt_tts_for_keyword("Eikun...", detector="openai_barge_clip"))
        self.app.tts.interrupt.assert_called_once()


class ReachyMicrophoneTests(unittest.TestCase):
    def test_respeaker_capture_uses_processed_channel(self):
        stream = sidecar.ReachyRemoteMicrophoneStream({}, None, 16000, 160)
        stream._capture_channels = 6
        samples = array("h", [32, 51, 73, 92, 114, 4000] * 160)
        pending = bytearray(samples.tobytes())
        stream._flush_capture_chunks(pending)
        self.assertEqual(stream.audio_q.get_nowait(), array("h", [32] * 160).tobytes())
        self.assertFalse(pending)

    def test_quiet_reachy_voice_crosses_the_adaptive_threshold(self):
        idle = deque([23, 24, 23, 22, 28, 70])
        threshold = sidecar.reachy_speech_threshold(520, idle)
        self.assertGreater(threshold, 28)
        self.assertLess(threshold, 110)
        pcm = array("h", [-150, 0, 200]).tobytes()
        normalized = sidecar.normalize_reachy_speech_level(pcm)
        self.assertEqual(array("h", normalized).tolist(), [-1200, 0, 1600])


if __name__ == "__main__":
    unittest.main()
