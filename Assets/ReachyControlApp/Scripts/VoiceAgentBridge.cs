using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Reachy.ControlApp
{
    [Serializable]
    public sealed class VoiceAgentJointTarget
    {
        public string joint_name = string.Empty;
        public float joint_degrees;
    }

    [Serializable]
    public sealed class VoiceAgentMotionStep
    {
        public string label = string.Empty;
        public float hold_seconds = 0.5f;
        public float speed_scale = 0.6f;
        public VoiceAgentJointTarget[] joint_targets = Array.Empty<VoiceAgentJointTarget>();
    }

    [Serializable]
    public sealed class VoiceAgentIntent
    {
        public string type = string.Empty;
        public string intent = string.Empty;
        public string pose_name = string.Empty;
        public string joint_name = string.Empty;
        public float joint_degrees;
        public float speed_scale = 0.6f;
        public VoiceAgentJointTarget[] joint_targets = Array.Empty<VoiceAgentJointTarget>();
        public VoiceAgentMotionStep[] motion_steps = Array.Empty<VoiceAgentMotionStep>();
        public float confidence = 1.0f;
        public bool requires_confirmation;
        public string reply_text = string.Empty;
        public string spoken_text = string.Empty;
        public string source_backend = string.Empty;
        public string source_mode = string.Empty;
        public string validation_status = string.Empty;
        public string validation_message = string.Empty;
        public bool reply_already_spoken;
        public bool transcript_is_final = true;

        [NonSerialized] public string raw_json = string.Empty;
    }

    public sealed class VoiceAgentBridge : IDisposable
    {
        public const string DefaultEndpoint = "http://127.0.0.1:8099/intent";
        public const string DefaultTtsEndpoint = "http://127.0.0.1:8099/speak";
        public const string DefaultHelpEndpoint = "http://127.0.0.1:8099/help";
        public const string DefaultListeningEndpoint = "http://127.0.0.1:8099/listening";

        public struct BridgeSnapshot
        {
            public bool Enabled;
            public bool PollInFlight;
            public bool BridgeReachable;
            public bool DegradedMode;
            public bool HeartbeatExpired;
            public string Endpoint;
            public int QueuedIntents;
            public int ReceivedIntentCount;
            public int SuccessfulPollCount;
            public int FailedPollCount;
            public int ConsecutivePollFailures;
            public double SecondsSinceLastHealthyPoll;
            public int EventLogCount;
            public bool MicActive;
            public bool Listening;
            public string SttBackend;
            public string LastTranscript;
            public bool LastTranscriptIsFinal;
            public float LastTranscriptConfidence;
            public bool TtsInFlight;
            public int QueuedTtsCount;
            public string TtsEndpoint;
            public int SuccessfulTtsCount;
            public int FailedTtsCount;
            public string LastTtsMessage;
            public string LastTtsError;
            public bool HelpInFlight;
            public string HelpEndpoint;
            public int SuccessfulHelpCount;
            public int FailedHelpCount;
            public string LastHelpMessage;
            public string LastHelpError;
            public string LastHelpAnswer;
            public bool ListeningToggleInFlight;
            public string ListeningEndpoint;
            public int SuccessfulListeningToggleCount;
            public int FailedListeningToggleCount;
            public string LastListeningToggleMessage;
            public string LastListeningToggleError;
            public string LastLogLine;
            public string LastMessage;
            public string LastError;
            public DateTime LastPollUtc;
        }

        private struct PollResult
        {
            public bool Success;
            public bool HasIntent;
            public VoiceAgentIntent Intent;
            public bool HasBridgeStatus;
            public bool MicActive;
            public bool Listening;
            public string SttBackend;
            public string Transcript;
            public bool TranscriptIsFinal;
            public float TranscriptConfidence;
            public string Message;
            public string Error;
        }

        private struct TtsRequest
        {
            public string Text;
            public bool Interrupt;
            public string TtsMode;
        }

        private struct TtsResult
        {
            public bool Success;
            public bool MirrorAttempted;
            public bool MirrorSuccess;
            public string RequestedText;
            public string Message;
            public string Error;
        }

        private struct HelpRequest
        {
            public string Query;
            public string Context;
        }

        private struct HelpResult
        {
            public bool Success;
            public string Answer;
            public string Message;
            public string Error;
        }

        private struct ListeningToggleRequest
        {
            public bool Enabled;
        }

        private struct ListeningToggleResult
        {
            public bool Success;
            public bool Enabled;
            public string Message;
            public string Error;
        }

        private struct BridgeLogEntry
        {
            public DateTime TimestampUtc;
            public string EventType;
            public string Severity;
            public string Detail;
        }

        [Serializable]
        private sealed class IntentEnvelope
        {
            public bool has_intent;
            public VoiceAgentIntent intent;
            public string message;
            public string transcript;
            public float confidence;
            public bool transcript_is_final = true;
            public bool mic_active;
            public bool listening;
            public string stt_backend;
        }

        [Serializable]
        private sealed class TtsPayload
        {
            public string text;
            public bool interrupt;
            public bool wait_for_completion;
            public string tts_mode;
        }

        [Serializable]
        private sealed class TtsStopPayload
        {
            public string reason;
        }

        [Serializable]
        private sealed class HelpPayload
        {
            public string query;
            public string context;
        }

        [Serializable]
        private sealed class HelpResponsePayload
        {
            public bool ok;
            public string answer;
            public string message;
            public string text;
        }

        [Serializable]
        private sealed class ListeningTogglePayload
        {
            public bool enabled;
        }

        [Serializable]
        private sealed class ListeningToggleResponsePayload
        {
            public bool ok;
            public bool enabled;
            public string message;
        }

        private readonly object _gate = new object();
        private readonly Queue<VoiceAgentIntent> _intentQueue = new Queue<VoiceAgentIntent>();
        private readonly Queue<BridgeLogEntry> _eventLog = new Queue<BridgeLogEntry>();
        private bool _enabled;
        private bool _bridgeReachable;
        private bool _degradedMode;
        private string _endpoint = DefaultEndpoint;
        private string _ttsEndpoint = DefaultTtsEndpoint;
        private string _ttsMirrorEndpoint = string.Empty;
        private bool _ttsMirrorEnabled;
        private string _helpEndpoint = DefaultHelpEndpoint;
        private string _listeningEndpoint = DefaultListeningEndpoint;
        private float _pollIntervalSeconds = 0.5f;
        private float _heartbeatTimeoutSeconds = 4f;
        private float _retryBackoffMinSeconds = 0.4f;
        private float _retryBackoffMaxSeconds = 4f;
        private int _degradedFailureThreshold = 5;
        private int _timeoutMs = 700;
        private float _nextPollAt;
        private float _lastUpdateUnscaledTime;
        private Task<PollResult> _pollTask;
        private readonly Queue<TtsRequest> _ttsQueue = new Queue<TtsRequest>();
        private Task<TtsResult> _ttsTask;
        private Task<TtsResult> _ttsInterruptTask;
        private bool _ttsInterruptAfterActiveRequest;
        private string _ttsInterruptAfterActiveRequestReason = string.Empty;
        private readonly Queue<HelpRequest> _helpQueue = new Queue<HelpRequest>();
        private Task<HelpResult> _helpTask;
        private readonly Queue<ListeningToggleRequest> _listeningToggleQueue = new Queue<ListeningToggleRequest>();
        private Task<ListeningToggleResult> _listeningToggleTask;
        private int _receivedIntentCount;
        private int _successfulPollCount;
        private int _failedPollCount;
        private int _consecutivePollFailures;
        private int _successfulTtsCount;
        private int _failedTtsCount;
        private int _successfulHelpCount;
        private int _failedHelpCount;
        private int _successfulListeningToggleCount;
        private int _failedListeningToggleCount;
        private bool _micActive;
        private bool _listening;
        private string _sttBackend = "unknown";
        private string _lastTranscript = string.Empty;
        private bool _lastTranscriptIsFinal = true;
        private float _lastTranscriptConfidence;
        private string _lastTtsMessage = "TTS idle.";
        private string _lastTtsError = string.Empty;
        private string _lastHelpMessage = "Help model idle.";
        private string _lastHelpError = string.Empty;
        private string _lastHelpAnswer = string.Empty;
        private string _lastListeningToggleMessage = "Listening control idle.";
        private string _lastListeningToggleError = string.Empty;
        private string _lastLogLine = string.Empty;
        private string _lastMessage = "Voice bridge idle.";
        private string _lastError = string.Empty;
        private DateTime _lastPollUtc;
        private DateTime _lastHealthyPollUtc = DateTime.MinValue;
        private bool _disposed;

        public void Dispose()
        {
            _disposed = true;
        }

        public void Configure(string endpoint, float pollIntervalSeconds, int timeoutMs)
        {
            lock (_gate)
            {
                _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();
                _pollIntervalSeconds = Math.Max(0.05f, pollIntervalSeconds);
                _timeoutMs = Math.Max(100, timeoutMs);
            }
        }

        public void ConfigureRobustness(
            float heartbeatTimeoutSeconds,
            float retryBackoffMinSeconds,
            float retryBackoffMaxSeconds,
            int degradedFailureThreshold)
        {
            lock (_gate)
            {
                _heartbeatTimeoutSeconds = Math.Max(0.5f, heartbeatTimeoutSeconds);
                _retryBackoffMinSeconds = Math.Max(0.05f, retryBackoffMinSeconds);
                _retryBackoffMaxSeconds = Math.Max(_retryBackoffMinSeconds, retryBackoffMaxSeconds);
                _degradedFailureThreshold = Math.Max(1, degradedFailureThreshold);
            }
        }

        public void RequestImmediatePoll(float nowUnscaledTime)
        {
            lock (_gate)
            {
                _nextPollAt = Math.Min(_nextPollAt, nowUnscaledTime);
                AddLogLocked("poll", "info", "Immediate poll requested.");
            }
        }

        public void ResetHealthState(float nowUnscaledTime)
        {
            lock (_gate)
            {
                _consecutivePollFailures = 0;
                _degradedMode = false;
                _bridgeReachable = false;
                _lastError = string.Empty;
                _lastMessage = "Voice bridge state reset; waiting for next poll.";
                _lastHealthyPollUtc = DateTime.UtcNow;
                _nextPollAt = nowUnscaledTime;
                AddLogLocked("bridge", "info", "Bridge health state reset by operator.");
            }
        }

        public string[] GetRecentLogLines(int maxLines)
        {
            lock (_gate)
            {
                if (maxLines <= 0 || _eventLog.Count == 0)
                {
                    return Array.Empty<string>();
                }

                int count = Math.Min(maxLines, _eventLog.Count);
                int skip = _eventLog.Count - count;
                var lines = new string[count];
                int index = 0;
                int seen = 0;
                foreach (BridgeLogEntry entry in _eventLog)
                {
                    if (seen++ < skip)
                    {
                        continue;
                    }

                    lines[index++] = FormatLogEntry(entry);
                }

                return lines;
            }
        }

        public void ClearLogs()
        {
            lock (_gate)
            {
                _eventLog.Clear();
                _lastLogLine = string.Empty;
                AddLogLocked("bridge", "info", "Bridge log buffer cleared.");
            }
        }

        public void ConfigureHelpEndpoint(string helpEndpoint)
        {
            lock (_gate)
            {
                _helpEndpoint = string.IsNullOrWhiteSpace(helpEndpoint) ? DefaultHelpEndpoint : helpEndpoint.Trim();
            }
        }

        public void ConfigureListeningEndpoint(string listeningEndpoint)
        {
            lock (_gate)
            {
                _listeningEndpoint = string.IsNullOrWhiteSpace(listeningEndpoint)
                    ? DefaultListeningEndpoint
                    : listeningEndpoint.Trim();
            }
        }

        public void EnqueueHelpRequest(string query, string context)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            lock (_gate)
            {
                _helpQueue.Enqueue(new HelpRequest
                {
                    Query = query.Trim(),
                    Context = context ?? string.Empty
                });

                AddLogLocked("help", "debug", "Queued local help request.");
            }
        }

        public void EnqueueListeningToggle(bool enabled)
        {
            lock (_gate)
            {
                _listeningToggleQueue.Enqueue(new ListeningToggleRequest
                {
                    Enabled = enabled
                });

                AddLogLocked("listening", "debug", enabled
                    ? "Queued remote listening enable request."
                    : "Queued remote listening disable request.");
            }
        }

        public void ConfigureTtsEndpoint(string ttsEndpoint)
        {
            lock (_gate)
            {
                _ttsEndpoint = string.IsNullOrWhiteSpace(ttsEndpoint) ? DefaultTtsEndpoint : ttsEndpoint.Trim();
            }
        }

        public void ConfigureTtsMirror(string ttsMirrorEndpoint, bool enabled)
        {
            lock (_gate)
            {
                _ttsMirrorEnabled = enabled && !string.IsNullOrWhiteSpace(ttsMirrorEndpoint);
                _ttsMirrorEndpoint = _ttsMirrorEnabled ? ttsMirrorEndpoint.Trim() : string.Empty;
            }
        }

        public void EnqueueTtsFeedback(string text, bool interrupt, string ttsMode = "")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            lock (_gate)
            {
                if (interrupt)
                {
                    _ttsQueue.Clear();
                }

                _ttsQueue.Enqueue(new TtsRequest
                {
                    Text = text.Trim(),
                    Interrupt = interrupt,
                    TtsMode = string.IsNullOrWhiteSpace(ttsMode) ? string.Empty : ttsMode.Trim()
                });

                AddLogLocked(
                    "tts",
                    "debug",
                    interrupt
                        ? "Queued interrupting TTS feedback."
                        : "Queued TTS feedback.");
            }
        }

        public void InterruptTtsFeedback(string reason)
        {
            string ttsStopEndpoint;
            string ttsMirrorStopEndpoint;
            int timeoutMs;
            string sanitizedReason = SanitizeBridgeText(reason);

            lock (_gate)
            {
                _ttsQueue.Clear();
                ttsStopEndpoint = BuildTtsStopEndpoint(_ttsEndpoint);
                ttsMirrorStopEndpoint = _ttsMirrorEnabled
                    ? BuildTtsStopEndpoint(_ttsMirrorEndpoint)
                    : string.Empty;
                timeoutMs = _timeoutMs;

                _lastTtsMessage = string.IsNullOrWhiteSpace(sanitizedReason)
                    ? "Interrupting active TTS playback."
                    : $"Interrupting active TTS playback. {sanitizedReason}";
                _lastTtsError = string.Empty;
                AddLogLocked("tts", "debug", _lastTtsMessage);
                if (_ttsTask != null)
                {
                    _ttsInterruptAfterActiveRequest = true;
                    _ttsInterruptAfterActiveRequestReason = sanitizedReason;
                }

                if (_ttsInterruptTask == null || _ttsInterruptTask.IsCompleted)
                {
                    _ttsInterruptTask = Task.Run(() => SendTtsInterruptRequest(
                        ttsStopEndpoint,
                        ttsMirrorStopEndpoint,
                        timeoutMs,
                        sanitizedReason));
                }
            }
        }

        public void SetEnabled(bool enabled, float nowUnscaledTime)
        {
            lock (_gate)
            {
                if (_enabled == enabled)
                {
                    return;
                }

                _enabled = enabled;
                _nextPollAt = nowUnscaledTime;
                if (!enabled)
                {
                    _lastMessage = "Voice bridge disabled.";
                    _bridgeReachable = false;
                    _micActive = false;
                    _listening = false;
                    _degradedMode = false;
                    _consecutivePollFailures = 0;
                    _ttsQueue.Clear();
                    _ttsInterruptAfterActiveRequest = false;
                    _ttsInterruptAfterActiveRequestReason = string.Empty;
                    _helpQueue.Clear();
                    _listeningToggleQueue.Clear();
                    _lastTtsMessage = "TTS idle.";
                    _lastTtsError = string.Empty;
                    _lastHelpMessage = "Help model idle.";
                    _lastHelpError = string.Empty;
                    _lastListeningToggleMessage = "Listening control idle.";
                    _lastListeningToggleError = string.Empty;
                    AddLogLocked("bridge", "info", "Voice bridge disabled.");
                }
                else
                {
                    _lastMessage = "Voice bridge enabled.";
                    _lastHealthyPollUtc = DateTime.UtcNow;
                    AddLogLocked("bridge", "info", "Voice bridge enabled.");
                }
            }
        }

        public void EnqueueMockIntent(VoiceAgentIntent intent)
        {
            if (intent == null)
            {
                return;
            }

            lock (_gate)
            {
                var copy = new VoiceAgentIntent
                {
                    type = intent.type ?? string.Empty,
                    intent = intent.intent ?? string.Empty,
                    pose_name = intent.pose_name ?? string.Empty,
                    joint_name = intent.joint_name ?? string.Empty,
                    joint_degrees = intent.joint_degrees,
                    speed_scale = intent.speed_scale,
                    joint_targets = intent.joint_targets ?? Array.Empty<VoiceAgentJointTarget>(),
                    motion_steps = intent.motion_steps ?? Array.Empty<VoiceAgentMotionStep>(),
                    confidence = intent.confidence,
                    requires_confirmation = intent.requires_confirmation,
                    reply_text = intent.reply_text ?? string.Empty,
                    spoken_text = intent.spoken_text ?? string.Empty,
                    source_backend = intent.source_backend ?? string.Empty,
                    source_mode = intent.source_mode ?? string.Empty,
                    validation_status = intent.validation_status ?? string.Empty,
                    validation_message = intent.validation_message ?? string.Empty,
                    reply_already_spoken = intent.reply_already_spoken,
                    transcript_is_final = intent.transcript_is_final,
                    raw_json = string.IsNullOrWhiteSpace(intent.raw_json) ? "mock_intent" : intent.raw_json
                };

                _intentQueue.Enqueue(copy);
                _receivedIntentCount++;
                string queuedKind = string.IsNullOrWhiteSpace(copy.intent) ? copy.type : copy.intent;
                _lastMessage = $"Mock intent queued ({queuedKind}).";
                AddLogLocked("intent", "debug", _lastMessage);
            }
        }

        public bool TryDequeueIntent(out VoiceAgentIntent intent)
        {
            lock (_gate)
            {
                if (_intentQueue.Count == 0)
                {
                    intent = null;
                    return false;
                }

                intent = _intentQueue.Dequeue();
                return true;
            }
        }

        public void Update(float nowUnscaledTime)
        {
            if (_disposed)
            {
                return;
            }

            _lastUpdateUnscaledTime = nowUnscaledTime;

            Task<PollResult> localPollTask = null;
            Task<TtsResult> localTtsTask = null;
            Task<TtsResult> localTtsInterruptTask = null;
            Task<HelpResult> localHelpTask = null;
            Task<ListeningToggleResult> localListeningToggleTask = null;
            string endpoint = string.Empty;
            string ttsEndpoint = string.Empty;
            string ttsMirrorEndpoint = string.Empty;
            string helpEndpoint = string.Empty;
            string listeningEndpoint = string.Empty;
            int timeout = 0;
            float pollInterval = 0.5f;
            bool shouldStartPoll = false;
            bool shouldStartTts = false;
            bool shouldMirrorTts = false;
            bool shouldStartHelp = false;
            bool shouldStartListeningToggle = false;
            TtsRequest nextTtsRequest = default(TtsRequest);
            HelpRequest nextHelpRequest = default(HelpRequest);
            ListeningToggleRequest nextListeningToggleRequest = default(ListeningToggleRequest);

            lock (_gate)
            {
                if (_pollTask != null && _pollTask.IsCompleted)
                {
                    localPollTask = _pollTask;
                    _pollTask = null;
                }

                if (_ttsTask != null && _ttsTask.IsCompleted)
                {
                    localTtsTask = _ttsTask;
                    _ttsTask = null;
                }

                if (_ttsInterruptTask != null && _ttsInterruptTask.IsCompleted)
                {
                    localTtsInterruptTask = _ttsInterruptTask;
                    _ttsInterruptTask = null;
                }

                if (_helpTask != null && _helpTask.IsCompleted)
                {
                    localHelpTask = _helpTask;
                    _helpTask = null;
                }

                if (_listeningToggleTask != null && _listeningToggleTask.IsCompleted)
                {
                    localListeningToggleTask = _listeningToggleTask;
                    _listeningToggleTask = null;
                }

                if (_enabled && _pollTask == null && nowUnscaledTime >= _nextPollAt)
                {
                    endpoint = _endpoint;
                    timeout = _timeoutMs;
                    pollInterval = _pollIntervalSeconds;
                    shouldStartPoll = true;
                }

                if (_ttsTask == null && _ttsQueue.Count > 0)
                {
                    ttsEndpoint = _ttsEndpoint;
                    ttsMirrorEndpoint = _ttsMirrorEndpoint;
                    shouldMirrorTts = _ttsMirrorEnabled;
                    timeout = _timeoutMs;
                    nextTtsRequest = _ttsQueue.Dequeue();
                    shouldStartTts = true;
                }

                if (_enabled && _helpTask == null && _helpQueue.Count > 0)
                {
                    helpEndpoint = _helpEndpoint;
                    timeout = _timeoutMs;
                    nextHelpRequest = _helpQueue.Dequeue();
                    shouldStartHelp = true;
                }

                if (_enabled && _listeningToggleTask == null && _listeningToggleQueue.Count > 0)
                {
                    listeningEndpoint = _listeningEndpoint;
                    timeout = _timeoutMs;
                    nextListeningToggleRequest = _listeningToggleQueue.Dequeue();
                    shouldStartListeningToggle = true;
                }
            }

            if (localPollTask != null)
            {
                ConsumePollResult(localPollTask);
            }

            if (localTtsTask != null)
            {
                ConsumeTtsResult(localTtsTask);
            }

            if (localTtsInterruptTask != null)
            {
                ConsumeTtsInterruptResult(localTtsInterruptTask);
            }

            if (localHelpTask != null)
            {
                ConsumeHelpResult(localHelpTask);
            }

            if (localListeningToggleTask != null)
            {
                ConsumeListeningToggleResult(localListeningToggleTask);
            }

            StartDeferredTtsInterruptIfNeeded();

            if (shouldStartPoll)
            {
                lock (_gate)
                {
                    _nextPollAt = nowUnscaledTime + pollInterval;
                    _pollTask = Task.Run(() => PollIntent(endpoint, timeout));
                }
            }

            if (shouldStartTts)
            {
                lock (_gate)
                {
                    _ttsTask = Task.Run(() => SendTtsRequest(
                        ttsEndpoint,
                        shouldMirrorTts ? ttsMirrorEndpoint : string.Empty,
                        timeout,
                        nextTtsRequest));
                }
            }

            if (shouldStartHelp)
            {
                lock (_gate)
                {
                    _helpTask = Task.Run(() => SendHelpRequest(helpEndpoint, timeout, nextHelpRequest));
                }
            }

            if (shouldStartListeningToggle)
            {
                lock (_gate)
                {
                    _listeningToggleTask = Task.Run(() =>
                        SendListeningToggleRequest(listeningEndpoint, timeout, nextListeningToggleRequest));
                }
            }
        }

        public BridgeSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                double secondsSinceHealthy = _lastHealthyPollUtc == DateTime.MinValue
                    ? 0.0
                    : Math.Max(0.0, (DateTime.UtcNow - _lastHealthyPollUtc).TotalSeconds);
                bool heartbeatExpired = _enabled &&
                    _lastHealthyPollUtc != DateTime.MinValue &&
                    secondsSinceHealthy > _heartbeatTimeoutSeconds;

                return new BridgeSnapshot
                {
                    Enabled = _enabled,
                    PollInFlight = _pollTask != null,
                    BridgeReachable = _bridgeReachable,
                    DegradedMode = _degradedMode,
                    HeartbeatExpired = heartbeatExpired,
                    Endpoint = _endpoint,
                    QueuedIntents = _intentQueue.Count,
                    ReceivedIntentCount = _receivedIntentCount,
                    SuccessfulPollCount = _successfulPollCount,
                    FailedPollCount = _failedPollCount,
                    ConsecutivePollFailures = _consecutivePollFailures,
                    SecondsSinceLastHealthyPoll = secondsSinceHealthy,
                    EventLogCount = _eventLog.Count,
                    MicActive = _micActive,
                    Listening = _listening,
                    SttBackend = _sttBackend,
                    LastTranscript = _lastTranscript,
                    LastTranscriptIsFinal = _lastTranscriptIsFinal,
                    LastTranscriptConfidence = _lastTranscriptConfidence,
                    TtsInFlight = _ttsTask != null || _ttsInterruptTask != null,
                    QueuedTtsCount = _ttsQueue.Count,
                    TtsEndpoint = _ttsEndpoint,
                    SuccessfulTtsCount = _successfulTtsCount,
                    FailedTtsCount = _failedTtsCount,
                    LastTtsMessage = _lastTtsMessage,
                    LastTtsError = _lastTtsError,
                    HelpInFlight = _helpTask != null,
                    HelpEndpoint = _helpEndpoint,
                    SuccessfulHelpCount = _successfulHelpCount,
                    FailedHelpCount = _failedHelpCount,
                    LastHelpMessage = _lastHelpMessage,
                    LastHelpError = _lastHelpError,
                    LastHelpAnswer = _lastHelpAnswer,
                    ListeningToggleInFlight = _listeningToggleTask != null,
                    ListeningEndpoint = _listeningEndpoint,
                    SuccessfulListeningToggleCount = _successfulListeningToggleCount,
                    FailedListeningToggleCount = _failedListeningToggleCount,
                    LastListeningToggleMessage = _lastListeningToggleMessage,
                    LastListeningToggleError = _lastListeningToggleError,
                    LastLogLine = _lastLogLine,
                    LastMessage = _lastMessage,
                    LastError = _lastError,
                    LastPollUtc = _lastPollUtc
                };
            }
        }

        private void ConsumePollResult(Task<PollResult> completedTask)
        {
            PollResult result;
            try
            {
                result = completedTask.Result;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _bridgeReachable = false;
                    _failedPollCount++;
                    _consecutivePollFailures++;
                    float backoffSeconds = ComputeBackoffSeconds(_consecutivePollFailures);
                    _nextPollAt = _lastUpdateUnscaledTime + backoffSeconds;
                    _lastPollUtc = DateTime.UtcNow;
                    _lastError = ex.Message;
                    _lastMessage = $"Voice bridge poll task crashed: {ex.Message}";
                    if (_consecutivePollFailures >= _degradedFailureThreshold)
                    {
                        _degradedMode = true;
                    }
                    AddLogLocked("poll", "error", _lastMessage);
                }
                return;
            }

            lock (_gate)
            {
                _lastPollUtc = DateTime.UtcNow;
                if (result.Success)
                {
                    _bridgeReachable = true;
                    _successfulPollCount++;
                    _lastError = string.Empty;
                    _consecutivePollFailures = 0;
                    if (_degradedMode)
                    {
                        _degradedMode = false;
                        AddLogLocked("poll", "info", "Recovered from degraded mode.");
                    }

                    _lastHealthyPollUtc = DateTime.UtcNow;
                    _nextPollAt = _lastUpdateUnscaledTime + _pollIntervalSeconds;
                    if (result.HasBridgeStatus)
                    {
                        _micActive = result.MicActive;
                        _listening = result.Listening;
                        if (!string.IsNullOrWhiteSpace(result.SttBackend))
                        {
                            _sttBackend = result.SttBackend;
                        }

                        if (!string.IsNullOrWhiteSpace(result.Transcript))
                        {
                            _lastTranscript = result.Transcript;
                            _lastTranscriptIsFinal = result.TranscriptIsFinal;
                            _lastTranscriptConfidence = Mathf.Clamp01(result.TranscriptConfidence);
                        }
                    }

                    _lastMessage = string.IsNullOrWhiteSpace(result.Message) ? "Voice bridge poll OK." : result.Message;
                    if (result.HasIntent && result.Intent != null)
                    {
                        _intentQueue.Enqueue(result.Intent);
                        _receivedIntentCount++;
                    }

                    AddLogLocked("poll", "debug", _lastMessage);
                }
                else
                {
                    _bridgeReachable = false;
                    _failedPollCount++;
                    _consecutivePollFailures++;
                    _lastError = result.Error;
                    _lastMessage = string.IsNullOrWhiteSpace(result.Message) ? "Voice bridge poll failed." : result.Message;
                    float backoffSeconds = ComputeBackoffSeconds(_consecutivePollFailures);
                    _nextPollAt = _lastUpdateUnscaledTime + backoffSeconds;
                    if (_consecutivePollFailures >= _degradedFailureThreshold)
                    {
                        if (!_degradedMode)
                        {
                            _degradedMode = true;
                            AddLogLocked(
                                "poll",
                                "warn",
                                $"Entering degraded mode after {_consecutivePollFailures} consecutive failures.");
                        }
                    }

                    AddLogLocked("poll", "warn", $"{_lastMessage} Next retry in {backoffSeconds:F2}s.");
                }
            }
        }

        private void ConsumeTtsResult(Task<TtsResult> completedTask)
        {
            TtsResult result;
            try
            {
                result = completedTask.Result;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _failedTtsCount++;
                    _lastTtsError = SanitizeBridgeText(ex.Message);
                    _lastTtsMessage = string.IsNullOrWhiteSpace(_lastTtsError)
                        ? "TTS task crashed."
                        : $"TTS task crashed: {_lastTtsError}";
                    AddLogLocked("tts", "error", _lastTtsMessage);
                }
                return;
            }

            lock (_gate)
            {
                string sanitizedMessage = SanitizeBridgeText(result.Message);
                string sanitizedError = SanitizeBridgeText(result.Error);
                if (result.Success)
                {
                    _successfulTtsCount++;
                    _lastTtsMessage = string.IsNullOrWhiteSpace(sanitizedMessage)
                        ? "TTS request sent."
                        : sanitizedMessage;
                    if (result.MirrorAttempted && !result.MirrorSuccess)
                    {
                        _lastTtsError = sanitizedError;
                        AddLogLocked("tts", "warn", _lastTtsMessage);
                    }
                    else
                    {
                        _lastTtsError = string.Empty;
                        AddLogLocked("tts", "debug", _lastTtsMessage);
                    }
                }
                else
                {
                    _failedTtsCount++;
                    _lastTtsError = sanitizedError;
                    _lastTtsMessage = string.IsNullOrWhiteSpace(sanitizedMessage)
                        ? "TTS request failed."
                        : sanitizedMessage;
                    AddLogLocked("tts", "warn", _lastTtsMessage);
                }
            }
        }

        private void ConsumeTtsInterruptResult(Task<TtsResult> completedTask)
        {
            TtsResult result;
            try
            {
                result = completedTask.Result;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _lastTtsError = SanitizeBridgeText(ex.Message);
                    _lastTtsMessage = string.IsNullOrWhiteSpace(_lastTtsError)
                        ? "TTS interrupt task crashed."
                        : $"TTS interrupt task crashed: {_lastTtsError}";
                    AddLogLocked("tts", "warn", _lastTtsMessage);
                }
                return;
            }

            lock (_gate)
            {
                string sanitizedMessage = SanitizeBridgeText(result.Message);
                string sanitizedError = SanitizeBridgeText(result.Error);
                if (result.Success)
                {
                    _lastTtsMessage = string.IsNullOrWhiteSpace(sanitizedMessage)
                        ? "TTS interrupt sent."
                        : sanitizedMessage;
                    if (result.MirrorAttempted && !result.MirrorSuccess)
                    {
                        _lastTtsError = sanitizedError;
                        AddLogLocked("tts", "warn", _lastTtsMessage);
                    }
                    else
                    {
                        _lastTtsError = string.Empty;
                        AddLogLocked("tts", "debug", _lastTtsMessage);
                    }
                }
                else
                {
                    _lastTtsError = sanitizedError;
                    _lastTtsMessage = string.IsNullOrWhiteSpace(sanitizedMessage)
                        ? "TTS interrupt failed."
                        : sanitizedMessage;
                    AddLogLocked("tts", "warn", _lastTtsMessage);
                }
            }
        }

        private void StartDeferredTtsInterruptIfNeeded()
        {
            lock (_gate)
            {
                if (!_ttsInterruptAfterActiveRequest ||
                    _ttsTask != null ||
                    _ttsInterruptTask != null)
                {
                    return;
                }

                _ttsInterruptAfterActiveRequest = false;
                string reason = _ttsInterruptAfterActiveRequestReason;
                _ttsInterruptAfterActiveRequestReason = string.Empty;
                _ttsInterruptTask = Task.Run(() => SendTtsInterruptRequest(
                    BuildTtsStopEndpoint(_ttsEndpoint),
                    _ttsMirrorEnabled ? BuildTtsStopEndpoint(_ttsMirrorEndpoint) : string.Empty,
                    _timeoutMs,
                    reason));
            }
        }

        private void ConsumeHelpResult(Task<HelpResult> completedTask)
        {
            HelpResult result;
            try
            {
                result = completedTask.Result;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _failedHelpCount++;
                    _lastHelpError = ex.Message;
                    _lastHelpMessage = $"Help request task crashed: {ex.Message}";
                    AddLogLocked("help", "error", _lastHelpMessage);
                }
                return;
            }

            lock (_gate)
            {
                if (result.Success)
                {
                    _successfulHelpCount++;
                    _lastHelpError = string.Empty;
                    _lastHelpMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "Help response received."
                        : result.Message;
                    _lastHelpAnswer = result.Answer ?? string.Empty;
                    AddLogLocked("help", "debug", _lastHelpMessage);
                }
                else
                {
                    _failedHelpCount++;
                    _lastHelpError = result.Error;
                    _lastHelpMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "Help request failed."
                        : result.Message;
                    AddLogLocked("help", "warn", _lastHelpMessage);
                }
            }
        }

        private void ConsumeListeningToggleResult(Task<ListeningToggleResult> completedTask)
        {
            ListeningToggleResult result;
            try
            {
                result = completedTask.Result;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _failedListeningToggleCount++;
                    _lastListeningToggleError = ex.Message;
                    _lastListeningToggleMessage = $"Listening control task crashed: {ex.Message}";
                    AddLogLocked("listening", "error", _lastListeningToggleMessage);
                }
                return;
            }

            lock (_gate)
            {
                if (result.Success)
                {
                    _successfulListeningToggleCount++;
                    _lastListeningToggleError = string.Empty;
                    _listening = result.Enabled;
                    _lastListeningToggleMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? $"Remote listening {(result.Enabled ? "enabled" : "disabled")}."
                        : result.Message;
                    AddLogLocked("listening", "debug", _lastListeningToggleMessage);
                }
                else
                {
                    _failedListeningToggleCount++;
                    _lastListeningToggleError = result.Error;
                    _lastListeningToggleMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "Remote listening toggle failed."
                        : result.Message;
                    AddLogLocked("listening", "warn", _lastListeningToggleMessage);
                }
            }
        }

        private static PollResult PollIntent(string endpoint, int timeoutMs)
        {
            try
            {
                HttpWebRequest request = WebRequest.CreateHttp(endpoint);
                request.Method = "GET";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.Accept = "application/json";
                request.Proxy = null;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    return ParseIntentResponse(body);
                }
            }
            catch (WebException webEx)
            {
                string detail = webEx.Message;
                if (webEx.Response != null)
                {
                    using (Stream responseStream = webEx.Response.GetResponseStream())
                    using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                    {
                        string responseText = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(responseText))
                        {
                            detail = $"{detail} ({responseText})";
                        }
                    }
                }

                return new PollResult
                {
                    Success = false,
                    HasIntent = false,
                    Intent = null,
                    Message = "Voice bridge endpoint is not reachable.",
                    Error = detail
                };
            }
            catch (Exception ex)
            {
                return new PollResult
                {
                    Success = false,
                    HasIntent = false,
                    Intent = null,
                    Message = "Voice bridge poll failed.",
                    Error = ex.Message
                };
            }
        }

        private static TtsResult SendTtsRequest(
            string endpoint,
            string mirrorEndpoint,
            int timeoutMs,
            TtsRequest requestPayload)
        {
            TtsResult primaryResult = SendSingleTtsRequest(endpoint, timeoutMs, requestPayload, waitForCompletion: true);
            if (!primaryResult.Success)
            {
                primaryResult.MirrorAttempted = false;
                primaryResult.MirrorSuccess = false;
                return primaryResult;
            }

            if (string.IsNullOrWhiteSpace(mirrorEndpoint))
            {
                primaryResult.MirrorAttempted = false;
                primaryResult.MirrorSuccess = true;
                return primaryResult;
            }

            TtsResult mirrorResult = SendSingleTtsRequest(mirrorEndpoint, timeoutMs, requestPayload, waitForCompletion: false);
            primaryResult.MirrorAttempted = true;
            primaryResult.MirrorSuccess = mirrorResult.Success;
            if (mirrorResult.Success)
            {
                primaryResult.Message = "Local TTS completed; robot speaker mirror accepted.";
                primaryResult.Error = string.Empty;
            }
            else
            {
                primaryResult.Message = string.IsNullOrWhiteSpace(mirrorResult.Error)
                    ? "Local TTS completed; robot speaker mirror failed."
                    : $"Local TTS completed; robot speaker mirror failed: {mirrorResult.Error}";
                primaryResult.Error = mirrorResult.Error;
            }

            return primaryResult;
        }

        private static TtsResult SendTtsInterruptRequest(
            string endpoint,
            string mirrorEndpoint,
            int timeoutMs,
            string reason)
        {
            TtsResult primaryResult = SendSingleTtsInterruptRequest(endpoint, timeoutMs, reason);
            if (!primaryResult.Success)
            {
                primaryResult.MirrorAttempted = false;
                primaryResult.MirrorSuccess = false;
                return primaryResult;
            }

            if (string.IsNullOrWhiteSpace(mirrorEndpoint))
            {
                primaryResult.MirrorAttempted = false;
                primaryResult.MirrorSuccess = true;
                return primaryResult;
            }

            TtsResult mirrorResult = SendSingleTtsInterruptRequest(mirrorEndpoint, timeoutMs, reason);
            primaryResult.MirrorAttempted = true;
            primaryResult.MirrorSuccess = mirrorResult.Success;
            if (mirrorResult.Success)
            {
                primaryResult.Message = "TTS interrupt accepted.";
                primaryResult.Error = string.Empty;
            }
            else
            {
                primaryResult.Message = string.IsNullOrWhiteSpace(mirrorResult.Error)
                    ? "TTS interrupt accepted locally; robot speaker mirror interrupt failed."
                    : $"TTS interrupt accepted locally; robot speaker mirror interrupt failed: {mirrorResult.Error}";
                primaryResult.Error = mirrorResult.Error;
            }

            return primaryResult;
        }

        private static TtsResult SendSingleTtsRequest(
            string endpoint,
            int timeoutMs,
            TtsRequest requestPayload,
            bool waitForCompletion)
        {
            try
            {
                int requestTimeoutMs = ComputeTtsRequestTimeoutMs(timeoutMs, requestPayload.Text, waitForCompletion);
                HttpWebRequest request = WebRequest.CreateHttp(endpoint);
                request.Method = "POST";
                request.Timeout = requestTimeoutMs;
                request.ReadWriteTimeout = requestTimeoutMs;
                request.Accept = "application/json";
                request.ContentType = "application/json";
                request.Proxy = null;

                string jsonBody = JsonUtility.ToJson(new TtsPayload
                {
                    text = requestPayload.Text ?? string.Empty,
                    interrupt = requestPayload.Interrupt,
                    wait_for_completion = waitForCompletion,
                    tts_mode = requestPayload.TtsMode ?? string.Empty
                });

                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                request.ContentLength = bodyBytes.Length;
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string responseBody = SanitizeBridgeText(reader.ReadToEnd());
                    string responseMessage = string.IsNullOrWhiteSpace(responseBody)
                        ? "TTS endpoint accepted speech request."
                        : $"TTS endpoint response: {responseBody}";
                    return new TtsResult
                    {
                        Success = true,
                        MirrorAttempted = false,
                        MirrorSuccess = true,
                        RequestedText = requestPayload.Text ?? string.Empty,
                        Message = responseMessage,
                        Error = string.Empty
                    };
                }
            }
            catch (WebException webEx)
            {
                string detail = FormatWebExceptionDetail(webEx, endpoint);

                return new TtsResult
                {
                    Success = false,
                    MirrorAttempted = false,
                    MirrorSuccess = false,
                    RequestedText = requestPayload.Text ?? string.Empty,
                    Message = "TTS endpoint is not reachable.",
                    Error = detail
                };
            }
            catch (Exception ex)
            {
                string detail = SanitizeBridgeText(ex.Message);
                return new TtsResult
                {
                    Success = false,
                    MirrorAttempted = false,
                    MirrorSuccess = false,
                    RequestedText = requestPayload.Text ?? string.Empty,
                    Message = "TTS request failed.",
                    Error = detail
                };
            }
        }

        private static int ComputeTtsRequestTimeoutMs(int timeoutMs, string text, bool waitForCompletion)
        {
            int baseTimeoutMs = Math.Max(250, timeoutMs);
            if (!waitForCompletion)
            {
                return baseTimeoutMs;
            }

            string trimmedText = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedText))
            {
                return Math.Max(baseTimeoutMs, 5000);
            }

            int wordCount = trimmedText.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
            int punctuationCount = 0;
            for (int i = 0; i < trimmedText.Length; i++)
            {
                char ch = trimmedText[i];
                if (ch == '.' || ch == '!' || ch == '?' || ch == ';' || ch == ':')
                {
                    punctuationCount++;
                }
            }

            double wordsPerSecond = Math.Max(0.75, 175.0 / 60.0);
            double estimatedSeconds = (Math.Max(1, wordCount) / wordsPerSecond) + (punctuationCount * 0.25) + 8.0;
            int estimatedTimeoutMs = (int)Math.Ceiling(Math.Min(600.0, estimatedSeconds) * 1000.0);
            return Math.Max(baseTimeoutMs, estimatedTimeoutMs);
        }

        private static TtsResult SendSingleTtsInterruptRequest(string endpoint, int timeoutMs, string reason)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return new TtsResult
                {
                    Success = false,
                    MirrorAttempted = false,
                    MirrorSuccess = false,
                    Message = "TTS interrupt endpoint is unavailable.",
                    Error = "endpoint is empty"
                };
            }

            try
            {
                HttpWebRequest request = WebRequest.CreateHttp(endpoint);
                request.Method = "POST";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.Accept = "application/json";
                request.ContentType = "application/json";
                request.Proxy = null;

                string jsonBody = JsonUtility.ToJson(new TtsStopPayload
                {
                    reason = reason ?? string.Empty
                });

                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                request.ContentLength = bodyBytes.Length;
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string responseBody = SanitizeBridgeText(reader.ReadToEnd());
                    string responseMessage = string.IsNullOrWhiteSpace(responseBody)
                        ? "TTS interrupt accepted."
                        : $"TTS interrupt response: {responseBody}";
                    return new TtsResult
                    {
                        Success = true,
                        MirrorAttempted = false,
                        MirrorSuccess = true,
                        Message = responseMessage,
                        Error = string.Empty
                    };
                }
            }
            catch (WebException webEx)
            {
                string detail = FormatWebExceptionDetail(webEx, endpoint);

                return new TtsResult
                {
                    Success = false,
                    MirrorAttempted = false,
                    MirrorSuccess = false,
                    Message = "TTS interrupt endpoint is not reachable.",
                    Error = detail
                };
            }
            catch (Exception ex)
            {
                string detail = SanitizeBridgeText(ex.Message);
                return new TtsResult
                {
                    Success = false,
                    MirrorAttempted = false,
                    MirrorSuccess = false,
                    Message = "TTS interrupt request failed.",
                    Error = detail
                };
            }
        }

        private static string BuildTtsStopEndpoint(string endpoint)
        {
            string trimmedEndpoint = SanitizeBridgeText(endpoint);
            if (string.IsNullOrWhiteSpace(trimmedEndpoint) ||
                !Uri.TryCreate(trimmedEndpoint, UriKind.Absolute, out Uri endpointUri))
            {
                return string.Empty;
            }

            string absolutePath = string.IsNullOrWhiteSpace(endpointUri.AbsolutePath)
                ? "/"
                : endpointUri.AbsolutePath;
            int lastSlashIndex = absolutePath.LastIndexOf('/');
            string stopPath = lastSlashIndex >= 0
                ? absolutePath.Substring(0, lastSlashIndex + 1) + "stop"
                : "/stop";

            var builder = new UriBuilder(endpointUri)
            {
                Path = stopPath,
                Query = string.Empty,
                Fragment = string.Empty
            };
            return builder.Uri.AbsoluteUri;
        }

        private static string FormatWebExceptionDetail(WebException webEx, string endpoint)
        {
            var parts = new List<string>();
            string trimmedEndpoint = SanitizeBridgeText(endpoint);
            if (!string.IsNullOrWhiteSpace(trimmedEndpoint))
            {
                parts.Add($"endpoint={trimmedEndpoint}");
            }

            if (webEx != null)
            {
                if (webEx.Status != WebExceptionStatus.Success)
                {
                    parts.Add($"status={webEx.Status}");
                }

                string message = SanitizeBridgeText(webEx.Message);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    parts.Add(message);
                }

                if (webEx.Response is HttpWebResponse httpResponse)
                {
                    parts.Add($"http={(int)httpResponse.StatusCode} {httpResponse.StatusCode}");
                }

                string responseText = ReadWebResponseBody(webEx.Response);
                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    parts.Add($"body={responseText}");
                }
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string ReadWebResponseBody(WebResponse response)
        {
            if (response == null)
            {
                return string.Empty;
            }

            try
            {
                using (Stream responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    return SanitizeBridgeText(reader.ReadToEnd());
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SanitizeBridgeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!char.IsControl(character) || character == '\r' || character == '\n' || character == '\t')
                {
                    builder.Append(character);
                }
            }

            return builder.ToString().Trim();
        }

        private static HelpResult SendHelpRequest(string endpoint, int timeoutMs, HelpRequest requestPayload)
        {
            try
            {
                HttpWebRequest request = WebRequest.CreateHttp(endpoint);
                request.Method = "POST";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.Accept = "application/json";
                request.ContentType = "application/json";
                request.Proxy = null;

                string jsonBody = JsonUtility.ToJson(new HelpPayload
                {
                    query = requestPayload.Query ?? string.Empty,
                    context = requestPayload.Context ?? string.Empty
                });

                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                request.ContentLength = bodyBytes.Length;
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string responseBody = reader.ReadToEnd();
                    return ParseHelpResponse(responseBody);
                }
            }
            catch (WebException webEx)
            {
                string detail = webEx.Message;
                if (webEx.Response != null)
                {
                    using (Stream responseStream = webEx.Response.GetResponseStream())
                    using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                    {
                        string responseText = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(responseText))
                        {
                            detail = $"{detail} ({responseText})";
                        }
                    }
                }

                return new HelpResult
                {
                    Success = false,
                    Answer = string.Empty,
                    Message = "Local help endpoint is not reachable.",
                    Error = detail
                };
            }
            catch (Exception ex)
            {
                return new HelpResult
                {
                    Success = false,
                    Answer = string.Empty,
                    Message = "Local help request failed.",
                    Error = ex.Message
                };
            }
        }

        private static ListeningToggleResult SendListeningToggleRequest(
            string endpoint,
            int timeoutMs,
            ListeningToggleRequest requestPayload)
        {
            try
            {
                HttpWebRequest request = WebRequest.CreateHttp(endpoint);
                request.Method = "POST";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.Accept = "application/json";
                request.ContentType = "application/json";
                request.Proxy = null;

                string jsonBody = JsonUtility.ToJson(new ListeningTogglePayload
                {
                    enabled = requestPayload.Enabled
                });

                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                request.ContentLength = bodyBytes.Length;
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                {
                    string responseBody = reader.ReadToEnd();
                    return ParseListeningToggleResponse(responseBody, requestPayload.Enabled);
                }
            }
            catch (WebException webEx)
            {
                string detail = webEx.Message;
                if (webEx.Response != null)
                {
                    using (Stream responseStream = webEx.Response.GetResponseStream())
                    using (var reader = new StreamReader(responseStream ?? Stream.Null, Encoding.UTF8))
                    {
                        string responseText = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(responseText))
                        {
                            detail = $"{detail} ({responseText})";
                        }
                    }
                }

                return new ListeningToggleResult
                {
                    Success = false,
                    Enabled = requestPayload.Enabled,
                    Message = "Listening control endpoint is not reachable.",
                    Error = detail
                };
            }
            catch (Exception ex)
            {
                return new ListeningToggleResult
                {
                    Success = false,
                    Enabled = requestPayload.Enabled,
                    Message = "Listening toggle request failed.",
                    Error = ex.Message
                };
            }
        }

        private static PollResult ParseIntentResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return new PollResult
                {
                    Success = true,
                    HasIntent = false,
                    Intent = null,
                    Message = "Voice bridge returned no intent.",
                    Error = string.Empty
                };
            }

            try
            {
                IntentEnvelope envelope = JsonUtility.FromJson<IntentEnvelope>(body);
                if (envelope != null)
                {
                    bool envelopeHasBridgeStatus =
                        HasJsonProperty(body, "\"mic_active\"") ||
                        HasJsonProperty(body, "\"listening\"") ||
                        HasJsonProperty(body, "\"stt_backend\"") ||
                        HasJsonProperty(body, "\"transcript\"") ||
                        HasJsonProperty(body, "\"transcript_is_final\"");

                    var baseEnvelopeResult = new PollResult
                    {
                        Success = true,
                        HasIntent = false,
                        Intent = null,
                        HasBridgeStatus = envelopeHasBridgeStatus,
                        MicActive = envelope.mic_active,
                        Listening = envelope.listening,
                        SttBackend = envelope.stt_backend ?? string.Empty,
                        Transcript = envelope.transcript ?? string.Empty,
                        TranscriptIsFinal = envelope.transcript_is_final,
                        TranscriptConfidence = envelope.confidence,
                        Message = "Voice bridge poll OK.",
                        Error = string.Empty
                    };

                    if (envelope.has_intent && envelope.intent != null)
                    {
                        envelope.intent.raw_json = body;
                        baseEnvelopeResult.HasIntent = true;
                        baseEnvelopeResult.Intent = envelope.intent;
                        baseEnvelopeResult.Message = "Voice bridge intent received.";
                        return baseEnvelopeResult;
                    }

                    if (!string.IsNullOrWhiteSpace(envelope.transcript))
                    {
                        bool isFinalTranscript = envelope.transcript_is_final;
                        baseEnvelopeResult.Intent = BuildTranscriptIntent(
                            envelope.transcript,
                            envelope.confidence,
                            isFinalTranscript,
                            body);
                        baseEnvelopeResult.HasIntent = isFinalTranscript;
                        baseEnvelopeResult.Message = isFinalTranscript
                            ? "Voice bridge transcript received."
                            : "Voice bridge partial transcript received.";
                        return baseEnvelopeResult;
                    }

                    if (!string.IsNullOrWhiteSpace(envelope.message))
                    {
                        baseEnvelopeResult.Message = envelope.message;
                        return baseEnvelopeResult;
                    }

                    if (baseEnvelopeResult.HasBridgeStatus)
                    {
                        return baseEnvelopeResult;
                    }
                }

                VoiceAgentIntent directIntent = JsonUtility.FromJson<VoiceAgentIntent>(body);
                if (directIntent != null &&
                    (!string.IsNullOrWhiteSpace(directIntent.intent) ||
                     !string.IsNullOrWhiteSpace(directIntent.spoken_text) ||
                     !string.IsNullOrWhiteSpace(directIntent.reply_text)))
                {
                    directIntent.raw_json = body;
                    bool hasStructuredIntent =
                        !string.IsNullOrWhiteSpace(directIntent.intent) ||
                        !string.IsNullOrWhiteSpace(directIntent.reply_text);
                    bool isFinalTranscript = directIntent.transcript_is_final;
                    bool hasTranscript = !string.IsNullOrWhiteSpace(directIntent.spoken_text);
                    bool shouldQueueIntent = hasStructuredIntent || isFinalTranscript;
                    return new PollResult
                    {
                        Success = true,
                        HasIntent = shouldQueueIntent,
                        Intent = directIntent,
                        HasBridgeStatus = hasTranscript,
                        Transcript = directIntent.spoken_text ?? string.Empty,
                        TranscriptIsFinal = isFinalTranscript,
                        TranscriptConfidence = directIntent.confidence,
                        Message = hasStructuredIntent
                            ? "Voice bridge direct intent received."
                            : (isFinalTranscript
                                ? "Voice bridge direct transcript received."
                                : "Voice bridge direct partial transcript received."),
                        Error = string.Empty
                    };
                }

                return new PollResult
                {
                    Success = true,
                    HasIntent = false,
                    Intent = null,
                    Message = "Voice bridge poll OK (no actionable intent).",
                    Error = string.Empty
                };
            }
            catch (Exception ex)
            {
                return new PollResult
                {
                    Success = false,
                    HasIntent = false,
                    Intent = null,
                    Message = "Voice bridge returned invalid JSON.",
                    Error = ex.Message
                };
            }
        }

        private static VoiceAgentIntent BuildTranscriptIntent(
            string transcript,
            float confidence,
            bool transcriptIsFinal,
            string rawJson)
        {
            return new VoiceAgentIntent
            {
                type = "transcript",
                intent = string.Empty,
                confidence = confidence,
                requires_confirmation = false,
                spoken_text = transcript ?? string.Empty,
                transcript_is_final = transcriptIsFinal,
                raw_json = rawJson ?? string.Empty
            };
        }

        private static bool HasJsonProperty(string json, string propertyToken)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyToken))
            {
                return false;
            }

            return json.IndexOf(propertyToken, StringComparison.Ordinal) >= 0;
        }

        private static HelpResult ParseHelpResponse(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return new HelpResult
                {
                    Success = false,
                    Answer = string.Empty,
                    Message = "Local help endpoint returned no response.",
                    Error = string.Empty
                };
            }

            try
            {
                HelpResponsePayload payload = JsonUtility.FromJson<HelpResponsePayload>(responseBody);
                if (payload != null)
                {
                    string answer = !string.IsNullOrWhiteSpace(payload.answer)
                        ? payload.answer
                        : payload.text;
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        return new HelpResult
                        {
                            Success = true,
                            Answer = answer,
                            Message = "Local help response received.",
                            Error = string.Empty
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(payload.message))
                    {
                        return new HelpResult
                        {
                            Success = payload.ok,
                            Answer = string.Empty,
                            Message = payload.message,
                            Error = payload.ok ? string.Empty : payload.message
                        };
                    }
                }
            }
            catch
            {
                // If JSON parsing fails, treat response as plain text.
            }

            return new HelpResult
            {
                Success = true,
                Answer = responseBody.Trim(),
                Message = "Local help plain-text response received.",
                Error = string.Empty
            };
        }

        private static ListeningToggleResult ParseListeningToggleResponse(
            string responseBody,
            bool requestedEnabled)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return new ListeningToggleResult
                {
                    Success = true,
                    Enabled = requestedEnabled,
                    Message = $"Remote listening {(requestedEnabled ? "enabled" : "disabled")}.",
                    Error = string.Empty
                };
            }

            try
            {
                ListeningToggleResponsePayload payload = JsonUtility.FromJson<ListeningToggleResponsePayload>(responseBody);
                if (payload != null)
                {
                    bool resolvedEnabled = HasJsonProperty(responseBody, "\"enabled\"")
                        ? payload.enabled
                        : requestedEnabled;
                    string resolvedMessage = !string.IsNullOrWhiteSpace(payload.message)
                        ? payload.message
                        : $"Remote listening {(resolvedEnabled ? "enabled" : "disabled")}.";
                    if (payload.ok || !HasJsonProperty(responseBody, "\"ok\""))
                    {
                        return new ListeningToggleResult
                        {
                            Success = true,
                            Enabled = resolvedEnabled,
                            Message = resolvedMessage,
                            Error = string.Empty
                        };
                    }

                    return new ListeningToggleResult
                    {
                        Success = false,
                        Enabled = resolvedEnabled,
                        Message = resolvedMessage,
                        Error = resolvedMessage
                    };
                }
            }
            catch
            {
                // Fall through to plain text result.
            }

            return new ListeningToggleResult
            {
                Success = true,
                Enabled = requestedEnabled,
                Message = responseBody.Trim(),
                Error = string.Empty
            };
        }

        private float ComputeBackoffSeconds(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0)
            {
                return _pollIntervalSeconds;
            }

            double exponent = Math.Max(0, consecutiveFailures - 1);
            double backoff = _retryBackoffMinSeconds * Math.Pow(2.0, exponent);
            if (backoff > _retryBackoffMaxSeconds)
            {
                backoff = _retryBackoffMaxSeconds;
            }

            return (float)Math.Max(_pollIntervalSeconds, backoff);
        }

        private void AddLogLocked(string eventType, string severity, string detail)
        {
            var entry = new BridgeLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                EventType = string.IsNullOrWhiteSpace(eventType) ? "bridge" : eventType,
                Severity = string.IsNullOrWhiteSpace(severity) ? "info" : severity,
                Detail = string.IsNullOrWhiteSpace(detail) ? "n/a" : detail
            };

            _eventLog.Enqueue(entry);
            while (_eventLog.Count > 60)
            {
                _eventLog.Dequeue();
            }

            _lastLogLine = FormatLogEntry(entry);
        }

        private static string FormatLogEntry(BridgeLogEntry entry)
        {
            return $"{entry.TimestampUtc:O} [{entry.Severity}] {entry.EventType}: {entry.Detail}";
        }
    }
}
