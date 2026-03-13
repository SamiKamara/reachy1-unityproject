using UnityEngine;

namespace Reachy.ControlApp
{
    public static class VoiceAgentStatusPanel
    {
        public struct State
        {
            public bool AgentEnabled;
            public bool BridgeReachable;
            public bool PollInFlight;
            public bool DegradedMode;
            public bool HeartbeatExpired;
            public string Endpoint;
            public int QueuedIntents;
            public int ReceivedIntents;
            public int PollFailures;
            public int ConsecutivePollFailures;
            public double SecondsSinceLastHealthyPoll;
            public int EventLogCount;
            public bool MicActive;
            public bool Listening;
            public string SttBackend;
            public bool LastTranscriptIsFinal;
            public float LastTranscriptConfidence;
            public bool PushToTalkEnabled;
            public string PushToTalkKey;
            public bool RequestedListeningEnabled;
            public bool SimulationOnlyMode;
            public bool DuplicateSuppressionEnabled;
            public float DuplicateSuppressionWindowSeconds;
            public bool SafeNumericParsingEnabled;
            public bool RequireTargetTokenForJoint;
            public bool RejectOutOfRangeJointCommands;
            public float JointMinDegrees;
            public float JointMaxDegrees;
            public int MinTranscriptChars;
            public int MinTranscriptWords;
            public bool ListeningToggleInFlight;
            public string ListeningEndpoint;
            public int SuccessfulListeningToggleRequests;
            public int FailedListeningToggleRequests;
            public string LastListeningToggleMessage;
            public string LastListeningToggleError;
            public bool TtsEnabled;
            public bool TtsInFlight;
            public string TtsEndpoint;
            public int SuccessfulTtsRequests;
            public int FailedTtsRequests;
            public string LastTtsMessage;
            public string LastTtsError;
            public string LastSpokenFeedback;
            public bool LocalHelpEnabled;
            public bool HelpInFlight;
            public string HelpEndpoint;
            public int SuccessfulHelpRequests;
            public int FailedHelpRequests;
            public string LastHelpMessage;
            public string LastHelpError;
            public string LastHelpAnswer;
            public string LastBridgeMessage;
            public string LastTranscript;
            public string LastParserMessage;
            public string LastIntentSummary;
            public string LastActionResult;
            public string PendingActionSummary;
            public string LastBridgeLogLine;
            public string AiModeLabel;
            public bool OnlineAiEnabled;
            public string OnlineModel;
            public string OnlineApiKeyStatus;
            public string OnlineLastKeyCheckUtc;
            public string OnlineLastReplyText;
            public string OnlineLastValidationResult;
            public string OnlineLastValidationFailure;
            public string OnlineLastConnectionTestResult;
            public string OnlineSourceBackend;
            public float OnlineLastLatencyMs;
            public string OnlineLastHttpError;
        }

        public static void DrawReadout(State state)
        {
            string bridgeState = state.AgentEnabled
                ? (state.BridgeReachable ? "Online" : "Offline")
                : "Disabled";
            string pollState = state.PollInFlight ? " (polling...)" : string.Empty;

            if (!string.IsNullOrWhiteSpace(state.AiModeLabel))
            {
                GUILayout.Label($"AI mode: {state.AiModeLabel}");
            }
            GUILayout.Label($"Bridge: {bridgeState}{pollState}");
            if (state.DegradedMode)
            {
                GUILayout.Label("Bridge mode: DEGRADED");
            }
            if (state.HeartbeatExpired)
            {
                GUILayout.Label($"Heartbeat: stale ({state.SecondsSinceLastHealthyPoll:F1}s since healthy poll)");
            }
            if (!string.IsNullOrWhiteSpace(state.Endpoint))
            {
                GUILayout.Label($"Endpoint: {state.Endpoint}");
            }
            GUILayout.Label(
                $"Poll failures: {state.PollFailures} | Consecutive: {state.ConsecutivePollFailures} | Last healthy: {state.SecondsSinceLastHealthyPoll:F1}s ago");
            GUILayout.Label($"Bridge logs in buffer: {state.EventLogCount}");
            string sttBackend = string.IsNullOrWhiteSpace(state.SttBackend) ? "unknown" : state.SttBackend;
            string listeningState = state.Listening ? "listening" : "idle";
            string micState = state.MicActive ? "on" : "off";
            GUILayout.Label($"STT: {sttBackend} | Mic: {micState} | Mode: {listeningState}");
            string pttLabel = state.PushToTalkEnabled
                ? $"ON ({state.PushToTalkKey})"
                : "off";
            GUILayout.Label(
                $"Push-to-talk: {pttLabel} | Requested listening: {(state.RequestedListeningEnabled ? "enabled" : "disabled")}");
            GUILayout.Label(
                $"Safety mode: {(state.SimulationOnlyMode ? "simulation-only" : "full")} | Dedupe: {(state.DuplicateSuppressionEnabled ? "on" : "off")} ({state.DuplicateSuppressionWindowSeconds:F2}s)");
            GUILayout.Label(
                $"Joint parse: {(state.SafeNumericParsingEnabled ? "safe" : "legacy")} | Target token: {(state.RequireTargetTokenForJoint ? "required" : "optional")} | Range policy: {(state.RejectOutOfRangeJointCommands ? "reject" : "allow")} [{state.JointMinDegrees:F1},{state.JointMaxDegrees:F1}]");
            GUILayout.Label($"Transcript gates: min {state.MinTranscriptChars} chars / {state.MinTranscriptWords} words");
            string listeningCtlState = state.ListeningToggleInFlight ? "sending" : "idle";
            GUILayout.Label(
                $"Listening ctl: {listeningCtlState} | Success: {state.SuccessfulListeningToggleRequests} | Fail: {state.FailedListeningToggleRequests}");
            if (!string.IsNullOrWhiteSpace(state.ListeningEndpoint))
            {
                GUILayout.Label($"Listening endpoint: {state.ListeningEndpoint}");
            }
            string ttsState = state.TtsEnabled
                ? (state.TtsInFlight ? "enabled (sending)" : "enabled")
                : "disabled";
            GUILayout.Label($"TTS: {ttsState} | Success: {state.SuccessfulTtsRequests} | Fail: {state.FailedTtsRequests}");
            if (!string.IsNullOrWhiteSpace(state.TtsEndpoint))
            {
                GUILayout.Label($"TTS endpoint: {state.TtsEndpoint}");
            }
            string helpState = state.LocalHelpEnabled
                ? (state.HelpInFlight ? "enabled (querying)" : "enabled")
                : "disabled";
            GUILayout.Label($"Help model: {helpState} | Success: {state.SuccessfulHelpRequests} | Fail: {state.FailedHelpRequests}");
            if (!string.IsNullOrWhiteSpace(state.HelpEndpoint))
            {
                GUILayout.Label($"Help endpoint: {state.HelpEndpoint}");
            }
            GUILayout.Label($"Queue: {state.QueuedIntents} | Received: {state.ReceivedIntents}");

            if (!string.IsNullOrWhiteSpace(state.LastBridgeMessage))
            {
                GUILayout.Label($"Bridge msg: {state.LastBridgeMessage}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastTranscript))
            {
                string transcriptKind = state.LastTranscriptIsFinal ? "final" : "partial";
                GUILayout.Label(
                    $"Transcript ({transcriptKind}, conf {state.LastTranscriptConfidence:F2}): {state.LastTranscript}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastParserMessage))
            {
                GUILayout.Label($"Parser: {state.LastParserMessage}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastTtsMessage))
            {
                GUILayout.Label($"TTS msg: {state.LastTtsMessage}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastListeningToggleMessage))
            {
                GUILayout.Label($"Listening msg: {state.LastListeningToggleMessage}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastListeningToggleError))
            {
                GUILayout.Label($"Listening err: {state.LastListeningToggleError}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastTtsError))
            {
                GUILayout.Label($"TTS err: {state.LastTtsError}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastHelpMessage))
            {
                GUILayout.Label($"Help msg: {state.LastHelpMessage}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastHelpError))
            {
                GUILayout.Label($"Help err: {state.LastHelpError}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastIntentSummary))
            {
                GUILayout.Label($"Intent: {state.LastIntentSummary}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastActionResult))
            {
                GUILayout.Label($"Action: {state.LastActionResult}");
            }

            if (!string.IsNullOrWhiteSpace(state.PendingActionSummary))
            {
                GUILayout.Label($"Pending confirm: {state.PendingActionSummary}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastSpokenFeedback))
            {
                GUILayout.Label($"Confirmed speech: {state.LastSpokenFeedback}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastHelpAnswer))
            {
                GUILayout.Label($"Help answer: {state.LastHelpAnswer}");
            }

            bool showOnlineSection =
                string.Equals(state.AiModeLabel, "Online AI") ||
                state.OnlineAiEnabled ||
                !string.IsNullOrWhiteSpace(state.OnlineModel) ||
                !string.IsNullOrWhiteSpace(state.OnlineLastReplyText);

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineApiKeyStatus))
            {
                GUILayout.Label(
                    $"Online API key: {state.OnlineApiKeyStatus} | Online enabled: {(state.OnlineAiEnabled ? "yes" : "no")}");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineModel))
            {
                GUILayout.Label($"Online model: {state.OnlineModel}");
            }

            if (showOnlineSection && state.OnlineLastLatencyMs >= 0f)
            {
                GUILayout.Label($"Online latency: {state.OnlineLastLatencyMs:F0} ms");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineLastKeyCheckUtc))
            {
                GUILayout.Label($"Online key check: {state.OnlineLastKeyCheckUtc}");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineLastValidationResult))
            {
                GUILayout.Label($"Online validation: {state.OnlineLastValidationResult}");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineLastValidationFailure))
            {
                GUILayout.Label($"Online validation detail: {state.OnlineLastValidationFailure}");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineLastReplyText))
            {
                GUILayout.Label($"Online reply: {state.OnlineLastReplyText}");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineLastConnectionTestResult))
            {
                GUILayout.Label($"Online test: {state.OnlineLastConnectionTestResult}");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineSourceBackend))
            {
                GUILayout.Label($"Online backend: {state.OnlineSourceBackend}");
            }

            if (showOnlineSection && !string.IsNullOrWhiteSpace(state.OnlineLastHttpError))
            {
                GUILayout.Label($"Online HTTP err: {state.OnlineLastHttpError}");
            }

            if (!string.IsNullOrWhiteSpace(state.LastBridgeLogLine))
            {
                GUILayout.Label($"Log: {state.LastBridgeLogLine}");
            }
        }
    }
}
