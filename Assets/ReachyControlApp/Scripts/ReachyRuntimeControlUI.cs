using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Reachy.ControlApp
{
    public class ReachyRuntimeControlUI : MonoBehaviour
    {
        private const float DesignMarginPixels = 10f;
        private const float DesignPanelGap = 10f;
        private const float DesignLeftPanelWidth = 500f;
        private const float DesignRightPanelWidth = 430f;
        private const float DesignExpandedPanelHeight = 640f;
        private const float DesignCollapsedPanelHeight = 95f;
        private const float ExpandedLeftPanelWidthRatio = 0.36f;
        private const float ExpandedRightPanelWidthRatio = 0.36f;
        private const float DesignCameraPanelWidth = 560f;
        private const float DesignCameraPanelHeight = 265f;

        [Header("Endpoints")]
        [SerializeField] private string simulationHost = "localhost";
        [SerializeField] private int simulationPort = 50055;
        [SerializeField] private string robotHost = "192.168.1.118";
        [SerializeField] private int robotPort = 3972;

        [Header("Runtime")]
        [SerializeField] private ReachyControlMode mode = ReachyControlMode.Simulation;
        [SerializeField] private bool startCollapsed;

        [Header("Automation")]
        [SerializeField] private bool autoConnectOnPlay = true;
        [SerializeField] private bool autoReconnect = true;
        [SerializeField] private bool allowRestartSignalRecovery = true;
        [SerializeField] private string robotFallbackHostsCsv = string.Empty;
        [SerializeField] private string robotFallbackPortsCsv = "50055";
        [SerializeField] private int connectAttemptsPerHost = 3;
        [SerializeField] private float retryDelaySeconds = 1.0f;
        [SerializeField] private float grpcConnectTimeoutSeconds = 3.0f;
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

        [Header("Single Joint Command")]
        [SerializeField] private string jointName = "r_shoulder_pitch";
        [SerializeField] private string goalDegrees = "0";
        
        [Header("Window Controls")]
        [SerializeField] private int windowedWidth = 1280;
        [SerializeField] private int windowedHeight = 720;

        private ReachyGrpcClient _client;
        private string _status = "Idle.";
        private Vector2 _leftPanelScroll;
        private Vector2 _jointScroll;
        private bool _collapsed;
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

        private void Awake()
        {
            _client = new ReachyGrpcClient();
            _collapsed = startCollapsed;
            _autoReconnectMode = mode;
            windowedWidth = Mathf.Clamp(windowedWidth, 320, 7680);
            windowedHeight = Mathf.Clamp(windowedHeight, 240, 4320);
            simulationCameraPort = Mathf.Clamp(simulationCameraPort, 1, 65535);
            robotCameraPort = Mathf.Clamp(robotCameraPort, 1, 65535);
            cameraRefreshIntervalSeconds = Mathf.Max(0.05f, cameraRefreshIntervalSeconds);
            cameraRpcTimeoutSeconds = Mathf.Max(0.2f, cameraRpcTimeoutSeconds);
            _windowedWidthText = windowedWidth.ToString(CultureInfo.InvariantCulture);
            _windowedHeightText = windowedHeight.ToString(CultureInfo.InvariantCulture);
        }

        private void OnDestroy()
        {
            if (_connectAttemptCoroutine != null)
            {
                StopCoroutine(_connectAttemptCoroutine);
                _connectAttemptCoroutine = null;
            }

            _cameraFetchTask = null;
            if (_cameraPreviewTexture != null)
            {
                Destroy(_cameraPreviewTexture);
                _cameraPreviewTexture = null;
            }

            _client?.Dispose();
            _client = null;
        }

        private void Update()
        {
            UpdateCameraPreview();

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

        private void OnGUI()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            }

            float topPanelsWidth = _collapsed
                ? DesignLeftPanelWidth
                : DesignLeftPanelWidth + DesignPanelGap + DesignRightPanelWidth;
            float designTotalWidth = topPanelsWidth;
            float designTotalHeight = _collapsed ? DesignCollapsedPanelHeight : DesignExpandedPanelHeight;

            float availableWidth = Mathf.Max(160f, Screen.width - (2f * DesignMarginPixels));
            float availableHeight = Mathf.Max(160f, Screen.height - (2f * DesignMarginPixels));
            _uiScale = Mathf.Min(1f, availableWidth / designTotalWidth, availableHeight / designTotalHeight);
            _uiScale = Mathf.Max(0.1f, _uiScale);

            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_uiScale, _uiScale, 1f));

            float logicalMargin = DesignMarginPixels / _uiScale;
            float logicalScreenWidth = Screen.width / _uiScale;
            float logicalScreenHeight = Screen.height / _uiScale;
            float leftPanelX = logicalMargin;
            float topY = logicalMargin;

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
                        Mathf.Max(140f, logicalScreenHeight - (2f * logicalMargin)));
                    float cameraPanelX = (logicalScreenWidth - cameraPanelWidth) * 0.5f;
                    float cameraPanelY = logicalScreenHeight - logicalMargin - cameraPanelHeight;
                    DrawCameraPreviewPanel(new Rect(cameraPanelX, cameraPanelY, cameraPanelWidth, cameraPanelHeight));
                }
            }
            else
            {
                float usableWidth = Mathf.Max(320f, logicalScreenWidth - (2f * logicalMargin));
                float topPanelHeight = Mathf.Max(220f, logicalScreenHeight - (2f * logicalMargin));

                float leftPanelWidth = usableWidth * ExpandedLeftPanelWidthRatio;
                float rightPanelWidth = usableWidth * ExpandedRightPanelWidthRatio;
                float centerPanelWidth = Mathf.Max(180f, usableWidth - leftPanelWidth - rightPanelWidth);

                float centerPanelX = leftPanelX + leftPanelWidth;
                float rightPanelX = centerPanelX + centerPanelWidth;

                DrawLeftPanel(new Rect(leftPanelX, topY, leftPanelWidth, topPanelHeight));
                DrawRightPanel(new Rect(rightPanelX, topY, rightPanelWidth, topPanelHeight));

                if (showCameraPreview)
                {
                    float maxCameraHeight = Mathf.Max(140f, topPanelHeight - 16f);
                    float cameraPanelHeight = Mathf.Min(DesignCameraPanelHeight, maxCameraHeight);
                    float cameraPanelY = logicalScreenHeight - logicalMargin - cameraPanelHeight;
                    DrawCameraPreviewPanel(new Rect(centerPanelX, cameraPanelY, centerPanelWidth, cameraPanelHeight));
                }
            }

            GUI.matrix = previousMatrix;
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

        private void DrawRightPanelHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Commands & Poses", _titleStyle);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Windowed", GUILayout.Width(76f), GUILayout.Height(24f)))
            {
                ApplyWindowedResolutionFromFields();
            }

            if (GUILayout.Button("Fullscreen", GUILayout.Width(80f), GUILayout.Height(24f)))
            {
                SetFullscreenToDesktopResolution();
            }

            if (GUILayout.Button("Exit", GUILayout.Width(52f), GUILayout.Height(24f)))
            {
                SetStatus("Exit requested", "Closing application.");
                Application.Quit();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Windowed", GUILayout.Width(62f));
            _windowedWidthText = GUILayout.TextField(_windowedWidthText, GUILayout.Width(58f));
            GUILayout.Label("x", GUILayout.Width(10f));
            _windowedHeightText = GUILayout.TextField(_windowedHeightText, GUILayout.Width(58f));
            if (GUILayout.Button("Apply", GUILayout.Width(52f), GUILayout.Height(22f)))
            {
                ApplyWindowedResolutionFromFields();
            }
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
                    _manualDisconnect = true;
                    _autoReconnectScheduled = false;
                    _client.Disconnect();
                    _connectedMode = null;
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
                    bool wasConnected = _client.IsConnected;
                    bool ok = _client.SendSingleJointGoal(jointName, goal, out string message);
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
                        bool wasConnected = _client.IsConnected;
                        bool ok = _client.SendPresetPose(presetName, out string message);
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

        private void DrawStatusSection()
        {
            GUILayout.Label("Status", _titleStyle);
            GUILayout.Label($"UI scale: {_uiScale:F2}x");
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
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(prepMessage))
                {
                    aggregate.AppendLine(prepMessage);
                }

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
                    SetStatus(statusHeader, aggregate.ToString().Trim());
                    return true;
                }

                aggregate.AppendLine();
            }

            _connectedMode = null;
            string failureDetail = aggregate.ToString().Trim();
            if (string.IsNullOrEmpty(failureDetail))
            {
                failureDetail = "No valid connection endpoints configured.";
            }

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
                return false;
            }

            _manualDisconnect = false;
            _autoReconnectScheduled = false;
            _isConnectAttemptInProgress = true;
            _connectAttemptMode = targetMode;
            _connectAttemptReconnectDelaySeconds = Mathf.Max(0f, reconnectDelayOnFailure);
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
