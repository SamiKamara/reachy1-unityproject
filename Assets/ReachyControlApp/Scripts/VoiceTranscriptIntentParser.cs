using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using UnityEngine;

namespace Reachy.ControlApp
{
    public sealed class VoiceTranscriptIntentParser
    {
        private const float DefaultTranscriptConfidence = 0.85f;
        private const int ShortLoneWordHelpFallbackMaxChars = 8;
        private static readonly string[][] HelpSynonymTokenSets =
        {
            new[] { "help" },
            new[] { "help", "me" },
            new[] { "i", "need", "help" },
            new[] { "need", "help" },
            new[] { "can", "you", "help" },
            new[] { "please", "help" },
            new[] { "give", "me", "help" },
            new[] { "voice", "help" },
            new[] { "show", "commands" },
            new[] { "show", "me", "commands" },
            new[] { "list", "commands" },
            new[] { "list", "voice", "commands" },
            new[] { "what", "can", "i", "say" },
            new[] { "what", "commands", "are", "available" },
            new[] { "what", "can", "you", "do" },
            new[] { "how", "do", "i", "use", "this" },
            new[] { "how", "to", "use", "this" },
            new[] { "give", "instructions" },
            new[] { "usage", "instructions" },
            new[] { "guide", "me" },
            new[] { "i", "need", "guidance" },
            new[] { "show", "help" },
            new[] { "open", "help" },
            new[] { "display", "help" },
            new[] { "need", "instructions" },
            new[] { "how", "does", "this", "work" },
            new[] { "teach", "me" },
            new[] { "walk", "me", "through", "commands" },
            new[] { "command", "list" },
            new[] { "available", "commands" },
            new[] { "help", "with", "commands" }
        };
        private static readonly string[][] HelloSynonymTokenSets =
        {
            new[] { "hello" },
            new[] { "hello", "there" },
            new[] { "hi" },
            new[] { "hi", "there" },
            new[] { "hey" },
            new[] { "hey", "there" },
            new[] { "hey", "robot" },
            new[] { "hello", "robot" },
            new[] { "hi", "robot" },
            new[] { "hey", "reachy" },
            new[] { "hello", "reachy" },
            new[] { "hi", "reachy" },
            new[] { "greetings" },
            new[] { "greetings", "robot" },
            new[] { "good", "morning" },
            new[] { "good", "afternoon" },
            new[] { "good", "evening" },
            new[] { "say", "hello" },
            new[] { "greet", "me" },
            new[] { "do", "a", "greeting" }
        };
        private static readonly string[][] WhoAreYouSynonymTokenSets =
        {
            new[] { "who", "are", "you" },
            new[] { "what", "are", "you" },
            new[] { "what", "are", "you", "exactly" },
            new[] { "what", "is", "this", "assistant" },
            new[] { "what", "is", "this", "agent" },
            new[] { "who", "am", "i", "talking", "to" },
            new[] { "identify", "yourself" },
            new[] { "tell", "me", "who", "you", "are" },
            new[] { "tell", "me", "what", "you", "are" },
            new[] { "what", "is", "your", "name" },
            new[] { "whats", "your", "name" },
            new[] { "who", "is", "this" },
            new[] { "are", "you", "a", "robot" },
            new[] { "are", "you", "an", "assistant" },
            new[] { "are", "you", "ai" },
            new[] { "what", "kind", "of", "assistant", "are", "you" },
            new[] { "what", "kind", "of", "ai", "are", "you" },
            new[] { "who", "is", "speaking" },
            new[] { "introduce", "yourself" },
            new[] { "tell", "me", "about", "yourself" }
        };
        private static readonly string[][] ShowMovementSynonymTokenSets =
        {
            new[] { "show", "movement" },
            new[] { "show", "motion" },
            new[] { "do", "something" },
            new[] { "do", "anything" },
            new[] { "move" },
            new[] { "move", "around" },
            new[] { "make", "a", "move" },
            new[] { "do", "a", "movement" },
            new[] { "do", "a", "motion" },
            new[] { "show", "me", "movement" },
            new[] { "show", "me", "motion" },
            new[] { "perform", "movement" },
            new[] { "perform", "a", "movement" },
            new[] { "perform", "motion" },
            new[] { "make", "it", "move" },
            new[] { "move", "a", "bit" },
            new[] { "start", "moving" },
            new[] { "do", "random", "movement" },
            new[] { "do", "random", "motion" },
            new[] { "surprise", "me" }
        };
        private static readonly string[][] StopMotionSynonymTokenSets =
        {
            new[] { "stop" },
            new[] { "stop", "now" },
            new[] { "stop", "motion" },
            new[] { "stop", "moving" },
            new[] { "halt" },
            new[] { "halt", "now" },
            new[] { "halt", "motion" },
            new[] { "halt", "movement" },
            new[] { "abort" },
            new[] { "abort", "command" },
            new[] { "cancel" },
            new[] { "cancel", "motion" },
            new[] { "cancel", "command" },
            new[] { "emergency", "stop" },
            new[] { "hard", "stop" },
            new[] { "freeze" },
            new[] { "freeze", "motion" },
            new[] { "cease", "movement" },
            new[] { "terminate", "motion" },
            new[] { "end", "movement" },
            new[] { "stop", "immediately" }
        };
        private static readonly string[][] ConfirmPendingSynonymTokenSets =
        {
            new[] { "confirm" },
            new[] { "confirm", "it" },
            new[] { "confirm", "command" },
            new[] { "yes" },
            new[] { "yes", "do", "it" },
            new[] { "yes", "execute" },
            new[] { "go", "ahead" },
            new[] { "go", "for", "it" },
            new[] { "proceed" },
            new[] { "approved" },
            new[] { "approve", "it" },
            new[] { "execute", "it" },
            new[] { "run", "it" },
            new[] { "do", "it" },
            new[] { "sounds", "good" },
            new[] { "looks", "good" },
            new[] { "ok", "do", "it" },
            new[] { "okay", "proceed" },
            new[] { "affirmative" },
            new[] { "that", "is", "right" }
        };
        private static readonly string[][] RejectPendingSynonymTokenSets =
        {
            new[] { "reject" },
            new[] { "reject", "it" },
            new[] { "decline" },
            new[] { "decline", "it" },
            new[] { "deny" },
            new[] { "deny", "it" },
            new[] { "no" },
            new[] { "no", "thanks" },
            new[] { "do", "not" },
            new[] { "do", "not", "do", "it" },
            new[] { "do", "not", "proceed" },
            new[] { "do", "not", "execute" },
            new[] { "not", "now" },
            new[] { "negative" },
            new[] { "skip", "it" },
            new[] { "dismiss", "that" },
            new[] { "forget", "it" },
            new[] { "hold", "off" },
            new[] { "never", "mind" },
            new[] { "i", "reject", "that" }
        };
        private static readonly string[][] DisconnectRobotSynonymTokenSets =
        {
            new[] { "disconnect" },
            new[] { "disconnect", "robot" },
            new[] { "disconnect", "now" },
            new[] { "go", "offline" },
            new[] { "switch", "offline" },
            new[] { "take", "robot", "offline" },
            new[] { "end", "connection" },
            new[] { "close", "connection" },
            new[] { "drop", "connection" },
            new[] { "cut", "connection" },
            new[] { "disconnect", "from", "robot" },
            new[] { "shut", "down", "connection" },
            new[] { "terminate", "connection" },
            new[] { "leave", "session" },
            new[] { "end", "session" },
            new[] { "disconnect", "session" },
            new[] { "unpair", "robot" },
            new[] { "go", "disconnected" },
            new[] { "disconnect", "please" },
            new[] { "disconnect", "link" }
        };
        private static readonly string[][] ConnectRobotSynonymTokenSets =
        {
            new[] { "connect" },
            new[] { "connect", "robot" },
            new[] { "connect", "now" },
            new[] { "reconnect" },
            new[] { "reconnect", "robot" },
            new[] { "go", "online" },
            new[] { "come", "online" },
            new[] { "bring", "robot", "online" },
            new[] { "start", "connection" },
            new[] { "open", "connection" },
            new[] { "establish", "connection" },
            new[] { "pair", "robot" },
            new[] { "link", "robot" },
            new[] { "join", "session" },
            new[] { "connect", "please" },
            new[] { "resume", "connection" },
            new[] { "restore", "connection" },
            new[] { "reconnect", "now" },
            new[] { "connect", "to", "robot" },
            new[] { "start", "robot", "connection" }
        };
        private static readonly string[][] StatusSynonymTokenSets =
        {
            new[] { "status" },
            new[] { "robot", "status" },
            new[] { "connection", "status" },
            new[] { "show", "status" },
            new[] { "check", "status" },
            new[] { "status", "report" },
            new[] { "what", "is", "status" },
            new[] { "what", "is", "the", "status" },
            new[] { "are", "you", "connected" },
            new[] { "is", "robot", "connected" },
            new[] { "connection", "state" },
            new[] { "health", "status" },
            new[] { "system", "status" },
            new[] { "current", "status" },
            new[] { "tell", "me", "status" },
            new[] { "get", "status" },
            new[] { "show", "connection" },
            new[] { "check", "connection" },
            new[] { "am", "i", "connected" },
            new[] { "are", "we", "connected" }
        };
        private static readonly string[][] MoveJointSynonymTokenSets =
        {
            new[] { "move" },
            new[] { "set" },
            new[] { "turn" },
            new[] { "rotate" },
            new[] { "bend" },
            new[] { "joint" },
            new[] { "adjust" },
            new[] { "tilt" },
            new[] { "twist" },
            new[] { "lift" },
            new[] { "raise" },
            new[] { "lower" },
            new[] { "drop" },
            new[] { "flex" },
            new[] { "extend" },
            new[] { "position" },
            new[] { "point" },
            new[] { "pitch" },
            new[] { "roll" },
            new[] { "yaw" },
            new[] { "swing" },
            new[] { "angle" }
        };
        private static readonly string[][] SetPoseSynonymTokenSets =
        {
            new[] { "set", "pose" },
            new[] { "change", "pose" },
            new[] { "switch", "pose" },
            new[] { "use", "pose" },
            new[] { "apply", "pose" },
            new[] { "go", "to", "pose" },
            new[] { "activate", "pose" },
            new[] { "load", "pose" },
            new[] { "run", "pose" },
            new[] { "do", "pose" },
            new[] { "pick", "pose" },
            new[] { "select", "pose" },
            new[] { "choose", "pose" },
            new[] { "make", "pose" },
            new[] { "show", "pose" },
            new[] { "assume", "pose" },
            new[] { "strike", "pose" },
            new[] { "set", "posture" },
            new[] { "change", "posture" },
            new[] { "set", "position" }
        };

        public bool UseSafeNumericParsing { get; set; } = true;
        public bool RequireTargetTokenForJoint { get; set; } = false;
        public bool RejectOutOfRangeJointCommands { get; set; } = true;
        public float JointMinDegrees { get; set; } = -180f;
        public float JointMaxDegrees { get; set; } = 180f;
        public int MinTranscriptChars { get; set; } = 4;
        public int MinTranscriptWords { get; set; } = 1;

        public bool TryParse(
            string transcript,
            float transcriptConfidence,
            IReadOnlyList<string> knownPresetNames,
            IReadOnlyList<string> knownJointNames,
            out VoiceAgentIntent parsedIntent,
            out string message)
        {
            parsedIntent = null;
            message = string.Empty;

            string raw = transcript ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                message = "Transcript is empty.";
                return false;
            }

            List<string> tokens = BuildWordTokens(raw);
            if (tokens.Count == 0)
            {
                message = "Transcript did not contain parseable tokens.";
                return false;
            }

            bool shortConfirmOrReject = IsShortConfirmOrReject(raw, tokens);
            bool shortLoneWordHelpFallback = IsShortLoneWordHelpFallbackCandidate(raw, tokens);
            float confidence = ResolveConfidence(transcriptConfidence);
            int minChars = Math.Max(0, MinTranscriptChars);
            if (raw.Trim().Length < minChars && !shortConfirmOrReject && !shortLoneWordHelpFallback)
            {
                parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
                message = $"Transcript routed to help: shorter than {minChars} characters.";
                return true;
            }

            int minWords = Math.Max(0, MinTranscriptWords);
            if (tokens.Count < minWords && !shortConfirmOrReject && !shortLoneWordHelpFallback)
            {
                parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
                message = $"Transcript routed to help: fewer than {minWords} words.";
                return true;
            }

            string compactTranscript = BuildCompactToken(raw);

            if (IsStopMotionPhrase(tokens))
            {
                parsedIntent = BuildIntent("stop_motion", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as stop_motion.";
                return true;
            }

            if (IsRejectPendingPhrase(tokens))
            {
                parsedIntent = BuildIntent("reject_pending", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as reject_pending.";
                return true;
            }

            if (IsConfirmPendingPhrase(tokens))
            {
                parsedIntent = BuildIntent("confirm_pending", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as confirm_pending.";
                return true;
            }

            if (IsDisconnectRobotPhrase(tokens))
            {
                parsedIntent = BuildIntent("disconnect_robot", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as disconnect_robot.";
                return true;
            }

            if (IsConnectRobotPhrase(tokens))
            {
                parsedIntent = BuildIntent("connect_robot", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as connect_robot.";
                return true;
            }

            if (IsStatusPhrase(tokens))
            {
                parsedIntent = BuildIntent("status", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as status.";
                return true;
            }

            if (IsWhoAreYouPhrase(tokens))
            {
                parsedIntent = BuildIntent("who_are_you", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as who_are_you.";
                return true;
            }

            if (IsHelpPhrase(tokens))
            {
                parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as help.";
                return true;
            }

            bool showMovementPhrase = IsShowMovementPhrase(
                tokens,
                raw,
                compactTranscript,
                knownJointNames);
            if (showMovementPhrase)
            {
                parsedIntent = BuildIntent("show_movement", confidence, requiresConfirmation: true, raw);
                message = "Transcript parsed as show_movement.";
                return true;
            }

            bool likelyPoseCommand = IsLikelySetPosePhrase(tokens);
            if (TryResolvePoseFromTranscript(compactTranscript, knownPresetNames, out string poseName))
            {
                parsedIntent = BuildIntent("set_pose", confidence, requiresConfirmation: true, raw);
                parsedIntent.pose_name = poseName;
                message = $"Transcript parsed as set_pose ({poseName}).";
                return true;
            }

            if (TryResolveJointFromTranscript(
                    raw,
                    compactTranscript,
                    knownJointNames,
                    out string jointName,
                    out float jointDegrees,
                    out string jointParseMessage))
            {
                parsedIntent = BuildIntent("move_joint", confidence, requiresConfirmation: true, raw);
                parsedIntent.joint_name = jointName;
                parsedIntent.joint_degrees = jointDegrees;
                message = $"Transcript parsed as move_joint ({jointName} -> {jointDegrees:F1} deg).";
                return true;
            }
            if (!string.IsNullOrWhiteSpace(jointParseMessage))
            {
                parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
                message = $"Transcript routed to help: {jointParseMessage}";
                return true;
            }
            if (likelyPoseCommand)
            {
                parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
                message = "Transcript routed to help: pose command detected but no known pose name matched.";
                return true;
            }

            if (IsHelloPhrase(tokens))
            {
                parsedIntent = BuildIntent("hello", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as hello.";
                return true;
            }

            if (shortLoneWordHelpFallback)
            {
                parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as help (short lone-word fallback).";
                return true;
            }

            parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
            message = "Transcript routed to help (unrecognized command).";
            return true;
        }

        private static VoiceAgentIntent BuildIntent(
            string intentName,
            float confidence,
            bool requiresConfirmation,
            string spokenText)
        {
            return new VoiceAgentIntent
            {
                type = "robot_command",
                intent = intentName,
                confidence = confidence,
                requires_confirmation = requiresConfirmation,
                spoken_text = spokenText ?? string.Empty,
                raw_json = string.Empty
            };
        }

        private static bool TryResolvePoseFromTranscript(
            string compactTranscript,
            IReadOnlyList<string> knownPresetNames,
            out string poseName)
        {
            poseName = string.Empty;
            if (string.IsNullOrWhiteSpace(compactTranscript) || knownPresetNames == null || knownPresetNames.Count == 0)
            {
                return false;
            }

            int bestMatchLength = 0;
            for (int i = 0; i < knownPresetNames.Count; i++)
            {
                string candidate = knownPresetNames[i];
                string compactCandidate = BuildCompactToken(candidate);
                if (string.IsNullOrWhiteSpace(compactCandidate))
                {
                    continue;
                }

                if (compactTranscript.IndexOf(compactCandidate, StringComparison.Ordinal) >= 0)
                {
                    if (compactCandidate.Length > bestMatchLength)
                    {
                        bestMatchLength = compactCandidate.Length;
                        poseName = candidate;
                    }
                }
            }

            return bestMatchLength > 0;
        }

        private bool TryResolveJointFromTranscript(
            string transcript,
            string compactTranscript,
            IReadOnlyList<string> knownJointNames,
            out string jointName,
            out float jointDegrees,
            out string parseMessage)
        {
            jointName = string.Empty;
            jointDegrees = 0f;
            parseMessage = string.Empty;

            List<string> tokens = BuildWordTokens(transcript);
            bool likelyJointCommand = IsLikelyMoveJointPhrase(tokens);
            if (!likelyJointCommand)
            {
                return false;
            }

            if (!TryExtractFirstFloat(
                    transcript,
                    UseSafeNumericParsing,
                    RequireTargetTokenForJoint,
                    out float parsedDegrees))
            {
                parseMessage = UseSafeNumericParsing
                    ? "Move-joint command rejected: missing safe numeric target (use e.g. 'to 10 degrees')."
                    : string.Empty;
                return false;
            }

            float minDegrees = Mathf.Min(JointMinDegrees, JointMaxDegrees);
            float maxDegrees = Mathf.Max(JointMinDegrees, JointMaxDegrees);
            if (RejectOutOfRangeJointCommands &&
                (parsedDegrees < minDegrees || parsedDegrees > maxDegrees))
            {
                parseMessage = $"Move-joint command rejected: target {parsedDegrees:F1} deg is outside [{minDegrees:F1}, {maxDegrees:F1}] deg.";
                return false;
            }

            if (knownJointNames == null || knownJointNames.Count == 0)
            {
                return false;
            }

            int bestMatchLength = 0;
            for (int i = 0; i < knownJointNames.Count; i++)
            {
                string candidate = knownJointNames[i];
                string compactCandidate = BuildCompactToken(candidate);
                if (string.IsNullOrWhiteSpace(compactCandidate))
                {
                    continue;
                }

                if (compactTranscript.IndexOf(compactCandidate, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                if (compactCandidate.Length > bestMatchLength)
                {
                    bestMatchLength = compactCandidate.Length;
                    jointName = candidate;
                }
            }

            if (bestMatchLength <= 0)
            {
                return false;
            }

            jointDegrees = parsedDegrees;
            return true;
        }

        private bool IsShowMovementPhrase(
            IReadOnlyList<string> tokens,
            string transcript,
            string compactTranscript,
            IReadOnlyList<string> knownJointNames)
        {
            if (!ContainsAnyTokenSequence(tokens, ShowMovementSynonymTokenSets))
            {
                return false;
            }

            return !LooksLikeJointSpecificCommand(tokens, transcript, compactTranscript, knownJointNames);
        }

        private bool LooksLikeJointSpecificCommand(
            IReadOnlyList<string> tokens,
            string transcript,
            string compactTranscript,
            IReadOnlyList<string> knownJointNames)
        {
            bool hasJointVerb = IsLikelyMoveJointPhrase(tokens);
            if (!hasJointVerb)
            {
                return false;
            }

            bool mentionsKnownJoint = TranscriptMentionsKnownJoint(compactTranscript, knownJointNames);
            bool hasJointDescriptor =
                ContainsToken(tokens, "joint") ||
                ContainsToken(tokens, "degree") ||
                ContainsToken(tokens, "degrees") ||
                ContainsToken(tokens, "deg") ||
                ContainsToken(tokens, "shoulder") ||
                ContainsToken(tokens, "elbow") ||
                ContainsToken(tokens, "wrist") ||
                ContainsToken(tokens, "gripper") ||
                ContainsToken(tokens, "arm") ||
                ContainsToken(tokens, "neck") ||
                ContainsToken(tokens, "head");
            bool hasTargetAngle = TryExtractFirstFloat(
                transcript,
                UseSafeNumericParsing,
                RequireTargetTokenForJoint,
                out _);

            return mentionsKnownJoint || hasJointDescriptor || hasTargetAngle;
        }

        private static bool TryExtractFirstFloat(
            string text,
            bool safeMode,
            bool requireTargetToken,
            out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i] ?? string.Empty;
                string cleaned = CleanNumericToken(token);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                if (safeMode)
                {
                    string previous = i > 0 ? (parts[i - 1] ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
                    string next = i + 1 < parts.Length ? (parts[i + 1] ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
                    bool hasTargetToken = previous == "to" || previous == "at" || previous == "around";
                    bool hasDegreeToken =
                        previous == "degree" || previous == "degrees" || previous == "deg" ||
                        next == "degree" || next == "degrees" || next == "deg";

                    if (requireTargetToken && !hasTargetToken)
                    {
                        continue;
                    }

                    if (!hasTargetToken && !hasDegreeToken)
                    {
                        continue;
                    }
                }

                if (float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static string CleanNumericToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(token.Length);
            bool hasDecimal = false;

            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if ((c == '+' || c == '-') && builder.Length == 0)
                {
                    builder.Append(c);
                    continue;
                }

                if (char.IsDigit(c))
                {
                    builder.Append(c);
                    continue;
                }

                if ((c == '.' || c == ',') && !hasDecimal)
                {
                    builder.Append('.');
                    hasDecimal = true;
                }
            }

            return builder.ToString();
        }

        private static List<string> BuildWordTokens(string value)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return tokens;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }
            }

            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
            }

            return tokens;
        }

        private static string BuildCompactToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        private static bool TranscriptMentionsKnownJoint(
            string compactTranscript,
            IReadOnlyList<string> knownJointNames)
        {
            if (string.IsNullOrWhiteSpace(compactTranscript) || knownJointNames == null || knownJointNames.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < knownJointNames.Count; i++)
            {
                string compactJointName = BuildCompactToken(knownJointNames[i]);
                if (string.IsNullOrWhiteSpace(compactJointName))
                {
                    continue;
                }

                if (compactTranscript.IndexOf(compactJointName, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsToken(IReadOnlyList<string> tokens, string token)
        {
            if (tokens == null || tokens.Count == 0 || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], token, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAnyTokenSequence(
            IReadOnlyList<string> tokens,
            IReadOnlyList<string[]> tokenSequences)
        {
            if (tokens == null || tokens.Count == 0 || tokenSequences == null || tokenSequences.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < tokenSequences.Count; i++)
            {
                if (ContainsTokenSequence(tokens, tokenSequences[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsStopMotionPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, StopMotionSynonymTokenSets);
        }

        private static bool IsConfirmPendingPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, ConfirmPendingSynonymTokenSets);
        }

        private static bool IsRejectPendingPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, RejectPendingSynonymTokenSets);
        }

        private static bool IsDisconnectRobotPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, DisconnectRobotSynonymTokenSets);
        }

        private static bool IsConnectRobotPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, ConnectRobotSynonymTokenSets);
        }

        private static bool IsStatusPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, StatusSynonymTokenSets);
        }

        private static bool IsWhoAreYouPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, WhoAreYouSynonymTokenSets);
        }

        private static bool IsHelloPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, HelloSynonymTokenSets);
        }

        private static bool IsLikelyMoveJointPhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, MoveJointSynonymTokenSets);
        }

        private static bool IsLikelySetPosePhrase(IReadOnlyList<string> tokens)
        {
            return ContainsAnyTokenSequence(tokens, SetPoseSynonymTokenSets);
        }

        private static bool IsHelpPhrase(IReadOnlyList<string> tokens)
        {
            if (ContainsAnyTokenSequence(tokens, HelpSynonymTokenSets))
            {
                return true;
            }

            return
                (ContainsToken(tokens, "how") && (ContainsToken(tokens, "use") || ContainsToken(tokens, "do"))) ||
                (ContainsToken(tokens, "what") && ContainsToken(tokens, "say"));
        }

        private static bool ContainsTokenSequence(IReadOnlyList<string> tokens, IReadOnlyList<string> sequence)
        {
            if (tokens == null || sequence == null || sequence.Count == 0 || tokens.Count < sequence.Count)
            {
                return false;
            }

            for (int i = 0; i <= tokens.Count - sequence.Count; i++)
            {
                bool matches = true;
                for (int j = 0; j < sequence.Count; j++)
                {
                    if (!string.Equals(tokens[i + j], sequence[j], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsShortConfirmOrReject(string rawTranscript, IReadOnlyList<string> tokens)
        {
            string trimmed = (rawTranscript ?? string.Empty).Trim().ToLowerInvariant();
            if (trimmed == "yes" || trimmed == "no")
            {
                return true;
            }

            return tokens != null &&
                tokens.Count == 1 &&
                (ContainsToken(tokens, "yes") || ContainsToken(tokens, "no"));
        }

        private static bool IsShortLoneWordHelpFallbackCandidate(string rawTranscript, IReadOnlyList<string> tokens)
        {
            if (tokens == null || tokens.Count != 1)
            {
                return false;
            }

            if (IsShortConfirmOrReject(rawTranscript, tokens))
            {
                return false;
            }

            string token = tokens[0]?.Trim() ?? string.Empty;
            return token.Length > 0 && token.Length <= ShortLoneWordHelpFallbackMaxChars;
        }

        private static float ResolveConfidence(float value)
        {
            if (value <= 0f)
            {
                return DefaultTranscriptConfidence;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }
    }
}
