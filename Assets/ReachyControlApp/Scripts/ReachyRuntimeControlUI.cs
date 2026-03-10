using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Reachy.ControlApp
{
    public class ReachyRuntimeControlUI : MonoBehaviour
    {
        private enum RuntimeMenuView
        {
            General = 0,
            AI = 1,
            AnimationsAndPoses = 2,
            ManualControl = 3,
            Teleoperation = 4,
            Connections = 5
        }

        private static readonly string[] RuntimeMenuViewLabels =
        {
            "General",
            "AI",
            "Animations & Poses",
            "Manual Control",
            "Teleoperation",
            "Connections"
        };
        private const float DesignMarginPixels = 10f;
        private const float DesignPanelGap = 10f;
        private const float DesignViewTopBarHeight = 42f;
        private const float DesignViewTopBarGap = 8f;
        private const float DesignMinimumTopBarWidth = 1220f;
        private const float DesignTopBarGroupGap = 24f;
        private const float DesignAiPanelWidthRatio = 0.32f;
        private const float DesignAiPanelMinWidth = 340f;
        private const float DesignAiPanelMaxWidth = 620f;
        private const float DesignAiCenterOpenMinWidth = 320f;
        private const float DesignAiMiddleSpaceFactor = 0.64f;
        private const float DesignLeftPanelWidth = 500f;
        private const float DesignRightPanelWidth = 430f;
        private const float DesignExpandedPanelHeight = 640f;
        private const float DesignCollapsedPanelHeight = 95f;
        private const float ExpandedLeftPanelWidthRatio = 0.36f;
        private const float ExpandedRightPanelWidthRatio = 0.36f;
        private const float DesignLocalAgentCollapsedHeight = 64f;
        private const float DesignCameraPanelWidth = 560f;
        private const float DesignCameraPanelHeight = 265f;
        private const int VoiceShowMovementPoseCount = 3;
        private const float VoiceShowMovementIntervalSeconds = 4f;
        private const float VoiceHelloReturnDelaySeconds = 4f;
        private const string VoiceHelloReturnPoseName = "Neutral Arms";
        private const int RuntimeRunLogHistoryCount = 5;
        private const string RuntimeRunLogFilePrefix = "runtime_run_";
        private const string LocalAiAgentActivationAnnouncement =
            "Local AI agent is now active. Use voice commands to control Reachy or ask for help.";
        private static readonly string[] DefaultSidecarKnownPoses =
        {
            "Neutral Arms",
            "T-Pose",
            "Tray Holding",
            "Hello Pose A",
            "Hello Pose B",
            "Hello Pose C",
            "Hello Pose D"
        };
        private static readonly string[] DefaultSidecarKnownJoints =
        {
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
            "l_gripper"
        };
        private static readonly string[] DefaultSidecarShowMovementSynonyms =
        {
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
            "surprise me"
        };
        private static readonly string[] DefaultSidecarHelpSynonyms =
        {
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
            "help with commands"
        };
        private static readonly string[] DefaultSidecarHelloSynonyms =
        {
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
            "do a greeting"
        };
        private static readonly string[] DefaultSidecarWhoAreYouSynonyms =
        {
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
            "tell me about yourself"
        };

        [Header("Endpoints")]
        [SerializeField] private string simulationHost = "localhost";
        [SerializeField] private int simulationPort = 50055;
        [SerializeField] private string robotHost = "192.168.1.109";
        [SerializeField] private int robotPort = 50055;

        [Header("Runtime")]
        [SerializeField] private ReachyControlMode mode = ReachyControlMode.Simulation;
        [SerializeField] private bool startCollapsed;

        [Header("Automation")]
        [SerializeField] private bool autoConnectOnPlay = true;
        [SerializeField] private bool autoReconnect = true;
        [SerializeField] private bool allowRestartSignalRecovery = true;
        [SerializeField] private string robotFallbackHostsCsv = string.Empty;
        [SerializeField] private string robotFallbackPortsCsv = "3972";
        [SerializeField] private int connectAttemptsPerHost = 3;
        [SerializeField] private float retryDelaySeconds = 1.0f;
        [SerializeField] private float grpcConnectTimeoutSeconds = 3.0f;
        [SerializeField] private float presetPoseTransitionSpeedScale = 0.6f;
        [SerializeField] private float postRestartWaitSeconds = 2.5f;
        [SerializeField] private float healthCheckIntervalSeconds = 2.0f;
        [SerializeField] private float reconnectCooldownSeconds = 4.0f;
        [SerializeField] private int robotRestartPort = 50059;
        [SerializeField] private bool resolveRobotHostnames = true;
        [SerializeField] private bool precheckRobotEndpointReachability = true;
        [SerializeField] private float precheckTimeoutSeconds = 1.5f;
        
        [Header("Camera Preview")]
        [SerializeField] private bool showCameraPreview = true;
        [SerializeField] private int simulationCameraPort = 50057;
        [SerializeField] private int robotCameraPort = 50057;
        [SerializeField] private float cameraRefreshIntervalSeconds = 0.2f;
        [SerializeField] private float cameraRpcTimeoutSeconds = 0.8f;
        [SerializeField] private bool cameraUseRightEye;

        [Header("Local AI Agent")]
        [SerializeField] private bool enableLocalAiAgent = false;
        [SerializeField] private bool localAiAgentPanelExpanded = false;
        [SerializeField] private string localAiAgentEndpoint = VoiceAgentBridge.DefaultEndpoint;
        [SerializeField] private float localAiAgentPollIntervalSeconds = 0.5f;
        [SerializeField] private float localAiAgentConfidenceThreshold = VoiceCommandRouter.DefaultConfidenceThreshold;
        [SerializeField] private bool localAiAgentEnableTranscriptParser = true;
        [SerializeField] private float localAiAgentTranscriptConfidence = 0.9f;
        [SerializeField] private int localAiAgentMinTranscriptChars = 4;
        [SerializeField] private int localAiAgentMinTranscriptWords = 1;
        [SerializeField] private bool localAiAgentUseSafeNumericParsing = true;
        [SerializeField] private bool localAiAgentRequireTargetTokenForJoint;
        [SerializeField] private bool localAiAgentRejectOutOfRangeJointCommands = true;
        [SerializeField] private float localAiAgentJointMinDegrees = -180f;
        [SerializeField] private float localAiAgentJointMaxDegrees = 180f;
        [SerializeField] private bool localAiAgentSuppressDuplicateCommands = true;
        [SerializeField] private float localAiAgentDuplicateCommandWindowSeconds = 1.25f;
        [SerializeField] private string localAiAgentTranscriptInput = "set neutral arms pose";
        [SerializeField] private bool localAiAgentMockTranscriptIsFinal = true;
        [SerializeField] private bool localAiAgentSimulationOnlyMode;
        [SerializeField] private bool localAiAgentEnableTtsFeedback = true;
        [SerializeField] private string localAiAgentTtsEndpoint = VoiceAgentBridge.DefaultTtsEndpoint;
        [SerializeField] private float localAiAgentTtsMinIntervalSeconds = 0.35f;
        [SerializeField] private bool localAiAgentEnablePushToTalk;
        [SerializeField] private string localAiAgentPushToTalkKey = "V";
        [SerializeField] private bool localAiAgentListeningEnabled = true;
        [SerializeField] private string localAiAgentListeningEndpoint = VoiceAgentBridge.DefaultListeningEndpoint;
        [SerializeField] private string localAiAgentPreferredMicrophoneDeviceName = string.Empty;
        [SerializeField] private bool localAiAgentIgnoreVirtualMicrophones = true;
        [SerializeField] private bool localAiAgentAutoStartSidecar = true;
        [SerializeField] private bool localAiAgentAutoStopAutoStartedSidecarOnDisable = true;
        [SerializeField] private bool localAiAgentSyncSidecarConfigOnStart = true;
        [SerializeField] private string localAiAgentSidecarPythonCommand = "python";
        [SerializeField] private string localAiAgentSidecarScriptRelativePath =
            "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py";
        [SerializeField] private string localAiAgentSidecarConfigRelativePath =
            "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json";
        [SerializeField] private string localAiAgentSidecarLogLevel = "warning";
        [SerializeField] private float localAiAgentSidecarStartupTimeoutSeconds = 8f;
        [SerializeField] private float localAiAgentSidecarRetryIntervalSeconds = 2f;
        [SerializeField] private int localAiAgentSidecarHealthTimeoutMs = 900;
        [SerializeField] private float localAiAgentHeartbeatTimeoutSeconds = 4f;
        [SerializeField] private float localAiAgentRetryBackoffMinSeconds = 0.4f;
        [SerializeField] private float localAiAgentRetryBackoffMaxSeconds = 4f;
        [SerializeField] private int localAiAgentDegradedFailureThreshold = 5;
        [SerializeField] private bool localAiAgentBlockMotionWhenBridgeUnhealthy = true;
        [SerializeField] private bool localAiAgentEnableLocalHelpModel = true;
        [SerializeField] private string localAiAgentHelpEndpoint = VoiceAgentBridge.DefaultHelpEndpoint;
        [SerializeField] private string localAiAgentHelpContext = "Reachy Unity app guidance only.";
        [SerializeField] private string localAiAgentHelpModelBackend = "llama_cpp";
        [SerializeField] private string localAiAgentHelpModelPath = string.Empty;
        [SerializeField] private int localAiAgentHelpModelMaxTokens = 96;
        [SerializeField] private float localAiAgentHelpModelTemperature = 0.2f;
        [SerializeField] private int localAiAgentHelpMaxAnswerChars = 360;
        [SerializeField] private bool localAiAgentShowRawIntentPayload;

        [Header("Single Joint Command")]
        [SerializeField] private string jointName = "r_shoulder_pitch";
        [SerializeField] private string goalDegrees = "0";
        
        [Header("Window Controls")]
        [SerializeField] private int windowedWidth = 1280;
        [SerializeField] private int windowedHeight = 720;
        [SerializeField] private bool runtimeFileLoggingEnabled = false;

        private ReachyGrpcClient _client;
        private string _status = "Idle.";
        private readonly object _runtimeLogGate = new object();
        private string _runtimeLogsDirectory = string.Empty;
        private string _runtimeSessionLogPath = string.Empty;
        private StreamWriter _runtimeLogWriter;
        private bool _runtimeLogSessionInitialized;
        private Vector2 _leftPanelScroll;
        private Vector2 _jointScroll;
        private bool _collapsed;
        private RuntimeMenuView _activeMenuView = RuntimeMenuView.General;
        private GUIStyle _titleStyle;
        private float _nextHealthCheckAt;
        private float _nextAutoReconnectAt;
        private bool _autoReconnectScheduled;
        private bool _initialAutoConnectAttempted;
        private bool _manualDisconnect;
        private ReachyControlMode? _connectedMode;
        private ReachyControlMode _autoReconnectMode;
        private bool _isConnectAttemptInProgress;
        private ReachyControlMode _connectAttemptMode;
        private float _connectAttemptReconnectDelaySeconds = 0.5f;
        private Coroutine _connectAttemptCoroutine;
        private Coroutine _voiceShowMovementCoroutine;
        private Coroutine _voiceHelloReturnCoroutine;
        private float _uiScale = 1f;
        private string _windowedWidthText;
        private string _windowedHeightText;
        private float _nextCameraFetchAt;
        private Task<ReachyGrpcClient.CameraImageFetchResult> _cameraFetchTask;
        private Texture2D _cameraPreviewTexture;
        private string _cameraPreviewStatus = "No camera frame yet.";
        private ReachyControlMode _cameraPreviewMode;
        private string _cameraPreviewHost = string.Empty;
        private int _cameraPreviewPort;
        private Vector2 _localAgentScroll;
        private Vector2 _connectionsScroll;
        private Vector2 _manualControlScroll;
        private Vector2 _animationsAndPosesScroll;
        private Vector2 _aiPrimaryScroll;
        private Vector2 _aiRuntimeScroll;
        private VoiceAgentBridge _voiceAgentBridge;
        private VoiceCommandRouter _voiceCommandRouter;
        private VoiceTranscriptIntentParser _voiceTranscriptParser;
        private VoiceAgentStatusPanel.State _voiceAgentStatusState;
        private bool _voiceHasPendingAction;
        private VoiceCommandRouter.RoutedAction _voicePendingAction;
        private string _voiceLastTranscript = "No transcripts yet.";
        private string _voiceLastParserMessage = "Parser idle.";
        private string _voiceLastIntentSummary = "No voice intents yet.";
        private string _voiceLastActionResult = "Idle.";
        private string _voiceLastSpokenFeedback = string.Empty;
        private string _voiceLastRawIntentPayload = string.Empty;
        private string _voiceLastLogExportPath = string.Empty;
        private int _voiceLastHandledHelpSuccessCount;
        private int _voiceLastHandledListeningSuccessCount;
        private int _voiceLastHandledListeningFailureCount;
        private string _voiceLastHelpAnswer = string.Empty;
        private float _voiceNextAllowedTtsAt;
        private bool? _voiceLastRequestedListeningEnabled;
        private string _voiceLastCommandFingerprint = string.Empty;
        private float _voiceLastCommandAt;
        private bool _voiceWarnedSttInactive;
        private bool _localAiMicDropdownOpen;
        private Vector2 _localAiMicDropdownScroll;
        private AudioSource _localAiMicTestAudioSource;
        private AudioClip _localAiMicTestRecordClip;
        private bool _localAiMicTestRecording;
        private bool _localAiMicTestButtonHeld;
        private string _localAiMicTestDevice = string.Empty;
        private bool _localAiAgentSidecarReady;
        private bool _localAiAgentSidecarStartupActive;
        private bool _localAiAgentSidecarStartedByUi;
        private bool _localAiAgentSidecarInitialProbeCompleted;
        private string _localAiAgentSidecarLastStartError = string.Empty;
        private bool _localAiAgentActivationAnnouncementPending;
        private bool _localAiAgentWasEnabledLastFrame;
        private float _localAiAgentSidecarStartupStartedAt;
        private float _localAiAgentSidecarNextStartAttemptAt;
        private float _localAiAgentSidecarNextProbeAt;
        private string _localAiAgentSidecarStatus = "Sidecar auto-start idle.";
        private System.Diagnostics.Process _localAiAgentSidecarProcess;
        private Task<SidecarProbeResult> _localAiAgentSidecarProbeTask;
        private bool _sidecarShutdownCleanupDone;

        private struct SidecarProbeResult
        {
            public bool Success;
            public bool Reachable;
            public string Message;
            public string Error;
        }

        [Serializable]
        private sealed class SidecarHealthEnvelope
        {
            public bool ok;
            public bool mic_active;
            public bool listening;
            public int selected_input_device_index = -1;
            public string selected_input_device_name = string.Empty;
            public string last_error = string.Empty;
        }

        [Serializable]
        private sealed class VoiceAgentConfig
        {
            public string stt_backend = "vosk";
            public string model_path = string.Empty;
            public float intent_confidence_threshold = VoiceCommandRouter.DefaultConfidenceThreshold;
            public float transcript_default_confidence = 0.85f;
            public bool transcript_parser_enabled = true;
            public int min_transcript_chars = 4;
            public int min_transcript_words = 1;
            public bool safe_numeric_parsing = true;
            public bool require_target_token_for_joint = false;
            public bool reject_out_of_range_joint_commands = true;
            public float joint_min_degrees = -180f;
            public float joint_max_degrees = 180f;
            public bool suppress_duplicate_commands = true;
            public float duplicate_command_window_seconds = 1.25f;
            public bool simulation_only_mode = false;
            public bool tts_feedback_enabled = true;
            public string tts_endpoint = VoiceAgentBridge.DefaultTtsEndpoint;
            public float tts_min_interval_seconds = 0.35f;
            public float heartbeat_timeout_seconds = 4f;
            public float retry_backoff_min_seconds = 0.4f;
            public float retry_backoff_max_seconds = 4f;
            public int degraded_failure_threshold = 5;
            public bool block_motion_when_bridge_unhealthy = true;
            public bool push_to_talk_enabled = false;
            public string push_to_talk_key = "V";
            public bool listening_enabled = true;
            public string listening_endpoint = VoiceAgentBridge.DefaultListeningEndpoint;
            public string preferred_microphone_device_name = string.Empty;
            public bool ignore_virtual_microphone_devices = true;
            public bool auto_start_sidecar = true;
            public bool auto_stop_autostarted_sidecar_on_disable = true;
            public bool sidecar_sync_config_on_start = true;
            public string sidecar_python_command = "python";
            public string sidecar_script_relative_path =
                "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py";
            public string sidecar_config_relative_path =
                "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json";
            public string sidecar_log_level = "warning";
            public float sidecar_startup_timeout_seconds = 8f;
            public float sidecar_retry_interval_seconds = 2f;
            public int sidecar_health_timeout_ms = 900;
            public bool local_help_model_enabled = true;
            public string help_endpoint = VoiceAgentBridge.DefaultHelpEndpoint;
            public string help_context = "Reachy Unity app guidance only.";
            public string help_model_backend = "llama_cpp";
            public string help_model_path = ".local_voice_models/llm/SmolLM2-360M-Instruct-Q4_K_M.gguf";
            public int help_model_max_tokens = 96;
            public float help_model_temperature = 0.2f;
            public int help_max_answer_chars = 360;
            public string ipc_endpoint = VoiceAgentBridge.DefaultEndpoint;
        }

        [Serializable]
        private sealed class LocalVoiceAgentSidecarConfig
        {
            public string bind_host = "127.0.0.1";
            public int bind_port = 8099;
            public string stt_backend = "vosk";
            public string stt_model_path = "../../../.local_voice_models/vosk-model-small-en-us-0.15";
            public int stt_sample_rate_hz = 16000;
            public float transcript_default_confidence = 0.85f;
            public float intent_confidence_threshold = VoiceCommandRouter.DefaultConfidenceThreshold;
            public string tts_backend = "pyttsx3";
            public int tts_rate = 175;
            public string tts_voice_name = string.Empty;
            public string[] known_poses = DefaultSidecarKnownPoses;
            public string[] known_joints = DefaultSidecarKnownJoints;
            public string[] show_movement_synonyms = DefaultSidecarShowMovementSynonyms;
            public string[] help_synonyms = DefaultSidecarHelpSynonyms;
            public string[] hello_synonyms = DefaultSidecarHelloSynonyms;
            public string[] who_are_you_synonyms = DefaultSidecarWhoAreYouSynonyms;
            public int log_history_size = 200;
            public int event_queue_size = 120;
            public string help_context = "Reachy Unity app guidance only.";
            public string help_model_backend = "llama_cpp";
            public string help_model_path = "../../../.local_voice_models/llm/SmolLM2-360M-Instruct-Q4_K_M.gguf";
            public int help_model_max_tokens = 96;
            public float help_model_temperature = 0.2f;
            public int help_max_answer_chars = 360;
            public string audio_input_device_name = string.Empty;
            public bool prefer_non_virtual_input_device = true;
            public bool start_listening_enabled = true;
            public int min_transcript_chars = 4;
            public int min_transcript_words = 1;
            public bool safe_numeric_parsing = true;
            public bool require_target_token_for_joint = false;
            public bool reject_out_of_range_joint_commands = true;
            public float joint_min_degrees = -180f;
            public float joint_max_degrees = 180f;
        }

        private void Awake()
        {
            _client = new ReachyGrpcClient();
            _voiceAgentBridge = new VoiceAgentBridge();
            _voiceCommandRouter = new VoiceCommandRouter();
            _voiceTranscriptParser = new VoiceTranscriptIntentParser();
            localAiAgentPanelExpanded = false;
            _collapsed = startCollapsed;
            _autoReconnectMode = mode;
            windowedWidth = Mathf.Clamp(windowedWidth, 320, 7680);
            windowedHeight = Mathf.Clamp(windowedHeight, 240, 4320);
            simulationCameraPort = Mathf.Clamp(simulationCameraPort, 1, 65535);
            robotCameraPort = Mathf.Clamp(robotCameraPort, 1, 65535);
            presetPoseTransitionSpeedScale = Mathf.Clamp(presetPoseTransitionSpeedScale, 0.05f, 2.0f);
            _client.PoseTransitionSpeedScale = presetPoseTransitionSpeedScale;
            cameraRefreshIntervalSeconds = Mathf.Max(0.05f, cameraRefreshIntervalSeconds);
            cameraRpcTimeoutSeconds = Mathf.Max(0.2f, cameraRpcTimeoutSeconds);
            localAiAgentPollIntervalSeconds = Mathf.Max(0.1f, localAiAgentPollIntervalSeconds);
            localAiAgentConfidenceThreshold = Mathf.Clamp01(localAiAgentConfidenceThreshold);
            localAiAgentTranscriptConfidence = Mathf.Clamp01(localAiAgentTranscriptConfidence);
            float normalizedJointMin = Mathf.Min(localAiAgentJointMinDegrees, localAiAgentJointMaxDegrees);
            float normalizedJointMax = Mathf.Max(localAiAgentJointMinDegrees, localAiAgentJointMaxDegrees);
            localAiAgentJointMinDegrees = normalizedJointMin;
            localAiAgentJointMaxDegrees = normalizedJointMax;
            localAiAgentDuplicateCommandWindowSeconds =
                Mathf.Max(0.05f, localAiAgentDuplicateCommandWindowSeconds);
            localAiAgentTtsMinIntervalSeconds = Mathf.Max(0.05f, localAiAgentTtsMinIntervalSeconds);
            localAiAgentHeartbeatTimeoutSeconds = Mathf.Max(0.5f, localAiAgentHeartbeatTimeoutSeconds);
            localAiAgentRetryBackoffMinSeconds = Mathf.Max(0.05f, localAiAgentRetryBackoffMinSeconds);
            localAiAgentRetryBackoffMaxSeconds = Mathf.Max(
                localAiAgentRetryBackoffMinSeconds,
                localAiAgentRetryBackoffMaxSeconds);
            localAiAgentDegradedFailureThreshold = Mathf.Max(1, localAiAgentDegradedFailureThreshold);
            localAiAgentHelpModelBackend = NormalizeHelpModelBackend(localAiAgentHelpModelBackend);
            localAiAgentHelpModelMaxTokens = Mathf.Clamp(localAiAgentHelpModelMaxTokens, 16, 512);
            localAiAgentHelpModelTemperature = Mathf.Clamp(localAiAgentHelpModelTemperature, 0f, 1.5f);
            localAiAgentHelpMaxAnswerChars = Mathf.Clamp(localAiAgentHelpMaxAnswerChars, 80, 1200);
            if (string.IsNullOrWhiteSpace(localAiAgentTtsEndpoint))
            {
                localAiAgentTtsEndpoint = VoiceAgentBridge.DefaultTtsEndpoint;
            }
            if (string.IsNullOrWhiteSpace(localAiAgentListeningEndpoint))
            {
                localAiAgentListeningEndpoint = VoiceAgentBridge.DefaultListeningEndpoint;
            }
            if (string.IsNullOrWhiteSpace(localAiAgentHelpEndpoint))
            {
                localAiAgentHelpEndpoint = VoiceAgentBridge.DefaultHelpEndpoint;
            }
            if (string.IsNullOrWhiteSpace(localAiAgentPushToTalkKey))
            {
                localAiAgentPushToTalkKey = "V";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarPythonCommand))
            {
                localAiAgentSidecarPythonCommand = "python";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarScriptRelativePath))
            {
                localAiAgentSidecarScriptRelativePath =
                    "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarConfigRelativePath))
            {
                localAiAgentSidecarConfigRelativePath =
                    "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarLogLevel))
            {
                localAiAgentSidecarLogLevel = "warning";
            }
            localAiAgentSidecarStartupTimeoutSeconds = Mathf.Max(1f, localAiAgentSidecarStartupTimeoutSeconds);
            localAiAgentSidecarRetryIntervalSeconds = Mathf.Max(0.25f, localAiAgentSidecarRetryIntervalSeconds);
            localAiAgentSidecarHealthTimeoutMs = Mathf.Clamp(localAiAgentSidecarHealthTimeoutMs, 200, 10000);
            bool loadedVoiceConfig = TryLoadVoiceAgentConfigFromDisk(out string startupVoiceConfigMessage);
            if (loadedVoiceConfig)
            {
                _voiceLastActionResult = startupVoiceConfigMessage;
            }
            else if (!string.IsNullOrWhiteSpace(startupVoiceConfigMessage) &&
                startupVoiceConfigMessage.IndexOf("not found", StringComparison.OrdinalIgnoreCase) < 0)
            {
                _voiceLastParserMessage = startupVoiceConfigMessage;
            }
            EnsurePreferredMicrophoneDevice();
            EnsureMicTestAudioSource();
            _windowedWidthText = windowedWidth.ToString(CultureInfo.InvariantCulture);
            _windowedHeightText = windowedHeight.ToString(CultureInfo.InvariantCulture);
            RefreshVoiceAgentStatusState();
            if (runtimeFileLoggingEnabled)
            {
                InitializeRuntimeLogSession();
                LogRuntimeEvent(
                    "lifecycle",
                    "awake",
                    $"mode={GetModeLabel(mode)}; autoConnectOnPlay={autoConnectOnPlay}; autoReconnect={autoReconnect}; runtimeFileLoggingEnabled={runtimeFileLoggingEnabled}.",
                    "INFO");
            }
        }

        private void OnDestroy()
        {
            if (_connectAttemptCoroutine != null)
            {
                StopCoroutine(_connectAttemptCoroutine);
                _connectAttemptCoroutine = null;
            }

            StopVoiceShowMovementSequence(updateStatus: false, reason: "UI destroyed");
            StopVoiceHelloReturnTimer(updateStatus: false, reason: "UI destroyed");

            _cameraFetchTask = null;
            if (_cameraPreviewTexture != null)
            {
                Destroy(_cameraPreviewTexture);
                _cameraPreviewTexture = null;
            }

            _voiceAgentBridge?.Dispose();
            _voiceAgentBridge = null;
            _voiceCommandRouter = null;
            _voiceTranscriptParser = null;
            StopMicTestRecordingSilently();
            CleanupAllSidecarsOnShutdown("UI destroyed");
            _localAiAgentSidecarProbeTask = null;

            _client?.Dispose();
            _client = null;
            ShutdownRuntimeLogSession("OnDestroy");
        }

        private void OnApplicationQuit()
        {
            CleanupAllSidecarsOnShutdown("application quit");
            ShutdownRuntimeLogSession("OnApplicationQuit");
        }

        private void Update()
        {
            UpdateCameraPreview();
            UpdateLocalAiAgent();

            if (_isConnectAttemptInProgress)
            {
                return;
            }

            if (_client != null && !_client.IsConnected && _connectedMode.HasValue)
            {
                _connectedMode = null;
            }

            if (!_initialAutoConnectAttempted && autoConnectOnPlay)
            {
                _initialAutoConnectAttempted = true;
                TryStartConnectAttempt("Auto-connect on Play", mode, 0.5f, ensureOneUiFrameBeforeConnect: false);
                return;
            }

            if (_client == null || !autoReconnect)
            {
                return;
            }

            if (_client.IsConnected)
            {
                float interval = Mathf.Max(0.5f, healthCheckIntervalSeconds);
                if (Time.unscaledTime >= _nextHealthCheckAt)
                {
                    _nextHealthCheckAt = Time.unscaledTime + interval;

                    bool pingOk = _client.Ping(out string pingMessage);
                    if (!pingOk)
                    {
                        ReachyControlMode droppedMode = _connectedMode ?? mode;
                        LogConnectionEvent(
                            droppedMode,
                            "health-check-failed",
                            $"Ping failed: {pingMessage}",
                            "WARN");
                        SetStatus("Connection lost", $"{pingMessage}\nScheduling auto-reconnect.");
                        _client.Disconnect();
                        ReachyControlMode reconnectMode = _connectedMode ?? mode;
                        _connectedMode = null;
                        if (!_manualDisconnect)
                        {
                            ScheduleAutoReconnect(0.5f, reconnectMode);
                        }
                    }
                }

                return;
            }

            if (!_autoReconnectScheduled || _manualDisconnect)
            {
                return;
            }

            if (Time.unscaledTime < _nextAutoReconnectAt)
            {
                return;
            }

            TryStartConnectAttempt(
                "Auto-reconnect",
                _autoReconnectMode,
                Mathf.Max(1.0f, reconnectCooldownSeconds),
                ensureOneUiFrameBeforeConnect: false);
        }

        private void UpdateCameraPreview()
        {
            if (_cameraFetchTask != null && _cameraFetchTask.IsCompleted)
            {
                try
                {
                    ReachyGrpcClient.CameraImageFetchResult fetchResult = _cameraFetchTask.Result;
                    if (!fetchResult.Success)
                    {
                        _cameraPreviewStatus = fetchResult.Message;
                    }
                    else if (fetchResult.ImageBytes == null || fetchResult.ImageBytes.Length == 0)
                    {
                        _cameraPreviewStatus = "Camera frame was empty.";
                    }
                    else
                    {
                        if (_cameraPreviewTexture == null)
                        {
                            _cameraPreviewTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        }

                        bool loaded = _cameraPreviewTexture.LoadImage(fetchResult.ImageBytes);
                        _cameraPreviewStatus = loaded
                            ? $"Live {GetModeLabel(_cameraPreviewMode)} camera ({(cameraUseRightEye ? "right" : "left")} eye) from {_cameraPreviewHost}:{_cameraPreviewPort}."
                            : "Received frame but failed to decode image.";
                    }
                }
                catch (Exception ex)
                {
                    _cameraPreviewStatus = $"Camera preview task failed: {ex.Message}";
                }
                finally
                {
                    _cameraFetchTask = null;
                }
            }

            if (!showCameraPreview || _client == null)
            {
                return;
            }

            if (_cameraFetchTask != null || Time.unscaledTime < _nextCameraFetchAt)
            {
                return;
            }

            ReachyControlMode previewMode = _connectedMode ?? mode;
            string host = previewMode == ReachyControlMode.Simulation ? simulationHost : robotHost;
            int port = previewMode == ReachyControlMode.Simulation ? simulationCameraPort : robotCameraPort;
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            {
                _cameraPreviewStatus = "Camera preview not configured: invalid host or port.";
                _nextCameraFetchAt = Time.unscaledTime + Mathf.Max(0.2f, cameraRefreshIntervalSeconds);
                return;
            }

            _cameraPreviewMode = previewMode;
            _cameraPreviewHost = host;
            _cameraPreviewPort = port;

            var cameraId = cameraUseRightEye
                ? Reachy.Sdk.Camera.CameraId.Right
                : Reachy.Sdk.Camera.CameraId.Left;
            double timeout = Math.Max(0.2f, cameraRpcTimeoutSeconds);

            _cameraFetchTask = Task.Run(() => _client.FetchCameraImage(host, port, cameraId, timeout));
            _nextCameraFetchAt = Time.unscaledTime + Mathf.Max(0.05f, cameraRefreshIntervalSeconds);
        }

        private void UpdateLocalAiAgent()
        {
            if (_voiceAgentBridge == null || _voiceCommandRouter == null || _voiceTranscriptParser == null)
            {
                return;
            }

            bool localAiEnabledNow = enableLocalAiAgent;
            if (localAiEnabledNow && !_localAiAgentWasEnabledLastFrame)
            {
                _localAiAgentActivationAnnouncementPending = true;
            }
            else if (!localAiEnabledNow)
            {
                _localAiAgentActivationAnnouncementPending = false;
            }
            _localAiAgentWasEnabledLastFrame = localAiEnabledNow;

            localAiAgentPollIntervalSeconds = Mathf.Max(0.1f, localAiAgentPollIntervalSeconds);
            localAiAgentConfidenceThreshold = Mathf.Clamp01(localAiAgentConfidenceThreshold);
            localAiAgentTranscriptConfidence = Mathf.Clamp01(localAiAgentTranscriptConfidence);
            localAiAgentMinTranscriptChars = Mathf.Max(0, localAiAgentMinTranscriptChars);
            localAiAgentMinTranscriptWords = Mathf.Max(0, localAiAgentMinTranscriptWords);
            if (localAiAgentJointMinDegrees > localAiAgentJointMaxDegrees)
            {
                float swap = localAiAgentJointMinDegrees;
                localAiAgentJointMinDegrees = localAiAgentJointMaxDegrees;
                localAiAgentJointMaxDegrees = swap;
            }
            localAiAgentDuplicateCommandWindowSeconds =
                Mathf.Max(0.05f, localAiAgentDuplicateCommandWindowSeconds);
            localAiAgentTtsMinIntervalSeconds = Mathf.Max(0.05f, localAiAgentTtsMinIntervalSeconds);
            localAiAgentHeartbeatTimeoutSeconds = Mathf.Max(0.5f, localAiAgentHeartbeatTimeoutSeconds);
            localAiAgentRetryBackoffMinSeconds = Mathf.Max(0.05f, localAiAgentRetryBackoffMinSeconds);
            localAiAgentRetryBackoffMaxSeconds = Mathf.Max(
                localAiAgentRetryBackoffMinSeconds,
                localAiAgentRetryBackoffMaxSeconds);
            localAiAgentDegradedFailureThreshold = Mathf.Max(1, localAiAgentDegradedFailureThreshold);
            localAiAgentHelpModelBackend = NormalizeHelpModelBackend(localAiAgentHelpModelBackend);
            localAiAgentHelpModelMaxTokens = Mathf.Clamp(localAiAgentHelpModelMaxTokens, 16, 512);
            localAiAgentHelpModelTemperature = Mathf.Clamp(localAiAgentHelpModelTemperature, 0f, 1.5f);
            localAiAgentHelpMaxAnswerChars = Mathf.Clamp(localAiAgentHelpMaxAnswerChars, 80, 1200);
            EnsurePreferredMicrophoneDevice();
            if (string.IsNullOrWhiteSpace(localAiAgentListeningEndpoint))
            {
                localAiAgentListeningEndpoint = VoiceAgentBridge.DefaultListeningEndpoint;
            }
            if (string.IsNullOrWhiteSpace(localAiAgentPushToTalkKey))
            {
                localAiAgentPushToTalkKey = "V";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarPythonCommand))
            {
                localAiAgentSidecarPythonCommand = "python";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarScriptRelativePath))
            {
                localAiAgentSidecarScriptRelativePath =
                    "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarConfigRelativePath))
            {
                localAiAgentSidecarConfigRelativePath =
                    "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json";
            }
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarLogLevel))
            {
                localAiAgentSidecarLogLevel = "warning";
            }
            localAiAgentSidecarStartupTimeoutSeconds = Mathf.Max(1f, localAiAgentSidecarStartupTimeoutSeconds);
            localAiAgentSidecarRetryIntervalSeconds = Mathf.Max(0.25f, localAiAgentSidecarRetryIntervalSeconds);
            localAiAgentSidecarHealthTimeoutMs = Mathf.Clamp(localAiAgentSidecarHealthTimeoutMs, 200, 10000);
            _voiceCommandRouter.ConfidenceThreshold = localAiAgentConfidenceThreshold;
            _voiceTranscriptParser.UseSafeNumericParsing = localAiAgentUseSafeNumericParsing;
            _voiceTranscriptParser.RequireTargetTokenForJoint = localAiAgentRequireTargetTokenForJoint;
            _voiceTranscriptParser.RejectOutOfRangeJointCommands = localAiAgentRejectOutOfRangeJointCommands;
            _voiceTranscriptParser.JointMinDegrees = localAiAgentJointMinDegrees;
            _voiceTranscriptParser.JointMaxDegrees = localAiAgentJointMaxDegrees;
            _voiceTranscriptParser.MinTranscriptChars = localAiAgentMinTranscriptChars;
            _voiceTranscriptParser.MinTranscriptWords = localAiAgentMinTranscriptWords;

            bool bridgeShouldBeEnabled = enableLocalAiAgent;
            if (localAiAgentAutoStartSidecar)
            {
                UpdateLocalAiAgentSidecarStartup(enableLocalAiAgent);
                bridgeShouldBeEnabled = enableLocalAiAgent && _localAiAgentSidecarReady;
            }
            else if (!enableLocalAiAgent)
            {
                _localAiAgentSidecarReady = false;
                _localAiAgentSidecarStartupActive = false;
                if (localAiAgentAutoStopAutoStartedSidecarOnDisable)
                {
                    StopAutoStartedSidecarIfRunning("AI disabled");
                }
            }

            int bridgeTimeoutMs = Mathf.Clamp(localAiAgentSidecarHealthTimeoutMs, 700, 10000);
            if (localAiAgentEnableLocalHelpModel)
            {
                bridgeTimeoutMs = Mathf.Max(bridgeTimeoutMs, 2500);
                if (string.Equals(
                    NormalizeHelpModelBackend(localAiAgentHelpModelBackend),
                    "llama_cpp",
                    StringComparison.Ordinal))
                {
                    bridgeTimeoutMs = Mathf.Max(bridgeTimeoutMs, 9000);
                }
            }
            _voiceAgentBridge.Configure(
                localAiAgentEndpoint,
                localAiAgentPollIntervalSeconds,
                timeoutMs: bridgeTimeoutMs);
            _voiceAgentBridge.ConfigureTtsEndpoint(localAiAgentTtsEndpoint);
            _voiceAgentBridge.ConfigureListeningEndpoint(localAiAgentListeningEndpoint);
            _voiceAgentBridge.ConfigureHelpEndpoint(localAiAgentHelpEndpoint);
            _voiceAgentBridge.ConfigureRobustness(
                localAiAgentHeartbeatTimeoutSeconds,
                localAiAgentRetryBackoffMinSeconds,
                localAiAgentRetryBackoffMaxSeconds,
                localAiAgentDegradedFailureThreshold);
            _voiceAgentBridge.SetEnabled(bridgeShouldBeEnabled, Time.unscaledTime);

            if (_localAiAgentActivationAnnouncementPending && bridgeShouldBeEnabled)
            {
                if (localAiAgentEnableTtsFeedback)
                {
                    QueueVoiceFeedback(
                        LocalAiAgentActivationAnnouncement,
                        interrupt: false,
                        bypassRateLimit: true);
                }

                _localAiAgentActivationAnnouncementPending = false;
            }

            if (bridgeShouldBeEnabled)
            {
                bool desiredListeningEnabled = localAiAgentListeningEnabled;
                if (localAiAgentEnablePushToTalk)
                {
                    KeyCode resolvedKey = ResolvePushToTalkKey();
                    desiredListeningEnabled = Input.GetKey(resolvedKey);
                }

                RequestRemoteListeningState(desiredListeningEnabled);
            }

            _voiceAgentBridge.Update(Time.unscaledTime);

            if (!enableLocalAiAgent)
            {
                _voiceHasPendingAction = false;
                _voiceLastSpokenFeedback = string.Empty;
                _voiceLastRequestedListeningEnabled = null;
                _voiceLastCommandFingerprint = string.Empty;
                _voiceWarnedSttInactive = false;
                StopMicTestRecordingSilently();
                RefreshVoiceAgentStatusState();
                return;
            }

            HandleMicTestReleaseFallback();

            if (!bridgeShouldBeEnabled)
            {
                _voiceHasPendingAction = false;
                _voiceLastSpokenFeedback = string.Empty;
                _voiceLastCommandFingerprint = string.Empty;
                _voiceWarnedSttInactive = false;
                _voiceLastActionResult = _localAiAgentSidecarStatus;
                RefreshVoiceAgentStatusState();
                return;
            }

            VoiceAgentBridge.BridgeSnapshot bridgeSnapshot = _voiceAgentBridge.GetSnapshot();
            string sttBackend = (bridgeSnapshot.SttBackend ?? string.Empty).Trim().ToLowerInvariant();
            bool sttInactive = string.IsNullOrWhiteSpace(sttBackend) || sttBackend == "none";
            if (sttInactive)
            {
                if (!_voiceWarnedSttInactive)
                {
                    _voiceWarnedSttInactive = true;
                    string extraDetail = string.IsNullOrWhiteSpace(bridgeSnapshot.LastError)
                        ? string.Empty
                        : $" Sidecar error: {bridgeSnapshot.LastError}";
                    _voiceLastActionResult =
                        "Voice STT backend is inactive. Spoken help/commands need sidecar STT (default: vosk) and model/dependencies installed." +
                        extraDetail;
                }
            }
            else
            {
                _voiceWarnedSttInactive = false;
            }

            if (bridgeSnapshot.SuccessfulListeningToggleCount > _voiceLastHandledListeningSuccessCount)
            {
                _voiceLastHandledListeningSuccessCount = bridgeSnapshot.SuccessfulListeningToggleCount;
                if (!string.IsNullOrWhiteSpace(bridgeSnapshot.LastListeningToggleMessage))
                {
                    _voiceLastActionResult = bridgeSnapshot.LastListeningToggleMessage;
                }
            }

            if (bridgeSnapshot.FailedListeningToggleCount > _voiceLastHandledListeningFailureCount)
            {
                _voiceLastHandledListeningFailureCount = bridgeSnapshot.FailedListeningToggleCount;
                if (!string.IsNullOrWhiteSpace(bridgeSnapshot.LastListeningToggleMessage))
                {
                    _voiceLastActionResult = bridgeSnapshot.LastListeningToggleMessage;
                }
            }

            if (bridgeSnapshot.SuccessfulHelpCount > _voiceLastHandledHelpSuccessCount)
            {
                _voiceLastHandledHelpSuccessCount = bridgeSnapshot.SuccessfulHelpCount;
                if (!string.IsNullOrWhiteSpace(bridgeSnapshot.LastHelpAnswer))
                {
                    _voiceLastHelpAnswer = bridgeSnapshot.LastHelpAnswer;
                    _voiceLastActionResult = $"Local help answer: {bridgeSnapshot.LastHelpAnswer}";
                    QueueVoiceFeedback(
                        bridgeSnapshot.LastHelpAnswer,
                        interrupt: false,
                        bypassRateLimit: true);
                }
            }

            int processedThisFrame = 0;
            while (_voiceAgentBridge.TryDequeueIntent(out VoiceAgentIntent incomingIntent))
            {
                HandleIncomingVoiceIntent(incomingIntent);
                processedThisFrame++;
                if (processedThisFrame >= 6)
                {
                    break;
                }
            }

            RefreshVoiceAgentStatusState();
        }

        private void HandleIncomingVoiceIntent(VoiceAgentIntent incomingIntent)
        {
            if (incomingIntent == null)
            {
                _voiceLastActionResult = "Voice bridge delivered a null intent.";
                return;
            }

            _voiceLastRawIntentPayload = incomingIntent.raw_json ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_voiceLastRawIntentPayload))
            {
                _voiceLastRawIntentPayload = $"intent={incomingIntent.intent}; spoken={incomingIntent.spoken_text}";
            }

            if (!string.IsNullOrWhiteSpace(incomingIntent.spoken_text))
            {
                _voiceLastTranscript = incomingIntent.spoken_text;
            }

            VoiceAgentIntent intentToRoute = incomingIntent;
            bool missingStructuredIntent = string.IsNullOrWhiteSpace(incomingIntent.intent);
            if (missingStructuredIntent)
            {
                if (!incomingIntent.transcript_is_final)
                {
                    string partialMessage = "Partial transcript received; waiting for final transcript.";
                    _voiceLastParserMessage = partialMessage;
                    _voiceLastIntentSummary = partialMessage;
                    _voiceLastActionResult = partialMessage;
                    return;
                }

                if (!localAiAgentEnableTranscriptParser)
                {
                    string disabledMessage = "Transcript parser is disabled; cannot map transcript to command.";
                    _voiceLastParserMessage = disabledMessage;
                    _voiceLastIntentSummary = disabledMessage;
                    _voiceLastActionResult = disabledMessage;
                    return;
                }

                bool parsed = _voiceTranscriptParser.TryParse(
                    incomingIntent.spoken_text,
                    incomingIntent.confidence,
                    _client?.PresetPoseNames,
                    _client?.JointNames,
                    out VoiceAgentIntent parsedIntent,
                    out string parserMessage);
                _voiceLastParserMessage = parserMessage;
                if (!parsed || parsedIntent == null)
                {
                    _voiceLastIntentSummary = parserMessage;
                    _voiceLastActionResult = parserMessage;
                    return;
                }

                parsedIntent.raw_json = incomingIntent.raw_json ?? string.Empty;
                intentToRoute = parsedIntent;
            }
            else
            {
                _voiceLastParserMessage = "Structured intent received from bridge (parser skipped).";
            }

            bool routed = _voiceCommandRouter.TryRoute(
                intentToRoute,
                _client?.PresetPoseNames,
                out VoiceCommandRouter.RoutedAction routedAction,
                out string routeMessage);

            _voiceLastIntentSummary = routeMessage;
            if (!routed)
            {
                _voiceLastActionResult = routeMessage;
                return;
            }

            if (IsDuplicateVoiceCommand(routedAction))
            {
                _voiceLastActionResult = $"Duplicate voice command ignored: {routedAction.Summary}";
                return;
            }

            if (_voiceHasPendingAction &&
                routedAction.Kind != VoiceCommandRouter.VoiceActionKind.StopMotion &&
                routedAction.Kind != VoiceCommandRouter.VoiceActionKind.ConfirmPending &&
                routedAction.Kind != VoiceCommandRouter.VoiceActionKind.RejectPending)
            {
                _voiceLastActionResult = "Pending voice action waiting for Confirm/Reject. Say 'cancel' to clear it.";
                return;
            }

            bool isMotionAction =
                routedAction.Kind == VoiceCommandRouter.VoiceActionKind.SetPose ||
                routedAction.Kind == VoiceCommandRouter.VoiceActionKind.MoveJoint ||
                routedAction.Kind == VoiceCommandRouter.VoiceActionKind.ShowMovement ||
                routedAction.Kind == VoiceCommandRouter.VoiceActionKind.Hello;
            if (isMotionAction && localAiAgentBlockMotionWhenBridgeUnhealthy)
            {
                if (!IsBridgeHealthyForMotion(out string bridgeHealthReason))
                {
                    _voiceLastActionResult = $"Motion blocked: {bridgeHealthReason}.";
                    _voiceLastIntentSummary = routeMessage;
                    QueueVoiceFeedback("Motion command blocked while voice bridge is unhealthy.");
                    return;
                }
            }

            if (routedAction.RequiresConfirmation)
            {
                _voiceHasPendingAction = true;
                _voicePendingAction = routedAction;
                _voiceLastActionResult = $"Awaiting confirmation: {routedAction.Summary}";
                QueueVoiceFeedback($"{routedAction.Summary} Should I execute it?", interrupt: false);
                return;
            }

            ExecuteVoiceAction(routedAction, out string actionMessage);
            _voiceLastActionResult = actionMessage;
            bool shouldSpeakActionMessage =
                !(routedAction.Kind == VoiceCommandRouter.VoiceActionKind.Help && localAiAgentEnableLocalHelpModel);
            if (shouldSpeakActionMessage)
            {
                QueueVoiceFeedback(
                    actionMessage,
                    interrupt: routedAction.Kind == VoiceCommandRouter.VoiceActionKind.StopMotion);
            }
        }

        private bool IsDuplicateVoiceCommand(VoiceCommandRouter.RoutedAction action)
        {
            if (!localAiAgentSuppressDuplicateCommands)
            {
                return false;
            }

            float windowSeconds = Mathf.Max(0.05f, localAiAgentDuplicateCommandWindowSeconds);
            string fingerprint = BuildVoiceCommandFingerprint(action);
            float now = Time.unscaledTime;

            bool duplicate = !string.IsNullOrWhiteSpace(_voiceLastCommandFingerprint) &&
                string.Equals(_voiceLastCommandFingerprint, fingerprint, StringComparison.Ordinal) &&
                (now - _voiceLastCommandAt) <= windowSeconds;
            if (duplicate)
            {
                return true;
            }

            _voiceLastCommandFingerprint = fingerprint;
            _voiceLastCommandAt = now;
            return false;
        }

        private static string BuildVoiceCommandFingerprint(VoiceCommandRouter.RoutedAction action)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3:F3}|{4}",
                action.Kind,
                action.PoseName ?? string.Empty,
                action.JointName ?? string.Empty,
                action.JointDegrees,
                action.RequiresConfirmation);
        }

        private void ConfirmPendingVoiceAction(bool queueFeedback = true)
        {
            if (!_voiceHasPendingAction)
            {
                return;
            }

            VoiceCommandRouter.RoutedAction action = _voicePendingAction;
            _voiceHasPendingAction = false;
            _voicePendingAction = default(VoiceCommandRouter.RoutedAction);
            ExecuteVoiceAction(action, out string actionMessage);
            _voiceLastActionResult = actionMessage;
            if (queueFeedback)
            {
                QueueVoiceFeedback(
                    actionMessage,
                    interrupt: action.Kind == VoiceCommandRouter.VoiceActionKind.StopMotion);
            }
        }

        private void RejectPendingVoiceAction(bool queueFeedback = true)
        {
            if (!_voiceHasPendingAction)
            {
                return;
            }

            _voiceHasPendingAction = false;
            _voicePendingAction = default(VoiceCommandRouter.RoutedAction);
            _voiceLastActionResult = "Pending voice action was rejected by operator.";
            if (queueFeedback)
            {
                QueueVoiceFeedback("Command canceled.");
            }
        }

        private void QueueVoiceFeedback(
            string message,
            bool interrupt = false,
            bool bypassRateLimit = false)
        {
            if (!enableLocalAiAgent || !localAiAgentEnableTtsFeedback || _voiceAgentBridge == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!interrupt && !bypassRateLimit && now < _voiceNextAllowedTtsAt)
            {
                return;
            }

            _voiceNextAllowedTtsAt = now + localAiAgentTtsMinIntervalSeconds;
            string trimmedMessage = message.Trim();
            _voiceAgentBridge.EnqueueTtsFeedback(trimmedMessage, interrupt);
            _voiceLastSpokenFeedback = trimmedMessage;
        }

        private KeyCode ResolvePushToTalkKey()
        {
            string raw = string.IsNullOrWhiteSpace(localAiAgentPushToTalkKey)
                ? "V"
                : localAiAgentPushToTalkKey.Trim();
            if (Enum.TryParse(raw, true, out KeyCode parsedKey))
            {
                return parsedKey;
            }

            return KeyCode.V;
        }

        private void RequestRemoteListeningState(bool enabled, bool force = false)
        {
            if (_voiceAgentBridge == null || !enableLocalAiAgent)
            {
                return;
            }

            if (!force && _voiceLastRequestedListeningEnabled.HasValue &&
                _voiceLastRequestedListeningEnabled.Value == enabled)
            {
                return;
            }

            _voiceAgentBridge.EnqueueListeningToggle(enabled);
            _voiceLastRequestedListeningEnabled = enabled;
        }

        private void UpdateLocalAiAgentSidecarStartup(bool enableRequested)
        {
            if (!enableRequested)
            {
                _localAiAgentSidecarReady = false;
                _localAiAgentSidecarStartupActive = false;
                _localAiAgentSidecarInitialProbeCompleted = false;
                _localAiAgentSidecarLastStartError = string.Empty;
                _localAiAgentSidecarProbeTask = null;
                _localAiAgentSidecarNextProbeAt = 0f;
                _localAiAgentSidecarStatus = "Sidecar auto-start idle.";
                if (localAiAgentAutoStopAutoStartedSidecarOnDisable)
                {
                    StopAutoStartedSidecarIfRunning("AI disabled");
                }
                return;
            }

            ConsumeSidecarProbeResultIfReady();

            if (_localAiAgentSidecarReady)
            {
                _localAiAgentSidecarStartupActive = false;
                _localAiAgentSidecarLastStartError = string.Empty;
                if (string.IsNullOrWhiteSpace(_localAiAgentSidecarStatus))
                {
                    _localAiAgentSidecarStatus = "Sidecar is reachable.";
                }
                return;
            }

            if (!_localAiAgentSidecarStartupActive)
            {
                _localAiAgentSidecarStartupActive = true;
                _localAiAgentSidecarInitialProbeCompleted = false;
                _localAiAgentSidecarStartupStartedAt = Time.unscaledTime;
                _localAiAgentSidecarNextStartAttemptAt = Time.unscaledTime;
                _localAiAgentSidecarNextProbeAt = Time.unscaledTime;
                _localAiAgentSidecarStatus = "Starting local sidecar before enabling AI bridge...";
                _localAiAgentSidecarLastStartError = string.Empty;
            }

            if (_localAiAgentSidecarProcess != null && _localAiAgentSidecarProcess.HasExited)
            {
                _localAiAgentSidecarProcess.Dispose();
                _localAiAgentSidecarProcess = null;
            }

            if (_localAiAgentSidecarInitialProbeCompleted &&
                _localAiAgentSidecarProbeTask == null &&
                (_localAiAgentSidecarProcess == null || _localAiAgentSidecarProcess.HasExited) &&
                Time.unscaledTime >= _localAiAgentSidecarNextStartAttemptAt)
            {
                if (TryStartLocalAiSidecarProcess(out string startMessage))
                {
                    _localAiAgentSidecarStatus = startMessage;
                    _localAiAgentSidecarLastStartError = string.Empty;
                    _localAiAgentSidecarNextProbeAt = Time.unscaledTime + 0.2f;
                }
                else
                {
                    _localAiAgentSidecarStatus = startMessage;
                    _localAiAgentSidecarLastStartError = startMessage;
                }

                _localAiAgentSidecarNextStartAttemptAt =
                    Time.unscaledTime + localAiAgentSidecarRetryIntervalSeconds;
            }

            if (_localAiAgentSidecarProbeTask == null && Time.unscaledTime >= _localAiAgentSidecarNextProbeAt)
            {
                string intentEndpoint = string.IsNullOrWhiteSpace(localAiAgentEndpoint)
                    ? VoiceAgentBridge.DefaultEndpoint
                    : localAiAgentEndpoint.Trim();
                int timeoutMs = Mathf.Clamp(localAiAgentSidecarHealthTimeoutMs, 200, 10000);
                _localAiAgentSidecarProbeTask = Task.Run(() => ProbeSidecar(intentEndpoint, timeoutMs));
                _localAiAgentSidecarNextProbeAt = Time.unscaledTime + 0.4f;
            }

            float elapsed = Time.unscaledTime - _localAiAgentSidecarStartupStartedAt;
            if (elapsed > localAiAgentSidecarStartupTimeoutSeconds && !_localAiAgentSidecarReady)
            {
                if (!string.IsNullOrWhiteSpace(_localAiAgentSidecarLastStartError))
                {
                    _localAiAgentSidecarStatus =
                        $"{_localAiAgentSidecarLastStartError} (startup timed out after {elapsed:F1}s).";
                }
                else
                {
                    _localAiAgentSidecarStatus =
                        $"Waiting for sidecar endpoint... ({elapsed:F1}s elapsed).";
                }
            }
        }

        private void ConsumeSidecarProbeResultIfReady()
        {
            if (_localAiAgentSidecarProbeTask == null || !_localAiAgentSidecarProbeTask.IsCompleted)
            {
                return;
            }

            SidecarProbeResult result;
            try
            {
                result = _localAiAgentSidecarProbeTask.Result;
            }
            catch (Exception ex)
            {
                _localAiAgentSidecarProbeTask = null;
                _localAiAgentSidecarInitialProbeCompleted = true;
                _localAiAgentSidecarReady = false;
                _localAiAgentSidecarStatus = $"Sidecar probe failed: {ex.Message}";
                _localAiAgentSidecarNextProbeAt = Time.unscaledTime + 0.8f;
                return;
            }

            _localAiAgentSidecarProbeTask = null;
            _localAiAgentSidecarInitialProbeCompleted = true;
            _localAiAgentSidecarReady = result.Success && result.Reachable;
            if (_localAiAgentSidecarReady)
            {
                _localAiAgentSidecarStatus = string.IsNullOrWhiteSpace(result.Message)
                    ? "Sidecar is reachable."
                    : result.Message;
            }
            else if (!string.IsNullOrWhiteSpace(result.Error))
            {
                _localAiAgentSidecarStatus = result.Error;
            }
            else if (!string.IsNullOrWhiteSpace(result.Message))
            {
                _localAiAgentSidecarStatus = result.Message;
            }
        }

        private bool TryStartLocalAiSidecarProcess(out string message)
        {
            message = string.Empty;
            string prestartMessage = string.Empty;

            if (localAiAgentSyncSidecarConfigOnStart)
            {
                bool synced = TrySyncLocalSidecarConfigFromUi(out string syncMessage);
                if (!synced)
                {
                    prestartMessage = $"Sidecar config sync failed ({syncMessage}). Starting with existing file.";
                }
                else
                {
                    prestartMessage = "Synced sidecar config from UI before start.";
                }
            }

            string pythonCommand = string.IsNullOrWhiteSpace(localAiAgentSidecarPythonCommand)
                ? "python"
                : localAiAgentSidecarPythonCommand.Trim();
            List<string> rawScriptCandidates = BuildRuntimeRelativePathCandidates(localAiAgentSidecarScriptRelativePath);
            var existingScriptCandidates = new List<string>();
            for (int i = 0; i < rawScriptCandidates.Count; i++)
            {
                string candidate = rawScriptCandidates[i];
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    existingScriptCandidates.Add(candidate);
                }
            }

            string scriptPath = SelectPreferredSidecarScriptPath(
                existingScriptCandidates,
                localAiAgentSidecarConfigRelativePath);
            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                string probes = SummarizeCandidatePaths(rawScriptCandidates, 5);
                message = string.IsNullOrWhiteSpace(prestartMessage)
                    ? $"Sidecar script not found: {scriptPath}. Probed: {probes}"
                    : $"{prestartMessage} Sidecar script not found: {scriptPath}. Probed: {probes}";
                return false;
            }

            string configFileName = Path.GetFileName(
                (localAiAgentSidecarConfigRelativePath ?? string.Empty)
                    .Trim()
                    .Replace('/', Path.DirectorySeparatorChar));
            string configPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(configFileName))
            {
                string siblingConfigPath = Path.Combine(
                    Path.GetDirectoryName(scriptPath) ?? Application.dataPath,
                    configFileName);
                if (File.Exists(siblingConfigPath))
                {
                    configPath = siblingConfigPath;
                }
            }
            if (string.IsNullOrWhiteSpace(configPath))
            {
                configPath = ResolveLocalAssetRelativePath(localAiAgentSidecarConfigRelativePath);
            }
            string workingDirectory = Path.GetDirectoryName(scriptPath) ?? Application.dataPath;
            string logLevel = string.IsNullOrWhiteSpace(localAiAgentSidecarLogLevel)
                ? "warning"
                : localAiAgentSidecarLogLevel.Trim().ToLowerInvariant();
            bool preferBundledVenvFirst = localAiAgentEnableLocalHelpModel &&
                string.Equals(
                    NormalizeHelpModelBackend(localAiAgentHelpModelBackend),
                    "llama_cpp",
                    StringComparison.Ordinal);
            List<string> pythonCandidates = BuildPythonCommandCandidates(
                workingDirectory,
                pythonCommand,
                preferBundledVenvFirst,
                existingScriptCandidates);

            string arguments;
            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                arguments = $"\"{scriptPath}\" --config \"{configPath}\" --log-level {logLevel}";
            }
            else
            {
                arguments = $"\"{scriptPath}\" --log-level {logLevel}";
            }

            string lastError = string.Empty;
            for (int i = 0; i < pythonCandidates.Count; i++)
            {
                string candidate = pythonCandidates[i];
                try
                {
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = arguments,
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };
                    _localAiAgentSidecarProcess = System.Diagnostics.Process.Start(startInfo);
                    _localAiAgentSidecarStartedByUi = _localAiAgentSidecarProcess != null;
                    if (_localAiAgentSidecarProcess == null)
                    {
                        lastError = $"No process handle returned for '{candidate}'.";
                        continue;
                    }

                    message = string.IsNullOrWhiteSpace(prestartMessage)
                        ? $"Started sidecar process (PID {_localAiAgentSidecarProcess.Id}) with '{candidate}'."
                        : $"{prestartMessage} Started sidecar process (PID {_localAiAgentSidecarProcess.Id}) with '{candidate}'.";
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = $"{candidate}: {ex.Message}";
                }
            }

            message = string.IsNullOrWhiteSpace(prestartMessage)
                ? $"Failed to start sidecar. Last error: {lastError}"
                : $"{prestartMessage} Failed to start sidecar. Last error: {lastError}";
            return false;
        }

        private static List<string> BuildPythonCommandCandidates(
            string workingDirectory,
            string configuredPythonCommand,
            bool preferBundledVenvFirst,
            IReadOnlyList<string> additionalScriptCandidates)
        {
            var candidates = new List<string>();
            string configured = string.IsNullOrWhiteSpace(configuredPythonCommand)
                ? "python"
                : configuredPythonCommand.Trim();
            bool configuredLooksDefault =
                string.Equals(configured, "python", StringComparison.OrdinalIgnoreCase);

            var bundledPythonCandidates = new List<string>();
            TryAddBundledVenvPythonCandidate(bundledPythonCandidates, workingDirectory);
            if (additionalScriptCandidates != null)
            {
                for (int i = 0; i < additionalScriptCandidates.Count; i++)
                {
                    string scriptPath = additionalScriptCandidates[i];
                    if (string.IsNullOrWhiteSpace(scriptPath))
                    {
                        continue;
                    }

                    string scriptDirectory = Path.GetDirectoryName(scriptPath);
                    TryAddBundledVenvPythonCandidate(bundledPythonCandidates, scriptDirectory);
                }
            }

            bundledPythonCandidates = DeduplicateCandidates(bundledPythonCandidates);
            bool shouldUseBundledFirst =
                bundledPythonCandidates.Count > 0 && (preferBundledVenvFirst || configuredLooksDefault);
            if (shouldUseBundledFirst)
            {
                candidates.AddRange(bundledPythonCandidates);
            }

            candidates.Add(configured);

            if (bundledPythonCandidates.Count > 0 && !shouldUseBundledFirst)
            {
                candidates.AddRange(bundledPythonCandidates);
            }

            return DeduplicateCandidates(candidates);
        }

        private static void TryAddBundledVenvPythonCandidate(List<string> candidates, string directory)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string bundledVenvPython = Path.Combine(directory, ".venv", "Scripts", "python.exe");
            if (File.Exists(bundledVenvPython))
            {
                candidates.Add(bundledVenvPython);
            }
        }

        private static string SelectPreferredSidecarScriptPath(
            IReadOnlyList<string> existingScriptCandidates,
            string configRelativePath)
        {
            if (existingScriptCandidates == null || existingScriptCandidates.Count == 0)
            {
                return string.Empty;
            }

            string configFileName = Path.GetFileName(
                (configRelativePath ?? string.Empty)
                    .Trim()
                    .Replace('/', Path.DirectorySeparatorChar));

            int bestScore = int.MinValue;
            string bestPath = existingScriptCandidates[0];
            for (int i = 0; i < existingScriptCandidates.Count; i++)
            {
                string candidate = existingScriptCandidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string directory = Path.GetDirectoryName(candidate) ?? string.Empty;
                int score = 0;
                string venvPython = Path.Combine(directory, ".venv", "Scripts", "python.exe");
                if (File.Exists(venvPython))
                {
                    score += 200;
                }

                if (!string.IsNullOrWhiteSpace(configFileName))
                {
                    string siblingConfig = Path.Combine(directory, configFileName);
                    if (File.Exists(siblingConfig))
                    {
                        score += 120;
                    }
                }

                if (candidate.IndexOf(
                    $"{Path.DirectorySeparatorChar}Assets{Path.DirectorySeparatorChar}ReachyControlApp{Path.DirectorySeparatorChar}LocalVoiceAgent{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 40;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPath = candidate;
                }
            }

            return bestPath;
        }

        private static List<string> DeduplicateCandidates(IReadOnlyList<string> items)
        {
            var unique = new List<string>();
            if (items == null)
            {
                return unique;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Count; i++)
            {
                string item = items[i];
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                string trimmed = item.Trim();
                if (seen.Add(trimmed))
                {
                    unique.Add(trimmed);
                }
            }

            return unique;
        }

        private static bool IsLikelyVirtualMicrophone(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return false;
            }

            string lowered = deviceName.Trim().ToLowerInvariant();
            string[] virtualTokens =
            {
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
                "sunflower"
            };

            for (int i = 0; i < virtualTokens.Length; i++)
            {
                if (lowered.IndexOf(virtualTokens[i], StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DeviceNameEquals(string left, string right)
        {
            return string.Equals(
                left?.Trim() ?? string.Empty,
                right?.Trim() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool DeviceNameContains(string source, string token)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int ScoreMicrophoneDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return int.MinValue;
            }

            int score = 0;
            bool isVirtual = IsLikelyVirtualMicrophone(deviceName);
            if (localAiAgentIgnoreVirtualMicrophones)
            {
                score += isVirtual ? -220 : 120;
            }
            else if (isVirtual)
            {
                score -= 40;
            }

            string preferred = localAiAgentPreferredMicrophoneDeviceName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                if (DeviceNameEquals(deviceName, preferred))
                {
                    score += 900;
                }
                else if (DeviceNameContains(deviceName, preferred) || DeviceNameContains(preferred, deviceName))
                {
                    score += 300;
                }
            }

            if (DeviceNameContains(deviceName, "headset"))
            {
                score += 30;
            }
            if (DeviceNameContains(deviceName, "microphone") || DeviceNameContains(deviceName, "mic"))
            {
                score += 20;
            }
            if (DeviceNameContains(deviceName, "array"))
            {
                score += 8;
            }

            return score;
        }

        private string[] GetOrderedMicrophoneDevices()
        {
            string[] rawDevices = Microphone.devices;
            if (rawDevices == null || rawDevices.Length == 0)
            {
                return Array.Empty<string>();
            }

            var unique = new List<string>(rawDevices.Length);
            for (int i = 0; i < rawDevices.Length; i++)
            {
                string candidate = rawDevices[i];
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (DeviceNameEquals(unique[j], candidate))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    unique.Add(candidate.Trim());
                }
            }

            unique.Sort((a, b) => ScoreMicrophoneDevice(b).CompareTo(ScoreMicrophoneDevice(a)));
            return unique.ToArray();
        }

        private bool HasMicrophoneDevice(string deviceName)
        {
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0 || string.IsNullOrWhiteSpace(deviceName))
            {
                return false;
            }

            for (int i = 0; i < devices.Length; i++)
            {
                if (DeviceNameEquals(devices[i], deviceName))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsurePreferredMicrophoneDevice(bool forceAuto = false)
        {
            string[] orderedDevices = GetOrderedMicrophoneDevices();
            if (orderedDevices.Length == 0)
            {
                localAiAgentPreferredMicrophoneDeviceName = string.Empty;
                return;
            }

            if (forceAuto || string.IsNullOrWhiteSpace(localAiAgentPreferredMicrophoneDeviceName))
            {
                localAiAgentPreferredMicrophoneDeviceName = orderedDevices[0];
                return;
            }

            if (!HasMicrophoneDevice(localAiAgentPreferredMicrophoneDeviceName))
            {
                localAiAgentPreferredMicrophoneDeviceName = orderedDevices[0];
            }
        }

        private void EnsureMicTestAudioSource()
        {
            if (_localAiMicTestAudioSource != null)
            {
                return;
            }

            _localAiMicTestAudioSource = GetComponent<AudioSource>();
            if (_localAiMicTestAudioSource == null)
            {
                _localAiMicTestAudioSource = gameObject.AddComponent<AudioSource>();
            }

            _localAiMicTestAudioSource.playOnAwake = false;
            _localAiMicTestAudioSource.loop = false;
        }

        private void HandleMicTestButton(bool holdToRecord)
        {
            if (holdToRecord)
            {
                _localAiMicTestButtonHeld = true;
                if (!_localAiMicTestRecording)
                {
                    StartMicTestRecording();
                }
                return;
            }

            if (_localAiMicTestButtonHeld && _localAiMicTestRecording)
            {
                _localAiMicTestButtonHeld = false;
                StopMicTestRecordingAndPlay();
                return;
            }

            if (!holdToRecord)
            {
                _localAiMicTestButtonHeld = false;
            }
        }

        private void HandleMicTestReleaseFallback()
        {
            if (!_localAiMicTestRecording || !_localAiMicTestButtonHeld)
            {
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                _localAiMicTestButtonHeld = false;
                StopMicTestRecordingAndPlay();
            }
        }

        private void HandleMicTestButtonFromGui(bool holdToRecord, EventType guiEventType)
        {
            if (guiEventType == EventType.Layout)
            {
                return;
            }

            HandleMicTestButton(holdToRecord);
        }

        private void ResetMicTestButtonHoldState()
        {
            _localAiMicTestButtonHeld = false;
        }

        private void StartMicTestRecording()
        {
            EnsurePreferredMicrophoneDevice();
            string device = localAiAgentPreferredMicrophoneDeviceName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(device))
            {
                _voiceLastActionResult = "Mic test failed: no microphone devices detected.";
                return;
            }

            if (!HasMicrophoneDevice(device))
            {
                _voiceLastActionResult = $"Mic test failed: selected mic '{device}' is not available.";
                return;
            }

            try
            {
                if (_localAiMicTestAudioSource != null && _localAiMicTestAudioSource.isPlaying)
                {
                    _localAiMicTestAudioSource.Stop();
                }

                _localAiMicTestRecordClip = Microphone.Start(device, loop: false, lengthSec: 10, frequency: 16000);
                if (_localAiMicTestRecordClip == null)
                {
                    _voiceLastActionResult = $"Mic test failed: could not start recording on '{device}'.";
                    _localAiMicTestRecording = false;
                    return;
                }

                _localAiMicTestDevice = device;
                _localAiMicTestRecording = true;
                _voiceLastActionResult = $"Mic test recording on '{device}'...";
            }
            catch (Exception ex)
            {
                _localAiMicTestRecording = false;
                _localAiMicTestDevice = string.Empty;
                _localAiMicTestRecordClip = null;
                _voiceLastActionResult = $"Mic test failed to start: {ex.Message}";
            }
        }

        private void StopMicTestRecordingAndPlay()
        {
            ResetMicTestButtonHoldState();
            string device = _localAiMicTestDevice ?? string.Empty;
            int recordedSamples = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(device))
                {
                    recordedSamples = Microphone.GetPosition(device);
                    Microphone.End(device);
                }
            }
            catch
            {
                // Ignore microphone stop failures in test path.
            }

            _localAiMicTestRecording = false;
            _localAiMicTestDevice = string.Empty;

            if (_localAiMicTestRecordClip == null)
            {
                _voiceLastActionResult = "Mic test: recording clip was unavailable.";
                return;
            }

            int channels = Mathf.Max(1, _localAiMicTestRecordClip.channels);
            int frequency = Mathf.Max(8000, _localAiMicTestRecordClip.frequency);
            int sampleCount = Mathf.Max(0, recordedSamples);
            if (sampleCount <= 0)
            {
                _voiceLastActionResult = "Mic test captured no audio.";
                _localAiMicTestRecordClip = null;
                return;
            }

            float[] data = new float[sampleCount * channels];
            _localAiMicTestRecordClip.GetData(data, 0);
            AudioClip playbackClip = AudioClip.Create(
                "LocalAiMicTestPlayback",
                sampleCount,
                channels,
                frequency,
                stream: false);
            playbackClip.SetData(data, 0);

            EnsureMicTestAudioSource();
            if (_localAiMicTestAudioSource != null)
            {
                _localAiMicTestAudioSource.clip = playbackClip;
                _localAiMicTestAudioSource.Play();
            }

            _localAiMicTestRecordClip = null;
            _voiceLastActionResult = $"Mic test playback started ({sampleCount / (float)frequency:F2}s).";
        }

        private void StopMicTestRecordingSilently()
        {
            ResetMicTestButtonHoldState();
            if (!_localAiMicTestRecording)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(_localAiMicTestDevice))
                {
                    Microphone.End(_localAiMicTestDevice);
                }
            }
            catch
            {
                // Ignore cleanup failures on shutdown.
            }

            _localAiMicTestRecording = false;
            _localAiMicTestDevice = string.Empty;
            _localAiMicTestRecordClip = null;
        }

        private void StopAutoStartedSidecarIfRunning(string reason)
        {
            if (!_localAiAgentSidecarStartedByUi)
            {
                return;
            }

            if (_localAiAgentSidecarProcess == null)
            {
                _localAiAgentSidecarStartedByUi = false;
                return;
            }

            try
            {
                if (!_localAiAgentSidecarProcess.HasExited)
                {
                    _localAiAgentSidecarProcess.Kill();
                    _localAiAgentSidecarProcess.WaitForExit(1200);
                }
            }
            catch
            {
                // Ignore cleanup errors during shutdown.
            }
            finally
            {
                _localAiAgentSidecarProcess.Dispose();
                _localAiAgentSidecarProcess = null;
                _localAiAgentSidecarStartedByUi = false;
                _localAiAgentSidecarStatus = $"Stopped auto-started sidecar ({reason}).";
            }
        }

        private void CleanupAllSidecarsOnShutdown(string reason)
        {
            if (_sidecarShutdownCleanupDone)
            {
                return;
            }

            _sidecarShutdownCleanupDone = true;
            StopAutoStartedSidecarIfRunning(reason);
            StopAnyLocalVoiceSidecarProcesses(reason);
        }

        private void StopAnyLocalVoiceSidecarProcesses(string reason)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                string scriptPath = ResolveLocalAssetRelativePath(localAiAgentSidecarScriptRelativePath);
                string scriptFileName = string.IsNullOrWhiteSpace(scriptPath)
                    ? "local_voice_agent_sidecar.py"
                    : Path.GetFileName(scriptPath);
                if (string.IsNullOrWhiteSpace(scriptFileName))
                {
                    scriptFileName = "local_voice_agent_sidecar.py";
                }

                string escapedScriptName = scriptFileName.Replace("'", "''");
                string command =
                    "$self = $PID; " +
                    "$procs = Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $self -and $_.CommandLine -and $_.CommandLine -like '*" +
                    escapedScriptName +
                    "*' }; " +
                    "$killed = 0; foreach ($p in $procs) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; $killed++ } catch {} }; " +
                    "Write-Output $killed";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (System.Diagnostics.Process cleanupProcess = System.Diagnostics.Process.Start(startInfo))
                {
                    if (cleanupProcess == null)
                    {
                        return;
                    }

                    if (!cleanupProcess.WaitForExit(2000))
                    {
                        try
                        {
                            cleanupProcess.Kill();
                        }
                        catch
                        {
                            // Ignore kill failures during shutdown cleanup.
                        }
                        return;
                    }

                    string output = cleanupProcess.StandardOutput.ReadToEnd().Trim();
                    if (int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out int killedCount) &&
                        killedCount > 0)
                    {
                        _localAiAgentSidecarStatus =
                            $"Stopped {killedCount} local sidecar process(es) on {reason}.";
                    }
                }
            }
            catch
            {
                // Best-effort cleanup; ignore failures on shutdown.
            }
#endif
        }

        private static SidecarProbeResult ProbeSidecar(string intentEndpoint, int timeoutMs)
        {
            string trimmedIntentEndpoint = string.IsNullOrWhiteSpace(intentEndpoint)
                ? VoiceAgentBridge.DefaultEndpoint
                : intentEndpoint.Trim();
            string healthEndpoint = TryBuildSiblingEndpoint(trimmedIntentEndpoint, "/health", out string builtHealth)
                ? builtHealth
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(healthEndpoint))
            {
                SidecarProbeResult healthProbe = ProbeEndpoint(healthEndpoint, timeoutMs, parseHealth: true);
                if (healthProbe.Success && healthProbe.Reachable)
                {
                    return healthProbe;
                }
            }

            return ProbeEndpoint(trimmedIntentEndpoint, timeoutMs, parseHealth: false);
        }

        private static SidecarProbeResult ProbeEndpoint(string endpoint, int timeoutMs, bool parseHealth)
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
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    if (!parseHealth)
                    {
                        return new SidecarProbeResult
                        {
                            Success = true,
                            Reachable = true,
                            Message = "Sidecar endpoint is reachable.",
                            Error = string.Empty
                        };
                    }

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        return new SidecarProbeResult
                        {
                            Success = true,
                            Reachable = true,
                            Message = "Sidecar health endpoint reachable.",
                            Error = string.Empty
                        };
                    }

                    SidecarHealthEnvelope parsed = JsonUtility.FromJson<SidecarHealthEnvelope>(body);
                    bool ok = parsed == null || parsed.ok;
                    string selectedDeviceName = parsed?.selected_input_device_name ?? string.Empty;
                    int selectedDeviceIndex = parsed != null ? parsed.selected_input_device_index : -1;
                    bool hasSelectedDevice = !string.IsNullOrWhiteSpace(selectedDeviceName);
                    string selectedDeviceSummary = hasSelectedDevice
                        ? $" Selected mic: [{selectedDeviceIndex}] {selectedDeviceName}."
                        : string.Empty;
                    string parsedLastError = parsed?.last_error ?? string.Empty;
                    return new SidecarProbeResult
                    {
                        Success = true,
                        Reachable = ok,
                        Message = ok
                            ? $"Sidecar health is OK.{selectedDeviceSummary}"
                            : "Sidecar health endpoint reported not OK.",
                        Error = ok
                            ? string.Empty
                            : string.IsNullOrWhiteSpace(parsedLastError)
                                ? "Sidecar health response was not OK."
                                : parsedLastError
                    };
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

                return new SidecarProbeResult
                {
                    Success = false,
                    Reachable = false,
                    Message = "Sidecar endpoint probe failed.",
                    Error = detail
                };
            }
            catch (Exception ex)
            {
                return new SidecarProbeResult
                {
                    Success = false,
                    Reachable = false,
                    Message = "Sidecar endpoint probe failed.",
                    Error = ex.Message
                };
            }
        }

        private static bool TryBuildSiblingEndpoint(string sourceEndpoint, string targetPath, out string endpoint)
        {
            endpoint = string.Empty;
            if (string.IsNullOrWhiteSpace(sourceEndpoint) || string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            if (!Uri.TryCreate(sourceEndpoint, UriKind.Absolute, out Uri sourceUri))
            {
                return false;
            }

            string normalizedPath = targetPath.StartsWith("/", StringComparison.Ordinal)
                ? targetPath
                : $"/{targetPath}";
            var builder = new UriBuilder(sourceUri)
            {
                Path = normalizedPath,
                Query = string.Empty
            };
            endpoint = builder.Uri.ToString();
            return true;
        }

        private static string ResolveLocalAssetRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            string trimmed = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(trimmed))
            {
                try
                {
                    return Path.GetFullPath(trimmed);
                }
                catch
                {
                    return trimmed;
                }
            }

            List<string> candidates = BuildRuntimeRelativePathCandidates(trimmed);
            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates.Count > 0
                ? candidates[0]
                : Path.Combine(Application.dataPath, trimmed);
        }

        private static List<string> BuildRuntimeRelativePathCandidates(string relativePath)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return candidates;
            }

            string trimmed = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(trimmed))
            {
                AddRuntimeCandidate(candidates, trimmed);
                return candidates;
            }

            string dataPath = Application.dataPath ?? string.Empty;
            string dataParent = Path.GetDirectoryName(dataPath) ?? string.Empty;
            string streamingAssetsPath = Application.streamingAssetsPath ?? string.Empty;
            string persistentDataPath = Application.persistentDataPath ?? string.Empty;
            string currentDirectory = Environment.CurrentDirectory ?? string.Empty;

            AddRuntimeCandidate(candidates, Path.Combine(dataPath, trimmed));
            AddRuntimeCandidate(candidates, Path.Combine(streamingAssetsPath, trimmed));
            AddRuntimeCandidate(candidates, Path.Combine(dataParent, trimmed));
            AddRuntimeCandidate(candidates, Path.Combine(dataParent, "Assets", trimmed));
            AddRuntimeCandidate(candidates, Path.Combine(currentDirectory, trimmed));
            AddRuntimeCandidate(candidates, Path.Combine(currentDirectory, "Assets", trimmed));
            AddRuntimeCandidate(candidates, Path.Combine(persistentDataPath, trimmed));
            AddRuntimeCandidate(candidates, Path.Combine(persistentDataPath, "Assets", trimmed));

            string cursor = dataParent;
            for (int depth = 0; depth < 6 && !string.IsNullOrWhiteSpace(cursor); depth++)
            {
                AddRuntimeCandidate(candidates, Path.Combine(cursor, trimmed));
                AddRuntimeCandidate(candidates, Path.Combine(cursor, "Assets", trimmed));
                cursor = Path.GetDirectoryName(cursor);
            }

            return candidates;
        }

        private static void AddRuntimeCandidate(List<string> candidates, string path)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                fullPath = path.Trim();
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i], fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(fullPath);
        }

        private static string SummarizeCandidatePaths(IReadOnlyList<string> candidates, int maxCount)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return "(none)";
            }

            int count = Math.Max(1, Math.Min(maxCount, candidates.Count));
            var builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(candidates[i]);
            }

            if (candidates.Count > count)
            {
                builder.Append(" | ...");
            }

            return builder.ToString();
        }

        private static string GetProjectRootPath()
        {
            string dataParent = Path.GetDirectoryName(Application.dataPath);
            if (TryFindProjectRootFrom(dataParent, out string discoveredFromData))
            {
                return discoveredFromData;
            }

            string currentDirectory = Environment.CurrentDirectory;
            if (TryFindProjectRootFrom(currentDirectory, out string discoveredFromCurrent))
            {
                return discoveredFromCurrent;
            }

            return string.IsNullOrWhiteSpace(dataParent) ? Application.dataPath : dataParent;
        }

        private static bool TryFindProjectRootFrom(string startPath, out string rootPath)
        {
            rootPath = string.Empty;
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return false;
            }

            string cursor;
            try
            {
                cursor = Path.GetFullPath(startPath);
            }
            catch
            {
                cursor = startPath;
            }

            for (int depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(cursor); depth++)
            {
                string marker = Path.Combine(cursor, "Assets", "ReachyControlApp");
                if (Directory.Exists(marker))
                {
                    rootPath = cursor;
                    return true;
                }

                cursor = Path.GetDirectoryName(cursor);
            }

            return false;
        }

        private static string ResolveUiHelpModelPathToAbsolute(string rawPath, string sidecarConfigPath)
        {
            string trimmed = (rawPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            string normalized = trimmed.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
            {
                try
                {
                    return Path.GetFullPath(normalized);
                }
                catch
                {
                    return normalized;
                }
            }

            string projectRoot = GetProjectRootPath();
            string projectCandidate;
            try
            {
                projectCandidate = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            }
            catch
            {
                projectCandidate = Path.Combine(projectRoot, normalized);
            }

            string sidecarDirectory = string.IsNullOrWhiteSpace(sidecarConfigPath)
                ? string.Empty
                : Path.GetDirectoryName(sidecarConfigPath);
            string sidecarCandidate = string.Empty;
            if (!string.IsNullOrWhiteSpace(sidecarDirectory))
            {
                try
                {
                    sidecarCandidate = Path.GetFullPath(Path.Combine(sidecarDirectory, normalized));
                }
                catch
                {
                    sidecarCandidate = Path.Combine(sidecarDirectory, normalized);
                }
            }

            bool projectExists = !string.IsNullOrWhiteSpace(projectCandidate) && File.Exists(projectCandidate);
            bool sidecarExists = !string.IsNullOrWhiteSpace(sidecarCandidate) && File.Exists(sidecarCandidate);
            if (sidecarExists && !projectExists)
            {
                return sidecarCandidate;
            }

            return projectCandidate;
        }

        private static bool TryMakeRelativePath(string baseDirectory, string targetPath, out string relativePath)
        {
            relativePath = string.Empty;
            if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            try
            {
                string baseFullPath = Path.GetFullPath(baseDirectory);
                if (!baseFullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    baseFullPath += Path.DirectorySeparatorChar;
                }

                string targetFullPath = Path.GetFullPath(targetPath);
                Uri baseUri = new Uri(baseFullPath);
                Uri targetUri = new Uri(targetFullPath);
                if (!string.Equals(baseUri.Scheme, targetUri.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string relative =
                    Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString())
                        .Replace('/', Path.DirectorySeparatorChar);
                relativePath = relative;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildSidecarHelpModelPathFromUi(string uiPath, string sidecarConfigPath)
        {
            string absolutePath = ResolveUiHelpModelPathToAbsolute(uiPath, sidecarConfigPath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            string sidecarDirectory = string.IsNullOrWhiteSpace(sidecarConfigPath)
                ? string.Empty
                : Path.GetDirectoryName(sidecarConfigPath);
            if (!string.IsNullOrWhiteSpace(sidecarDirectory) &&
                TryMakeRelativePath(sidecarDirectory, absolutePath, out string relativePath) &&
                !string.IsNullOrWhiteSpace(relativePath))
            {
                return relativePath.Replace('\\', '/');
            }

            return absolutePath.Replace('\\', '/');
        }

        private static string BuildUiHelpModelPathFromSidecar(string sidecarPath, string sidecarConfigPath)
        {
            string trimmed = (sidecarPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            string normalized = trimmed.Replace('/', Path.DirectorySeparatorChar);
            string absolutePath;
            if (Path.IsPathRooted(normalized))
            {
                try
                {
                    absolutePath = Path.GetFullPath(normalized);
                }
                catch
                {
                    absolutePath = normalized;
                }
            }
            else
            {
                string sidecarDirectory = string.IsNullOrWhiteSpace(sidecarConfigPath)
                    ? string.Empty
                    : Path.GetDirectoryName(sidecarConfigPath);
                string combinedBase = string.IsNullOrWhiteSpace(sidecarDirectory)
                    ? GetProjectRootPath()
                    : sidecarDirectory;
                try
                {
                    absolutePath = Path.GetFullPath(Path.Combine(combinedBase, normalized));
                }
                catch
                {
                    absolutePath = Path.Combine(combinedBase, normalized);
                }
            }

            string projectRoot = GetProjectRootPath();
            if (TryMakeRelativePath(projectRoot, absolutePath, out string relativeToProject) &&
                !string.IsNullOrWhiteSpace(relativeToProject))
            {
                return relativeToProject.Replace('\\', '/');
            }

            return absolutePath.Replace('\\', '/');
        }

        private static string NormalizeHelpModelBackend(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "llama_cpp")
            {
                return normalized;
            }

            return "rule_based";
        }

        private bool IsBridgeHealthyForMotion(out string reason)
        {
            reason = string.Empty;
            if (_voiceAgentBridge == null)
            {
                reason = "voice bridge is not initialized";
                return false;
            }

            VoiceAgentBridge.BridgeSnapshot snapshot = _voiceAgentBridge.GetSnapshot();
            if (snapshot.DegradedMode)
            {
                reason = "voice bridge is in degraded mode";
                return false;
            }

            if (snapshot.HeartbeatExpired)
            {
                reason = "voice bridge heartbeat is stale";
                return false;
            }

            return true;
        }

        private void ForceVoiceBridgePoll()
        {
            if (_voiceAgentBridge == null)
            {
                _voiceLastActionResult = "Cannot force poll: voice bridge is not initialized.";
                return;
            }

            _voiceAgentBridge.RequestImmediatePoll(Time.unscaledTime);
            _voiceLastActionResult = "Requested immediate voice bridge poll.";
        }

        private void ResetVoiceBridgeState()
        {
            if (_voiceAgentBridge == null)
            {
                _voiceLastActionResult = "Cannot reset bridge: voice bridge is not initialized.";
                return;
            }

            _voiceAgentBridge.ResetHealthState(Time.unscaledTime);
            _voiceLastActionResult = "Voice bridge state reset requested.";
        }

        private void ExportVoiceBridgeLogs()
        {
            if (_voiceAgentBridge == null)
            {
                _voiceLastActionResult = "Cannot export logs: voice bridge is not initialized.";
                return;
            }

            try
            {
                string[] lines = _voiceAgentBridge.GetRecentLogLines(400);
                string logsDir = Path.Combine(Application.dataPath, "ReachyControlApp", "Logs");
                Directory.CreateDirectory(logsDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string filePath = Path.Combine(logsDir, $"voice_bridge_{stamp}.log");
                if (lines == null || lines.Length == 0)
                {
                    lines = new[] { $"{DateTime.UtcNow:O} [info] bridge: no log lines available." };
                }

                File.WriteAllLines(filePath, lines, Encoding.UTF8);
                _voiceLastLogExportPath = filePath;
                _voiceLastActionResult = $"Exported voice bridge logs to {filePath}";
            }
            catch (Exception ex)
            {
                _voiceLastActionResult = $"Failed to export voice bridge logs: {ex.Message}";
            }
        }

        private void ClearVoiceBridgeLogs()
        {
            if (_voiceAgentBridge == null)
            {
                _voiceLastActionResult = "Cannot clear logs: voice bridge is not initialized.";
                return;
            }

            _voiceAgentBridge.ClearLogs();
            _voiceLastActionResult = "Cleared in-memory voice bridge logs.";
        }

        private bool QueueLocalHelpRequest(out string message)
        {
            message = string.Empty;
            if (_voiceAgentBridge == null)
            {
                message = "Cannot request local help: voice bridge is not initialized.";
                return false;
            }

            if (!localAiAgentEnableLocalHelpModel)
            {
                message = "Local help model is disabled.";
                return false;
            }

            string query = string.IsNullOrWhiteSpace(_voiceLastTranscript)
                ? "How do I use the Reachy Unity control app?"
                : _voiceLastTranscript.Trim();
            string context = localAiAgentHelpContext ?? string.Empty;
            _voiceAgentBridge.EnqueueHelpRequest(query, context);
            message = "Requested local help response from sidecar model.";
            return true;
        }

        private bool ExecuteVoiceAction(VoiceCommandRouter.RoutedAction action, out string message)
        {
            message = string.Empty;
            LogRuntimeEvent("voice", "action-received", $"{action.Kind}: {action.Summary}");
            if (IsActionBlockedBySimulationOnlyMode(action, out string blockReason))
            {
                message = blockReason;
                LogRuntimeEvent("voice", "action-blocked", blockReason, "WARN");
                return false;
            }

            switch (action.Kind)
            {
                case VoiceCommandRouter.VoiceActionKind.Help:
                    if (localAiAgentEnableLocalHelpModel)
                    {
                        bool queued = QueueLocalHelpRequest(out string helpMessage);
                        if (queued)
                        {
                            message = helpMessage;
                            SetStatus("Voice help requested", helpMessage);
                            return true;
                        }
                    }

                    message = "Help: use voice intents like hello, who_are_you, set_pose, move_joint, show_movement, status, connect_robot, disconnect_robot, stop_motion, confirm_pending, reject_pending.";
                    return true;

                case VoiceCommandRouter.VoiceActionKind.Status:
                    if (_client != null && _client.IsConnected)
                    {
                        message = $"Connected to {_client.ConnectedHost}:{_client.ConnectedPort} ({GetConnectedModeLabel()}).";
                    }
                    else
                    {
                        message = "No active robot connection.";
                    }
                    return true;

                case VoiceCommandRouter.VoiceActionKind.ConnectRobot:
                {
                    bool started = TryStartConnectAttempt("Voice connect", mode, 0.5f, ensureOneUiFrameBeforeConnect: true);
                    message = started
                        ? $"Started voice-driven connect attempt for {GetModeLabel(mode)}."
                        : "Could not start voice connect attempt.";
                    LogConnectionEvent(
                        mode,
                        "voice-connect",
                        message,
                        started ? "INFO" : "WARN");
                    return started;
                }

                case VoiceCommandRouter.VoiceActionKind.DisconnectRobot:
                    if (_client == null || !_client.IsConnected)
                    {
                        message = "Disconnect ignored: not currently connected.";
                        LogRuntimeEvent("voice", "disconnect-ignored", message, "WARN");
                        return false;
                    }

                    StopVoiceShowMovementSequence(updateStatus: false, reason: "Disconnected by voice command.");
                    StopVoiceHelloReturnTimer(updateStatus: false, reason: "Disconnected by voice command.");
                    ReachyControlMode disconnectedMode = _connectedMode ?? mode;
                    _manualDisconnect = true;
                    _autoReconnectScheduled = false;
                    _client.Disconnect();
                    _connectedMode = null;
                    message = "Disconnected by voice command.";
                    LogConnectionEvent(disconnectedMode, "voice-disconnect", message);
                    SetStatus("Voice disconnect", message);
                    return true;

                case VoiceCommandRouter.VoiceActionKind.ConfirmPending:
                    if (!_voiceHasPendingAction)
                    {
                        message = "Confirm ignored: no pending voice action.";
                        return false;
                    }

                    ConfirmPendingVoiceAction(queueFeedback: false);
                    message = _voiceLastActionResult;
                    return true;

                case VoiceCommandRouter.VoiceActionKind.RejectPending:
                    if (!_voiceHasPendingAction)
                    {
                        message = "Reject ignored: no pending voice action.";
                        return false;
                    }

                    RejectPendingVoiceAction(queueFeedback: false);
                    message = _voiceLastActionResult;
                    return true;

                case VoiceCommandRouter.VoiceActionKind.SetPose:
                    if (_client == null || !_client.IsConnected)
                    {
                        message = "Pose command blocked: robot is not connected.";
                        LogRuntimeEvent("voice", "set-pose-blocked", message, "WARN");
                        return false;
                    }

                    StopVoiceShowMovementSequence(updateStatus: false, reason: "Interrupted by set_pose command.");
                    StopVoiceHelloReturnTimer(updateStatus: false, reason: "Interrupted by set_pose command.");
                    bool setPoseTargetsRealRobot = IsRealRobotSessionActive();
                    LogMotionEvent(
                        "voice",
                        "set-pose-attempt",
                        $"pose={action.PoseName}; summary={action.Summary}",
                        success: true,
                        targetsRealRobot: setPoseTargetsRealRobot);
                    bool wasConnected = _client.IsConnected;
                    bool poseOk = _client.SendPresetPose(action.PoseName, out string poseMessage);
                    HandlePotentialDisconnectAfterOperation($"voice pose '{action.PoseName}'", wasConnected);
                    LogMotionEvent(
                        "voice",
                        "set-pose-result",
                        $"pose={action.PoseName}; result={(poseOk ? "ok" : "failed")}; detail={poseMessage}",
                        success: poseOk,
                        targetsRealRobot: setPoseTargetsRealRobot);
                    message = poseOk
                        ? $"Voice pose '{action.PoseName}' sent. {poseMessage}"
                        : $"Voice pose '{action.PoseName}' failed. {poseMessage}";
                    SetStatus(poseOk ? "Voice pose sent" : "Voice pose failed", message);
                    return poseOk;

                case VoiceCommandRouter.VoiceActionKind.MoveJoint:
                    if (_client == null || !_client.IsConnected)
                    {
                        message = "Move-joint command blocked: robot is not connected.";
                        LogRuntimeEvent("voice", "move-joint-blocked", message, "WARN");
                        return false;
                    }

                    StopVoiceShowMovementSequence(updateStatus: false, reason: "Interrupted by move_joint command.");
                    StopVoiceHelloReturnTimer(updateStatus: false, reason: "Interrupted by move_joint command.");
                    bool moveJointTargetsRealRobot = IsRealRobotSessionActive();
                    LogMotionEvent(
                        "voice",
                        "move-joint-attempt",
                        $"joint={action.JointName}; degrees={action.JointDegrees.ToString("F3", CultureInfo.InvariantCulture)}; summary={action.Summary}",
                        success: true,
                        targetsRealRobot: moveJointTargetsRealRobot);
                    bool wasConnectedBeforeJoint = _client.IsConnected;
                    bool jointOk = _client.SendSingleJointGoal(action.JointName, action.JointDegrees, out string jointMessage);
                    HandlePotentialDisconnectAfterOperation($"voice move_joint '{action.JointName}'", wasConnectedBeforeJoint);
                    LogMotionEvent(
                        "voice",
                        "move-joint-result",
                        $"joint={action.JointName}; degrees={action.JointDegrees.ToString("F3", CultureInfo.InvariantCulture)}; result={(jointOk ? "ok" : "failed")}; detail={jointMessage}",
                        success: jointOk,
                        targetsRealRobot: moveJointTargetsRealRobot);
                    message = jointOk
                        ? $"Voice joint command sent for '{action.JointName}'. {jointMessage}"
                        : $"Voice joint command failed for '{action.JointName}'. {jointMessage}";
                    SetStatus(jointOk ? "Voice joint command sent" : "Voice joint command failed", message);
                    return jointOk;

                case VoiceCommandRouter.VoiceActionKind.ShowMovement:
                    return TryStartVoiceShowMovementSequence(out message);

                case VoiceCommandRouter.VoiceActionKind.Hello:
                    if (_client == null || !_client.IsConnected)
                    {
                        message = "Hello command blocked: robot is not connected.";
                        LogRuntimeEvent("voice", "hello-blocked", message, "WARN");
                        return false;
                    }

                    StopVoiceShowMovementSequence(updateStatus: false, reason: "Interrupted by hello command.");
                    StopVoiceHelloReturnTimer(updateStatus: false, reason: "Restarted by hello command.");
                    bool helloTargetsRealRobot = IsRealRobotSessionActive();
                    LogMotionEvent(
                        "voice",
                        "hello-attempt",
                        $"pose={VoiceCommandRouter.HelloPoseName}",
                        success: true,
                        targetsRealRobot: helloTargetsRealRobot);
                    bool wasConnectedBeforeHello = _client.IsConnected;
                    bool helloPoseOk = _client.SendPresetPose(
                        VoiceCommandRouter.HelloPoseName,
                        out string helloPoseMessage);
                    HandlePotentialDisconnectAfterOperation(
                        $"voice hello pose '{VoiceCommandRouter.HelloPoseName}'",
                        wasConnectedBeforeHello);
                    LogMotionEvent(
                        "voice",
                        "hello-result",
                        $"pose={VoiceCommandRouter.HelloPoseName}; result={(helloPoseOk ? "ok" : "failed")}; detail={helloPoseMessage}",
                        success: helloPoseOk,
                        targetsRealRobot: helloTargetsRealRobot);
                    if (helloPoseOk)
                    {
                        _voiceHelloReturnCoroutine = StartCoroutine(
                            VoiceHelloReturnToNeutralCoroutine(VoiceHelloReturnDelaySeconds));
                    }

                    message = helloPoseOk
                        ? VoiceCommandRouter.HelloResponseText
                        : $"{VoiceCommandRouter.HelloResponseText} (failed to set '{VoiceCommandRouter.HelloPoseName}': {helloPoseMessage})";
                    SetStatus(helloPoseOk ? "Voice hello" : "Voice hello failed", message);
                    return helloPoseOk;

                case VoiceCommandRouter.VoiceActionKind.WhoAreYou:
                    message = VoiceCommandRouter.WhoAreYouResponseText;
                    SetStatus("Voice identity", message);
                    return true;

                case VoiceCommandRouter.VoiceActionKind.StopMotion:
                    bool hadPending = _voiceHasPendingAction;
                    bool stoppedSequence = StopVoiceShowMovementSequence(
                        updateStatus: false,
                        reason: "Stopped by voice command.");
                    StopVoiceHelloReturnTimer(updateStatus: false, reason: "Stopped by voice command.");
                    _voiceHasPendingAction = false;
                    _voicePendingAction = default(VoiceCommandRouter.RoutedAction);
                    message = hadPending || stoppedSequence
                        ? BuildStopAcknowledgementMessage(hadPending, stoppedSequence)
                        : "Stop command acknowledged. No explicit robot stop API is available yet.";
                    LogMotionEvent(
                        "voice",
                        "stop-motion",
                        $"hadPending={hadPending}; stoppedSequence={stoppedSequence}; message={message}",
                        success: true,
                        targetsRealRobot: IsRealRobotSessionActive());
                    SetStatus("Voice stop", message);
                    return true;

                default:
                    message = "Voice action is not implemented.";
                    return false;
            }
        }

        private bool TryStartVoiceShowMovementSequence(out string message)
        {
            message = string.Empty;
            if (_client == null || !_client.IsConnected)
            {
                message = "Show movement command blocked: robot is not connected.";
                LogRuntimeEvent("voice", "show-movement-blocked", message, "WARN");
                return false;
            }

            IReadOnlyList<string> availablePoses = _client.PresetPoseNames;
            if (availablePoses == null || availablePoses.Count == 0)
            {
                message = "Show movement command blocked: no preset poses are available.";
                LogRuntimeEvent("voice", "show-movement-blocked", message, "WARN");
                return false;
            }

            StopVoiceShowMovementSequence(updateStatus: false, reason: "Restarted by show movement command.");
            StopVoiceHelloReturnTimer(updateStatus: false, reason: "Interrupted by show movement command.");
            List<string> selectedPoses = BuildRandomPoseSequence(availablePoses, VoiceShowMovementPoseCount);
            if (selectedPoses.Count == 0)
            {
                message = "Show movement command blocked: could not select random poses.";
                LogRuntimeEvent("voice", "show-movement-blocked", message, "WARN");
                return false;
            }

            bool targetsRealRobot = IsRealRobotSessionActive();
            LogMotionEvent(
                "voice",
                "show-movement-start",
                $"poseCount={selectedPoses.Count}; poses={string.Join(", ", selectedPoses)}",
                success: true,
                targetsRealRobot: targetsRealRobot);
            _voiceShowMovementCoroutine = StartCoroutine(VoiceShowMovementSequenceCoroutine(selectedPoses));
            message =
                $"Started show movement: {selectedPoses.Count} random poses with {VoiceShowMovementIntervalSeconds:F0}s spacing.";
            SetStatus("Voice show movement", message);
            return true;
        }

        private IEnumerator VoiceShowMovementSequenceCoroutine(IReadOnlyList<string> poseNames)
        {
            if (poseNames == null || poseNames.Count == 0)
            {
                _voiceShowMovementCoroutine = null;
                yield break;
            }

            float intervalSeconds = Mathf.Max(0f, VoiceShowMovementIntervalSeconds);
            for (int i = 0; i < poseNames.Count; i++)
            {
                if (_client == null || !_client.IsConnected)
                {
                    string disconnectedMessage = "Show movement stopped: robot disconnected.";
                    _voiceLastActionResult = disconnectedMessage;
                    LogRuntimeEvent("voice", "show-movement-stopped", disconnectedMessage, "WARN");
                    SetStatus("Voice show movement stopped", disconnectedMessage);
                    _voiceShowMovementCoroutine = null;
                    yield break;
                }

                string poseName = poseNames[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(poseName))
                {
                    continue;
                }

                bool wasConnected = _client.IsConnected;
                bool targetsRealRobot = IsRealRobotSessionActive();
                bool poseOk = _client.SendPresetPose(poseName, out string poseMessage);
                HandlePotentialDisconnectAfterOperation($"voice show_movement pose '{poseName}'", wasConnected);
                LogMotionEvent(
                    "voice",
                    "show-movement-pose",
                    $"index={i + 1}/{poseNames.Count}; pose={poseName}; result={(poseOk ? "ok" : "failed")}; detail={poseMessage}",
                    success: poseOk,
                    targetsRealRobot: targetsRealRobot);

                string perPoseMessage = poseOk
                    ? $"Show movement pose {i + 1}/{poseNames.Count} sent ('{poseName}'). {poseMessage}"
                    : $"Show movement pose {i + 1}/{poseNames.Count} failed ('{poseName}'). {poseMessage}";
                _voiceLastActionResult = perPoseMessage;
                SetStatus(
                    poseOk ? "Voice show movement pose sent" : "Voice show movement pose failed",
                    perPoseMessage);

                if (i < poseNames.Count - 1 && intervalSeconds > 0f)
                {
                    yield return new WaitForSecondsRealtime(intervalSeconds);
                }
            }

            string completedMessage = $"Show movement completed ({poseNames.Count} poses).";
            _voiceLastActionResult = completedMessage;
            LogMotionEvent(
                "voice",
                "show-movement-complete",
                completedMessage,
                success: true,
                targetsRealRobot: IsRealRobotSessionActive());
            SetStatus("Voice show movement", completedMessage);
            _voiceShowMovementCoroutine = null;
        }

        private static List<string> BuildRandomPoseSequence(IReadOnlyList<string> availablePoses, int desiredCount)
        {
            var uniquePoses = new List<string>();
            if (availablePoses == null || desiredCount <= 0)
            {
                return uniquePoses;
            }

            for (int i = 0; i < availablePoses.Count; i++)
            {
                string pose = (availablePoses[i] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(pose))
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < uniquePoses.Count; j++)
                {
                    if (string.Equals(uniquePoses[j], pose, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    uniquePoses.Add(pose);
                }
            }

            for (int i = uniquePoses.Count - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                string tmp = uniquePoses[i];
                uniquePoses[i] = uniquePoses[swapIndex];
                uniquePoses[swapIndex] = tmp;
            }

            int uniqueCount = Math.Min(desiredCount, uniquePoses.Count);
            var sequence = new List<string>(desiredCount);
            for (int i = 0; i < uniqueCount; i++)
            {
                sequence.Add(uniquePoses[i]);
            }

            while (sequence.Count < desiredCount && uniquePoses.Count > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, uniquePoses.Count);
                sequence.Add(uniquePoses[randomIndex]);
            }

            return sequence;
        }

        private bool StopVoiceShowMovementSequence(bool updateStatus, string reason)
        {
            if (_voiceShowMovementCoroutine == null)
            {
                return false;
            }

            StopCoroutine(_voiceShowMovementCoroutine);
            _voiceShowMovementCoroutine = null;
            LogRuntimeEvent(
                "voice",
                "show-movement-stopped",
                string.IsNullOrWhiteSpace(reason) ? "Show movement sequence stopped." : reason,
                "WARN");

            if (updateStatus)
            {
                string finalReason = string.IsNullOrWhiteSpace(reason)
                    ? "Show movement sequence stopped."
                    : $"Show movement sequence stopped: {reason}";
                _voiceLastActionResult = finalReason;
                SetStatus("Voice show movement stopped", finalReason);
            }

            return true;
        }

        private IEnumerator VoiceHelloReturnToNeutralCoroutine(float delaySeconds)
        {
            float safeDelay = Mathf.Max(0f, delaySeconds);
            if (safeDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(safeDelay);
            }

            _voiceHelloReturnCoroutine = null;
            if (_client == null || !_client.IsConnected)
            {
                LogRuntimeEvent("voice", "hello-return-skipped", "Return-to-neutral skipped because robot is disconnected.", "WARN");
                yield break;
            }

            bool wasConnected = _client.IsConnected;
            bool targetsRealRobot = IsRealRobotSessionActive();
            LogMotionEvent(
                "voice",
                "hello-return-attempt",
                $"pose={VoiceHelloReturnPoseName}",
                success: true,
                targetsRealRobot: targetsRealRobot);
            bool neutralOk = _client.SendPresetPose(VoiceHelloReturnPoseName, out string neutralMessage);
            HandlePotentialDisconnectAfterOperation(
                $"voice hello return pose '{VoiceHelloReturnPoseName}'",
                wasConnected);
            LogMotionEvent(
                "voice",
                "hello-return-result",
                $"pose={VoiceHelloReturnPoseName}; result={(neutralOk ? "ok" : "failed")}; detail={neutralMessage}",
                success: neutralOk,
                targetsRealRobot: targetsRealRobot);

            string message = neutralOk
                ? $"Returned to neutral pose '{VoiceHelloReturnPoseName}' after hello."
                : $"Failed to return to neutral pose '{VoiceHelloReturnPoseName}' after hello. {neutralMessage}";
            _voiceLastActionResult = message;
            SetStatus(
                neutralOk ? "Voice hello neutral pose sent" : "Voice hello neutral pose failed",
                message);
        }

        private bool StopVoiceHelloReturnTimer(bool updateStatus, string reason)
        {
            if (_voiceHelloReturnCoroutine == null)
            {
                return false;
            }

            StopCoroutine(_voiceHelloReturnCoroutine);
            _voiceHelloReturnCoroutine = null;
            LogRuntimeEvent(
                "voice",
                "hello-return-timer-stopped",
                string.IsNullOrWhiteSpace(reason) ? "Hello return timer stopped." : reason,
                "WARN");

            if (updateStatus)
            {
                string finalReason = string.IsNullOrWhiteSpace(reason)
                    ? "Hello return-to-neutral timer stopped."
                    : $"Hello return-to-neutral timer stopped: {reason}";
                _voiceLastActionResult = finalReason;
                SetStatus("Voice hello timer stopped", finalReason);
            }

            return true;
        }

        private static string BuildStopAcknowledgementMessage(bool hadPendingAction, bool stoppedShowMovement)
        {
            if (hadPendingAction && stoppedShowMovement)
            {
                return "Stop command acknowledged. Pending voice action was cancelled and show movement sequence was stopped.";
            }

            if (hadPendingAction)
            {
                return "Stop command acknowledged. Pending voice action was cancelled.";
            }

            if (stoppedShowMovement)
            {
                return "Stop command acknowledged. Show movement sequence was stopped.";
            }

            return "Stop command acknowledged. No explicit robot stop API is available yet.";
        }

        private bool IsActionBlockedBySimulationOnlyMode(
            VoiceCommandRouter.RoutedAction action,
            out string reason)
        {
            reason = string.Empty;
            if (!localAiAgentSimulationOnlyMode)
            {
                return false;
            }

            if (action.Kind != VoiceCommandRouter.VoiceActionKind.ConnectRobot &&
                action.Kind != VoiceCommandRouter.VoiceActionKind.SetPose &&
                action.Kind != VoiceCommandRouter.VoiceActionKind.MoveJoint &&
                action.Kind != VoiceCommandRouter.VoiceActionKind.ShowMovement &&
                action.Kind != VoiceCommandRouter.VoiceActionKind.Hello)
            {
                return false;
            }

            bool targetsRealRobotConnect = action.Kind == VoiceCommandRouter.VoiceActionKind.ConnectRobot &&
                mode == ReachyControlMode.RealRobot;
            bool currentlyControllingRealRobot = action.Kind != VoiceCommandRouter.VoiceActionKind.ConnectRobot &&
                IsConnectionForMode(ReachyControlMode.RealRobot);

            if (!targetsRealRobotConnect && !currentlyControllingRealRobot)
            {
                return false;
            }

            reason = action.Kind == VoiceCommandRouter.VoiceActionKind.ConnectRobot
                ? "Voice connect blocked: simulation-only mode is enabled and target mode is Real Robot."
                : "Voice motion blocked: simulation-only mode is enabled for this session.";
            return true;
        }

        private void EnqueueMockPoseIntent(string poseName)
        {
            if (_voiceAgentBridge == null)
            {
                return;
            }

            _voiceAgentBridge.EnqueueMockIntent(new VoiceAgentIntent
            {
                type = "robot_command",
                intent = "set_pose",
                pose_name = poseName,
                confidence = 0.92f,
                requires_confirmation = true,
                spoken_text = $"set {poseName}"
            });
        }

        private void EnqueueMockStatusIntent()
        {
            if (_voiceAgentBridge == null)
            {
                return;
            }

            _voiceAgentBridge.EnqueueMockIntent(new VoiceAgentIntent
            {
                type = "robot_command",
                intent = "status",
                confidence = 0.95f,
                requires_confirmation = false,
                spoken_text = "what is robot status"
            });
        }

        private void EnqueueMockHelpIntent()
        {
            if (_voiceAgentBridge == null)
            {
                return;
            }

            _voiceAgentBridge.EnqueueMockIntent(new VoiceAgentIntent
            {
                type = "robot_command",
                intent = "help",
                confidence = 0.99f,
                requires_confirmation = false,
                spoken_text = "help me use reachy"
            });
        }

        private void EnqueueMockTranscriptIntent(
            string transcript,
            float transcriptConfidence,
            bool transcriptIsFinal)
        {
            if (_voiceAgentBridge == null)
            {
                return;
            }

            string safeTranscript = string.IsNullOrWhiteSpace(transcript)
                ? "status"
                : transcript.Trim();

            _voiceAgentBridge.EnqueueMockIntent(new VoiceAgentIntent
            {
                type = "transcript",
                intent = string.Empty,
                confidence = Mathf.Clamp01(transcriptConfidence),
                requires_confirmation = false,
                spoken_text = safeTranscript,
                transcript_is_final = transcriptIsFinal,
                raw_json = "mock_transcript"
            });
        }

        private void RefreshVoiceAgentStatusState()
        {
            if (_voiceAgentBridge == null)
            {
                return;
            }

            VoiceAgentBridge.BridgeSnapshot snapshot = _voiceAgentBridge.GetSnapshot();
            _voiceAgentStatusState = new VoiceAgentStatusPanel.State
            {
                AgentEnabled = snapshot.Enabled,
                BridgeReachable = snapshot.BridgeReachable,
                PollInFlight = snapshot.PollInFlight,
                DegradedMode = snapshot.DegradedMode,
                HeartbeatExpired = snapshot.HeartbeatExpired,
                Endpoint = snapshot.Endpoint,
                QueuedIntents = snapshot.QueuedIntents,
                ReceivedIntents = snapshot.ReceivedIntentCount,
                PollFailures = snapshot.FailedPollCount,
                ConsecutivePollFailures = snapshot.ConsecutivePollFailures,
                SecondsSinceLastHealthyPoll = snapshot.SecondsSinceLastHealthyPoll,
                EventLogCount = snapshot.EventLogCount,
                MicActive = snapshot.MicActive,
                Listening = snapshot.Listening,
                SttBackend = snapshot.SttBackend,
                LastTranscriptIsFinal = snapshot.LastTranscriptIsFinal,
                LastTranscriptConfidence = snapshot.LastTranscriptConfidence,
                PushToTalkEnabled = localAiAgentEnablePushToTalk,
                PushToTalkKey = localAiAgentPushToTalkKey,
                RequestedListeningEnabled = _voiceLastRequestedListeningEnabled ?? localAiAgentListeningEnabled,
                SimulationOnlyMode = localAiAgentSimulationOnlyMode,
                DuplicateSuppressionEnabled = localAiAgentSuppressDuplicateCommands,
                DuplicateSuppressionWindowSeconds = localAiAgentDuplicateCommandWindowSeconds,
                SafeNumericParsingEnabled = localAiAgentUseSafeNumericParsing,
                RequireTargetTokenForJoint = localAiAgentRequireTargetTokenForJoint,
                RejectOutOfRangeJointCommands = localAiAgentRejectOutOfRangeJointCommands,
                JointMinDegrees = localAiAgentJointMinDegrees,
                JointMaxDegrees = localAiAgentJointMaxDegrees,
                MinTranscriptChars = localAiAgentMinTranscriptChars,
                MinTranscriptWords = localAiAgentMinTranscriptWords,
                ListeningToggleInFlight = snapshot.ListeningToggleInFlight,
                ListeningEndpoint = snapshot.ListeningEndpoint,
                SuccessfulListeningToggleRequests = snapshot.SuccessfulListeningToggleCount,
                FailedListeningToggleRequests = snapshot.FailedListeningToggleCount,
                LastListeningToggleMessage = snapshot.LastListeningToggleMessage,
                LastListeningToggleError = snapshot.LastListeningToggleError,
                TtsEnabled = localAiAgentEnableTtsFeedback,
                TtsInFlight = snapshot.TtsInFlight,
                TtsEndpoint = snapshot.TtsEndpoint,
                SuccessfulTtsRequests = snapshot.SuccessfulTtsCount,
                FailedTtsRequests = snapshot.FailedTtsCount,
                LastTtsMessage = snapshot.LastTtsMessage,
                LastTtsError = snapshot.LastTtsError,
                LastSpokenFeedback = _voiceLastSpokenFeedback,
                LocalHelpEnabled = localAiAgentEnableLocalHelpModel,
                HelpInFlight = snapshot.HelpInFlight,
                HelpEndpoint = snapshot.HelpEndpoint,
                SuccessfulHelpRequests = snapshot.SuccessfulHelpCount,
                FailedHelpRequests = snapshot.FailedHelpCount,
                LastHelpMessage = snapshot.LastHelpMessage,
                LastHelpError = snapshot.LastHelpError,
                LastHelpAnswer = string.IsNullOrWhiteSpace(snapshot.LastHelpAnswer)
                    ? _voiceLastHelpAnswer
                    : snapshot.LastHelpAnswer,
                LastBridgeMessage = snapshot.LastMessage,
                LastTranscript = string.IsNullOrWhiteSpace(snapshot.LastTranscript)
                    ? _voiceLastTranscript
                    : snapshot.LastTranscript,
                LastParserMessage = _voiceLastParserMessage,
                LastIntentSummary = _voiceLastIntentSummary,
                LastActionResult = _voiceLastActionResult,
                PendingActionSummary = _voiceHasPendingAction ? _voicePendingAction.Summary : string.Empty,
                LastBridgeLogLine = snapshot.LastLogLine
            };
        }

        private string GetVoiceAgentConfigPath()
        {
            string relativePath = Path.Combine("ReachyControlApp", "voice_agent_config.json");
            string resolved = ResolveLocalAssetRelativePath(relativePath);
            if (File.Exists(resolved) || Application.isEditor)
            {
                return resolved;
            }

            return Path.Combine(
                Application.persistentDataPath,
                "ReachyControlApp",
                "voice_agent_config.json");
        }

        private string GetLocalAiSidecarConfigPath()
        {
            if (string.IsNullOrWhiteSpace(localAiAgentSidecarConfigRelativePath))
            {
                return string.Empty;
            }

            string resolved = ResolveLocalAssetRelativePath(localAiAgentSidecarConfigRelativePath);
            if (File.Exists(resolved))
            {
                return resolved;
            }

            string scriptPath = ResolveLocalAssetRelativePath(localAiAgentSidecarScriptRelativePath);
            if (!string.IsNullOrWhiteSpace(scriptPath) && File.Exists(scriptPath))
            {
                string configFileName = Path.GetFileName(
                    localAiAgentSidecarConfigRelativePath.Trim().Replace('/', Path.DirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(configFileName))
                {
                    string siblingPath = Path.Combine(
                        Path.GetDirectoryName(scriptPath) ?? Application.dataPath,
                        configFileName);
                    if (File.Exists(siblingPath))
                    {
                        return siblingPath;
                    }
                }
            }

            if (!Application.isEditor)
            {
                string normalized = localAiAgentSidecarConfigRelativePath
                    .Trim()
                    .Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(Application.persistentDataPath, normalized);
            }

            return resolved;
        }

        private bool TryLoadVoiceAgentConfigFromDisk(out string message)
        {
            message = string.Empty;
            string configPath = GetVoiceAgentConfigPath();
            if (!File.Exists(configPath))
            {
                message = $"Voice config not found at: {configPath}";
                return false;
            }

            try
            {
                string json = File.ReadAllText(configPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    message = "Voice config file is empty.";
                    return false;
                }
                bool hasSyncOnStartField = json.IndexOf(
                    "\"sidecar_sync_config_on_start\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasIgnoreVirtualMicField = json.IndexOf(
                    "\"ignore_virtual_microphone_devices\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasPreferredMicField = json.IndexOf(
                    "\"preferred_microphone_device_name\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;

                VoiceAgentConfig config = JsonUtility.FromJson<VoiceAgentConfig>(json);
                if (config == null)
                {
                    message = "Voice config JSON is invalid.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(config.ipc_endpoint))
                {
                    localAiAgentEndpoint = config.ipc_endpoint.Trim();
                }

                localAiAgentConfidenceThreshold = Mathf.Clamp01(config.intent_confidence_threshold);
                if (config.transcript_default_confidence > 0f)
                {
                    localAiAgentTranscriptConfidence = Mathf.Clamp01(config.transcript_default_confidence);
                }

                localAiAgentEnableTranscriptParser = config.transcript_parser_enabled;
                localAiAgentMinTranscriptChars = Math.Max(0, config.min_transcript_chars);
                localAiAgentMinTranscriptWords = Math.Max(0, config.min_transcript_words);
                localAiAgentUseSafeNumericParsing = config.safe_numeric_parsing;
                localAiAgentRequireTargetTokenForJoint = config.require_target_token_for_joint;
                localAiAgentRejectOutOfRangeJointCommands = config.reject_out_of_range_joint_commands;
                localAiAgentJointMinDegrees = config.joint_min_degrees;
                localAiAgentJointMaxDegrees = config.joint_max_degrees;
                localAiAgentSuppressDuplicateCommands = config.suppress_duplicate_commands;
                if (config.duplicate_command_window_seconds > 0f)
                {
                    localAiAgentDuplicateCommandWindowSeconds =
                        Mathf.Max(0.05f, config.duplicate_command_window_seconds);
                }
                localAiAgentSimulationOnlyMode = config.simulation_only_mode;
                localAiAgentEnableTtsFeedback = config.tts_feedback_enabled;
                if (!string.IsNullOrWhiteSpace(config.tts_endpoint))
                {
                    localAiAgentTtsEndpoint = config.tts_endpoint.Trim();
                }
                if (config.tts_min_interval_seconds > 0f)
                {
                    localAiAgentTtsMinIntervalSeconds = Mathf.Max(0.05f, config.tts_min_interval_seconds);
                }
                if (config.heartbeat_timeout_seconds > 0f)
                {
                    localAiAgentHeartbeatTimeoutSeconds = Mathf.Max(0.5f, config.heartbeat_timeout_seconds);
                }
                if (config.retry_backoff_min_seconds > 0f)
                {
                    localAiAgentRetryBackoffMinSeconds = Mathf.Max(0.05f, config.retry_backoff_min_seconds);
                }
                if (config.retry_backoff_max_seconds > 0f)
                {
                    localAiAgentRetryBackoffMaxSeconds = Mathf.Max(
                        localAiAgentRetryBackoffMinSeconds,
                        config.retry_backoff_max_seconds);
                }
                if (config.degraded_failure_threshold > 0)
                {
                    localAiAgentDegradedFailureThreshold = Math.Max(1, config.degraded_failure_threshold);
                }
                localAiAgentBlockMotionWhenBridgeUnhealthy = config.block_motion_when_bridge_unhealthy;
                localAiAgentEnablePushToTalk = config.push_to_talk_enabled;
                localAiAgentListeningEnabled = config.listening_enabled;
                if (!string.IsNullOrWhiteSpace(config.push_to_talk_key))
                {
                    localAiAgentPushToTalkKey = config.push_to_talk_key.Trim();
                }
                if (!string.IsNullOrWhiteSpace(config.listening_endpoint))
                {
                    localAiAgentListeningEndpoint = config.listening_endpoint.Trim();
                }
                localAiAgentIgnoreVirtualMicrophones = hasIgnoreVirtualMicField
                    ? config.ignore_virtual_microphone_devices
                    : true;
                localAiAgentPreferredMicrophoneDeviceName = hasPreferredMicField
                    ? (config.preferred_microphone_device_name ?? string.Empty)
                    : string.Empty;
                EnsurePreferredMicrophoneDevice();
                localAiAgentAutoStartSidecar = config.auto_start_sidecar;
                localAiAgentAutoStopAutoStartedSidecarOnDisable =
                    config.auto_stop_autostarted_sidecar_on_disable;
                localAiAgentSyncSidecarConfigOnStart = hasSyncOnStartField
                    ? config.sidecar_sync_config_on_start
                    : true;
                if (!string.IsNullOrWhiteSpace(config.sidecar_python_command))
                {
                    localAiAgentSidecarPythonCommand = config.sidecar_python_command.Trim();
                }
                if (!string.IsNullOrWhiteSpace(config.sidecar_script_relative_path))
                {
                    localAiAgentSidecarScriptRelativePath = config.sidecar_script_relative_path.Trim();
                }
                if (!string.IsNullOrWhiteSpace(config.sidecar_config_relative_path))
                {
                    localAiAgentSidecarConfigRelativePath = config.sidecar_config_relative_path.Trim();
                }
                if (!string.IsNullOrWhiteSpace(config.sidecar_log_level))
                {
                    localAiAgentSidecarLogLevel = config.sidecar_log_level.Trim().ToLowerInvariant();
                }
                if (config.sidecar_startup_timeout_seconds > 0f)
                {
                    localAiAgentSidecarStartupTimeoutSeconds =
                        Mathf.Max(1f, config.sidecar_startup_timeout_seconds);
                }
                if (config.sidecar_retry_interval_seconds > 0f)
                {
                    localAiAgentSidecarRetryIntervalSeconds =
                        Mathf.Max(0.25f, config.sidecar_retry_interval_seconds);
                }
                if (config.sidecar_health_timeout_ms > 0)
                {
                    localAiAgentSidecarHealthTimeoutMs =
                        Mathf.Clamp(config.sidecar_health_timeout_ms, 200, 10000);
                }
                localAiAgentEnableLocalHelpModel = config.local_help_model_enabled;
                if (!string.IsNullOrWhiteSpace(config.help_endpoint))
                {
                    localAiAgentHelpEndpoint = config.help_endpoint.Trim();
                }
                if (!string.IsNullOrWhiteSpace(config.help_context))
                {
                    localAiAgentHelpContext = config.help_context;
                }
                localAiAgentHelpModelBackend = NormalizeHelpModelBackend(config.help_model_backend);
                localAiAgentHelpModelPath = config.help_model_path ?? string.Empty;
                localAiAgentHelpModelMaxTokens = Mathf.Clamp(config.help_model_max_tokens, 16, 512);
                localAiAgentHelpModelTemperature = Mathf.Clamp(config.help_model_temperature, 0f, 1.5f);
                localAiAgentHelpMaxAnswerChars = Mathf.Clamp(config.help_max_answer_chars, 80, 1200);
                message =
                    $"Loaded voice config ({config.stt_backend}) from {configPath}. " +
                    $"Endpoint={localAiAgentEndpoint}, Conf={localAiAgentConfidenceThreshold:F2}, MinTxt={localAiAgentMinTranscriptChars}c/{localAiAgentMinTranscriptWords}w, NumSafe={localAiAgentUseSafeNumericParsing}, Range=[{localAiAgentJointMinDegrees:F1},{localAiAgentJointMaxDegrees:F1}], Dedupe={localAiAgentSuppressDuplicateCommands}({localAiAgentDuplicateCommandWindowSeconds:F2}s), SimOnly={localAiAgentSimulationOnlyMode}, TTS={localAiAgentEnableTtsFeedback}, PTT={localAiAgentEnablePushToTalk}({localAiAgentPushToTalkKey}), AutoStartSidecar={localAiAgentAutoStartSidecar}, SyncSidecarCfgOnStart={localAiAgentSyncSidecarConfigOnStart}, HelpModel={localAiAgentEnableLocalHelpModel}({localAiAgentHelpModelBackend}), HB={localAiAgentHeartbeatTimeoutSeconds:F1}s, SafeMotion={localAiAgentBlockMotionWhenBridgeUnhealthy}.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to load voice config: {ex.Message}";
                return false;
            }
        }

        private bool TrySaveVoiceAgentConfigToDisk(out string message)
        {
            message = string.Empty;
            string configPath = GetVoiceAgentConfigPath();

            try
            {
                VoiceAgentConfig config = new VoiceAgentConfig();
                if (File.Exists(configPath))
                {
                    string existingJson = File.ReadAllText(configPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(existingJson))
                    {
                        VoiceAgentConfig existingConfig = JsonUtility.FromJson<VoiceAgentConfig>(existingJson);
                        if (existingConfig != null)
                        {
                            config = existingConfig;
                        }
                    }
                }

                float normalizedJointMin = Mathf.Min(localAiAgentJointMinDegrees, localAiAgentJointMaxDegrees);
                float normalizedJointMax = Mathf.Max(localAiAgentJointMinDegrees, localAiAgentJointMaxDegrees);
                localAiAgentJointMinDegrees = normalizedJointMin;
                localAiAgentJointMaxDegrees = normalizedJointMax;

                config.intent_confidence_threshold = Mathf.Clamp01(localAiAgentConfidenceThreshold);
                config.transcript_default_confidence = Mathf.Clamp01(localAiAgentTranscriptConfidence);
                config.transcript_parser_enabled = localAiAgentEnableTranscriptParser;
                config.min_transcript_chars = Mathf.Max(0, localAiAgentMinTranscriptChars);
                config.min_transcript_words = Mathf.Max(0, localAiAgentMinTranscriptWords);
                config.safe_numeric_parsing = localAiAgentUseSafeNumericParsing;
                config.require_target_token_for_joint = localAiAgentRequireTargetTokenForJoint;
                config.reject_out_of_range_joint_commands = localAiAgentRejectOutOfRangeJointCommands;
                config.joint_min_degrees = localAiAgentJointMinDegrees;
                config.joint_max_degrees = localAiAgentJointMaxDegrees;
                config.suppress_duplicate_commands = localAiAgentSuppressDuplicateCommands;
                config.duplicate_command_window_seconds = Mathf.Max(0.05f, localAiAgentDuplicateCommandWindowSeconds);
                config.simulation_only_mode = localAiAgentSimulationOnlyMode;
                config.tts_feedback_enabled = localAiAgentEnableTtsFeedback;
                config.tts_endpoint = string.IsNullOrWhiteSpace(localAiAgentTtsEndpoint)
                    ? VoiceAgentBridge.DefaultTtsEndpoint
                    : localAiAgentTtsEndpoint.Trim();
                config.tts_min_interval_seconds = Mathf.Max(0.05f, localAiAgentTtsMinIntervalSeconds);
                config.heartbeat_timeout_seconds = Mathf.Max(0.5f, localAiAgentHeartbeatTimeoutSeconds);
                config.retry_backoff_min_seconds = Mathf.Max(0.05f, localAiAgentRetryBackoffMinSeconds);
                config.retry_backoff_max_seconds = Mathf.Max(
                    config.retry_backoff_min_seconds,
                    localAiAgentRetryBackoffMaxSeconds);
                config.degraded_failure_threshold = Mathf.Max(1, localAiAgentDegradedFailureThreshold);
                config.block_motion_when_bridge_unhealthy = localAiAgentBlockMotionWhenBridgeUnhealthy;
                config.push_to_talk_enabled = localAiAgentEnablePushToTalk;
                config.push_to_talk_key = string.IsNullOrWhiteSpace(localAiAgentPushToTalkKey)
                    ? "V"
                    : localAiAgentPushToTalkKey.Trim();
                config.listening_enabled = localAiAgentListeningEnabled;
                config.listening_endpoint = string.IsNullOrWhiteSpace(localAiAgentListeningEndpoint)
                    ? VoiceAgentBridge.DefaultListeningEndpoint
                    : localAiAgentListeningEndpoint.Trim();
                config.ignore_virtual_microphone_devices = localAiAgentIgnoreVirtualMicrophones;
                config.preferred_microphone_device_name =
                    localAiAgentPreferredMicrophoneDeviceName ?? string.Empty;
                config.auto_start_sidecar = localAiAgentAutoStartSidecar;
                config.auto_stop_autostarted_sidecar_on_disable =
                    localAiAgentAutoStopAutoStartedSidecarOnDisable;
                config.sidecar_sync_config_on_start = localAiAgentSyncSidecarConfigOnStart;
                config.sidecar_python_command = string.IsNullOrWhiteSpace(localAiAgentSidecarPythonCommand)
                    ? "python"
                    : localAiAgentSidecarPythonCommand.Trim();
                config.sidecar_script_relative_path = string.IsNullOrWhiteSpace(localAiAgentSidecarScriptRelativePath)
                    ? "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py"
                    : localAiAgentSidecarScriptRelativePath.Trim();
                config.sidecar_config_relative_path = string.IsNullOrWhiteSpace(localAiAgentSidecarConfigRelativePath)
                    ? "ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json"
                    : localAiAgentSidecarConfigRelativePath.Trim();
                config.sidecar_log_level = string.IsNullOrWhiteSpace(localAiAgentSidecarLogLevel)
                    ? "warning"
                    : localAiAgentSidecarLogLevel.Trim().ToLowerInvariant();
                config.sidecar_startup_timeout_seconds = Mathf.Max(1f, localAiAgentSidecarStartupTimeoutSeconds);
                config.sidecar_retry_interval_seconds = Mathf.Max(0.25f, localAiAgentSidecarRetryIntervalSeconds);
                config.sidecar_health_timeout_ms = Mathf.Clamp(localAiAgentSidecarHealthTimeoutMs, 200, 10000);
                config.local_help_model_enabled = localAiAgentEnableLocalHelpModel;
                config.help_endpoint = string.IsNullOrWhiteSpace(localAiAgentHelpEndpoint)
                    ? VoiceAgentBridge.DefaultHelpEndpoint
                    : localAiAgentHelpEndpoint.Trim();
                config.help_context = localAiAgentHelpContext ?? string.Empty;
                config.help_model_backend = NormalizeHelpModelBackend(localAiAgentHelpModelBackend);
                config.help_model_path = localAiAgentHelpModelPath ?? string.Empty;
                config.help_model_max_tokens = Mathf.Clamp(localAiAgentHelpModelMaxTokens, 16, 512);
                config.help_model_temperature = Mathf.Clamp(localAiAgentHelpModelTemperature, 0f, 1.5f);
                config.help_max_answer_chars = Mathf.Clamp(localAiAgentHelpMaxAnswerChars, 80, 1200);
                config.ipc_endpoint = string.IsNullOrWhiteSpace(localAiAgentEndpoint)
                    ? VoiceAgentBridge.DefaultEndpoint
                    : localAiAgentEndpoint.Trim();

                string directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(configPath, json + Environment.NewLine, Encoding.UTF8);
                message = $"Saved voice config to: {configPath}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to save voice config: {ex.Message}";
                return false;
            }
        }

        private bool TrySyncLocalSidecarConfigFromUi(out string message)
        {
            message = string.Empty;
            string sidecarConfigPath = GetLocalAiSidecarConfigPath();
            if (string.IsNullOrWhiteSpace(sidecarConfigPath))
            {
                message = "Cannot sync sidecar config: sidecar config path is empty.";
                return false;
            }

            try
            {
                LocalVoiceAgentSidecarConfig config = new LocalVoiceAgentSidecarConfig();
                if (File.Exists(sidecarConfigPath))
                {
                    string existingJson = File.ReadAllText(sidecarConfigPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(existingJson))
                    {
                        LocalVoiceAgentSidecarConfig existingConfig =
                            JsonUtility.FromJson<LocalVoiceAgentSidecarConfig>(existingJson);
                        if (existingConfig != null)
                        {
                            config = existingConfig;
                        }
                    }
                }

                if (config.known_poses == null || config.known_poses.Length == 0)
                {
                    config.known_poses = DefaultSidecarKnownPoses;
                }

                if (config.known_joints == null || config.known_joints.Length == 0)
                {
                    config.known_joints = DefaultSidecarKnownJoints;
                }
                if (config.show_movement_synonyms == null || config.show_movement_synonyms.Length == 0)
                {
                    config.show_movement_synonyms = DefaultSidecarShowMovementSynonyms;
                }
                if (config.help_synonyms == null || config.help_synonyms.Length == 0)
                {
                    config.help_synonyms = DefaultSidecarHelpSynonyms;
                }
                if (config.hello_synonyms == null || config.hello_synonyms.Length == 0)
                {
                    config.hello_synonyms = DefaultSidecarHelloSynonyms;
                }
                if (config.who_are_you_synonyms == null || config.who_are_you_synonyms.Length == 0)
                {
                    config.who_are_you_synonyms = DefaultSidecarWhoAreYouSynonyms;
                }

                float normalizedJointMin = Mathf.Min(localAiAgentJointMinDegrees, localAiAgentJointMaxDegrees);
                float normalizedJointMax = Mathf.Max(localAiAgentJointMinDegrees, localAiAgentJointMaxDegrees);
                localAiAgentJointMinDegrees = normalizedJointMin;
                localAiAgentJointMaxDegrees = normalizedJointMax;

                string voiceConfigSttBackend = string.Empty;
                string voiceConfigSttModelPath = string.Empty;
                try
                {
                    string voiceConfigPath = GetVoiceAgentConfigPath();
                    if (File.Exists(voiceConfigPath))
                    {
                        string voiceJson = File.ReadAllText(voiceConfigPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(voiceJson))
                        {
                            VoiceAgentConfig voiceConfig = JsonUtility.FromJson<VoiceAgentConfig>(voiceJson);
                            if (voiceConfig != null)
                            {
                                voiceConfigSttBackend = (voiceConfig.stt_backend ?? string.Empty).Trim().ToLowerInvariant();
                                voiceConfigSttModelPath = (voiceConfig.model_path ?? string.Empty).Trim();
                            }
                        }
                    }
                }
                catch
                {
                    // Keep sidecar sync resilient; ignore optional voice-config STT parsing failures.
                }

                config.intent_confidence_threshold = Mathf.Clamp01(localAiAgentConfidenceThreshold);
                config.transcript_default_confidence = Mathf.Clamp01(localAiAgentTranscriptConfidence);
                if (!string.IsNullOrWhiteSpace(voiceConfigSttBackend))
                {
                    config.stt_backend = voiceConfigSttBackend;
                }
                else if (string.IsNullOrWhiteSpace(config.stt_backend))
                {
                    config.stt_backend = "vosk";
                }
                string normalizedSttModelPath = !string.IsNullOrWhiteSpace(voiceConfigSttModelPath)
                    ? voiceConfigSttModelPath
                    : BuildUiHelpModelPathFromSidecar(config.stt_model_path, sidecarConfigPath);
                if (string.IsNullOrWhiteSpace(normalizedSttModelPath))
                {
                    normalizedSttModelPath = ".local_voice_models/vosk-model-small-en-us-0.15";
                }
                config.stt_model_path = BuildSidecarHelpModelPathFromUi(
                    normalizedSttModelPath,
                    sidecarConfigPath);
                config.start_listening_enabled = localAiAgentListeningEnabled;
                config.min_transcript_chars = Mathf.Max(0, localAiAgentMinTranscriptChars);
                config.min_transcript_words = Mathf.Max(0, localAiAgentMinTranscriptWords);
                config.safe_numeric_parsing = localAiAgentUseSafeNumericParsing;
                config.require_target_token_for_joint = localAiAgentRequireTargetTokenForJoint;
                config.reject_out_of_range_joint_commands = localAiAgentRejectOutOfRangeJointCommands;
                config.joint_min_degrees = localAiAgentJointMinDegrees;
                config.joint_max_degrees = localAiAgentJointMaxDegrees;
                config.help_context = localAiAgentHelpContext ?? string.Empty;
                config.help_model_backend = NormalizeHelpModelBackend(localAiAgentHelpModelBackend);
                config.help_model_path = BuildSidecarHelpModelPathFromUi(
                    localAiAgentHelpModelPath,
                    sidecarConfigPath);
                config.help_model_max_tokens = Mathf.Clamp(localAiAgentHelpModelMaxTokens, 16, 512);
                config.help_model_temperature = Mathf.Clamp(localAiAgentHelpModelTemperature, 0f, 1.5f);
                config.help_max_answer_chars = Mathf.Clamp(localAiAgentHelpMaxAnswerChars, 80, 1200);
                config.audio_input_device_name = localAiAgentPreferredMicrophoneDeviceName ?? string.Empty;
                config.prefer_non_virtual_input_device = localAiAgentIgnoreVirtualMicrophones;

                string directory = Path.GetDirectoryName(sidecarConfigPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(sidecarConfigPath, json + Environment.NewLine, Encoding.UTF8);
                message = $"Synced sidecar parser/help config to: {sidecarConfigPath}";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to sync sidecar config: {ex.Message}";
                return false;
            }
        }

        private bool TryLoadLocalSidecarConfigIntoUi(out string message)
        {
            message = string.Empty;
            string sidecarConfigPath = GetLocalAiSidecarConfigPath();
            if (string.IsNullOrWhiteSpace(sidecarConfigPath))
            {
                message = "Cannot load sidecar config: sidecar config path is empty.";
                return false;
            }

            if (!File.Exists(sidecarConfigPath))
            {
                message = $"Sidecar config not found at: {sidecarConfigPath}";
                return false;
            }

            try
            {
                string json = File.ReadAllText(sidecarConfigPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    message = "Sidecar config file is empty.";
                    return false;
                }
                bool hasAudioInputDeviceField = json.IndexOf(
                    "\"audio_input_device_name\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasPreferNonVirtualInputField = json.IndexOf(
                    "\"prefer_non_virtual_input_device\"",
                    StringComparison.OrdinalIgnoreCase) >= 0;

                LocalVoiceAgentSidecarConfig config =
                    JsonUtility.FromJson<LocalVoiceAgentSidecarConfig>(json);
                if (config == null)
                {
                    message = "Sidecar config JSON is invalid.";
                    return false;
                }

                localAiAgentConfidenceThreshold = Mathf.Clamp01(config.intent_confidence_threshold);
                localAiAgentTranscriptConfidence = Mathf.Clamp01(config.transcript_default_confidence);
                localAiAgentListeningEnabled = config.start_listening_enabled;
                localAiAgentMinTranscriptChars = Mathf.Max(0, config.min_transcript_chars);
                localAiAgentMinTranscriptWords = Mathf.Max(0, config.min_transcript_words);
                localAiAgentUseSafeNumericParsing = config.safe_numeric_parsing;
                localAiAgentRequireTargetTokenForJoint = config.require_target_token_for_joint;
                localAiAgentRejectOutOfRangeJointCommands = config.reject_out_of_range_joint_commands;
                localAiAgentJointMinDegrees = Mathf.Min(config.joint_min_degrees, config.joint_max_degrees);
                localAiAgentJointMaxDegrees = Mathf.Max(config.joint_min_degrees, config.joint_max_degrees);
                if (!string.IsNullOrWhiteSpace(config.help_context))
                {
                    localAiAgentHelpContext = config.help_context;
                }
                localAiAgentHelpModelBackend = NormalizeHelpModelBackend(config.help_model_backend);
                localAiAgentHelpModelPath = BuildUiHelpModelPathFromSidecar(
                    config.help_model_path,
                    sidecarConfigPath);
                localAiAgentHelpModelMaxTokens = Mathf.Clamp(config.help_model_max_tokens, 16, 512);
                localAiAgentHelpModelTemperature = Mathf.Clamp(config.help_model_temperature, 0f, 1.5f);
                localAiAgentHelpMaxAnswerChars = Mathf.Clamp(config.help_max_answer_chars, 80, 1200);
                localAiAgentPreferredMicrophoneDeviceName = hasAudioInputDeviceField
                    ? (config.audio_input_device_name ?? string.Empty)
                    : string.Empty;
                localAiAgentIgnoreVirtualMicrophones = hasPreferNonVirtualInputField
                    ? config.prefer_non_virtual_input_device
                    : true;
                EnsurePreferredMicrophoneDevice();

                message =
                    $"Loaded sidecar config from {sidecarConfigPath}. " +
                    $"Conf={localAiAgentConfidenceThreshold:F2}, MinTxt={localAiAgentMinTranscriptChars}c/{localAiAgentMinTranscriptWords}w, " +
                    $"NumSafe={localAiAgentUseSafeNumericParsing}, Range=[{localAiAgentJointMinDegrees:F1},{localAiAgentJointMaxDegrees:F1}], " +
                    $"ListeningDefault={localAiAgentListeningEnabled}, HelpBackend={localAiAgentHelpModelBackend}.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to load sidecar config: {ex.Message}";
                return false;
            }
        }

        private void OnGUI()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            }

            float topPanelsWidth = _collapsed
                ? DesignLeftPanelWidth
                : DesignLeftPanelWidth + DesignPanelGap + DesignRightPanelWidth;
            float topBarWidth = Mathf.Max(topPanelsWidth, DesignMinimumTopBarWidth);
            float designTotalWidth = topBarWidth;
            float designTotalHeight =
                (_collapsed ? DesignCollapsedPanelHeight : DesignExpandedPanelHeight) +
                DesignViewTopBarHeight +
                DesignViewTopBarGap;

            float availableWidth = Mathf.Max(160f, Screen.width - (2f * DesignMarginPixels));
            float availableHeight = Mathf.Max(160f, Screen.height - (2f * DesignMarginPixels));
            _uiScale = Mathf.Min(1f, availableWidth / designTotalWidth, availableHeight / designTotalHeight);
            _uiScale = Mathf.Max(0.1f, _uiScale);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_uiScale, _uiScale, 1f));

            float logicalMargin = DesignMarginPixels / _uiScale;
            float logicalScreenWidth = Screen.width / _uiScale;
            float logicalScreenHeight = Screen.height / _uiScale;
            float topBarX = 0f;
            float topBarY = logicalMargin;
            DrawViewTopBar(new Rect(topBarX, topBarY, logicalScreenWidth, DesignViewTopBarHeight));

            float leftPanelX = logicalMargin;
            float topY = topBarY + DesignViewTopBarHeight + DesignViewTopBarGap;

            if (_activeMenuView != RuntimeMenuView.General)
            {
                if (_activeMenuView == RuntimeMenuView.AI)
                {
                    DrawAiView(topY, logicalMargin, logicalScreenWidth, logicalScreenHeight);
                    GUI.matrix = previousMatrix;
                    return;
                }

                if (_activeMenuView == RuntimeMenuView.AnimationsAndPoses)
                {
                    DrawAnimationsAndPosesView(topY, logicalMargin, logicalScreenWidth, logicalScreenHeight);
                    GUI.matrix = previousMatrix;
                    return;
                }

                if (_activeMenuView == RuntimeMenuView.ManualControl)
                {
                    DrawManualControlView(topY, logicalMargin, logicalScreenWidth, logicalScreenHeight);
                    GUI.matrix = previousMatrix;
                    return;
                }

                if (_activeMenuView == RuntimeMenuView.Connections)
                {
                    DrawConnectionsView(topY, logicalMargin, logicalScreenWidth, logicalScreenHeight);
                    GUI.matrix = previousMatrix;
                    return;
                }

                float placeholderHeight = Mathf.Max(160f, logicalScreenHeight - topY - logicalMargin);
                DrawPlaceholderMenuView(new Rect(leftPanelX, topY, topBarWidth, placeholderHeight));
                GUI.matrix = previousMatrix;
                return;
            }

            if (localAiAgentPanelExpanded)
            {
                float expandedPanelWidth = Mathf.Max(240f, logicalScreenWidth - (2f * logicalMargin));
                float expandedPanelHeight = Mathf.Max(160f, logicalScreenHeight - topY - logicalMargin);
                DrawLocalAgentPanel(new Rect(leftPanelX, topY, expandedPanelWidth, expandedPanelHeight));
                GUI.matrix = previousMatrix;
                return;
            }

            if (_collapsed)
            {
                float collapsedHeight = DesignCollapsedPanelHeight;
                DrawLeftPanel(new Rect(leftPanelX, topY, DesignLeftPanelWidth, collapsedHeight));

                if (showCameraPreview)
                {
                    float logicalMaxWidth = Mathf.Max(260f, logicalScreenWidth - (2f * logicalMargin));
                    float cameraPanelWidth = Mathf.Min(DesignCameraPanelWidth, logicalMaxWidth);
                    float cameraPanelHeight = Mathf.Min(
                        DesignCameraPanelHeight,
                        Mathf.Max(140f, logicalScreenHeight - topY - logicalMargin));
                    float cameraPanelX = (logicalScreenWidth - cameraPanelWidth) * 0.5f;
                    float cameraPanelY = logicalScreenHeight - logicalMargin - cameraPanelHeight;
                    DrawCameraPreviewPanel(new Rect(cameraPanelX, cameraPanelY, cameraPanelWidth, cameraPanelHeight));
                }
            }
            else
            {
                float usableWidth = Mathf.Max(320f, logicalScreenWidth - (2f * logicalMargin));
                float topPanelHeight = Mathf.Max(220f, logicalScreenHeight - topY - logicalMargin);

                float leftPanelWidth = usableWidth * ExpandedLeftPanelWidthRatio;
                float rightPanelWidth = usableWidth * ExpandedRightPanelWidthRatio;
                float centerPanelWidth = Mathf.Max(180f, usableWidth - leftPanelWidth - rightPanelWidth);

                float centerPanelX = leftPanelX + leftPanelWidth;
                float rightPanelX = centerPanelX + centerPanelWidth;
                float cameraPanelHeight = 0f;
                float cameraPanelY = 0f;
                bool drawCameraPanel = showCameraPreview;
                if (drawCameraPanel)
                {
                    float maxCameraHeight = Mathf.Max(120f, topPanelHeight - DesignLocalAgentCollapsedHeight);
                    cameraPanelHeight = Mathf.Min(DesignCameraPanelHeight, maxCameraHeight);
                    cameraPanelY = logicalScreenHeight - logicalMargin - cameraPanelHeight;
                }

                float agentPanelHeight;
                if (localAiAgentPanelExpanded)
                {
                    if (drawCameraPanel)
                    {
                        float expandedPanelBottomY = cameraPanelY;
                        agentPanelHeight = Mathf.Max(0f, expandedPanelBottomY - topY);
                    }
                    else
                    {
                        agentPanelHeight = topPanelHeight;
                    }
                }
                else
                {
                    agentPanelHeight = DesignLocalAgentCollapsedHeight;
                }

                DrawLeftPanel(new Rect(leftPanelX, topY, leftPanelWidth, topPanelHeight));
                DrawRightPanel(new Rect(rightPanelX, topY, rightPanelWidth, topPanelHeight));
                DrawLocalAgentPanel(new Rect(centerPanelX, topY, centerPanelWidth, agentPanelHeight));

                if (drawCameraPanel)
                {
                    float minCameraY = topY + agentPanelHeight + (localAiAgentPanelExpanded ? 0f : DesignPanelGap);
                    float finalCameraPanelY = Mathf.Max(
                        minCameraY,
                        logicalScreenHeight - logicalMargin - cameraPanelHeight);
                    DrawCameraPreviewPanel(new Rect(centerPanelX, finalCameraPanelY, centerPanelWidth, cameraPanelHeight));
                }
            }

            GUI.matrix = previousMatrix;
        }

        private void DrawViewTopBar(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Views", GUILayout.Width(44f));

            for (int i = 0; i < RuntimeMenuViewLabels.Length; i++)
            {
                RuntimeMenuView candidateView = (RuntimeMenuView)i;
                bool isActive = candidateView == _activeMenuView;
                float buttonWidth = CalcUiButtonWidth(RuntimeMenuViewLabels[i], 72f);
                bool toggled = GUILayout.Toggle(
                    isActive,
                    RuntimeMenuViewLabels[i],
                    GUI.skin.button,
                    GUILayout.Width(buttonWidth),
                    GUILayout.Height(24f));
                if (toggled && !isActive)
                {
                    _activeMenuView = candidateView;
                    if (_activeMenuView != RuntimeMenuView.General)
                    {
                        localAiAgentPanelExpanded = false;
                    }
                }

                if (i < RuntimeMenuViewLabels.Length - 1)
                {
                    GUILayout.Space(8f);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(DesignTopBarGroupGap);

            GUILayout.Label("Windowed", GUILayout.Width(CalcUiLabelWidth("Windowed", 4f)));
            _windowedWidthText = GUILayout.TextField(_windowedWidthText, GUILayout.Width(58f));
            GUILayout.Label("x", GUILayout.Width(CalcUiLabelWidth("x", 2f)));
            _windowedHeightText = GUILayout.TextField(_windowedHeightText, GUILayout.Width(58f));

            if (GUILayout.Button("Windowed", GUILayout.Width(CalcUiButtonWidth("Windowed", 76f)), GUILayout.Height(24f)))
            {
                ApplyWindowedResolutionFromFields();
            }

            if (GUILayout.Button("Fullscreen", GUILayout.Width(CalcUiButtonWidth("Fullscreen", 88f)), GUILayout.Height(24f)))
            {
                SetFullscreenToDesktopResolution();
            }

            if (GUILayout.Button("Exit", GUILayout.Width(CalcUiButtonWidth("Exit", 52f)), GUILayout.Height(24f)))
            {
                SetStatus("Exit requested", "Closing application.");
                Application.Quit();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static float CalcUiButtonWidth(string text, float minWidth)
        {
            GUIStyle style = GUI.skin != null ? GUI.skin.button : null;
            if (style == null)
            {
                return Mathf.Max(minWidth, 72f);
            }

            float width = style.CalcSize(new GUIContent(text)).x + 18f;
            return Mathf.Max(minWidth, width);
        }

        private static float CalcUiLabelWidth(string text, float extraPadding)
        {
            GUIStyle style = GUI.skin != null ? GUI.skin.label : null;
            if (style == null)
            {
                return Mathf.Max(12f, (text?.Length ?? 0) * 8f + extraPadding);
            }

            return style.CalcSize(new GUIContent(text)).x + extraPadding;
        }

        private static float AiW(float baseWidth, float widthScale, float minWidth = 20f)
        {
            return Mathf.Max(minWidth, baseWidth * widthScale);
        }

        private static float GetAiRuntimeContentWidth(float panelWidth)
        {
            float innerWidth = Mathf.Max(220f, panelWidth - 24f);
            float minWidth = Mathf.Min(236f, innerWidth);
            float maxWidth = Mathf.Max(minWidth, Mathf.Min(336f, innerWidth));
            return Mathf.Clamp(innerWidth * 0.62f, minWidth, maxWidth);
        }

        private static float GetAiRuntimeHalfWidth(float totalWidth)
        {
            return Mathf.Max(96f, (totalWidth - 8f) * 0.5f);
        }

        private static void BeginAiCenteredColumn(float width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(width));
        }

        private static void EndAiCenteredColumn()
        {
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawPlaceholderMenuView(Rect area)
        {
            string selectedViewName = RuntimeMenuViewLabels[(int)_activeMenuView];
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label(selectedViewName, _titleStyle);
            GUILayout.Space(10f);
            GUILayout.Label($"Menu items for the {selectedViewName} view will be added here.");
            GUILayout.EndArea();
        }

        private void DrawAnimationsAndPosesView(
            float topY,
            float logicalMargin,
            float logicalScreenWidth,
            float logicalScreenHeight)
        {
            float usableWidth = Mathf.Max(320f, logicalScreenWidth - (2f * logicalMargin));
            float usableHeight = Mathf.Max(220f, logicalScreenHeight - topY - logicalMargin);
            float desiredPanelWidth = Mathf.Clamp(
                usableWidth * DesignAiPanelWidthRatio,
                DesignAiPanelMinWidth,
                DesignAiPanelMaxWidth);
            float maxPanelWidthByOpenSpace = Mathf.Max(180f, (usableWidth - DesignAiCenterOpenMinWidth) * 0.5f);
            float leftPanelWidth = Mathf.Min(desiredPanelWidth, maxPanelWidthByOpenSpace);
            float rightPanelWidth = leftPanelWidth;

            float originalCenterGap = Mathf.Max(0f, usableWidth - leftPanelWidth - rightPanelWidth);
            float targetCenterGap = originalCenterGap * DesignAiMiddleSpaceFactor;
            float expandPerSide = Mathf.Max(0f, (originalCenterGap - targetCenterGap) * 0.5f);
            leftPanelWidth += expandPerSide;
            rightPanelWidth += expandPerSide;

            float minimumGap = Mathf.Max(12f, DesignPanelGap);
            float availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
            if (availableGap < minimumGap)
            {
                float deficit = minimumGap - availableGap;
                float leftShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, leftPanelWidth - 140f));
                float rightShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, rightPanelWidth - 140f));
                leftPanelWidth -= leftShrink;
                rightPanelWidth -= rightShrink;

                availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
                if (availableGap < minimumGap)
                {
                    float remainingDeficit = minimumGap - availableGap;
                    float rightExtraShrink = Mathf.Min(remainingDeficit, Mathf.Max(0f, rightPanelWidth - 140f));
                    rightPanelWidth -= rightExtraShrink;
                }
            }

            float leftPanelX = logicalMargin;
            float rightPanelX = logicalScreenWidth - logicalMargin - rightPanelWidth;

            DrawAnimationsAndPosesControlsPanel(new Rect(leftPanelX, topY, leftPanelWidth, usableHeight));
            DrawAnimationsAndPosesStatusPanel(new Rect(rightPanelX, topY, rightPanelWidth, usableHeight));
        }

        private void DrawAnimationsAndPosesControlsPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Animations & Poses", _titleStyle);

            float bodyHeight = Mathf.Max(120f, area.height - 38f);
            _animationsAndPosesScroll = GUILayout.BeginScrollView(
                _animationsAndPosesScroll,
                false,
                true,
                GUILayout.Height(bodyHeight));

            DrawPoseSpeedSliderSection();
            GUILayout.Space(10f);
            DrawPresetSection();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawAnimationsAndPosesStatusPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            DrawStatusSection();
            GUILayout.EndArea();
        }

        private void DrawManualControlView(
            float topY,
            float logicalMargin,
            float logicalScreenWidth,
            float logicalScreenHeight)
        {
            float usableWidth = Mathf.Max(320f, logicalScreenWidth - (2f * logicalMargin));
            float usableHeight = Mathf.Max(220f, logicalScreenHeight - topY - logicalMargin);
            float desiredPanelWidth = Mathf.Clamp(
                usableWidth * DesignAiPanelWidthRatio,
                DesignAiPanelMinWidth,
                DesignAiPanelMaxWidth);
            float maxPanelWidthByOpenSpace = Mathf.Max(180f, (usableWidth - DesignAiCenterOpenMinWidth) * 0.5f);
            float leftPanelWidth = Mathf.Min(desiredPanelWidth, maxPanelWidthByOpenSpace);
            float rightPanelWidth = leftPanelWidth;

            float originalCenterGap = Mathf.Max(0f, usableWidth - leftPanelWidth - rightPanelWidth);
            float targetCenterGap = originalCenterGap * DesignAiMiddleSpaceFactor;
            float expandPerSide = Mathf.Max(0f, (originalCenterGap - targetCenterGap) * 0.5f);
            leftPanelWidth += expandPerSide;
            rightPanelWidth += expandPerSide;

            float minimumGap = Mathf.Max(12f, DesignPanelGap);
            float availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
            if (availableGap < minimumGap)
            {
                float deficit = minimumGap - availableGap;
                float leftShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, leftPanelWidth - 140f));
                float rightShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, rightPanelWidth - 140f));
                leftPanelWidth -= leftShrink;
                rightPanelWidth -= rightShrink;

                availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
                if (availableGap < minimumGap)
                {
                    float remainingDeficit = minimumGap - availableGap;
                    float rightExtraShrink = Mathf.Min(remainingDeficit, Mathf.Max(0f, rightPanelWidth - 140f));
                    rightPanelWidth -= rightExtraShrink;
                }
            }

            float leftPanelX = logicalMargin;
            float rightPanelX = logicalScreenWidth - logicalMargin - rightPanelWidth;

            DrawManualControlPanel(new Rect(leftPanelX, topY, leftPanelWidth, usableHeight));
            DrawManualControlStatusPanel(new Rect(rightPanelX, topY, rightPanelWidth, usableHeight));
        }

        private void DrawManualControlPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Manual Control", _titleStyle);

            float bodyHeight = Mathf.Max(120f, area.height - 38f);
            _manualControlScroll = GUILayout.BeginScrollView(
                _manualControlScroll,
                false,
                true,
                GUILayout.Height(bodyHeight));

            DrawJointSection();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawManualControlStatusPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            DrawStatusSection();
            GUILayout.EndArea();
        }

        private void DrawConnectionsView(
            float topY,
            float logicalMargin,
            float logicalScreenWidth,
            float logicalScreenHeight)
        {
            float usableWidth = Mathf.Max(320f, logicalScreenWidth - (2f * logicalMargin));
            float usableHeight = Mathf.Max(220f, logicalScreenHeight - topY - logicalMargin);
            float desiredPanelWidth = Mathf.Clamp(
                usableWidth * DesignAiPanelWidthRatio,
                DesignAiPanelMinWidth,
                DesignAiPanelMaxWidth);
            float maxPanelWidthByOpenSpace = Mathf.Max(180f, (usableWidth - DesignAiCenterOpenMinWidth) * 0.5f);
            float leftPanelWidth = Mathf.Min(desiredPanelWidth, maxPanelWidthByOpenSpace);
            float rightPanelWidth = leftPanelWidth;

            float originalCenterGap = Mathf.Max(0f, usableWidth - leftPanelWidth - rightPanelWidth);
            float targetCenterGap = originalCenterGap * DesignAiMiddleSpaceFactor;
            float expandPerSide = Mathf.Max(0f, (originalCenterGap - targetCenterGap) * 0.5f);
            leftPanelWidth += expandPerSide;
            rightPanelWidth += expandPerSide;

            float minimumGap = Mathf.Max(12f, DesignPanelGap);
            float availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
            if (availableGap < minimumGap)
            {
                float deficit = minimumGap - availableGap;
                float leftShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, leftPanelWidth - 140f));
                float rightShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, rightPanelWidth - 140f));
                leftPanelWidth -= leftShrink;
                rightPanelWidth -= rightShrink;

                availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
                if (availableGap < minimumGap)
                {
                    float remainingDeficit = minimumGap - availableGap;
                    float rightExtraShrink = Mathf.Min(remainingDeficit, Mathf.Max(0f, rightPanelWidth - 140f));
                    rightPanelWidth -= rightExtraShrink;
                }
            }

            float leftPanelX = logicalMargin;
            float rightPanelX = logicalScreenWidth - logicalMargin - rightPanelWidth;

            DrawConnectionsPanel(new Rect(leftPanelX, topY, leftPanelWidth, usableHeight));
            DrawConnectionsStatusPanel(new Rect(rightPanelX, topY, rightPanelWidth, usableHeight));
        }

        private void DrawConnectionsPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("Connections", _titleStyle);

            float bodyHeight = Mathf.Max(120f, area.height - 38f);
            _connectionsScroll = GUILayout.BeginScrollView(
                _connectionsScroll,
                false,
                true,
                GUILayout.Height(bodyHeight));

            DrawConnectionSection();
            GUILayout.Space(10f);
            DrawAutomationSection();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawConnectionsStatusPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            DrawStatusSection();
            GUILayout.EndArea();
        }

        private void DrawPoseSpeedSliderSection()
        {
            GUILayout.Label("Pose Speed", _titleStyle);
            GUILayout.Label("Adjust preset pose transition speed.");

            presetPoseTransitionSpeedScale = GUILayout.HorizontalSlider(presetPoseTransitionSpeedScale, 0.05f, 2.0f);
            presetPoseTransitionSpeedScale = Mathf.Clamp(presetPoseTransitionSpeedScale, 0.05f, 2.0f);
            _client.PoseTransitionSpeedScale = presetPoseTransitionSpeedScale;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Slow");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Mathf.RoundToInt(presetPoseTransitionSpeedScale * 100f)}%");
            GUILayout.FlexibleSpace();
            GUILayout.Label("Fast");
            GUILayout.EndHorizontal();
        }

        private void DrawAiView(float topY, float logicalMargin, float logicalScreenWidth, float logicalScreenHeight)
        {
            float usableWidth = Mathf.Max(320f, logicalScreenWidth - (2f * logicalMargin));
            float usableHeight = Mathf.Max(220f, logicalScreenHeight - topY - logicalMargin);
            float desiredPanelWidth = Mathf.Clamp(
                usableWidth * DesignAiPanelWidthRatio,
                DesignAiPanelMinWidth,
                DesignAiPanelMaxWidth);
            float maxPanelWidthByOpenSpace = Mathf.Max(180f, (usableWidth - DesignAiCenterOpenMinWidth) * 0.5f);
            float aiPanelWidth = Mathf.Min(desiredPanelWidth, maxPanelWidthByOpenSpace);
            float leftPanelWidth = aiPanelWidth;
            float rightPanelWidth = aiPanelWidth;

            float originalCenterGap = Mathf.Max(0f, usableWidth - leftPanelWidth - rightPanelWidth);
            float targetCenterGap = originalCenterGap * DesignAiMiddleSpaceFactor;
            float expandPerSide = Mathf.Max(0f, (originalCenterGap - targetCenterGap) * 0.5f);
            leftPanelWidth += expandPerSide;
            rightPanelWidth += expandPerSide;

            float minimumGap = Mathf.Max(12f, DesignPanelGap);
            float availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
            if (availableGap < minimumGap)
            {
                float deficit = minimumGap - availableGap;
                float leftShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, leftPanelWidth - 140f));
                float rightShrink = Mathf.Min(deficit * 0.5f, Mathf.Max(0f, rightPanelWidth - 140f));
                leftPanelWidth -= leftShrink;
                rightPanelWidth -= rightShrink;

                availableGap = usableWidth - leftPanelWidth - rightPanelWidth;
                if (availableGap < minimumGap)
                {
                    float remainingDeficit = minimumGap - availableGap;
                    float rightExtraShrink = Mathf.Min(remainingDeficit, Mathf.Max(0f, rightPanelWidth - 140f));
                    rightPanelWidth -= rightExtraShrink;
                }
            }

            float leftPanelX = logicalMargin;
            float rightPanelX = logicalScreenWidth - logicalMargin - rightPanelWidth;
            float centerPanelX = leftPanelX + leftPanelWidth;
            float centerPanelWidth = Mathf.Max(0f, rightPanelX - centerPanelX);

            DrawAiPrimaryPanel(new Rect(leftPanelX, topY, leftPanelWidth, usableHeight));
            DrawAiRuntimePanel(new Rect(rightPanelX, topY, rightPanelWidth, usableHeight));

            float statusPanelHeight = Mathf.Min(DesignCameraPanelHeight, Mathf.Max(220f, usableHeight * 0.38f));
            float statusPanelY = topY + usableHeight - statusPanelHeight;
            if (centerPanelWidth >= 180f && statusPanelHeight >= 120f)
            {
                DrawAiStatusWindowPanel(new Rect(centerPanelX, statusPanelY, centerPanelWidth, statusPanelHeight));
            }
        }

        private void DrawAiPrimaryPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("AI Input & Parsing", _titleStyle);
            float aiWidthScale = Mathf.Clamp((area.width - 24f) / 700f, 0.62f, 0.92f);
            bool aiCompact = area.width < 620f;

            bool previousEnabled = enableLocalAiAgent;
            enableLocalAiAgent = GUILayout.Toggle(enableLocalAiAgent, "Enable local AI agent");
            if (previousEnabled && !enableLocalAiAgent)
            {
                RejectPendingVoiceAction();
            }

            GUILayout.Label("Voice endpoint, parser safety, listening, and microphone controls.");

            float bodyHeight = Mathf.Max(60f, area.height - 80f);
            _aiPrimaryScroll = GUILayout.BeginScrollView(
                _aiPrimaryScroll,
                false,
                true,
                GUILayout.Height(bodyHeight));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Endpoint", GUILayout.Width(AiW(62f, aiWidthScale)));
            localAiAgentEndpoint = GUILayout.TextField(localAiAgentEndpoint);
            GUILayout.EndHorizontal();

            if (aiCompact)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Load cfg", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    bool loaded = TryLoadVoiceAgentConfigFromDisk(out string loadMessage);
                    _voiceLastActionResult = loadMessage;
                    if (!loaded)
                    {
                        _voiceLastParserMessage = loadMessage;
                    }
                }
                if (GUILayout.Button("Save cfg", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    bool saved = TrySaveVoiceAgentConfigToDisk(out string saveMessage);
                    _voiceLastActionResult = saveMessage;
                    if (!saved)
                    {
                        _voiceLastParserMessage = saveMessage;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Sync sidecar", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    bool synced = TrySyncLocalSidecarConfigFromUi(out string syncMessage);
                    _voiceLastActionResult = syncMessage;
                    if (!synced)
                    {
                        _voiceLastParserMessage = syncMessage;
                    }
                }
                if (GUILayout.Button("Load sidecar", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    bool loadedSidecar = TryLoadLocalSidecarConfigIntoUi(out string sidecarLoadMessage);
                    _voiceLastActionResult = sidecarLoadMessage;
                    if (!loadedSidecar)
                    {
                        _voiceLastParserMessage = sidecarLoadMessage;
                    }
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Load cfg", GUILayout.Width(AiW(72f, aiWidthScale)), GUILayout.Height(22f)))
                {
                    bool loaded = TryLoadVoiceAgentConfigFromDisk(out string loadMessage);
                    _voiceLastActionResult = loadMessage;
                    if (!loaded)
                    {
                        _voiceLastParserMessage = loadMessage;
                    }
                }
                if (GUILayout.Button("Save cfg", GUILayout.Width(AiW(72f, aiWidthScale)), GUILayout.Height(22f)))
                {
                    bool saved = TrySaveVoiceAgentConfigToDisk(out string saveMessage);
                    _voiceLastActionResult = saveMessage;
                    if (!saved)
                    {
                        _voiceLastParserMessage = saveMessage;
                    }
                }
                if (GUILayout.Button("Sync sidecar", GUILayout.Width(AiW(92f, aiWidthScale)), GUILayout.Height(22f)))
                {
                    bool synced = TrySyncLocalSidecarConfigFromUi(out string syncMessage);
                    _voiceLastActionResult = syncMessage;
                    if (!synced)
                    {
                        _voiceLastParserMessage = syncMessage;
                    }
                }
                if (GUILayout.Button("Load sidecar", GUILayout.Width(AiW(92f, aiWidthScale)), GUILayout.Height(22f)))
                {
                    bool loadedSidecar = TryLoadLocalSidecarConfigIntoUi(out string sidecarLoadMessage);
                    _voiceLastActionResult = sidecarLoadMessage;
                    if (!loadedSidecar)
                    {
                        _voiceLastParserMessage = sidecarLoadMessage;
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Poll(s)", GUILayout.Width(AiW(62f, aiWidthScale)));
            string pollText = GUILayout.TextField(
                localAiAgentPollIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(64f, aiWidthScale)));
            if (float.TryParse(pollText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedPoll))
            {
                localAiAgentPollIntervalSeconds = Mathf.Max(0.1f, parsedPoll);
            }

            GUILayout.Label("Conf", GUILayout.Width(AiW(34f, aiWidthScale)));
            string confText = GUILayout.TextField(
                localAiAgentConfidenceThreshold.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(64f, aiWidthScale)));
            if (float.TryParse(confText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedConf))
            {
                localAiAgentConfidenceThreshold = Mathf.Clamp01(parsedConf);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentEnableTranscriptParser = GUILayout.Toggle(
                localAiAgentEnableTranscriptParser,
                "Enable transcript parser",
                GUILayout.Width(AiW(170f, aiWidthScale)));
            GUILayout.Label("STT conf", GUILayout.Width(AiW(52f, aiWidthScale)));
            string sttConfText = GUILayout.TextField(
                localAiAgentTranscriptConfidence.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(64f, aiWidthScale)));
            if (float.TryParse(sttConfText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSttConf))
            {
                localAiAgentTranscriptConfidence = Mathf.Clamp01(parsedSttConf);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Min chars", GUILayout.Width(AiW(58f, aiWidthScale)));
            string minCharsText = GUILayout.TextField(
                localAiAgentMinTranscriptChars.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(56f, aiWidthScale)));
            if (int.TryParse(
                minCharsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedMinChars))
            {
                localAiAgentMinTranscriptChars = Mathf.Max(0, parsedMinChars);
            }

            GUILayout.Label("Min words", GUILayout.Width(AiW(60f, aiWidthScale)));
            string minWordsText = GUILayout.TextField(
                localAiAgentMinTranscriptWords.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(56f, aiWidthScale)));
            if (int.TryParse(
                minWordsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedMinWords))
            {
                localAiAgentMinTranscriptWords = Mathf.Max(0, parsedMinWords);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentUseSafeNumericParsing = GUILayout.Toggle(
                localAiAgentUseSafeNumericParsing,
                "Safe numeric parsing",
                GUILayout.Width(AiW(170f, aiWidthScale)));
            localAiAgentRequireTargetTokenForJoint = GUILayout.Toggle(
                localAiAgentRequireTargetTokenForJoint,
                "Require 'to/at'",
                GUILayout.Width(AiW(110f, aiWidthScale)));
            localAiAgentRejectOutOfRangeJointCommands = GUILayout.Toggle(
                localAiAgentRejectOutOfRangeJointCommands,
                "Reject out-of-range",
                GUILayout.Width(AiW(140f, aiWidthScale)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Joint min", GUILayout.Width(AiW(58f, aiWidthScale)));
            string jointMinText = GUILayout.TextField(
                localAiAgentJointMinDegrees.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(70f, aiWidthScale)));
            if (float.TryParse(
                jointMinText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedJointMin))
            {
                localAiAgentJointMinDegrees = parsedJointMin;
            }

            GUILayout.Label("max", GUILayout.Width(AiW(30f, aiWidthScale)));
            string jointMaxText = GUILayout.TextField(
                localAiAgentJointMaxDegrees.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(70f, aiWidthScale)));
            if (float.TryParse(
                jointMaxText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedJointMax))
            {
                localAiAgentJointMaxDegrees = parsedJointMax;
            }
            if (localAiAgentJointMinDegrees > localAiAgentJointMaxDegrees)
            {
                float swap = localAiAgentJointMinDegrees;
                localAiAgentJointMinDegrees = localAiAgentJointMaxDegrees;
                localAiAgentJointMaxDegrees = swap;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentSuppressDuplicateCommands = GUILayout.Toggle(
                localAiAgentSuppressDuplicateCommands,
                "Suppress duplicates",
                GUILayout.Width(AiW(170f, aiWidthScale)));
            GUILayout.Label("Dup win", GUILayout.Width(AiW(52f, aiWidthScale)));
            string dupWindowText = GUILayout.TextField(
                localAiAgentDuplicateCommandWindowSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(64f, aiWidthScale)));
            if (float.TryParse(
                dupWindowText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedDupWindow))
            {
                localAiAgentDuplicateCommandWindowSeconds = Mathf.Max(0.05f, parsedDupWindow);
            }
            localAiAgentSimulationOnlyMode = GUILayout.Toggle(
                localAiAgentSimulationOnlyMode,
                "Simulation-only voice",
                GUILayout.Width(AiW(150f, aiWidthScale)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentEnableTtsFeedback = GUILayout.Toggle(
                localAiAgentEnableTtsFeedback,
                "Enable TTS feedback",
                GUILayout.Width(AiW(170f, aiWidthScale)));
            GUILayout.Label("TTS gap", GUILayout.Width(AiW(52f, aiWidthScale)));
            string ttsGapText = GUILayout.TextField(
                localAiAgentTtsMinIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(AiW(64f, aiWidthScale)));
            if (float.TryParse(ttsGapText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedTtsGap))
            {
                localAiAgentTtsMinIntervalSeconds = Mathf.Max(0.05f, parsedTtsGap);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("TTS url", GUILayout.Width(AiW(62f, aiWidthScale)));
            localAiAgentTtsEndpoint = GUILayout.TextField(localAiAgentTtsEndpoint);
            bool previousSpeakButtonEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent && localAiAgentEnableTtsFeedback;
            if (GUILayout.Button("Speak test", GUILayout.Width(AiW(88f, aiWidthScale)), GUILayout.Height(22f)))
            {
                QueueVoiceFeedback("Local TTS feedback test message.", interrupt: false);
            }
            GUI.enabled = previousSpeakButtonEnabled;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentEnablePushToTalk = GUILayout.Toggle(
                localAiAgentEnablePushToTalk,
                "Push-to-talk mode",
                GUILayout.Width(AiW(170f, aiWidthScale)));
            GUILayout.Label("PTT key", GUILayout.Width(AiW(52f, aiWidthScale)));
            localAiAgentPushToTalkKey = GUILayout.TextField(localAiAgentPushToTalkKey, GUILayout.Width(AiW(64f, aiWidthScale)));
            if (!localAiAgentEnablePushToTalk)
            {
                localAiAgentListeningEnabled = GUILayout.Toggle(
                    localAiAgentListeningEnabled,
                    "Listening enabled",
                    GUILayout.Width(AiW(128f, aiWidthScale)));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Listen url", GUILayout.Width(AiW(62f, aiWidthScale)));
            localAiAgentListeningEndpoint = GUILayout.TextField(localAiAgentListeningEndpoint);
            bool previousListenButtonEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent;
            if (GUILayout.Button("Listen ON", GUILayout.Width(AiW(78f, aiWidthScale)), GUILayout.Height(22f)))
            {
                localAiAgentListeningEnabled = true;
                RequestRemoteListeningState(true, force: true);
            }
            if (GUILayout.Button("Listen OFF", GUILayout.Width(AiW(82f, aiWidthScale)), GUILayout.Height(22f)))
            {
                localAiAgentListeningEnabled = false;
                RequestRemoteListeningState(false, force: true);
            }
            GUI.enabled = previousListenButtonEnabled;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentIgnoreVirtualMicrophones = GUILayout.Toggle(
                localAiAgentIgnoreVirtualMicrophones,
                "Prefer physical mic",
                GUILayout.Width(AiW(136f, aiWidthScale)));
            if (GUILayout.Button("Auto mic", GUILayout.Width(AiW(72f, aiWidthScale)), GUILayout.Height(22f)))
            {
                EnsurePreferredMicrophoneDevice(forceAuto: true);
            }
            if (GUILayout.Button("Refresh mics", GUILayout.Width(AiW(92f, aiWidthScale)), GUILayout.Height(22f)))
            {
                EnsurePreferredMicrophoneDevice();
            }
            GUILayout.EndHorizontal();

            string[] orderedMicDevices = GetOrderedMicrophoneDevices();
            string selectedMicLabel = string.IsNullOrWhiteSpace(localAiAgentPreferredMicrophoneDeviceName)
                ? "No microphone detected"
                : localAiAgentPreferredMicrophoneDeviceName;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mic", GUILayout.Width(AiW(62f, aiWidthScale)));
            float micButtonWidth = Mathf.Max(
                84f,
                area.width - AiW(62f, aiWidthScale) - AiW(130f, aiWidthScale) - 42f);
            if (GUILayout.Button(selectedMicLabel, GUILayout.Width(micButtonWidth), GUILayout.Height(22f)))
            {
                _localAiMicDropdownOpen = !_localAiMicDropdownOpen;
            }

            bool holdMicTest = GUILayout.RepeatButton(
                _localAiMicTestRecording ? "Release: play mic test" : "Hold: mic test",
                GUILayout.Width(AiW(130f, aiWidthScale)),
                GUILayout.Height(22f));
            HandleMicTestButtonFromGui(holdMicTest, Event.current.type);
            GUILayout.EndHorizontal();

            if (_localAiMicDropdownOpen)
            {
                _localAiMicDropdownScroll = GUILayout.BeginScrollView(
                    _localAiMicDropdownScroll,
                    false,
                    true,
                    GUILayout.Height(110f));

                if (orderedMicDevices.Length == 0)
                {
                    GUILayout.Label("No microphone devices detected.");
                }
                else
                {
                    for (int i = 0; i < orderedMicDevices.Length; i++)
                    {
                        string deviceName = orderedMicDevices[i];
                        bool isSelected = DeviceNameEquals(deviceName, localAiAgentPreferredMicrophoneDeviceName);
                        string virtualTag = IsLikelyVirtualMicrophone(deviceName) ? " (virtual)" : string.Empty;
                        string buttonLabel = (isSelected ? "> " : "  ") + deviceName + virtualTag;
                        if (GUILayout.Button(buttonLabel, GUILayout.Height(22f)))
                        {
                            localAiAgentPreferredMicrophoneDeviceName = deviceName;
                            _localAiMicDropdownOpen = false;
                            _voiceLastActionResult = $"Selected microphone: {deviceName}";
                        }
                    }
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawAiRuntimePanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("AI Runtime & Tools", _titleStyle);
            GUILayout.Label("Sidecar lifecycle, health policy, local help model, and diagnostics.");
            float bodyHeight = Mathf.Max(60f, area.height - 58f);
            float contentWidth = GetAiRuntimeContentWidth(area.width);
            float formWidth = Mathf.Max(200f, contentWidth - 18f);
            float halfWidth = GetAiRuntimeHalfWidth(formWidth);
            _aiRuntimeScroll = GUILayout.BeginScrollView(
                _aiRuntimeScroll,
                false,
                true,
                GUILayout.Height(bodyHeight));
            BeginAiCenteredColumn(contentWidth);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Sidecar");
            GUILayout.BeginHorizontal();
            localAiAgentAutoStartSidecar = GUILayout.Toggle(
                localAiAgentAutoStartSidecar,
                "Auto-start",
                GUILayout.Width(halfWidth));
            localAiAgentAutoStopAutoStartedSidecarOnDisable = GUILayout.Toggle(
                localAiAgentAutoStopAutoStartedSidecarOnDisable,
                "Stop on disable",
                GUILayout.Width(halfWidth));
            GUILayout.EndHorizontal();

            GUILayout.Label("Python command");
            localAiAgentSidecarPythonCommand = GUILayout.TextField(localAiAgentSidecarPythonCommand);

            GUILayout.Label("Script path");
            localAiAgentSidecarScriptRelativePath = GUILayout.TextField(localAiAgentSidecarScriptRelativePath);

            GUILayout.Label("Config path");
            localAiAgentSidecarConfigRelativePath = GUILayout.TextField(localAiAgentSidecarConfigRelativePath);

            localAiAgentSyncSidecarConfigOnStart = GUILayout.Toggle(
                localAiAgentSyncSidecarConfigOnStart,
                "Sync UI config before start");

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Log level");
            localAiAgentSidecarLogLevel = GUILayout.TextField(localAiAgentSidecarLogLevel);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Startup s");
            string sidecarStartupText = GUILayout.TextField(
                localAiAgentSidecarStartupTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            if (float.TryParse(
                sidecarStartupText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedSidecarStartup))
            {
                localAiAgentSidecarStartupTimeoutSeconds = Mathf.Max(1f, parsedSidecarStartup);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Retry s");
            string sidecarRetryText = GUILayout.TextField(
                localAiAgentSidecarRetryIntervalSeconds.ToString(CultureInfo.InvariantCulture));
            if (float.TryParse(
                sidecarRetryText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedSidecarRetry))
            {
                localAiAgentSidecarRetryIntervalSeconds = Mathf.Max(0.25f, parsedSidecarRetry);
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Probe ms");
            string sidecarProbeMsText = GUILayout.TextField(
                localAiAgentSidecarHealthTimeoutMs.ToString(CultureInfo.InvariantCulture));
            if (int.TryParse(
                sidecarProbeMsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedProbeMs))
            {
                localAiAgentSidecarHealthTimeoutMs = Mathf.Clamp(parsedProbeMs, 200, 10000);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            bool previousSidecarButtonsEnabled = GUI.enabled;
            GUI.enabled = localAiAgentAutoStartSidecar;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start sidecar", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                bool started = TryStartLocalAiSidecarProcess(out string startMessage);
                _localAiAgentSidecarStatus = startMessage;
                if (started)
                {
                    _localAiAgentSidecarStartupActive = true;
                    _localAiAgentSidecarInitialProbeCompleted = true;
                    _localAiAgentSidecarStartupStartedAt = Time.unscaledTime;
                    _localAiAgentSidecarNextProbeAt = Time.unscaledTime + 0.2f;
                    _localAiAgentSidecarReady = false;
                }
            }
            if (GUILayout.Button("Stop sidecar", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                StopAutoStartedSidecarIfRunning("manual stop");
                _localAiAgentSidecarReady = false;
            }
            GUILayout.EndHorizontal();
            GUI.enabled = previousSidecarButtonsEnabled;

            if (!string.IsNullOrWhiteSpace(_localAiAgentSidecarStatus))
            {
                GUILayout.Label($"Status: {_localAiAgentSidecarStatus} (ready={_localAiAgentSidecarReady})");
            }
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Bridge policy");
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Heartbeat s");
            string heartbeatText = GUILayout.TextField(
                localAiAgentHeartbeatTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            if (float.TryParse(heartbeatText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedHeartbeat))
            {
                localAiAgentHeartbeatTimeoutSeconds = Mathf.Max(0.5f, parsedHeartbeat);
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Degrade after");
            string degradeText = GUILayout.TextField(
                localAiAgentDegradedFailureThreshold.ToString(CultureInfo.InvariantCulture));
            if (int.TryParse(degradeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDegradeThreshold))
            {
                localAiAgentDegradedFailureThreshold = Mathf.Max(1, parsedDegradeThreshold);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Backoff min");
            string retryMinText = GUILayout.TextField(
                localAiAgentRetryBackoffMinSeconds.ToString(CultureInfo.InvariantCulture));
            if (float.TryParse(retryMinText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedRetryMin))
            {
                localAiAgentRetryBackoffMinSeconds = Mathf.Max(0.05f, parsedRetryMin);
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Backoff max");
            string retryMaxText = GUILayout.TextField(
                localAiAgentRetryBackoffMaxSeconds.ToString(CultureInfo.InvariantCulture));
            if (float.TryParse(retryMaxText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedRetryMax))
            {
                localAiAgentRetryBackoffMaxSeconds = Mathf.Max(localAiAgentRetryBackoffMinSeconds, parsedRetryMax);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            localAiAgentBlockMotionWhenBridgeUnhealthy = GUILayout.Toggle(
                localAiAgentBlockMotionWhenBridgeUnhealthy,
                "Block motion when bridge is stale");
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Local help");
            GUILayout.BeginHorizontal();
            localAiAgentEnableLocalHelpModel = GUILayout.Toggle(
                localAiAgentEnableLocalHelpModel,
                "Enable help model",
                GUILayout.Width(halfWidth));
            bool previousHelpBtnEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent && localAiAgentEnableLocalHelpModel;
            if (GUILayout.Button("Ask help", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                QueueLocalHelpRequest(out string helpMsg);
                _voiceLastActionResult = helpMsg;
            }
            GUI.enabled = previousHelpBtnEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Label("Help URL");
            localAiAgentHelpEndpoint = GUILayout.TextField(localAiAgentHelpEndpoint);

            GUILayout.Label("Help context");
            localAiAgentHelpContext = GUILayout.TextField(localAiAgentHelpContext);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Backend");
            localAiAgentHelpModelBackend = GUILayout.TextField(localAiAgentHelpModelBackend);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Temperature");
            string helpTempText = GUILayout.TextField(
                localAiAgentHelpModelTemperature.ToString(CultureInfo.InvariantCulture));
            if (float.TryParse(helpTempText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedHelpTemp))
            {
                localAiAgentHelpModelTemperature = Mathf.Clamp(parsedHelpTemp, 0f, 1.5f);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Max tokens");
            string helpMaxTokensText = GUILayout.TextField(
                localAiAgentHelpModelMaxTokens.ToString(CultureInfo.InvariantCulture));
            if (int.TryParse(helpMaxTokensText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHelpMaxTokens))
            {
                localAiAgentHelpModelMaxTokens = Mathf.Clamp(parsedHelpMaxTokens, 16, 512);
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(halfWidth));
            GUILayout.Label("Max chars");
            string helpMaxCharsText = GUILayout.TextField(
                localAiAgentHelpMaxAnswerChars.ToString(CultureInfo.InvariantCulture));
            if (int.TryParse(helpMaxCharsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHelpMaxChars))
            {
                localAiAgentHelpMaxAnswerChars = Mathf.Clamp(parsedHelpMaxChars, 80, 1200);
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Label("Model path");
            localAiAgentHelpModelPath = GUILayout.TextField(localAiAgentHelpModelPath);
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Diagnostics");
            bool previousBridgeControlEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force poll", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                ForceVoiceBridgePoll();
            }
            if (GUILayout.Button("Reset bridge", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                ResetVoiceBridgeState();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save logs", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                ExportVoiceBridgeLogs();
            }
            if (GUILayout.Button("Clear logs", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                ClearVoiceBridgeLogs();
            }
            GUILayout.EndHorizontal();
            GUI.enabled = previousBridgeControlEnabled;

            if (!string.IsNullOrWhiteSpace(_voiceLastLogExportPath))
            {
                GUILayout.Label($"Log file: {_voiceLastLogExportPath}");
            }

            GUILayout.Label("Mock STT");
            localAiAgentTranscriptInput = GUILayout.TextField(localAiAgentTranscriptInput);

            GUILayout.BeginHorizontal();
            localAiAgentMockTranscriptIsFinal = GUILayout.Toggle(
                localAiAgentMockTranscriptIsFinal,
                "Final",
                GUILayout.Width(halfWidth));
            bool previousButtonEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent;
            if (GUILayout.Button("Send mock STT", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                EnqueueMockTranscriptIntent(
                    localAiAgentTranscriptInput,
                    localAiAgentTranscriptConfidence,
                    localAiAgentMockTranscriptIsFinal);
            }
            GUI.enabled = previousButtonEnabled;
            GUILayout.EndHorizontal();

            if (enableLocalAiAgent)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Mock pose", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    EnqueueMockPoseIntent("Neutral Arms");
                }
                if (GUILayout.Button("Mock status", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    EnqueueMockStatusIntent();
                }
                if (GUILayout.Button("Mock help", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    EnqueueMockHelpIntent();
                }
                GUILayout.EndHorizontal();
            }

            if (_voiceHasPendingAction)
            {
                GUILayout.Label($"Pending: {_voicePendingAction.Summary}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    ConfirmPendingVoiceAction();
                }
                if (GUILayout.Button("Reject", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    RejectPendingVoiceAction();
                }
                GUILayout.EndHorizontal();
            }

            localAiAgentShowRawIntentPayload = GUILayout.Toggle(localAiAgentShowRawIntentPayload, "Show raw payload");
            GUILayout.EndVertical();

            EndAiCenteredColumn();

            GUILayout.Space(8f);
            GUILayout.Label("Bridge readout");
            VoiceAgentStatusPanel.DrawReadout(_voiceAgentStatusState);
            if (localAiAgentShowRawIntentPayload && !string.IsNullOrWhiteSpace(_voiceLastRawIntentPayload))
            {
                GUILayout.Label("Raw intent payload");
                GUILayout.TextArea(_voiceLastRawIntentPayload, GUILayout.Height(64f));
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawAiStatusWindowPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            DrawStatusSection();
            GUILayout.EndArea();
        }

        private void DrawLeftPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Reachy 1 Control Panel", _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_collapsed ? "Expand" : "Collapse", GUILayout.Width(90f)))
            {
                _collapsed = !_collapsed;
            }
            GUILayout.EndHorizontal();

            if (_collapsed)
            {
                GUILayout.Label($"Status: {_status}");
                GUILayout.EndArea();
                return;
            }

            float scrollHeight = Mathf.Max(120f, area.height - 45f);
            _leftPanelScroll = GUILayout.BeginScrollView(
                _leftPanelScroll,
                false,
                true,
                GUILayout.Height(scrollHeight));

            DrawConnectionSection();
            GUILayout.Space(10f);
            DrawAutomationSection();

            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawRightPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            DrawRightPanelHeader();
            GUILayout.Space(4f);
            DrawJointSection();
            GUILayout.Space(10f);
            DrawPresetSection();
            GUILayout.Space(10f);
            DrawStatusSection();
            GUILayout.EndArea();
        }

        private void DrawCameraPreviewPanel(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Reachy Camera Preview", _titleStyle);
            GUILayout.FlexibleSpace();

            bool nextUseRightEye = GUILayout.Toggle(cameraUseRightEye, "Right eye", GUILayout.Width(90f));
            if (nextUseRightEye != cameraUseRightEye)
            {
                cameraUseRightEye = nextUseRightEye;
                _nextCameraFetchAt = 0f;
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70f), GUILayout.Height(22f)))
            {
                _nextCameraFetchAt = 0f;
            }

            GUILayout.EndHorizontal();

            GUILayout.Label(
                $"Source mode: {GetModeLabel(_cameraPreviewMode)} | Endpoint: {_cameraPreviewHost}:{_cameraPreviewPort}");

            Rect imageRect = GUILayoutUtility.GetRect(
                area.width - 24f,
                Mathf.Max(120f, area.height - 85f),
                GUILayout.ExpandWidth(true));

            if (_cameraPreviewTexture != null)
            {
                GUI.DrawTexture(imageRect, _cameraPreviewTexture, ScaleMode.ScaleToFit, false);
            }
            else
            {
                GUI.Box(imageRect, "No camera frame yet.");
            }

            GUILayout.Label(_cameraPreviewStatus);
            GUILayout.EndArea();
        }

        private void DrawLocalAgentPanel(
            Rect area,
            bool showExpandControls = true,
            bool forceExpanded = false,
            string panelTitle = "Local AI Agent")
        {
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(panelTitle, _titleStyle);
            GUILayout.FlexibleSpace();

            bool panelExpanded = forceExpanded || localAiAgentPanelExpanded;

            if (showExpandControls)
            {
                bool previousGuiEnabled = GUI.enabled;
                GUI.enabled = !localAiAgentPanelExpanded;
                if (GUILayout.Button("Expand", GUILayout.Width(68f), GUILayout.Height(22f)))
                {
                    localAiAgentPanelExpanded = true;
                }

                GUI.enabled = localAiAgentPanelExpanded;
                if (GUILayout.Button("Collapse", GUILayout.Width(68f), GUILayout.Height(22f)))
                {
                    localAiAgentPanelExpanded = false;
                }
                GUI.enabled = previousGuiEnabled;
                panelExpanded = forceExpanded || localAiAgentPanelExpanded;
            }
            GUILayout.EndHorizontal();

            bool previousEnabled = enableLocalAiAgent;
            enableLocalAiAgent = GUILayout.Toggle(enableLocalAiAgent, "Enable local AI agent");
            if (previousEnabled && !enableLocalAiAgent)
            {
                RejectPendingVoiceAction();
            }

            if (!panelExpanded)
            {
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Phase 6E: parser safety + sidecar lifecycle + config + optional local help LLM.");

            float bodyHeight = Mathf.Max(60f, area.height - 74f);
            _localAgentScroll = GUILayout.BeginScrollView(
                _localAgentScroll,
                false,
                true,
                GUILayout.Height(bodyHeight));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Endpoint", GUILayout.Width(62f));
            localAiAgentEndpoint = GUILayout.TextField(localAiAgentEndpoint);
            if (GUILayout.Button("Load cfg", GUILayout.Width(72f), GUILayout.Height(22f)))
            {
                bool loaded = TryLoadVoiceAgentConfigFromDisk(out string loadMessage);
                _voiceLastActionResult = loadMessage;
                if (!loaded)
                {
                    _voiceLastParserMessage = loadMessage;
                }
            }
            if (GUILayout.Button("Save cfg", GUILayout.Width(72f), GUILayout.Height(22f)))
            {
                bool saved = TrySaveVoiceAgentConfigToDisk(out string saveMessage);
                _voiceLastActionResult = saveMessage;
                if (!saved)
                {
                    _voiceLastParserMessage = saveMessage;
                }
            }
            if (GUILayout.Button("Sync sidecar", GUILayout.Width(92f), GUILayout.Height(22f)))
            {
                bool synced = TrySyncLocalSidecarConfigFromUi(out string syncMessage);
                _voiceLastActionResult = syncMessage;
                if (!synced)
                {
                    _voiceLastParserMessage = syncMessage;
                }
            }
            if (GUILayout.Button("Load sidecar", GUILayout.Width(92f), GUILayout.Height(22f)))
            {
                bool loadedSidecar = TryLoadLocalSidecarConfigIntoUi(out string sidecarLoadMessage);
                _voiceLastActionResult = sidecarLoadMessage;
                if (!loadedSidecar)
                {
                    _voiceLastParserMessage = sidecarLoadMessage;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Poll(s)", GUILayout.Width(62f));
            string pollText = GUILayout.TextField(
                localAiAgentPollIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(64f));
            if (float.TryParse(pollText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedPoll))
            {
                localAiAgentPollIntervalSeconds = Mathf.Max(0.1f, parsedPoll);
            }

            GUILayout.Label("Conf", GUILayout.Width(34f));
            string confText = GUILayout.TextField(
                localAiAgentConfidenceThreshold.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(64f));
            if (float.TryParse(confText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedConf))
            {
                localAiAgentConfidenceThreshold = Mathf.Clamp01(parsedConf);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentEnableTranscriptParser = GUILayout.Toggle(
                localAiAgentEnableTranscriptParser,
                "Enable transcript parser",
                GUILayout.Width(170f));
            GUILayout.Label("STT conf", GUILayout.Width(52f));
            string sttConfText = GUILayout.TextField(
                localAiAgentTranscriptConfidence.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(64f));
            if (float.TryParse(sttConfText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSttConf))
            {
                localAiAgentTranscriptConfidence = Mathf.Clamp01(parsedSttConf);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Min chars", GUILayout.Width(58f));
            string minCharsText = GUILayout.TextField(
                localAiAgentMinTranscriptChars.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(56f));
            if (int.TryParse(
                minCharsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedMinChars))
            {
                localAiAgentMinTranscriptChars = Mathf.Max(0, parsedMinChars);
            }

            GUILayout.Label("Min words", GUILayout.Width(60f));
            string minWordsText = GUILayout.TextField(
                localAiAgentMinTranscriptWords.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(56f));
            if (int.TryParse(
                minWordsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedMinWords))
            {
                localAiAgentMinTranscriptWords = Mathf.Max(0, parsedMinWords);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentUseSafeNumericParsing = GUILayout.Toggle(
                localAiAgentUseSafeNumericParsing,
                "Safe numeric parsing",
                GUILayout.Width(170f));
            localAiAgentRequireTargetTokenForJoint = GUILayout.Toggle(
                localAiAgentRequireTargetTokenForJoint,
                "Require 'to/at'",
                GUILayout.Width(110f));
            localAiAgentRejectOutOfRangeJointCommands = GUILayout.Toggle(
                localAiAgentRejectOutOfRangeJointCommands,
                "Reject out-of-range",
                GUILayout.Width(140f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Joint min", GUILayout.Width(58f));
            string jointMinText = GUILayout.TextField(
                localAiAgentJointMinDegrees.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(
                jointMinText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedJointMin))
            {
                localAiAgentJointMinDegrees = parsedJointMin;
            }

            GUILayout.Label("max", GUILayout.Width(30f));
            string jointMaxText = GUILayout.TextField(
                localAiAgentJointMaxDegrees.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(
                jointMaxText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedJointMax))
            {
                localAiAgentJointMaxDegrees = parsedJointMax;
            }
            if (localAiAgentJointMinDegrees > localAiAgentJointMaxDegrees)
            {
                float swap = localAiAgentJointMinDegrees;
                localAiAgentJointMinDegrees = localAiAgentJointMaxDegrees;
                localAiAgentJointMaxDegrees = swap;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentSuppressDuplicateCommands = GUILayout.Toggle(
                localAiAgentSuppressDuplicateCommands,
                "Suppress duplicates",
                GUILayout.Width(170f));
            GUILayout.Label("Dup win", GUILayout.Width(52f));
            string dupWindowText = GUILayout.TextField(
                localAiAgentDuplicateCommandWindowSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(64f));
            if (float.TryParse(
                dupWindowText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedDupWindow))
            {
                localAiAgentDuplicateCommandWindowSeconds = Mathf.Max(0.05f, parsedDupWindow);
            }
            localAiAgentSimulationOnlyMode = GUILayout.Toggle(
                localAiAgentSimulationOnlyMode,
                "Simulation-only voice",
                GUILayout.Width(150f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentEnableTtsFeedback = GUILayout.Toggle(
                localAiAgentEnableTtsFeedback,
                "Enable TTS feedback",
                GUILayout.Width(170f));
            GUILayout.Label("TTS gap", GUILayout.Width(52f));
            string ttsGapText = GUILayout.TextField(
                localAiAgentTtsMinIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(64f));
            if (float.TryParse(ttsGapText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedTtsGap))
            {
                localAiAgentTtsMinIntervalSeconds = Mathf.Max(0.05f, parsedTtsGap);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("TTS url", GUILayout.Width(62f));
            localAiAgentTtsEndpoint = GUILayout.TextField(localAiAgentTtsEndpoint);
            bool previousSpeakButtonEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent && localAiAgentEnableTtsFeedback;
            if (GUILayout.Button("Speak test", GUILayout.Width(88f), GUILayout.Height(22f)))
            {
                QueueVoiceFeedback("Local TTS feedback test message.", interrupt: false);
            }
            GUI.enabled = previousSpeakButtonEnabled;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentEnablePushToTalk = GUILayout.Toggle(
                localAiAgentEnablePushToTalk,
                "Push-to-talk mode",
                GUILayout.Width(170f));
            GUILayout.Label("PTT key", GUILayout.Width(52f));
            localAiAgentPushToTalkKey = GUILayout.TextField(localAiAgentPushToTalkKey, GUILayout.Width(64f));
            if (!localAiAgentEnablePushToTalk)
            {
                localAiAgentListeningEnabled = GUILayout.Toggle(
                    localAiAgentListeningEnabled,
                    "Listening enabled",
                    GUILayout.Width(128f));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Listen url", GUILayout.Width(62f));
            localAiAgentListeningEndpoint = GUILayout.TextField(localAiAgentListeningEndpoint);
            bool previousListenButtonEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent;
            if (GUILayout.Button("Listen ON", GUILayout.Width(78f), GUILayout.Height(22f)))
            {
                localAiAgentListeningEnabled = true;
                RequestRemoteListeningState(true, force: true);
            }
            if (GUILayout.Button("Listen OFF", GUILayout.Width(82f), GUILayout.Height(22f)))
            {
                localAiAgentListeningEnabled = false;
                RequestRemoteListeningState(false, force: true);
            }
            GUI.enabled = previousListenButtonEnabled;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            localAiAgentIgnoreVirtualMicrophones = GUILayout.Toggle(
                localAiAgentIgnoreVirtualMicrophones,
                "Prefer physical mic",
                GUILayout.Width(136f));
            if (GUILayout.Button("Auto mic", GUILayout.Width(72f), GUILayout.Height(22f)))
            {
                EnsurePreferredMicrophoneDevice(forceAuto: true);
            }
            if (GUILayout.Button("Refresh mics", GUILayout.Width(92f), GUILayout.Height(22f)))
            {
                EnsurePreferredMicrophoneDevice();
            }
            GUILayout.EndHorizontal();

            string[] orderedMicDevices = GetOrderedMicrophoneDevices();
            string selectedMicLabel = string.IsNullOrWhiteSpace(localAiAgentPreferredMicrophoneDeviceName)
                ? "No microphone detected"
                : localAiAgentPreferredMicrophoneDeviceName;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mic", GUILayout.Width(62f));
            if (GUILayout.Button(selectedMicLabel, GUILayout.Height(22f)))
            {
                _localAiMicDropdownOpen = !_localAiMicDropdownOpen;
            }

            bool holdMicTest = GUILayout.RepeatButton(
                _localAiMicTestRecording ? "Release: play mic test" : "Hold: mic test",
                GUILayout.Width(130f),
                GUILayout.Height(22f));
            HandleMicTestButtonFromGui(holdMicTest, Event.current.type);
            GUILayout.EndHorizontal();

            if (_localAiMicDropdownOpen)
            {
                _localAiMicDropdownScroll = GUILayout.BeginScrollView(
                    _localAiMicDropdownScroll,
                    false,
                    true,
                    GUILayout.Height(110f));

                if (orderedMicDevices.Length == 0)
                {
                    GUILayout.Label("No microphone devices detected.");
                }
                else
                {
                    for (int i = 0; i < orderedMicDevices.Length; i++)
                    {
                        string deviceName = orderedMicDevices[i];
                        bool isSelected = DeviceNameEquals(deviceName, localAiAgentPreferredMicrophoneDeviceName);
                        string virtualTag = IsLikelyVirtualMicrophone(deviceName) ? " (virtual)" : string.Empty;
                        string buttonLabel = (isSelected ? "> " : "  ") + deviceName + virtualTag;
                        if (GUILayout.Button(buttonLabel, GUILayout.Height(22f)))
                        {
                            localAiAgentPreferredMicrophoneDeviceName = deviceName;
                            _localAiMicDropdownOpen = false;
                            _voiceLastActionResult = $"Selected microphone: {deviceName}";
                        }
                    }
                }

                GUILayout.EndScrollView();
            }

            GUILayout.BeginHorizontal();
            localAiAgentAutoStartSidecar = GUILayout.Toggle(
                localAiAgentAutoStartSidecar,
                "Auto-start sidecar",
                GUILayout.Width(150f));
            localAiAgentAutoStopAutoStartedSidecarOnDisable = GUILayout.Toggle(
                localAiAgentAutoStopAutoStartedSidecarOnDisable,
                "Stop on disable",
                GUILayout.Width(118f));
            GUILayout.Label("Py cmd", GUILayout.Width(44f));
            localAiAgentSidecarPythonCommand = GUILayout.TextField(localAiAgentSidecarPythonCommand, GUILayout.Width(88f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Script rel", GUILayout.Width(62f));
            localAiAgentSidecarScriptRelativePath = GUILayout.TextField(localAiAgentSidecarScriptRelativePath);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Cfg rel", GUILayout.Width(62f));
            localAiAgentSidecarConfigRelativePath = GUILayout.TextField(localAiAgentSidecarConfigRelativePath);
            GUILayout.EndHorizontal();

            localAiAgentSyncSidecarConfigOnStart = GUILayout.Toggle(
                localAiAgentSyncSidecarConfigOnStart,
                "Sync sidecar config from UI before sidecar start");

            GUILayout.BeginHorizontal();
            GUILayout.Label("S log", GUILayout.Width(38f));
            localAiAgentSidecarLogLevel = GUILayout.TextField(localAiAgentSidecarLogLevel, GUILayout.Width(58f));
            GUILayout.Label("Start TO", GUILayout.Width(54f));
            string sidecarStartupText = GUILayout.TextField(
                localAiAgentSidecarStartupTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(56f));
            if (float.TryParse(
                sidecarStartupText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedSidecarStartup))
            {
                localAiAgentSidecarStartupTimeoutSeconds = Mathf.Max(1f, parsedSidecarStartup);
            }

            GUILayout.Label("Retry", GUILayout.Width(34f));
            string sidecarRetryText = GUILayout.TextField(
                localAiAgentSidecarRetryIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(56f));
            if (float.TryParse(
                sidecarRetryText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedSidecarRetry))
            {
                localAiAgentSidecarRetryIntervalSeconds = Mathf.Max(0.25f, parsedSidecarRetry);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Probe ms", GUILayout.Width(62f));
            string sidecarProbeMsText = GUILayout.TextField(
                localAiAgentSidecarHealthTimeoutMs.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(68f));
            if (int.TryParse(
                sidecarProbeMsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsedProbeMs))
            {
                localAiAgentSidecarHealthTimeoutMs = Mathf.Clamp(parsedProbeMs, 200, 10000);
            }

            bool previousSidecarButtonsEnabled = GUI.enabled;
            GUI.enabled = localAiAgentAutoStartSidecar;
            if (GUILayout.Button("Start sidecar", GUILayout.Width(92f), GUILayout.Height(22f)))
            {
                bool started = TryStartLocalAiSidecarProcess(out string startMessage);
                _localAiAgentSidecarStatus = startMessage;
                if (started)
                {
                    _localAiAgentSidecarStartupActive = true;
                    _localAiAgentSidecarInitialProbeCompleted = true;
                    _localAiAgentSidecarStartupStartedAt = Time.unscaledTime;
                    _localAiAgentSidecarNextProbeAt = Time.unscaledTime + 0.2f;
                    _localAiAgentSidecarReady = false;
                }
            }
            if (GUILayout.Button("Stop sidecar", GUILayout.Width(86f), GUILayout.Height(22f)))
            {
                StopAutoStartedSidecarIfRunning("manual stop");
                _localAiAgentSidecarReady = false;
            }
            GUI.enabled = previousSidecarButtonsEnabled;
            GUILayout.EndHorizontal();

            if (localAiAgentAutoStartSidecar)
            {
                GUILayout.Label(
                    $"Sidecar status: {_localAiAgentSidecarStatus} (ready={_localAiAgentSidecarReady})");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("HB(s)", GUILayout.Width(42f));
            string heartbeatText = GUILayout.TextField(
                localAiAgentHeartbeatTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(52f));
            if (float.TryParse(heartbeatText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedHeartbeat))
            {
                localAiAgentHeartbeatTimeoutSeconds = Mathf.Max(0.5f, parsedHeartbeat);
            }

            GUILayout.Label("Retry min", GUILayout.Width(56f));
            string retryMinText = GUILayout.TextField(
                localAiAgentRetryBackoffMinSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(52f));
            if (float.TryParse(retryMinText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedRetryMin))
            {
                localAiAgentRetryBackoffMinSeconds = Mathf.Max(0.05f, parsedRetryMin);
            }

            GUILayout.Label("max", GUILayout.Width(30f));
            string retryMaxText = GUILayout.TextField(
                localAiAgentRetryBackoffMaxSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(52f));
            if (float.TryParse(retryMaxText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedRetryMax))
            {
                localAiAgentRetryBackoffMaxSeconds = Mathf.Max(localAiAgentRetryBackoffMinSeconds, parsedRetryMax);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Degr fail", GUILayout.Width(60f));
            string degradeText = GUILayout.TextField(
                localAiAgentDegradedFailureThreshold.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(52f));
            if (int.TryParse(degradeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDegradeThreshold))
            {
                localAiAgentDegradedFailureThreshold = Mathf.Max(1, parsedDegradeThreshold);
            }
            GUILayout.EndHorizontal();

            localAiAgentBlockMotionWhenBridgeUnhealthy = GUILayout.Toggle(
                localAiAgentBlockMotionWhenBridgeUnhealthy,
                "Block motion commands while bridge degraded/stale");

            GUILayout.BeginHorizontal();
            localAiAgentEnableLocalHelpModel = GUILayout.Toggle(
                localAiAgentEnableLocalHelpModel,
                "Enable local help model",
                GUILayout.Width(170f));
            GUILayout.Label("Help url", GUILayout.Width(52f));
            localAiAgentHelpEndpoint = GUILayout.TextField(localAiAgentHelpEndpoint);
            bool previousHelpBtnEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent && localAiAgentEnableLocalHelpModel;
            if (GUILayout.Button("Ask help", GUILayout.Width(78f), GUILayout.Height(22f)))
            {
                QueueLocalHelpRequest(out string helpMsg);
                _voiceLastActionResult = helpMsg;
            }
            GUI.enabled = previousHelpBtnEnabled;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Help ctx", GUILayout.Width(62f));
            localAiAgentHelpContext = GUILayout.TextField(localAiAgentHelpContext);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Help LLM", GUILayout.Width(62f));
            localAiAgentHelpModelBackend = GUILayout.TextField(localAiAgentHelpModelBackend, GUILayout.Width(92f));
            GUILayout.Label("Max tok", GUILayout.Width(50f));
            string helpMaxTokensText = GUILayout.TextField(
                localAiAgentHelpModelMaxTokens.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(56f));
            if (int.TryParse(helpMaxTokensText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHelpMaxTokens))
            {
                localAiAgentHelpModelMaxTokens = Mathf.Clamp(parsedHelpMaxTokens, 16, 512);
            }

            GUILayout.Label("Temp", GUILayout.Width(34f));
            string helpTempText = GUILayout.TextField(
                localAiAgentHelpModelTemperature.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(52f));
            if (float.TryParse(helpTempText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedHelpTemp))
            {
                localAiAgentHelpModelTemperature = Mathf.Clamp(parsedHelpTemp, 0f, 1.5f);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Help mdl", GUILayout.Width(62f));
            localAiAgentHelpModelPath = GUILayout.TextField(localAiAgentHelpModelPath);
            GUILayout.Label("Max chr", GUILayout.Width(48f));
            string helpMaxCharsText = GUILayout.TextField(
                localAiAgentHelpMaxAnswerChars.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(56f));
            if (int.TryParse(helpMaxCharsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHelpMaxChars))
            {
                localAiAgentHelpMaxAnswerChars = Mathf.Clamp(parsedHelpMaxChars, 80, 1200);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool previousBridgeControlEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent;
            if (GUILayout.Button("Force poll", GUILayout.Height(22f)))
            {
                ForceVoiceBridgePoll();
            }
            if (GUILayout.Button("Reset bridge", GUILayout.Height(22f)))
            {
                ResetVoiceBridgeState();
            }
            if (GUILayout.Button("Save logs", GUILayout.Height(22f)))
            {
                ExportVoiceBridgeLogs();
            }
            if (GUILayout.Button("Clear logs", GUILayout.Height(22f)))
            {
                ClearVoiceBridgeLogs();
            }
            GUI.enabled = previousBridgeControlEnabled;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_voiceLastLogExportPath))
            {
                GUILayout.Label($"Log file: {_voiceLastLogExportPath}");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("STT text", GUILayout.Width(62f));
            localAiAgentTranscriptInput = GUILayout.TextField(localAiAgentTranscriptInput);
            localAiAgentMockTranscriptIsFinal = GUILayout.Toggle(
                localAiAgentMockTranscriptIsFinal,
                "Final",
                GUILayout.Width(60f));
            bool previousButtonEnabled = GUI.enabled;
            GUI.enabled = enableLocalAiAgent;
            if (GUILayout.Button("Mock STT", GUILayout.Width(88f), GUILayout.Height(22f)))
            {
                EnqueueMockTranscriptIntent(
                    localAiAgentTranscriptInput,
                    localAiAgentTranscriptConfidence,
                    localAiAgentMockTranscriptIsFinal);
            }
            GUI.enabled = previousButtonEnabled;
            GUILayout.EndHorizontal();

            if (enableLocalAiAgent)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Mock Pose", GUILayout.Height(22f)))
                {
                    EnqueueMockPoseIntent("Neutral Arms");
                }
                if (GUILayout.Button("Mock Status", GUILayout.Height(22f)))
                {
                    EnqueueMockStatusIntent();
                }
                if (GUILayout.Button("Mock Help", GUILayout.Height(22f)))
                {
                    EnqueueMockHelpIntent();
                }
                GUILayout.EndHorizontal();
            }

            if (_voiceHasPendingAction)
            {
                GUILayout.Label($"Pending: {_voicePendingAction.Summary}");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm", GUILayout.Height(22f)))
                {
                    ConfirmPendingVoiceAction();
                }
                if (GUILayout.Button("Reject", GUILayout.Height(22f)))
                {
                    RejectPendingVoiceAction();
                }
                GUILayout.EndHorizontal();
            }

            VoiceAgentStatusPanel.DrawReadout(_voiceAgentStatusState);
            localAiAgentShowRawIntentPayload = GUILayout.Toggle(localAiAgentShowRawIntentPayload, "Show raw payload");
            if (localAiAgentShowRawIntentPayload && !string.IsNullOrWhiteSpace(_voiceLastRawIntentPayload))
            {
                GUILayout.TextArea(_voiceLastRawIntentPayload, GUILayout.Height(38f));
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRightPanelHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Commands & Poses", _titleStyle);
            GUILayout.EndHorizontal();
        }

        private void DrawConnectionSection()
        {
            GUILayout.Label("Connection", _titleStyle);

            string[] modeOptions = { "Simulation", "Real Robot" };
            mode = (ReachyControlMode)GUILayout.Toolbar((int)mode, modeOptions);

            string host = mode == ReachyControlMode.Simulation ? simulationHost : robotHost;
            int port = mode == ReachyControlMode.Simulation ? simulationPort : robotPort;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Host", GUILayout.Width(60f));
            host = GUILayout.TextField(host);
            GUILayout.Label("Port", GUILayout.Width(40f));
            string portText = GUILayout.TextField(port.ToString(CultureInfo.InvariantCulture), GUILayout.Width(90f));
            if (int.TryParse(portText, out int parsedPort))
            {
                port = parsedPort;
            }
            GUILayout.EndHorizontal();

            if (mode == ReachyControlMode.Simulation)
            {
                simulationHost = host;
                simulationPort = port;
            }
            else
            {
                robotHost = host;
                robotPort = port;
            }

            bool hasActiveConnection = !_isConnectAttemptInProgress && _client != null && _client.IsConnected;
            bool connectedForSelectedMode = hasActiveConnection && IsConnectionForMode(mode);
            bool showAttemptingConnectLabel =
                _isConnectAttemptInProgress &&
                _connectAttemptMode == mode &&
                !connectedForSelectedMode;
            string connectButtonLabel = showAttemptingConnectLabel ? "Attempting to connect..." : "Connect";

            GUILayout.BeginHorizontal();
            if (!connectedForSelectedMode)
            {
                bool previousEnabled = GUI.enabled;
                GUI.enabled = !_isConnectAttemptInProgress;
                if (GUILayout.Button(connectButtonLabel, GUILayout.Height(30f)))
                {
                    if (hasActiveConnection)
                    {
                        SetStatus(
                            "Switching mode connection",
                            $"Switching from {GetConnectedModeLabel()} to {GetModeLabel(mode)}."
                        );
                    }

                    TryStartConnectAttempt("Manual connect", mode, 0.5f, ensureOneUiFrameBeforeConnect: true);
                }
                GUI.enabled = previousEnabled;
            }
            else
            {
                bool previousEnabled = GUI.enabled;
                GUI.enabled = !_isConnectAttemptInProgress;
                if (GUILayout.Button("Disconnect", GUILayout.Height(30f)))
                {
                    ReachyControlMode disconnectedMode = _connectedMode ?? mode;
                    _manualDisconnect = true;
                    _autoReconnectScheduled = false;
                    _client.Disconnect();
                    _connectedMode = null;
                    LogConnectionEvent(disconnectedMode, "manual-disconnect", "Disconnected by UI button.");
                    SetStatus("Disconnected", "Connection closed.");
                }
                GUI.enabled = previousEnabled;
            }

            {
                bool previousEnabled = GUI.enabled;
                GUI.enabled = !_isConnectAttemptInProgress;
                if (GUILayout.Button("Refresh Joints", GUILayout.Height(30f)))
                {
                    bool wasConnected = _client.IsConnected;
                    bool ok = _client.RefreshJoints(out string message);
                    ReachyControlMode activeMode = _connectedMode ?? mode;
                    LogConnectionEvent(
                        activeMode,
                        "refresh-joints",
                        $"Result={(ok ? "ok" : "failed")}; message={message}",
                        ok ? "INFO" : "ERROR");
                    SetStatus(ok ? "Joint list updated" : "Refresh failed", message);
                    HandlePotentialDisconnectAfterOperation("joint refresh", wasConnected);
                }
                GUI.enabled = previousEnabled;
            }
            GUILayout.EndHorizontal();

            if (!hasActiveConnection)
            {
                GUILayout.BeginHorizontal();
                bool previousEnabled = GUI.enabled;
                GUI.enabled = !_isConnectAttemptInProgress;
                if (GUILayout.Button("Recover Now", GUILayout.Height(24f)))
                {
                    TryStartConnectAttempt("Manual recovery", mode, 0.5f, ensureOneUiFrameBeforeConnect: true);
                }
                GUI.enabled = previousEnabled;

                if (_autoReconnectScheduled)
                {
                    previousEnabled = GUI.enabled;
                    GUI.enabled = !_isConnectAttemptInProgress;
                    if (GUILayout.Button("Cancel Auto-Reconnect", GUILayout.Height(24f)))
                    {
                        _autoReconnectScheduled = false;
                        SetStatus("Auto-reconnect cancelled", "Recovery loop paused.");
                    }
                    GUI.enabled = previousEnabled;
                }

                GUILayout.EndHorizontal();
            }

            if (hasActiveConnection)
            {
                string connectedModeLabel = GetConnectedModeLabel();
                GUILayout.Label($"Connected mode: {connectedModeLabel}");
                GUILayout.Label($"Connected endpoint: {_client.ConnectedHost}:{_client.ConnectedPort}");
                GUILayout.Label($"Discovered joints: {_client.JointNames.Count}");
                if (!connectedForSelectedMode)
                {
                    GUILayout.Label($"Selected mode is {GetModeLabel(mode)}. Press Connect to switch modes.");
                }
            }
            else
            {
                GUILayout.Label("Not connected.");
                if (_isConnectAttemptInProgress)
                {
                    GUILayout.Label($"Connecting to {GetModeLabel(_connectAttemptMode)}...");
                }
                if (_autoReconnectScheduled)
                {
                    float inSeconds = Mathf.Max(0f, _nextAutoReconnectAt - Time.unscaledTime);
                    GUILayout.Label($"Auto-reconnect in {inSeconds:F1}s.");
                }
            }
        }

        private void DrawAutomationSection()
        {
            GUILayout.Label("Automation", _titleStyle);

            autoConnectOnPlay = GUILayout.Toggle(autoConnectOnPlay, "Auto-connect when Play starts");
            autoReconnect = GUILayout.Toggle(autoReconnect, "Watch connection and auto-reconnect");
            bool previousShowCameraPreview = showCameraPreview;
            showCameraPreview = GUILayout.Toggle(showCameraPreview, "Show bottom-center camera preview");
            if (showCameraPreview && !previousShowCameraPreview)
            {
                _nextCameraFetchAt = 0f;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Attempts/host", GUILayout.Width(100f));
            string attemptsText = GUILayout.TextField(
                Math.Max(1, connectAttemptsPerHost).ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (int.TryParse(attemptsText, out int parsedAttempts))
            {
                connectAttemptsPerHost = Math.Max(1, parsedAttempts);
            }

            GUILayout.Label("Retry delay", GUILayout.Width(80f));
            string retryText = GUILayout.TextField(
                retryDelaySeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(retryText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedRetry))
            {
                retryDelaySeconds = Mathf.Max(0f, parsedRetry);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("gRPC timeout", GUILayout.Width(100f));
            string connectTimeoutText = GUILayout.TextField(
                grpcConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(
                connectTimeoutText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedConnectTimeout))
            {
                grpcConnectTimeoutSeconds = Mathf.Max(0.2f, parsedConnectTimeout);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Pose speed %", GUILayout.Width(100f));
            string poseSpeedPercentText = GUILayout.TextField(
                (presetPoseTransitionSpeedScale * 100f).ToString("F0", CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(
                poseSpeedPercentText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedPoseSpeedPercent))
            {
                presetPoseTransitionSpeedScale = Mathf.Clamp(parsedPoseSpeedPercent / 100f, 0.05f, 2.0f);
            }

            _client.PoseTransitionSpeedScale = presetPoseTransitionSpeedScale;
            GUILayout.Label("(preset transitions)", GUILayout.Width(130f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Post-restart wait", GUILayout.Width(100f));
            string postRestartText = GUILayout.TextField(
                postRestartWaitSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(postRestartText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedWait))
            {
                postRestartWaitSeconds = Mathf.Max(0f, parsedWait);
            }

            GUILayout.Label("Health every", GUILayout.Width(80f));
            string healthText = GUILayout.TextField(
                healthCheckIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(healthText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedHealth))
            {
                healthCheckIntervalSeconds = Mathf.Max(0.5f, parsedHealth);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Sim cam port", GUILayout.Width(100f));
            string simCamPortText = GUILayout.TextField(
                simulationCameraPort.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (int.TryParse(simCamPortText, out int parsedSimCamPort))
            {
                simulationCameraPort = Mathf.Clamp(parsedSimCamPort, 1, 65535);
            }

            GUILayout.Label("Robot cam port", GUILayout.Width(100f));
            string robotCamPortText = GUILayout.TextField(
                robotCameraPort.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (int.TryParse(robotCamPortText, out int parsedRobotCamPort))
            {
                robotCameraPort = Mathf.Clamp(parsedRobotCamPort, 1, 65535);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Frame every", GUILayout.Width(100f));
            string cameraRefreshText = GUILayout.TextField(
                cameraRefreshIntervalSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(
                cameraRefreshText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedCameraRefresh))
            {
                cameraRefreshIntervalSeconds = Mathf.Max(0.05f, parsedCameraRefresh);
            }

            GUILayout.Label("Cam timeout", GUILayout.Width(80f));
            string cameraTimeoutText = GUILayout.TextField(
                cameraRpcTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(
                cameraTimeoutText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsedCameraTimeout))
            {
                cameraRpcTimeoutSeconds = Mathf.Max(0.2f, parsedCameraTimeout);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Reconnect cool-down", GUILayout.Width(130f));
            string cooldownText = GUILayout.TextField(
                reconnectCooldownSeconds.ToString(CultureInfo.InvariantCulture),
                GUILayout.Width(70f));
            if (float.TryParse(cooldownText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedCooldown))
            {
                reconnectCooldownSeconds = Mathf.Max(1f, parsedCooldown);
            }
            GUILayout.EndHorizontal();

            bool previousEyeSelection = cameraUseRightEye;
            int eyeSelection = cameraUseRightEye ? 1 : 0;
            string[] eyeLabels = { "Left Eye", "Right Eye" };
            eyeSelection = GUILayout.Toolbar(eyeSelection, eyeLabels, GUILayout.Height(22f));
            cameraUseRightEye = eyeSelection == 1;
            if (cameraUseRightEye != previousEyeSelection)
            {
                _nextCameraFetchAt = 0f;
            }

            if (mode == ReachyControlMode.RealRobot)
            {
                GUILayout.Label("Fallback hosts (comma separated, optional host:port):");
                robotFallbackHostsCsv = GUILayout.TextField(robotFallbackHostsCsv);

                GUILayout.Label("Fallback ports (comma separated):");
                robotFallbackPortsCsv = GUILayout.TextField(robotFallbackPortsCsv);

                allowRestartSignalRecovery = GUILayout.Toggle(
                    allowRestartSignalRecovery,
                    "Use gRPC restart-signal recovery between retries (real robot)"
                );

                resolveRobotHostnames = GUILayout.Toggle(
                    resolveRobotHostnames,
                    "Resolve hostnames to IPv4 before attempting gRPC"
                );

                precheckRobotEndpointReachability = GUILayout.Toggle(
                    precheckRobotEndpointReachability,
                    "Pre-check TCP reachability before gRPC connect"
                );

                GUILayout.BeginHorizontal();
                GUILayout.Label("Restart port", GUILayout.Width(100f));
                string restartPortText = GUILayout.TextField(
                    robotRestartPort.ToString(CultureInfo.InvariantCulture),
                    GUILayout.Width(70f));
                if (int.TryParse(restartPortText, out int parsedRestartPort))
                {
                    robotRestartPort = Mathf.Clamp(parsedRestartPort, 1, 65535);
                }

                GUILayout.Label("TCP timeout", GUILayout.Width(80f));
                string probeTimeoutText = GUILayout.TextField(
                    precheckTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                    GUILayout.Width(70f));
                if (float.TryParse(
                    probeTimeoutText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float parsedProbeTimeout))
                {
                    precheckTimeoutSeconds = Mathf.Max(0.2f, parsedProbeTimeout);
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("Real Robot-only checks: hostname resolve, TCP precheck, restart port/fallback ports.");
            }
        }

        private void DrawJointSection()
        {
            GUILayout.Label("Single Joint Command", _titleStyle);

            if (_isConnectAttemptInProgress)
            {
                GUILayout.Label("Connection attempt in progress. Joint commands are temporarily disabled.");
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Joint", GUILayout.Width(60f));
            jointName = GUILayout.TextField(jointName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Goal (deg)", GUILayout.Width(60f));
            goalDegrees = GUILayout.TextField(goalDegrees, GUILayout.Width(120f));
            if (GUILayout.Button("Send Goal", GUILayout.Height(26f)))
            {
                if (!float.TryParse(goalDegrees, NumberStyles.Float, CultureInfo.InvariantCulture, out float goal))
                {
                    SetStatus("Input error", $"Invalid degree value '{goalDegrees}'.");
                }
                else
                {
                    bool targetsRealRobot = IsRealRobotSessionActive();
                    LogMotionEvent(
                        "manual",
                        "single-joint-attempt",
                        $"joint={jointName}; degrees={goal.ToString("F3", CultureInfo.InvariantCulture)}; mode={GetConnectedModeLabel()}",
                        success: true,
                        targetsRealRobot: targetsRealRobot);
                    bool wasConnected = _client.IsConnected;
                    bool ok = _client.SendSingleJointGoal(jointName, goal, out string message);
                    LogMotionEvent(
                        "manual",
                        "single-joint-result",
                        $"joint={jointName}; degrees={goal.ToString("F3", CultureInfo.InvariantCulture)}; result={(ok ? "ok" : "failed")}; detail={message}",
                        success: ok,
                        targetsRealRobot: targetsRealRobot);
                    SetStatus(ok ? "Command sent" : "Command failed", message);
                    HandlePotentialDisconnectAfterOperation("single joint command", wasConnected);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Known joints (first 20):");
            _jointScroll = GUILayout.BeginScrollView(_jointScroll, GUILayout.Height(100f));
            int max = Math.Min(_client.JointNames.Count, 20);
            for (int i = 0; i < max; i++)
            {
                GUILayout.Label($"- {_client.JointNames[i]}");
            }
            if (max == 0)
            {
                GUILayout.Label("(none loaded)");
            }
            GUILayout.EndScrollView();
        }

        private void DrawPresetSection()
        {
            GUILayout.Label("Preset Poses", _titleStyle);
            GUILayout.Label("One click sends full-arm pose targets. Use carefully on real hardware.");

            IReadOnlyList<string> presetNames = _client.PresetPoseNames;
            if (presetNames == null || presetNames.Count == 0)
            {
                GUILayout.Label("No presets available.");
                return;
            }

            const int buttonsPerRow = 2;
            int index = 0;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = !_isConnectAttemptInProgress;
            while (index < presetNames.Count)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < buttonsPerRow && index < presetNames.Count; col++, index++)
                {
                    string presetName = presetNames[index];
                    if (GUILayout.Button(presetName, GUILayout.Height(28f)))
                    {
                        bool targetsRealRobot = IsRealRobotSessionActive();
                        LogMotionEvent(
                            "manual",
                            "preset-attempt",
                            $"pose={presetName}; mode={GetConnectedModeLabel()}",
                            success: true,
                            targetsRealRobot: targetsRealRobot);
                        bool wasConnected = _client.IsConnected;
                        bool ok = _client.SendPresetPose(presetName, out string message);
                        LogMotionEvent(
                            "manual",
                            "preset-result",
                            $"pose={presetName}; result={(ok ? "ok" : "failed")}; detail={message}",
                            success: ok,
                            targetsRealRobot: targetsRealRobot);
                        SetStatus(ok ? $"Preset '{presetName}' sent" : $"Preset '{presetName}' failed", message);
                        HandlePotentialDisconnectAfterOperation($"preset '{presetName}'", wasConnected);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUI.enabled = previousEnabled;

            if (_isConnectAttemptInProgress)
            {
                GUILayout.Label("Connection attempt in progress. Preset commands are temporarily disabled.");
            }
        }

        private void InitializeRuntimeLogSession()
        {
            if (_runtimeLogSessionInitialized)
            {
                return;
            }

            try
            {
                string logsDirectory = EnsureRuntimeLogsDirectory();
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string runtimeTag = Application.isEditor ? "editor" : "player";
                _runtimeSessionLogPath = Path.Combine(
                    logsDirectory,
                    $"{RuntimeRunLogFilePrefix}{stamp}_{runtimeTag}.log");

                _runtimeLogWriter = new StreamWriter(
                    new FileStream(_runtimeSessionLogPath, FileMode.Create, FileAccess.Write, FileShare.Read),
                    Encoding.UTF8);
                _runtimeLogWriter.AutoFlush = true;
                _runtimeLogSessionInitialized = true;

                PruneRuntimeRunLogs();
                LogRuntimeEvent(
                    "lifecycle",
                    "run-start",
                    $"session={_runtimeSessionLogPath}; persistentDataPath={Application.persistentDataPath}; company={Application.companyName}; product={Application.productName}.",
                    "INFO",
                    forceWrite: true);
            }
            catch (Exception ex)
            {
                _runtimeLogSessionInitialized = false;
                _runtimeLogWriter = null;
                Debug.LogWarning($"[ReachyControlUI] Failed to initialize runtime logs: {ex.Message}");
            }
        }

        private string EnsureRuntimeLogsDirectory()
        {
            if (string.IsNullOrWhiteSpace(_runtimeLogsDirectory))
            {
                _runtimeLogsDirectory = Path.Combine(
                    Application.persistentDataPath,
                    "ReachyControlApp",
                    "Logs",
                    "RuntimeRuns");
            }

            Directory.CreateDirectory(_runtimeLogsDirectory);
            return _runtimeLogsDirectory;
        }

        private void PruneRuntimeRunLogs()
        {
            if (string.IsNullOrWhiteSpace(_runtimeLogsDirectory) || !Directory.Exists(_runtimeLogsDirectory))
            {
                return;
            }

            try
            {
                string[] files = Directory.GetFiles(
                    _runtimeLogsDirectory,
                    RuntimeRunLogFilePrefix + "*.log",
                    SearchOption.TopDirectoryOnly);
                if (files.Length <= RuntimeRunLogHistoryCount)
                {
                    return;
                }

                Array.Sort(
                    files,
                    (left, right) => string.CompareOrdinal(Path.GetFileName(right), Path.GetFileName(left)));
                for (int i = RuntimeRunLogHistoryCount; i < files.Length; i++)
                {
                    try
                    {
                        File.Delete(files[i]);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ReachyControlUI] Could not delete old run log '{files[i]}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ReachyControlUI] Could not prune run logs: {ex.Message}");
            }
        }

        private void ShutdownRuntimeLogSession(string reason)
        {
            lock (_runtimeLogGate)
            {
                if (_runtimeLogWriter == null)
                {
                    _runtimeLogSessionInitialized = false;
                    return;
                }

                try
                {
                    string finalReason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
                    _runtimeLogWriter.WriteLine(
                        $"{DateTime.UtcNow:O} [INFO] [lifecycle] run-stop :: reason={NormalizeLogText(finalReason)}");
                    _runtimeLogWriter.Flush();
                    _runtimeLogWriter.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ReachyControlUI] Failed to close runtime log session: {ex.Message}");
                }
                finally
                {
                    _runtimeLogWriter = null;
                    _runtimeLogSessionInitialized = false;
                }
            }
        }

        private void LogRuntimeEvent(
            string category,
            string title,
            string detail,
            string severity = "INFO",
            bool forceWrite = false)
        {
            if (!forceWrite && !runtimeFileLoggingEnabled)
            {
                return;
            }

            lock (_runtimeLogGate)
            {
                if (_runtimeLogWriter == null || !_runtimeLogSessionInitialized)
                {
                    return;
                }

                try
                {
                    string normalizedCategory = string.IsNullOrWhiteSpace(category)
                        ? "general"
                        : NormalizeLogText(category).ToLowerInvariant();
                    string normalizedTitle = string.IsNullOrWhiteSpace(title)
                        ? "event"
                        : NormalizeLogText(title);
                    string normalizedDetail = NormalizeLogText(detail);
                    string normalizedSeverity = NormalizeLogSeverity(severity);
                    string line = $"{DateTime.UtcNow:O} [{normalizedSeverity}] [{normalizedCategory}] {normalizedTitle}";
                    if (!string.IsNullOrWhiteSpace(normalizedDetail))
                    {
                        line += $" :: {normalizedDetail}";
                    }

                    _runtimeLogWriter.WriteLine(line);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ReachyControlUI] Runtime logging write failed: {ex.Message}");
                }
            }
        }

        private static string NormalizeLogSeverity(string severity)
        {
            string normalized = string.IsNullOrWhiteSpace(severity)
                ? "INFO"
                : severity.Trim().ToUpperInvariant();
            if (normalized == "DEBUG" || normalized == "INFO" || normalized == "WARN" || normalized == "ERROR")
            {
                return normalized;
            }

            return "INFO";
        }

        private static string NormalizeLogText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\r\n", " | ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private static string InferStatusSeverity(string header, string detail)
        {
            string combined = ((header ?? string.Empty) + " " + (detail ?? string.Empty)).ToLowerInvariant();
            if (combined.Contains("fail") ||
                combined.Contains("error") ||
                combined.Contains("exception") ||
                combined.Contains("timeout"))
            {
                return "ERROR";
            }

            if (combined.Contains("lost") ||
                combined.Contains("drop") ||
                combined.Contains("blocked") ||
                combined.Contains("cancel") ||
                combined.Contains("warning"))
            {
                return "WARN";
            }

            return "INFO";
        }

        private bool IsRealRobotSessionActive()
        {
            return _client != null && _client.IsConnected && IsConnectionForMode(ReachyControlMode.RealRobot);
        }

        private void LogConnectionEvent(
            ReachyControlMode targetMode,
            string title,
            string detail,
            string severity = "INFO",
            bool forceWrite = false)
        {
            string category = targetMode == ReachyControlMode.RealRobot ? "connect-real-robot" : "connect-sim";
            LogRuntimeEvent(category, title, detail, severity, forceWrite);
        }

        private void LogMotionEvent(
            string source,
            string action,
            string detail,
            bool success,
            bool targetsRealRobot)
        {
            string category = targetsRealRobot ? "motion-real-robot" : "motion";
            string title = $"{source}:{action}";
            string severity = success ? "INFO" : "ERROR";
            LogRuntimeEvent(category, title, detail, severity);
        }

        private void OpenRuntimeLogsFolderInExplorer()
        {
            try
            {
                string logsDirectory = EnsureRuntimeLogsDirectory();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logsDirectory,
                    Verb = "open",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
#else
                Application.OpenURL("file://" + logsDirectory.Replace('\\', '/'));
#endif
                LogRuntimeEvent(
                    "logging",
                    "open-folder",
                    $"Opened runtime log folder at {logsDirectory}.",
                    "INFO",
                    forceWrite: true);
            }
            catch (Exception ex)
            {
                LogRuntimeEvent(
                    "logging",
                    "open-folder-failed",
                    ex.Message,
                    "ERROR",
                    forceWrite: true);
                SetStatus("Log folder error", $"Could not open runtime log folder: {ex.Message}");
            }
        }

        private void DrawStatusSection()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Status", _titleStyle);
            GUILayout.FlexibleSpace();
            bool previousLoggingEnabled = runtimeFileLoggingEnabled;
            runtimeFileLoggingEnabled = GUILayout.Toggle(runtimeFileLoggingEnabled, "File Log", GUILayout.Width(78f));
            if (runtimeFileLoggingEnabled != previousLoggingEnabled)
            {
                if (runtimeFileLoggingEnabled)
                {
                    InitializeRuntimeLogSession();
                    LogRuntimeEvent(
                        "logging",
                        "toggle",
                        "Runtime file logging enabled by operator.",
                        "INFO",
                        forceWrite: true);
                }
                else
                {
                    LogRuntimeEvent(
                        "logging",
                        "toggle",
                        "Runtime file logging disabled by operator.",
                        "WARN",
                        forceWrite: true);
                }
            }

            if (GUILayout.Button("Open Logs", GUILayout.Width(82f), GUILayout.Height(22f)))
            {
                OpenRuntimeLogsFolderInExplorer();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"UI scale: {_uiScale:F2}x");
            if (!string.IsNullOrWhiteSpace(_runtimeSessionLogPath))
            {
                GUILayout.Label($"Run log: {_runtimeSessionLogPath}");
            }
            GUILayout.TextArea(_status, GUILayout.Height(120f));
        }

        private void SetStatus(string header, string detail)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {header}");
            if (!string.IsNullOrWhiteSpace(detail))
            {
                sb.AppendLine(detail);
            }
            _status = sb.ToString();
            Debug.Log($"[ReachyControlUI] {header} - {detail}");
            LogRuntimeEvent("status", header, detail, InferStatusSeverity(header, detail));
        }

        private void ApplyWindowedResolutionFromFields()
        {
            if (!int.TryParse(_windowedWidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedWidth) ||
                !int.TryParse(_windowedHeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHeight))
            {
                SetStatus("Window mode error", "Width/height must be valid integers.");
                return;
            }

            if (parsedWidth < 320 || parsedHeight < 240)
            {
                SetStatus("Window mode error", "Resolution too small. Minimum is 320x240.");
                return;
            }

            windowedWidth = Mathf.Clamp(parsedWidth, 320, 7680);
            windowedHeight = Mathf.Clamp(parsedHeight, 240, 4320);
            _windowedWidthText = windowedWidth.ToString(CultureInfo.InvariantCulture);
            _windowedHeightText = windowedHeight.ToString(CultureInfo.InvariantCulture);

            Screen.SetResolution(windowedWidth, windowedHeight, FullScreenMode.Windowed);
            SetStatus("Window mode", $"Set windowed resolution to {windowedWidth}x{windowedHeight}.");
        }

        private void SetFullscreenToDesktopResolution()
        {
            int width = Display.main != null ? Display.main.systemWidth : 0;
            int height = Display.main != null ? Display.main.systemHeight : 0;

            if (width <= 0 || height <= 0)
            {
                Resolution desktop = Screen.currentResolution;
                width = desktop.width;
                height = desktop.height;
            }

            if (width <= 0 || height <= 0)
            {
                SetStatus("Fullscreen error", "Could not detect desktop resolution.");
                return;
            }

            Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
            SetStatus("Window mode", $"Set fullscreen to desktop resolution {width}x{height}.");
        }

        private bool TryConnectWithAutomation(ReachyControlMode targetMode, string statusHeader)
        {
            if (_client == null)
            {
                SetStatus("Connection error", "Client is not initialized.");
                return false;
            }

            List<Endpoint> endpoints = BuildConnectionCandidates(targetMode);
            LogConnectionEvent(
                targetMode,
                "connect-with-automation",
                $"{statusHeader}: evaluating {endpoints.Count} endpoint candidate(s).");
            bool allowRestart = targetMode == ReachyControlMode.RealRobot && allowRestartSignalRecovery;
            int attempts = Math.Max(1, connectAttemptsPerHost);
            double retryDelay = Math.Max(0f, retryDelaySeconds);
            double connectTimeout = Math.Max(0.2f, grpcConnectTimeoutSeconds);
            double postRestartWait = Math.Max(0f, postRestartWaitSeconds);
            int restartPort = targetMode == ReachyControlMode.RealRobot ? robotRestartPort : 0;

            var aggregate = new StringBuilder();
            foreach (Endpoint endpoint in endpoints)
            {
                if (!TryPrepareEndpointForConnect(targetMode, endpoint, out Endpoint preparedEndpoint, out string prepMessage))
                {
                    aggregate.AppendLine($"{endpoint.Host}:{endpoint.Port}");
                    aggregate.AppendLine($"Skipped: {prepMessage}");
                    aggregate.AppendLine();
                    LogConnectionEvent(
                        targetMode,
                        "endpoint-skipped",
                        $"{endpoint.Host}:{endpoint.Port} skipped before gRPC connect: {prepMessage}",
                        "WARN");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(prepMessage))
                {
                    aggregate.AppendLine(prepMessage);
                    LogConnectionEvent(
                        targetMode,
                        "endpoint-prepared",
                        prepMessage);
                }

                LogConnectionEvent(
                    targetMode,
                    "attempt-endpoint",
                    $"Trying {preparedEndpoint.Host}:{preparedEndpoint.Port} (attempts={attempts}, timeout={connectTimeout:F1}s, retryDelay={retryDelay:F1}s, restartRecovery={allowRestart}).");
                bool ok = _client.ConnectWithRecovery(
                    preparedEndpoint.Host,
                    preparedEndpoint.Port,
                    attempts,
                    connectTimeout,
                    allowRestart,
                    retryDelay,
                    postRestartWait,
                    restartPort,
                    out string connectMessage);

                aggregate.AppendLine($"{preparedEndpoint.Host}:{preparedEndpoint.Port}");
                aggregate.AppendLine(connectMessage);

                if (ok)
                {
                    if (targetMode == ReachyControlMode.Simulation)
                    {
                        simulationHost = preparedEndpoint.Host;
                        simulationPort = preparedEndpoint.Port;
                    }
                    else
                    {
                        robotHost = preparedEndpoint.Host;
                        robotPort = preparedEndpoint.Port;
                    }

                    _manualDisconnect = false;
                    _autoReconnectScheduled = false;
                    _connectedMode = targetMode;
                    _nextHealthCheckAt = Time.unscaledTime + Mathf.Max(0.5f, healthCheckIntervalSeconds);
                    LogConnectionEvent(
                        targetMode,
                        "connect-success",
                        $"{preparedEndpoint.Host}:{preparedEndpoint.Port} connected successfully. {connectMessage}");
                    SetStatus(statusHeader, aggregate.ToString().Trim());
                    return true;
                }

                LogConnectionEvent(
                    targetMode,
                    "connect-failure",
                    $"{preparedEndpoint.Host}:{preparedEndpoint.Port} failed to connect. {connectMessage}",
                    "ERROR");
                aggregate.AppendLine();
            }

            _connectedMode = null;
            string failureDetail = aggregate.ToString().Trim();
            if (string.IsNullOrEmpty(failureDetail))
            {
                failureDetail = "No valid connection endpoints configured.";
            }

            LogConnectionEvent(
                targetMode,
                "connect-failed-all-endpoints",
                $"{statusHeader}: {failureDetail}",
                "ERROR");
            SetStatus($"{statusHeader} failed", failureDetail);
            return false;
        }

        private bool TryStartConnectAttempt(
            string statusHeader,
            ReachyControlMode targetMode,
            float reconnectDelayOnFailure,
            bool ensureOneUiFrameBeforeConnect)
        {
            if (_client == null)
            {
                SetStatus("Connection error", "Client is not initialized.");
                return false;
            }

            if (_isConnectAttemptInProgress)
            {
                LogConnectionEvent(
                    targetMode,
                    "connect-attempt-ignored",
                    $"{statusHeader}: connect attempt already in progress.",
                    "WARN");
                return false;
            }

            _manualDisconnect = false;
            _autoReconnectScheduled = false;
            _isConnectAttemptInProgress = true;
            _connectAttemptMode = targetMode;
            _connectAttemptReconnectDelaySeconds = Mathf.Max(0f, reconnectDelayOnFailure);
            LogConnectionEvent(
                targetMode,
                "connect-attempt-started",
                $"{statusHeader}: ensureOneUiFrameBeforeConnect={ensureOneUiFrameBeforeConnect}, reconnectDelayOnFailure={_connectAttemptReconnectDelaySeconds:F2}s.");
            SetStatus(statusHeader, $"Attempting connection to {GetModeLabel(targetMode)}...");
            _connectAttemptCoroutine = StartCoroutine(
                ConnectAttemptCoroutine(statusHeader, targetMode, ensureOneUiFrameBeforeConnect));
            return true;
        }

        private IEnumerator ConnectAttemptCoroutine(
            string statusHeader,
            ReachyControlMode targetMode,
            bool ensureOneUiFrameBeforeConnect)
        {
            if (ensureOneUiFrameBeforeConnect)
            {
                yield return null;
                yield return new WaitForEndOfFrame();
            }

            bool connected = false;
            try
            {
                connected = TryConnectWithAutomation(targetMode, statusHeader);
            }
            finally
            {
                _isConnectAttemptInProgress = false;
                _connectAttemptCoroutine = null;
            }

            if (!connected && autoReconnect && !_manualDisconnect)
            {
                ScheduleAutoReconnect(_connectAttemptReconnectDelaySeconds, targetMode);
            }
        }

        private void ScheduleAutoReconnect(float delaySeconds, ReachyControlMode reconnectMode)
        {
            _autoReconnectScheduled = true;
            _autoReconnectMode = reconnectMode;
            _nextAutoReconnectAt = Time.unscaledTime + Mathf.Max(0f, delaySeconds);
            LogConnectionEvent(
                reconnectMode,
                "auto-reconnect-scheduled",
                $"Next reconnect in {Mathf.Max(0f, delaySeconds):F2}s.");
        }

        private void HandlePotentialDisconnectAfterOperation(string context, bool wasConnectedBeforeOperation)
        {
            if (!wasConnectedBeforeOperation || _client.IsConnected || !autoReconnect || _manualDisconnect)
            {
                return;
            }

            ReachyControlMode reconnectMode = _connectedMode ?? mode;
            _connectedMode = null;
            ScheduleAutoReconnect(0.5f, reconnectMode);
            LogConnectionEvent(
                reconnectMode,
                "connection-dropped-after-operation",
                $"Detected after {context}.",
                "WARN");
            SetStatus("Connection dropped", $"Detected after {context}. Auto-reconnect scheduled.");
        }

        private bool IsConnectionForMode(ReachyControlMode queryMode)
        {
            if (_isConnectAttemptInProgress)
            {
                return false;
            }

            if (_client == null || !_client.IsConnected)
            {
                return false;
            }

            if (_connectedMode.HasValue)
            {
                return _connectedMode.Value == queryMode;
            }

            string expectedHost = queryMode == ReachyControlMode.Simulation ? simulationHost : robotHost;
            int expectedPort = queryMode == ReachyControlMode.Simulation ? simulationPort : robotPort;
            return string.Equals(_client.ConnectedHost, expectedHost, StringComparison.OrdinalIgnoreCase)
                && _client.ConnectedPort == expectedPort;
        }

        private static string GetModeLabel(ReachyControlMode value)
        {
            return value == ReachyControlMode.Simulation ? "Simulation" : "Real Robot";
        }

        private string GetConnectedModeLabel()
        {
            return _connectedMode.HasValue ? GetModeLabel(_connectedMode.Value) : "Unknown";
        }

        private List<Endpoint> BuildConnectionCandidates(ReachyControlMode targetMode)
        {
            var endpoints = new List<Endpoint>();
            string primaryHost = targetMode == ReachyControlMode.Simulation ? simulationHost : robotHost;
            int primaryPort = targetMode == ReachyControlMode.Simulation ? simulationPort : robotPort;
            AddEndpointIfNew(endpoints, primaryHost, primaryPort);

            if (targetMode != ReachyControlMode.RealRobot)
            {
                return endpoints;
            }

            if (!string.IsNullOrWhiteSpace(robotFallbackHostsCsv))
            {
                string[] tokens = robotFallbackHostsCsv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in tokens)
                {
                    string trimmed = token.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        continue;
                    }

                    string host = trimmed;
                    int port = primaryPort;
                    int colonIdx = trimmed.LastIndexOf(':');
                    if (colonIdx > 0 && colonIdx < trimmed.Length - 1)
                    {
                        string possiblePort = trimmed.Substring(colonIdx + 1);
                        if (int.TryParse(possiblePort, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort))
                        {
                            host = trimmed.Substring(0, colonIdx).Trim();
                            port = parsedPort;
                        }
                    }

                    AddEndpointIfNew(endpoints, host, port);
                }
            }

            List<int> fallbackPorts = ParsePortsCsv(robotFallbackPortsCsv);
            if (fallbackPorts.Count > 0)
            {
                var distinctHosts = new List<string>();
                for (int i = 0; i < endpoints.Count; i++)
                {
                    AddHostIfNew(distinctHosts, endpoints[i].Host);
                }

                for (int i = 0; i < distinctHosts.Count; i++)
                {
                    string host = distinctHosts[i];
                    for (int p = 0; p < fallbackPorts.Count; p++)
                    {
                        AddEndpointIfNew(endpoints, host, fallbackPorts[p]);
                    }
                }
            }

            return endpoints;
        }

        private static List<int> ParsePortsCsv(string csv)
        {
            var ports = new List<int>();
            if (string.IsNullOrWhiteSpace(csv))
            {
                return ports;
            }

            string[] tokens = csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                string trimmed = token.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
                {
                    continue;
                }

                if (port <= 0 || port > 65535 || ports.Contains(port))
                {
                    continue;
                }

                ports.Add(port);
            }

            return ports;
        }

        private static void AddHostIfNew(List<string> hosts, string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            for (int i = 0; i < hosts.Count; i++)
            {
                if (string.Equals(hosts[i], host, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            hosts.Add(host);
        }

        private bool TryPrepareEndpointForConnect(
            ReachyControlMode targetMode,
            Endpoint endpoint,
            out Endpoint preparedEndpoint,
            out string prepMessage)
        {
            preparedEndpoint = endpoint;
            prepMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                prepMessage = "Host is empty.";
                return false;
            }

            if (endpoint.Port <= 0 || endpoint.Port > 65535)
            {
                prepMessage = $"Invalid port '{endpoint.Port}'.";
                return false;
            }

            if (targetMode != ReachyControlMode.RealRobot)
            {
                return true;
            }

            string host = endpoint.Host.Trim();
            if (resolveRobotHostnames && ShouldResolveHost(host))
            {
                if (!TryResolveHostToIPv4(host, out string resolvedHost, out string resolutionMessage))
                {
                    prepMessage = resolutionMessage;
                    return false;
                }

                if (!string.Equals(resolvedHost, host, StringComparison.OrdinalIgnoreCase))
                {
                    prepMessage = $"{host}:{endpoint.Port} resolved to {resolvedHost}:{endpoint.Port}.";
                }

                host = resolvedHost;
            }

            if (precheckRobotEndpointReachability)
            {
                int timeoutMs = Mathf.Max(100, Mathf.RoundToInt(precheckTimeoutSeconds * 1000f));
                if (!TryCheckTcpReachability(host, endpoint.Port, timeoutMs, out string reachabilityMessage))
                {
                    prepMessage = reachabilityMessage;
                    return false;
                }
            }

            preparedEndpoint = new Endpoint { Host = host, Port = endpoint.Port };
            return true;
        }

        private static bool ShouldResolveHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return !IPAddress.TryParse(host, out _);
        }

        private static bool TryResolveHostToIPv4(string host, out string resolvedHost, out string message)
        {
            resolvedHost = host;
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(host))
            {
                message = "Host is empty.";
                return false;
            }

            if (IPAddress.TryParse(host, out IPAddress parsedIp))
            {
                if (parsedIp.AddressFamily == AddressFamily.InterNetwork)
                {
                    resolvedHost = parsedIp.ToString();
                    return true;
                }

                message = $"Host '{host}' is not an IPv4 address.";
                return false;
            }

            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                for (int i = 0; i < addresses.Length; i++)
                {
                    IPAddress address = addresses[i];
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        resolvedHost = address.ToString();
                        return true;
                    }
                }

                message = $"No IPv4 address found for '{host}'.";
                return false;
            }
            catch (Exception ex)
            {
                message = $"DNS lookup failed for '{host}': {ex.Message}";
                return false;
            }
        }

        private static bool TryCheckTcpReachability(string host, int port, int timeoutMs, out string message)
        {
            message = string.Empty;

            try
            {
                using (var tcpClient = new TcpClient())
                {
                    IAsyncResult connectResult = tcpClient.BeginConnect(host, port, null, null);
                    bool completed = connectResult.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (!completed)
                    {
                        message = $"TCP connect timeout after {timeoutMs} ms.";
                        return false;
                    }

                    tcpClient.EndConnect(connectResult);
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static void AddEndpointIfNew(List<Endpoint> endpoints, string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            if (port <= 0 || port > 65535)
            {
                return;
            }

            for (int i = 0; i < endpoints.Count; i++)
            {
                Endpoint existing = endpoints[i];
                if (string.Equals(existing.Host, host, StringComparison.OrdinalIgnoreCase) && existing.Port == port)
                {
                    return;
                }
            }

            endpoints.Add(new Endpoint { Host = host, Port = port });
        }

        private struct Endpoint
        {
            public string Host;
            public int Port;
        }
    }
}
