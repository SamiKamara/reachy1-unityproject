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

            if (ContainsToken(tokens, "cancel") ||
                ContainsToken(tokens, "halt") ||
                ContainsToken(tokens, "abort") ||
                ContainsToken(tokens, "stop"))
            {
                parsedIntent = BuildIntent("stop_motion", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as stop_motion.";
                return true;
            }

            bool confirmPhrase =
                ContainsToken(tokens, "confirm") ||
                ContainsToken(tokens, "approved") ||
                ContainsToken(tokens, "approve") ||
                ContainsToken(tokens, "proceed") ||
                ContainsToken(tokens, "yes") ||
                (ContainsToken(tokens, "go") && ContainsToken(tokens, "ahead")) ||
                (ContainsToken(tokens, "execute") && ContainsToken(tokens, "it"));
            if (confirmPhrase)
            {
                parsedIntent = BuildIntent("confirm_pending", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as confirm_pending.";
                return true;
            }

            bool rejectPhrase =
                ContainsToken(tokens, "reject") ||
                ContainsToken(tokens, "decline") ||
                ContainsToken(tokens, "deny") ||
                ContainsToken(tokens, "no") ||
                (ContainsToken(tokens, "do") && ContainsToken(tokens, "not"));
            if (rejectPhrase)
            {
                parsedIntent = BuildIntent("reject_pending", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as reject_pending.";
                return true;
            }

            if (ContainsToken(tokens, "disconnect") ||
                (ContainsToken(tokens, "go") && ContainsToken(tokens, "offline")))
            {
                parsedIntent = BuildIntent("disconnect_robot", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as disconnect_robot.";
                return true;
            }

            if (ContainsToken(tokens, "connect") ||
                ContainsToken(tokens, "reconnect"))
            {
                parsedIntent = BuildIntent("connect_robot", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as connect_robot.";
                return true;
            }

            if (ContainsToken(tokens, "status") ||
                (ContainsToken(tokens, "connected") && (ContainsToken(tokens, "are") || ContainsToken(tokens, "is"))))
            {
                parsedIntent = BuildIntent("status", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as status.";
                return true;
            }

            if (ContainsToken(tokens, "help") ||
                (ContainsToken(tokens, "how") && (ContainsToken(tokens, "use") || ContainsToken(tokens, "do"))) ||
                (ContainsToken(tokens, "what") && ContainsToken(tokens, "say")))
            {
                parsedIntent = BuildIntent("help", confidence, requiresConfirmation: false, raw);
                message = "Transcript parsed as help.";
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

            if (TryResolvePoseFromTranscript(compactTranscript, knownPresetNames, out string poseName))
            {
                parsedIntent = BuildIntent("set_pose", confidence, requiresConfirmation: true, raw);
                parsedIntent.pose_name = poseName;
                message = $"Transcript parsed as set_pose ({poseName}).";
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
            bool likelyJointCommand =
                ContainsToken(tokens, "move") ||
                ContainsToken(tokens, "set") ||
                ContainsToken(tokens, "turn") ||
                ContainsToken(tokens, "rotate") ||
                ContainsToken(tokens, "bend") ||
                ContainsToken(tokens, "joint");
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
