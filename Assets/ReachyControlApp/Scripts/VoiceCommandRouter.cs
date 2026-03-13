using System;
using System.Collections.Generic;

namespace Reachy.ControlApp
{
    public sealed class VoiceCommandRouter
    {
        public const float DefaultConfidenceThreshold = 0.78f;
        public const string HelloPoseName = "Hello Pose C";
        public const string HelloResponseText = "hello there how may i assist you";
        public const string WhoAreYouResponseText = "i am reachy's local ai assistant for voice robot control";

        public enum VoiceActionKind
        {
            None = 0,
            Help = 1,
            Status = 2,
            SetPose = 3,
            MoveJoint = 4,
            StopMotion = 5,
            ConnectRobot = 6,
            DisconnectRobot = 7,
            ConfirmPending = 8,
            RejectPending = 9,
            ShowMovement = 10,
            Hello = 11,
            WhoAreYou = 12
        }

        public struct RoutedAction
        {
            public VoiceActionKind Kind;
            public string PoseName;
            public string JointName;
            public float JointDegrees;
            public bool RequiresConfirmation;
            public string Summary;
        }

        public float ConfidenceThreshold { get; set; } = DefaultConfidenceThreshold;

        public bool TryRoute(
            VoiceAgentIntent intent,
            IReadOnlyList<string> knownPresetNames,
            out RoutedAction action,
            out string message)
        {
            action = default(RoutedAction);
            message = string.Empty;

            if (intent == null)
            {
                message = "Voice intent is null.";
                return false;
            }

            string intentName = (intent.intent ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(intentName))
            {
                message = "Voice intent did not include an intent type.";
                return false;
            }

            bool confidenceProvided = intent.confidence > 0f;
            bool lowConfidence = confidenceProvided && intent.confidence < ConfidenceThreshold;

            switch (intentName)
            {
                case "help":
                    action.Kind = VoiceActionKind.Help;
                    action.Summary = "Voice help requested.";
                    break;
                case "none":
                    action.Kind = VoiceActionKind.None;
                    action.Summary = string.IsNullOrWhiteSpace(intent.reply_text)
                        ? "Online reply with no robot action."
                        : intent.reply_text.Trim();
                    break;
                case "status":
                    action.Kind = VoiceActionKind.Status;
                    action.Summary = "Voice status requested.";
                    break;
                case "set_pose":
                    if (!TryResolvePose(intent.pose_name, knownPresetNames, out string resolvedPose))
                    {
                        message = $"Unknown pose '{intent.pose_name}'.";
                        return false;
                    }

                    action.Kind = VoiceActionKind.SetPose;
                    action.PoseName = resolvedPose;
                    action.Summary = $"Set pose '{resolvedPose}'.";
                    break;
                case "move_joint":
                    if (string.IsNullOrWhiteSpace(intent.joint_name))
                    {
                        message = "Move-joint intent is missing joint name.";
                        return false;
                    }

                    action.Kind = VoiceActionKind.MoveJoint;
                    action.JointName = intent.joint_name.Trim();
                    action.JointDegrees = intent.joint_degrees;
                    action.Summary = $"Move joint '{action.JointName}' to {action.JointDegrees:F1} deg.";
                    break;
                case "show_movement":
                    action.Kind = VoiceActionKind.ShowMovement;
                    action.Summary = "Show movement sequence: 3 random poses at 4-second intervals.";
                    break;
                case "hello":
                    action.Kind = VoiceActionKind.Hello;
                    action.PoseName = HelloPoseName;
                    action.Summary = HelloResponseText;
                    break;
                case "who_are_you":
                    action.Kind = VoiceActionKind.WhoAreYou;
                    action.Summary = WhoAreYouResponseText;
                    break;
                case "stop_motion":
                    action.Kind = VoiceActionKind.StopMotion;
                    action.Summary = "Stop robot motion.";
                    break;
                case "connect_robot":
                    action.Kind = VoiceActionKind.ConnectRobot;
                    action.Summary = "Connect to selected robot endpoint.";
                    break;
                case "disconnect_robot":
                    action.Kind = VoiceActionKind.DisconnectRobot;
                    action.Summary = "Disconnect current robot connection.";
                    break;
                case "confirm_pending":
                    action.Kind = VoiceActionKind.ConfirmPending;
                    action.Summary = "Confirm pending voice action.";
                    break;
                case "reject_pending":
                    action.Kind = VoiceActionKind.RejectPending;
                    action.Summary = "Reject pending voice action.";
                    break;
                default:
                    message = $"Unsupported voice intent '{intentName}'.";
                    return false;
            }

            bool motionIntentRequiringConfirm = action.Kind == VoiceActionKind.SetPose ||
                action.Kind == VoiceActionKind.MoveJoint ||
                action.Kind == VoiceActionKind.ShowMovement;
            action.RequiresConfirmation = motionIntentRequiringConfirm || intent.requires_confirmation || lowConfidence;
            if (action.Kind == VoiceActionKind.StopMotion)
            {
                action.RequiresConfirmation = false;
            }
            if (action.Kind == VoiceActionKind.ConfirmPending || action.Kind == VoiceActionKind.RejectPending)
            {
                action.RequiresConfirmation = false;
            }
            if (action.Kind == VoiceActionKind.Hello)
            {
                action.RequiresConfirmation = false;
            }
            if (action.Kind == VoiceActionKind.WhoAreYou)
            {
                action.RequiresConfirmation = false;
            }
            if (action.Kind == VoiceActionKind.None)
            {
                action.RequiresConfirmation = false;
            }

            if (!string.IsNullOrWhiteSpace(intent.spoken_text) &&
                action.Kind != VoiceActionKind.None)
            {
                action.Summary += $" Heard: \"{intent.spoken_text}\".";
            }

            message = action.RequiresConfirmation
                ? $"{action.Summary} Confirmation required."
                : action.Summary;
            return true;
        }

        private static bool TryResolvePose(
            string requestedPoseName,
            IReadOnlyList<string> knownPresetNames,
            out string resolvedPoseName)
        {
            resolvedPoseName = string.Empty;
            if (string.IsNullOrWhiteSpace(requestedPoseName) || knownPresetNames == null)
            {
                return false;
            }

            string requested = requestedPoseName.Trim();
            for (int i = 0; i < knownPresetNames.Count; i++)
            {
                string candidate = knownPresetNames[i];
                if (string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedPoseName = candidate;
                    return true;
                }
            }

            string normalizedRequested = NormalizeToken(requested);
            if (string.IsNullOrEmpty(normalizedRequested))
            {
                return false;
            }

            for (int i = 0; i < knownPresetNames.Count; i++)
            {
                string candidate = knownPresetNames[i];
                if (NormalizeToken(candidate) == normalizedRequested)
                {
                    resolvedPoseName = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = new List<char>(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    chars.Add(char.ToLowerInvariant(c));
                }
            }

            return new string(chars.ToArray());
        }
    }
}
