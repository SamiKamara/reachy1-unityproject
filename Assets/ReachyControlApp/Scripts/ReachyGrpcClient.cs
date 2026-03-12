using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

using Reachy.Sdk.Camera;
using Reachy.Sdk.Joint;
using Reachy.Sdk.Mobility;
using Reachy.Sdk.Restart;

namespace Reachy.ControlApp
{
    public sealed class ReachyGrpcClient : IDisposable
    {
        private const double DefaultRpcTimeoutSeconds = 3.0;
        private const double HealthCheckRpcTimeoutSeconds = 1.5;
        private const double MotionPreparationRpcTimeoutSeconds = 1.5;
        private const double MobilityRpcTimeoutSeconds = 1.5;
        private const double RestartRpcTimeoutSeconds = 2.0;
        private const int MotionPreparationSettleDelayMs = 100;
        private const int DedicatedMobilityPortPrimary = 50061;
        private const int DedicatedMobilityPortLegacy = 50051;
        private const float DefaultPoseTransitionSpeedScale = 0.6f;
        private const float MinPoseTransitionSpeedScale = 0.05f;
        private const float MaxPoseTransitionSpeedScale = 2.0f;
        private const float DefaultManualJointSpeedLimitPercent = 50.0f;
        private const string NeutralArmsPoseName = "Neutral Arms";
        private const string TPoseName = "T-Pose";
        private const string TrayHoldingPoseName = "Tray Holding";
        private const string HelloPoseName = "Hello Pose A";
        private const string HelloPoseWaveName = "Hello Pose B";
        private const string HelloPoseRightName = "Hello Pose C";
        private const string HelloPoseRightWaveName = "Hello Pose D";
        private const float Deg2Rad = (float)(Math.PI / 180.0);
        private const float Rad2Deg = (float)(180.0 / Math.PI);

        private Channel _jointChannel;
        private JointService.JointServiceClient _jointClient;
        private Channel _mobilityChannel;
        private MobilityService.MobilityServiceClient _mobilityClient;
        private MobileBasePresenceService.MobileBasePresenceServiceClient _mobileBasePresenceClient;
        private Channel _cameraChannel;
        private CameraService.CameraServiceClient _cameraClient;
        private readonly object _cameraLock = new object();
        private string _cameraHost = string.Empty;
        private int _cameraPort;
        private int _mobilityPort;
        private bool? _cachedMobileBasePresence;
        private bool _mobilityConfiguredForSpeedMode;
        private bool _mobilityServiceUnavailable;
        private float _poseTransitionSpeedScale = DefaultPoseTransitionSpeedScale;
        private readonly List<string> _jointNames = new List<string>();
        private readonly List<string> _presetPoseNames = new List<string>();
        private readonly List<PosePreset> _presetPoses = BuildPresetPoseLibrary();

        public bool IsConnected { get; private set; }
        public string ConnectedHost { get; private set; } = string.Empty;
        public int ConnectedPort { get; private set; }

        public IReadOnlyList<string> JointNames => _jointNames;
        public IReadOnlyList<string> PresetPoseNames => _presetPoseNames;
        public float PoseTransitionSpeedScale
        {
            get => _poseTransitionSpeedScale;
            set => _poseTransitionSpeedScale = ClampPoseTransitionSpeedScale(value);
        }

        public struct CameraImageFetchResult
        {
            public bool Success;
            public byte[] ImageBytes;
            public string Message;
        }

        public ReachyGrpcClient()
        {
            for (int i = 0; i < _presetPoses.Count; i++)
            {
                _presetPoseNames.Add(_presetPoses[i].Name);
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        public bool Connect(string host, int port, out string message)
        {
            message = string.Empty;

            return ConnectInternal(host, port, DefaultRpcTimeoutSeconds, out message);
        }

        public bool ConnectWithRecovery(
            string host,
            int port,
            int maxAttempts,
            double connectTimeoutSeconds,
            bool sendRestartSignalBetweenAttempts,
            double retryDelaySeconds,
            double postRestartWaitSeconds,
            int restartPort,
            out string message)
        {
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(host))
            {
                message = "Host is empty.";
                return false;
            }

            if (port <= 0 || port > 65535)
            {
                message = $"Invalid port '{port}'.";
                return false;
            }

            if (maxAttempts < 1)
            {
                maxAttempts = 1;
            }

            if (connectTimeoutSeconds <= 0)
            {
                connectTimeoutSeconds = DefaultRpcTimeoutSeconds;
            }

            var details = new StringBuilder();
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (ConnectInternal(host, port, connectTimeoutSeconds, out string connectMessage))
                {
                    if (attempt == 1)
                    {
                        message = connectMessage;
                    }
                    else
                    {
                        message =
                            $"Connected after {attempt} attempt(s). " +
                            $"{host}:{port}. Joints discovered: {_jointNames.Count}.";
                    }

                    return true;
                }

                details.AppendLine($"Attempt {attempt}/{maxAttempts}: {connectMessage}");

                bool shouldTryRestart = sendRestartSignalBetweenAttempts && attempt < maxAttempts;
                if (shouldTryRestart)
                {
                    int restartTargetPort = restartPort > 0 ? restartPort : port;
                    bool restartOk = TrySendRestartSignal(host, restartTargetPort, out string restartMessage);
                    if (!restartOk && restartTargetPort != port)
                    {
                        bool fallbackOk = TrySendRestartSignal(host, port, out string fallbackMessage);
                        restartOk = fallbackOk;
                        restartMessage =
                            $"{restartMessage} Fallback on {host}:{port} => " +
                            $"{(fallbackOk ? "OK" : "failed")} ({fallbackMessage})";
                    }

                    details.AppendLine($"Restart signal: {(restartOk ? "OK" : "failed")} ({restartMessage})");

                    if (postRestartWaitSeconds > 0)
                    {
                        int waitMs = (int)(postRestartWaitSeconds * 1000.0);
                        if (waitMs > 0)
                        {
                            Thread.Sleep(waitMs);
                        }
                    }
                }

                if (attempt < maxAttempts && retryDelaySeconds > 0)
                {
                    int delayMs = (int)(retryDelaySeconds * 1000.0);
                    if (delayMs > 0)
                    {
                        Thread.Sleep(delayMs);
                    }
                }
            }

            message = $"Connection failed after {maxAttempts} attempt(s).\n{details}";
            return false;
        }

        public void Disconnect()
        {
            IsConnected = false;
            ConnectedHost = string.Empty;
            ConnectedPort = 0;
            _jointNames.Clear();
            ResetMobilityClients();

            if (_jointChannel != null)
            {
                ShutdownChannelQuietly(_jointChannel);
            }

            _jointClient = null;
            _jointChannel = null;
            DisconnectCamera();
        }

        public CameraImageFetchResult FetchCameraImage(
            string host,
            int port,
            CameraId cameraId,
            double timeoutSeconds)
        {
            var result = new CameraImageFetchResult
            {
                Success = false,
                ImageBytes = null,
                Message = string.Empty
            };

            if (string.IsNullOrWhiteSpace(host))
            {
                result.Message = "Camera host is empty.";
                return result;
            }

            if (port <= 0 || port > 65535)
            {
                result.Message = $"Invalid camera port '{port}'.";
                return result;
            }

            double timeout = timeoutSeconds > 0 ? timeoutSeconds : HealthCheckRpcTimeoutSeconds;
            if (timeout <= 0)
            {
                timeout = 1.0;
            }

            try
            {
                lock (_cameraLock)
                {
                    if (!EnsureCameraClientLocked(host, port, out string ensureMessage))
                    {
                        result.Message = ensureMessage;
                        return result;
                    }

                    var request = new ImageRequest
                    {
                        Camera = new Reachy.Sdk.Camera.Camera { Id = cameraId }
                    };

                    Image image = _cameraClient.GetImage(
                        request,
                        deadline: DateTime.UtcNow.AddSeconds(timeout));

                    if (image == null || image.Data == null || image.Data.Length == 0)
                    {
                        result.Message = "Camera service returned an empty frame.";
                        return result;
                    }

                    result.Success = true;
                    result.ImageBytes = image.Data.ToByteArray();
                    result.Message = $"Fetched {(cameraId == CameraId.Left ? "left" : "right")} camera frame.";
                    return result;
                }
            }
            catch (RpcException rpcEx)
            {
                lock (_cameraLock)
                {
                    DisconnectCameraLocked();
                }

                result.Message = $"Camera RPC {rpcEx.Status.StatusCode}: {rpcEx.Status.Detail}";
                return result;
            }
            catch (Exception ex)
            {
                lock (_cameraLock)
                {
                    DisconnectCameraLocked();
                }

                result.Message = $"Camera fetch failed: {ex.Message}";
                return result;
            }
        }

        public bool RefreshJoints(out string message)
        {
            message = string.Empty;

            if (!IsConnected || _jointClient == null)
            {
                message = "Not connected.";
                return false;
            }

            try
            {
                JointsId joints = _jointClient.GetAllJointsId(
                    new Empty(),
                    deadline: DateTime.UtcNow.AddSeconds(DefaultRpcTimeoutSeconds)
                );

                _jointNames.Clear();
                if (joints != null && joints.Names != null)
                {
                    _jointNames.AddRange(joints.Names);
                }

                message = $"Joint list refreshed ({_jointNames.Count} joints).";
                return true;
            }
            catch (Exception ex)
            {
                HandleRpcFailure(ex);
                message = $"Refresh failed: {ex.Message}";
                return false;
            }
        }

        public bool SendSingleJointGoal(string jointName, float goalDegrees, out string message)
        {
            if (!EnsureConnected(out message))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(jointName))
            {
                message = "Joint name is empty.";
                return false;
            }

            var command = new JointsCommand();
            command.Commands.Add(new JointCommand
            {
                Id = new JointId { Name = jointName },
                GoalPosition = DegreesToRadians(goalDegrees)
            });

            return SendMotionCommand(command, out message);
        }

        public bool TryGetJointPositions(
            IReadOnlyList<string> jointNames,
            out Dictionary<string, float> positionsDegrees,
            out string message)
        {
            positionsDegrees = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (!EnsureConnected(out message))
            {
                return false;
            }

            if (jointNames == null || jointNames.Count == 0)
            {
                message = "No joint names requested.";
                return false;
            }

            var request = new JointsStateRequest();
            request.RequestedFields.Add(JointField.PresentPosition);

            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < jointNames.Count; i++)
            {
                string jointName = jointNames[i];
                if (string.IsNullOrWhiteSpace(jointName) || !uniqueNames.Add(jointName))
                {
                    continue;
                }

                request.Ids.Add(new JointId { Name = jointName });
            }

            if (request.Ids.Count == 0)
            {
                message = "No valid joint names requested.";
                return false;
            }

            try
            {
                JointsState response = _jointClient.GetJointsState(
                    request,
                    deadline: DateTime.UtcNow.AddSeconds(MotionPreparationRpcTimeoutSeconds));

                if (response == null || response.States == null || response.Ids == null)
                {
                    message = "Joint state response was empty.";
                    return false;
                }

                int max = Math.Min(response.Ids.Count, response.States.Count);
                for (int i = 0; i < max; i++)
                {
                    JointId id = response.Ids[i];
                    JointState state = response.States[i];
                    if (id == null || string.IsNullOrWhiteSpace(id.Name) || state == null || !state.PresentPosition.HasValue)
                    {
                        continue;
                    }

                    positionsDegrees[id.Name] = RadiansToDegrees((float)state.PresentPosition.Value);
                }

                message = positionsDegrees.Count > 0
                    ? $"Fetched {positionsDegrees.Count} joint position(s)."
                    : "Joint state response contained no present positions.";
                return positionsDegrees.Count > 0;
            }
            catch (Exception ex)
            {
                HandleRpcFailure(ex);
                message = $"Joint state fetch failed: {ex.Message}";
                return false;
            }
        }

        public bool SendJointGoals(IReadOnlyDictionary<string, float> jointGoalsDegrees, float speedLimitPercent, out string message)
        {
            if (!EnsureConnected(out message))
            {
                return false;
            }

            if (jointGoalsDegrees == null || jointGoalsDegrees.Count == 0)
            {
                message = "Joint goal set is empty.";
                return false;
            }

            float normalizedSpeedLimit = ClampManualJointSpeedLimitPercent(speedLimitPercent);
            var command = new JointsCommand();
            int validGoalCount = 0;
            foreach (KeyValuePair<string, float> goal in jointGoalsDegrees)
            {
                if (string.IsNullOrWhiteSpace(goal.Key))
                {
                    continue;
                }

                command.Commands.Add(new JointCommand
                {
                    Id = new JointId { Name = goal.Key },
                    GoalPosition = DegreesToRadians(goal.Value),
                    SpeedLimit = normalizedSpeedLimit
                });
                validGoalCount++;
            }

            if (validGoalCount <= 0)
            {
                message = "Joint goal set contains no valid joint names.";
                return false;
            }

            bool ok = SendMotionCommand(command, out string sendMessage);
            message = ok
                ? $"Sent {validGoalCount} joint goal(s) at {normalizedSpeedLimit:F0}% speed. {sendMessage}"
                : $"Failed to send {validGoalCount} joint goal(s). {sendMessage}";
            return ok;
        }

        public bool TryGetMobileBasePresence(out bool present, out string message)
        {
            present = false;
            if (!EnsureConnected(out message))
            {
                return false;
            }

            if (_mobilityServiceUnavailable)
            {
                if (!HasDedicatedMobilityChannel &&
                    TryBindDedicatedMobilityEndpoint(out present, out string dedicatedMessage))
                {
                    message = dedicatedMessage;
                    return true;
                }

                message = "Mobility service is unavailable on the connected endpoint.";
                return false;
            }

            EnsureMobilityClients();
            if (_mobileBasePresenceClient == null)
            {
                if (!HasDedicatedMobilityChannel &&
                    TryBindDedicatedMobilityEndpoint(out present, out string dedicatedMessage))
                {
                    message = dedicatedMessage;
                    return true;
                }

                message = "Mobile base presence client is unavailable.";
                return false;
            }

            if (_cachedMobileBasePresence.HasValue)
            {
                present = _cachedMobileBasePresence.Value;
                if (!present &&
                    !HasDedicatedMobilityChannel &&
                    TryBindDedicatedMobilityEndpoint(out bool dedicatedPresent, out string dedicatedMessage))
                {
                    present = dedicatedPresent;
                    message = dedicatedMessage;
                    return true;
                }

                message = BuildMobileBasePresenceMessage(present);
                return true;
            }

            try
            {
                MobileBasePresence response = _mobileBasePresenceClient.GetMobileBasePresence(
                    new Empty(),
                    deadline: DateTime.UtcNow.AddSeconds(MobilityRpcTimeoutSeconds));
                present = response != null && response.Presence == true;
                _cachedMobileBasePresence = present;

                if (!present &&
                    !HasDedicatedMobilityChannel &&
                    TryBindDedicatedMobilityEndpoint(out bool dedicatedPresent, out string dedicatedMessage))
                {
                    present = dedicatedPresent;
                    message = dedicatedMessage;
                    return true;
                }

                message = BuildMobileBasePresenceMessage(present);
                return true;
            }
            catch (RpcException rpcEx)
            {
                if (!HasDedicatedMobilityChannel &&
                    ShouldTryDedicatedMobilityEndpoint(rpcEx.StatusCode) &&
                    TryBindDedicatedMobilityEndpoint(out present, out string dedicatedMessage))
                {
                    message = dedicatedMessage;
                    return true;
                }

                if (rpcEx.StatusCode == StatusCode.Unimplemented)
                {
                    _mobilityServiceUnavailable = true;
                }

                message = $"Mobile base presence RPC {rpcEx.StatusCode}: {rpcEx.Status.Detail}";
                return false;
            }
            catch (Exception ex)
            {
                message = $"Mobile base presence failed: {ex.Message}";
                return false;
            }
        }

        public bool SendBaseVelocity(float xVel, float yVel, float rotVel, float durationSeconds, out string message)
        {
            if (!EnsureConnected(out message))
            {
                return false;
            }

            EnsureMobilityClients();
            if (_mobilityClient == null)
            {
                message = "Mobility client is unavailable.";
                return false;
            }

            if (!TryGetMobileBasePresence(out bool present, out string presenceMessage))
            {
                message = presenceMessage;
                return false;
            }

            if (!present)
            {
                message = presenceMessage;
                return false;
            }

            float normalizedDuration = NormalizeBaseVelocityDuration(durationSeconds);

            try
            {
                if (!_mobilityConfiguredForSpeedMode)
                {
                    ControlModeCommandAck controlAck = _mobilityClient.SetControlMode(
                        new ControlModeCommand { Mode = ControlModePossiblities.Pid },
                        deadline: DateTime.UtcNow.AddSeconds(MobilityRpcTimeoutSeconds));
                    if (controlAck == null || controlAck.Success != true)
                    {
                        message = "Mobile base rejected PID control mode.";
                        return false;
                    }

                    ZuuuModeCommandAck modeAck = _mobilityClient.SetZuuuMode(
                        new ZuuuModeCommand { Mode = ZuuuModePossiblities.Speed },
                        deadline: DateTime.UtcNow.AddSeconds(MobilityRpcTimeoutSeconds));
                    if (modeAck == null || modeAck.Success != true)
                    {
                        message = "Mobile base rejected SPEED mode.";
                        return false;
                    }

                    _mobilityConfiguredForSpeedMode = true;
                }

                SetSpeedAck ack = _mobilityClient.SendSetSpeed(
                    new SetSpeedVector
                    {
                        XVel = xVel,
                        YVel = yVel,
                        RotVel = rotVel,
                        Duration = normalizedDuration
                    },
                    deadline: DateTime.UtcNow.AddSeconds(MobilityRpcTimeoutSeconds));

                if (ack != null && ack.Success == true)
                {
                    message =
                        $"Base velocity sent (x={xVel:F2} m/s, y={yVel:F2} m/s, rot={rotVel:F2} rad/s, duration={normalizedDuration:F2}s).";
                    return true;
                }

                message = "Base velocity command sent but was not acknowledged as successful.";
                return false;
            }
            catch (RpcException rpcEx)
            {
                if (!HasDedicatedMobilityChannel &&
                    ShouldTryDedicatedMobilityEndpoint(rpcEx.StatusCode) &&
                    TryBindDedicatedMobilityEndpoint(out bool dedicatedPresent, out string dedicatedMessage))
                {
                    if (!dedicatedPresent)
                    {
                        message = dedicatedMessage;
                        return false;
                    }

                    _mobilityConfiguredForSpeedMode = false;
                    return SendBaseVelocity(xVel, yVel, rotVel, durationSeconds, out message);
                }

                if (rpcEx.StatusCode == StatusCode.Unimplemented)
                {
                    _mobilityServiceUnavailable = true;
                }

                if (rpcEx.StatusCode == StatusCode.Unavailable ||
                    rpcEx.StatusCode == StatusCode.Cancelled ||
                    rpcEx.StatusCode == StatusCode.DeadlineExceeded)
                {
                    _mobilityConfiguredForSpeedMode = false;
                }

                message = $"Base command RPC {rpcEx.StatusCode}: {rpcEx.Status.Detail}";
                return false;
            }
            catch (Exception ex)
            {
                _mobilityConfiguredForSpeedMode = false;
                message = $"Base command failed: {ex.Message}";
                return false;
            }
        }

        public bool SendNeutralArmsPreset(out string message)
        {
            return SendPresetPose(NeutralArmsPoseName, out message);
        }

        public bool SendTPosePreset(out string message)
        {
            return SendPresetPose(TPoseName, out message);
        }

        public bool SendPresetPose(string presetName, out string message)
        {
            if (!EnsureConnected(out message))
            {
                return false;
            }

            PosePreset preset = FindPresetByName(presetName);
            if (preset == null)
            {
                message = $"Unknown preset pose '{presetName}'.";
                return false;
            }

            var command = new JointsCommand();
            float poseSpeedPercent = ClampPoseTransitionSpeedScale(PoseTransitionSpeedScale) * 100.0f;
            for (int i = 0; i < preset.Goals.Count; i++)
            {
                PoseJointGoal goal = preset.Goals[i];
                command.Commands.Add(new JointCommand
                {
                    Id = new JointId { Name = goal.JointName },
                    GoalPosition = DegreesToRadians(goal.GoalDegrees),
                    SpeedLimit = poseSpeedPercent
                });
            }

            bool ok = SendMotionCommand(command, out string sendMessage);
            message = ok
                ? $"Preset '{preset.Name}' sent ({preset.Goals.Count} joints, speed {poseSpeedPercent:F0}%). {sendMessage}"
                : $"Preset '{preset.Name}' failed. {sendMessage}";
            return ok;
        }

        public bool Ping(out string message)
        {
            if (!EnsureConnected(out message))
            {
                return false;
            }

            try
            {
                _jointClient.GetAllJointsId(
                    new Empty(),
                    deadline: DateTime.UtcNow.AddSeconds(HealthCheckRpcTimeoutSeconds)
                );

                message = "Connection healthy.";
                return true;
            }
            catch (Exception ex)
            {
                HandleRpcFailure(ex);
                message = $"Ping failed: {ex.Message}";
                return false;
            }
        }

        public bool TrySendRestartSignal(string host, int port, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(host))
            {
                message = "Host is empty.";
                return false;
            }

            if (port <= 0 || port > 65535)
            {
                message = $"Invalid port '{port}'.";
                return false;
            }

            Channel restartChannel = null;
            try
            {
                restartChannel = new Channel($"{host}:{port}", ChannelCredentials.Insecure);
                var restartClient = new RestartService.RestartServiceClient(restartChannel);

                RestartSignalAck ack = restartClient.SendRestartSignal(
                    new RestartCmd { Cmd = SignalType.Restart },
                    deadline: DateTime.UtcNow.AddSeconds(RestartRpcTimeoutSeconds)
                );

                bool success = ack != null && ack.Success == true;
                message = success
                    ? "Restart acknowledged by remote service."
                    : "Restart service responded but did not acknowledge success.";
                return success;
            }
            catch (RpcException rpcEx)
            {
                message = $"RPC {rpcEx.Status.StatusCode}: {rpcEx.Status.Detail}";
                return false;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
            finally
            {
                if (restartChannel != null)
                {
                    try
                    {
                        restartChannel.ShutdownAsync().Wait(1500);
                    }
                    catch
                    {
                        // Ignore cleanup errors during disconnect.
                    }
                }
            }
        }

        private bool SendMotionCommand(JointsCommand command, out string message)
        {
            message = string.Empty;

            if (!EnsureConnected(out message))
            {
                return false;
            }

            if (command == null || command.Commands == null || command.Commands.Count == 0)
            {
                message = "Joint command is empty.";
                return false;
            }

            JointsCommand targetCommand = command.Clone();
            ForceMotionComplianceOff(targetCommand);

            string preparationMessage = PrepareJointsForMotion(targetCommand);
            bool ok = SendJointCommandRaw(targetCommand, out string sendMessage);

            var details = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(preparationMessage))
            {
                details.Add(preparationMessage);
            }

            if (!string.IsNullOrWhiteSpace(sendMessage))
            {
                details.Add(sendMessage);
            }

            message = details.Count > 0 ? string.Join(" ", details) : string.Empty;

            return ok;
        }

        private bool SendJointCommandRaw(JointsCommand command, out string message)
        {
            if (!EnsureConnected(out message))
            {
                return false;
            }

            try
            {
                JointsCommandAck ack = _jointClient.SendJointsCommands(
                    command,
                    deadline: DateTime.UtcNow.AddSeconds(DefaultRpcTimeoutSeconds)
                );

                if (ack != null && ack.Success == true)
                {
                    message = "Command acknowledged.";
                    return true;
                }

                message = "Command sent but was not acknowledged as successful.";
                return false;
            }
            catch (Exception ex)
            {
                HandleRpcFailure(ex);
                message = $"Command failed: {ex.Message}";
                return false;
            }
        }

        private static void ForceMotionComplianceOff(JointsCommand command)
        {
            if (command == null || command.Commands == null)
            {
                return;
            }

            for (int i = 0; i < command.Commands.Count; i++)
            {
                JointCommand jointCommand = command.Commands[i];
                if (jointCommand == null)
                {
                    continue;
                }

                jointCommand.Compliant = false;
            }
        }

        private string PrepareJointsForMotion(JointsCommand targetCommand)
        {
            if (targetCommand == null || targetCommand.Commands == null || targetCommand.Commands.Count == 0)
            {
                return string.Empty;
            }

            JointsState stateResponse = TryGetJointsState(targetCommand, out string stateMessage);
            if (stateResponse == null)
            {
                return string.IsNullOrWhiteSpace(stateMessage)
                    ? "Joint state unavailable; using direct compliance-off motion."
                    : $"Joint state unavailable ({stateMessage}); using direct compliance-off motion.";
            }

            Dictionary<string, JointStateSnapshot> stateMap = BuildJointStateSnapshotMap(stateResponse);
            if (stateMap.Count == 0)
            {
                return "Joint state response was empty; using direct compliance-off motion.";
            }

            var stiffenCommand = new JointsCommand();
            int preparedCount = 0;
            int missingStateCount = 0;
            int alreadyStiffCount = 0;

            for (int i = 0; i < targetCommand.Commands.Count; i++)
            {
                JointCommand targetJointCommand = targetCommand.Commands[i];
                if (targetJointCommand == null || targetJointCommand.Id == null)
                {
                    continue;
                }

                if (!TryResolveSnapshot(stateMap, targetJointCommand.Id, out JointStateSnapshot snapshot))
                {
                    missingStateCount++;
                    continue;
                }

                if (snapshot.Compliant.HasValue && !snapshot.Compliant.Value)
                {
                    alreadyStiffCount++;
                    continue;
                }

                var stiffenJoint = new JointCommand
                {
                    Id = targetJointCommand.Id.Clone(),
                    Compliant = false
                };

                if (snapshot.PresentPosition.HasValue)
                {
                    stiffenJoint.GoalPosition = snapshot.PresentPosition.Value;
                }
                else if (targetJointCommand.GoalPosition.HasValue)
                {
                    stiffenJoint.GoalPosition = targetJointCommand.GoalPosition.Value;
                }

                stiffenCommand.Commands.Add(stiffenJoint);
                preparedCount++;
            }

            if (preparedCount <= 0)
            {
                if (missingStateCount > 0)
                {
                    return $"State missing for {missingStateCount} joint(s); direct compliance-off motion active.";
                }

                if (alreadyStiffCount > 0)
                {
                    return string.Empty;
                }

                return "Motion preparation skipped; direct compliance-off motion active.";
            }

            bool prepOk = SendJointCommandRaw(stiffenCommand, out string prepMessage);
            if (prepOk)
            {
                Thread.Sleep(MotionPreparationSettleDelayMs);

                if (missingStateCount > 0)
                {
                    return $"Prepared {preparedCount} joint(s) for motion. State missing for {missingStateCount} joint(s); direct compliance-off fallback active.";
                }

                return $"Prepared {preparedCount} joint(s) for motion.";
            }

            return $"Motion preparation failed ({prepMessage}); continuing with direct compliance-off motion.";
        }

        private JointsState TryGetJointsState(JointsCommand command, out string message)
        {
            message = string.Empty;

            if (_jointClient == null)
            {
                message = "Not connected.";
                return null;
            }

            var request = new JointsStateRequest();
            request.RequestedFields.Add(JointField.Compliant);
            request.RequestedFields.Add(JointField.PresentPosition);

            var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < command.Commands.Count; i++)
            {
                JointCommand jointCommand = command.Commands[i];
                if (jointCommand == null || jointCommand.Id == null)
                {
                    continue;
                }

                JointId requestId = BuildStateRequestId(jointCommand.Id, addedKeys);
                if (requestId != null)
                {
                    request.Ids.Add(requestId);
                }
            }

            if (request.Ids.Count == 0)
            {
                message = "No valid joint ids in motion command.";
                return null;
            }

            try
            {
                return _jointClient.GetJointsState(
                    request,
                    deadline: DateTime.UtcNow.AddSeconds(MotionPreparationRpcTimeoutSeconds)
                );
            }
            catch (RpcException rpcEx)
            {
                message = $"RPC {rpcEx.Status.StatusCode}: {rpcEx.Status.Detail}";
                return null;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return null;
            }
        }

        private static JointId BuildStateRequestId(JointId sourceId, HashSet<string> addedKeys)
        {
            if (sourceId == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(sourceId.Name))
            {
                string key = BuildJointNameKey(sourceId.Name);
                if (addedKeys.Add(key))
                {
                    return new JointId { Name = sourceId.Name };
                }

                return null;
            }

            if (sourceId.Uid != 0)
            {
                string key = BuildJointUidKey(sourceId.Uid);
                if (addedKeys.Add(key))
                {
                    return new JointId { Uid = sourceId.Uid };
                }
            }

            return null;
        }

        private static Dictionary<string, JointStateSnapshot> BuildJointStateSnapshotMap(JointsState stateResponse)
        {
            var snapshots = new Dictionary<string, JointStateSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (stateResponse == null)
            {
                return snapshots;
            }

            int pairCount = Math.Min(stateResponse.Ids.Count, stateResponse.States.Count);
            for (int i = 0; i < pairCount; i++)
            {
                JointId id = stateResponse.Ids[i];
                JointState state = stateResponse.States[i];
                if (state == null)
                {
                    continue;
                }

                var snapshot = new JointStateSnapshot(state.Compliant, state.PresentPosition);

                if (id != null)
                {
                    if (!string.IsNullOrWhiteSpace(id.Name))
                    {
                        snapshots[BuildJointNameKey(id.Name)] = snapshot;
                    }

                    if (id.Uid != 0)
                    {
                        snapshots[BuildJointUidKey(id.Uid)] = snapshot;
                    }
                }

                if (!string.IsNullOrWhiteSpace(state.Name))
                {
                    snapshots[BuildJointNameKey(state.Name)] = snapshot;
                }

                if (state.Uid.HasValue && state.Uid.Value != 0)
                {
                    snapshots[BuildJointUidKey(state.Uid.Value)] = snapshot;
                }
            }

            return snapshots;
        }

        private static bool TryResolveSnapshot(
            Dictionary<string, JointStateSnapshot> stateMap,
            JointId jointId,
            out JointStateSnapshot snapshot)
        {
            snapshot = null;
            if (stateMap == null || jointId == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(jointId.Name))
            {
                return stateMap.TryGetValue(BuildJointNameKey(jointId.Name), out snapshot);
            }

            if (jointId.Uid != 0)
            {
                return stateMap.TryGetValue(BuildJointUidKey(jointId.Uid), out snapshot);
            }

            return false;
        }

        private static string BuildJointNameKey(string jointName)
        {
            return (jointName ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string BuildJointUidKey(uint jointUid)
        {
            return $"uid:{jointUid}";
        }

        private static float ClampPoseTransitionSpeedScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return DefaultPoseTransitionSpeedScale;
            }

            if (value < MinPoseTransitionSpeedScale)
            {
                return MinPoseTransitionSpeedScale;
            }

            if (value > MaxPoseTransitionSpeedScale)
            {
                return MaxPoseTransitionSpeedScale;
            }

            return value;
        }

        private bool EnsureConnected(out string message)
        {
            if (!IsConnected || _jointClient == null)
            {
                message = "Not connected.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private void EnsureMobilityClients()
        {
            Channel channel = _mobilityChannel ?? _jointChannel;
            if (channel == null)
            {
                _mobilityClient = null;
                _mobileBasePresenceClient = null;
                return;
            }

            if (_mobilityClient == null)
            {
                _mobilityClient = new MobilityService.MobilityServiceClient(channel);
            }

            if (_mobileBasePresenceClient == null)
            {
                _mobileBasePresenceClient = new MobileBasePresenceService.MobileBasePresenceServiceClient(channel);
            }
        }

        private bool HasDedicatedMobilityChannel => _mobilityChannel != null;

        private static bool ShouldTryDedicatedMobilityEndpoint(StatusCode statusCode)
        {
            return statusCode == StatusCode.Unimplemented ||
                   statusCode == StatusCode.Unavailable ||
                   statusCode == StatusCode.Cancelled ||
                   statusCode == StatusCode.DeadlineExceeded;
        }

        private string BuildMobileBasePresenceMessage(bool present)
        {
            if (HasDedicatedMobilityChannel &&
                !string.IsNullOrWhiteSpace(ConnectedHost) &&
                _mobilityPort > 0)
            {
                return present
                    ? $"Dedicated mobility endpoint {ConnectedHost}:{_mobilityPort} reports a mobile base."
                    : $"Dedicated mobility endpoint {ConnectedHost}:{_mobilityPort} reports no mobile base.";
            }

            return present
                ? "Connected endpoint reports a mobile base."
                : "Connected endpoint reports no mobile base.";
        }

        private bool TryBindDedicatedMobilityEndpoint(out bool present, out string message)
        {
            present = false;
            message = string.Empty;

            if (!IsConnected || string.IsNullOrWhiteSpace(ConnectedHost))
            {
                message = "Not connected.";
                return false;
            }

            int[] candidatePorts = { DedicatedMobilityPortPrimary, DedicatedMobilityPortLegacy };
            string lastFailure = string.Empty;
            for (int i = 0; i < candidatePorts.Length; i++)
            {
                int candidatePort = candidatePorts[i];
                if (candidatePort <= 0 ||
                    candidatePort == ConnectedPort ||
                    (HasDedicatedMobilityChannel && candidatePort == _mobilityPort))
                {
                    continue;
                }

                Channel candidateChannel = null;
                try
                {
                    candidateChannel = new Channel($"{ConnectedHost}:{candidatePort}", ChannelCredentials.Insecure);
                    var presenceClient = new MobileBasePresenceService.MobileBasePresenceServiceClient(candidateChannel);
                    MobileBasePresence response = presenceClient.GetMobileBasePresence(
                        new Empty(),
                        deadline: DateTime.UtcNow.AddSeconds(MobilityRpcTimeoutSeconds));

                    present = response != null && response.Presence == true;
                    if (_mobilityChannel != null)
                    {
                        ShutdownChannelQuietly(_mobilityChannel);
                    }

                    _mobilityChannel = candidateChannel;
                    candidateChannel = null;
                    _mobilityPort = candidatePort;
                    _mobileBasePresenceClient = presenceClient;
                    _mobilityClient = new MobilityService.MobilityServiceClient(_mobilityChannel);
                    _cachedMobileBasePresence = present;
                    _mobilityConfiguredForSpeedMode = false;
                    _mobilityServiceUnavailable = false;
                    message = BuildMobileBasePresenceMessage(present);
                    return true;
                }
                catch (RpcException rpcEx)
                {
                    lastFailure = $"{ConnectedHost}:{candidatePort} => {rpcEx.StatusCode}: {rpcEx.Status.Detail}";
                }
                catch (Exception ex)
                {
                    lastFailure = $"{ConnectedHost}:{candidatePort} => {ex.Message}";
                }
                finally
                {
                    if (candidateChannel != null)
                    {
                        ShutdownChannelQuietly(candidateChannel);
                    }
                }
            }

            message = string.IsNullOrWhiteSpace(lastFailure)
                ? "Dedicated mobility endpoint probe failed."
                : $"Dedicated mobility endpoint probe failed. {lastFailure}";
            return false;
        }

        private void ResetMobilityClients()
        {
            _cachedMobileBasePresence = null;
            _mobilityConfiguredForSpeedMode = false;
            _mobilityServiceUnavailable = false;
            _mobilityClient = null;
            _mobileBasePresenceClient = null;
            _mobilityPort = 0;

            if (_mobilityChannel != null)
            {
                ShutdownChannelQuietly(_mobilityChannel);
                _mobilityChannel = null;
            }
        }

        private static void ShutdownChannelQuietly(Channel channel)
        {
            if (channel == null)
            {
                return;
            }

            try
            {
                channel.ShutdownAsync().Wait(1500);
            }
            catch
            {
                // Ignore cleanup errors during disconnect.
            }
        }

        private void DisconnectCamera()
        {
            lock (_cameraLock)
            {
                DisconnectCameraLocked();
            }
        }

        private void DisconnectCameraLocked()
        {
            _cameraHost = string.Empty;
            _cameraPort = 0;

            if (_cameraChannel != null)
            {
                try
                {
                    _cameraChannel.ShutdownAsync().Wait(1500);
                }
                catch
                {
                    // Ignore cleanup errors during disconnect.
                }
            }

            _cameraClient = null;
            _cameraChannel = null;
        }

        private bool EnsureCameraClientLocked(string host, int port, out string message)
        {
            message = string.Empty;

            if (_cameraClient != null &&
                string.Equals(_cameraHost, host, StringComparison.OrdinalIgnoreCase) &&
                _cameraPort == port)
            {
                return true;
            }

            DisconnectCameraLocked();

            try
            {
                _cameraChannel = new Channel($"{host}:{port}", ChannelCredentials.Insecure);
                _cameraClient = new CameraService.CameraServiceClient(_cameraChannel);
                _cameraHost = host;
                _cameraPort = port;
                return true;
            }
            catch (Exception ex)
            {
                DisconnectCameraLocked();
                message = $"Camera connection failed: {ex.Message}";
                return false;
            }
        }

        private bool ConnectInternal(string host, int port, double timeoutSeconds, out string message)
        {
            message = string.Empty;

            if (string.IsNullOrWhiteSpace(host))
            {
                message = "Host is empty.";
                return false;
            }

            if (port <= 0 || port > 65535)
            {
                message = $"Invalid port '{port}'.";
                return false;
            }

            Disconnect();

            try
            {
                _jointChannel = new Channel($"{host}:{port}", ChannelCredentials.Insecure);
                _jointClient = new JointService.JointServiceClient(_jointChannel);
                EnsureMobilityClients();

                JointsId joints = _jointClient.GetAllJointsId(
                    new Empty(),
                    deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds)
                );

                _jointNames.Clear();
                if (joints != null && joints.Names != null)
                {
                    _jointNames.AddRange(joints.Names);
                }

                IsConnected = true;
                ConnectedHost = host;
                ConnectedPort = port;
                message = $"Connected to {host}:{port}. Joints discovered: {_jointNames.Count}.";
                return true;
            }
            catch (Exception ex)
            {
                Disconnect();
                message = $"Connection failed: {ex.Message}";
                return false;
            }
        }

        private void HandleRpcFailure(Exception ex)
        {
            if (!(ex is RpcException rpcEx))
            {
                return;
            }

            switch (rpcEx.StatusCode)
            {
                case StatusCode.Unavailable:
                case StatusCode.DeadlineExceeded:
                case StatusCode.Cancelled:
                case StatusCode.Unknown:
                case StatusCode.Internal:
                case StatusCode.Unimplemented:
                    Disconnect();
                    break;
            }
        }

        private static float DegreesToRadians(float degrees)
        {
            return degrees * Deg2Rad;
        }

        private static float RadiansToDegrees(float radians)
        {
            return radians * Rad2Deg;
        }

        private static float ClampManualJointSpeedLimitPercent(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return DefaultManualJointSpeedLimitPercent;
            }

            if (value < 1.0f)
            {
                return 1.0f;
            }

            if (value > 100.0f)
            {
                return 100.0f;
            }

            return value;
        }

        private static float NormalizeBaseVelocityDuration(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0.12f;
            }

            if (value < 0.05f)
            {
                return 0.05f;
            }

            if (value > 0.5f)
            {
                return 0.5f;
            }

            return value;
        }

        private static List<PosePreset> BuildPresetPoseLibrary()
        {
            return new List<PosePreset>
            {
                new PosePreset(
                    NeutralArmsPoseName,
                    new List<PoseJointGoal>
                    {
                        new PoseJointGoal("r_shoulder_pitch", 0.0f),
                        new PoseJointGoal("r_shoulder_roll", 0.0f),
                        new PoseJointGoal("r_arm_yaw", 0.0f),
                        new PoseJointGoal("r_elbow_pitch", 0.0f),
                        new PoseJointGoal("r_forearm_yaw", 0.0f),
                        new PoseJointGoal("r_wrist_pitch", 0.0f),
                        new PoseJointGoal("r_wrist_roll", 0.0f),
                        new PoseJointGoal("r_gripper", 0.0f),
                        new PoseJointGoal("l_shoulder_pitch", 0.0f),
                        new PoseJointGoal("l_shoulder_roll", 0.0f),
                        new PoseJointGoal("l_arm_yaw", 0.0f),
                        new PoseJointGoal("l_elbow_pitch", 0.0f),
                        new PoseJointGoal("l_forearm_yaw", 0.0f),
                        new PoseJointGoal("l_wrist_pitch", 0.0f),
                        new PoseJointGoal("l_wrist_roll", 0.0f),
                        new PoseJointGoal("l_gripper", 0.0f),
                    }
                ),
                new PosePreset(
                    TPoseName,
                    new List<PoseJointGoal>
                    {
                        new PoseJointGoal("r_shoulder_pitch", 0.0f),
                        new PoseJointGoal("r_shoulder_roll", -90.0f),
                        new PoseJointGoal("r_arm_yaw", 0.0f),
                        new PoseJointGoal("r_elbow_pitch", 0.0f),
                        new PoseJointGoal("r_forearm_yaw", 0.0f),
                        new PoseJointGoal("r_wrist_pitch", 0.0f),
                        new PoseJointGoal("r_wrist_roll", 0.0f),
                        new PoseJointGoal("r_gripper", 0.0f),
                        new PoseJointGoal("l_shoulder_pitch", 0.0f),
                        new PoseJointGoal("l_shoulder_roll", 90.0f),
                        new PoseJointGoal("l_arm_yaw", 0.0f),
                        new PoseJointGoal("l_elbow_pitch", 0.0f),
                        new PoseJointGoal("l_forearm_yaw", 0.0f),
                        new PoseJointGoal("l_wrist_pitch", 0.0f),
                        new PoseJointGoal("l_wrist_roll", 0.0f),
                        new PoseJointGoal("l_gripper", 0.0f),
                    }
                ),
                new PosePreset(
                    TrayHoldingPoseName,
                    new List<PoseJointGoal>
                    {
                        new PoseJointGoal("r_shoulder_pitch", -40.0f),
                        new PoseJointGoal("r_shoulder_roll", -15.0f),
                        new PoseJointGoal("r_arm_yaw", -5.0f),
                        new PoseJointGoal("r_elbow_pitch", -95.0f),
                        new PoseJointGoal("r_forearm_yaw", -75.0f),
                        new PoseJointGoal("r_wrist_pitch", 20.0f),
                        new PoseJointGoal("r_wrist_roll", 0.0f),
                        new PoseJointGoal("r_gripper", 0.0f),
                        new PoseJointGoal("l_shoulder_pitch", -40.0f),
                        new PoseJointGoal("l_shoulder_roll", 15.0f),
                        new PoseJointGoal("l_arm_yaw", 5.0f),
                        new PoseJointGoal("l_elbow_pitch", -95.0f),
                        new PoseJointGoal("l_forearm_yaw", 75.0f),
                        new PoseJointGoal("l_wrist_pitch", 20.0f),
                        new PoseJointGoal("l_wrist_roll", 0.0f),
                        new PoseJointGoal("l_gripper", 0.0f),
                    }
                ),
                new PosePreset(
                    HelloPoseName,
                    new List<PoseJointGoal>
                    {
                        new PoseJointGoal("r_shoulder_pitch", 0.0f),
                        new PoseJointGoal("r_shoulder_roll", 0.0f),
                        new PoseJointGoal("r_arm_yaw", 0.0f),
                        new PoseJointGoal("r_elbow_pitch", 0.0f),
                        new PoseJointGoal("r_forearm_yaw", 0.0f),
                        new PoseJointGoal("r_wrist_pitch", 0.0f),
                        new PoseJointGoal("r_wrist_roll", 0.0f),
                        new PoseJointGoal("r_gripper", 0.0f),
                        new PoseJointGoal("l_shoulder_pitch", -85.0f),
                        new PoseJointGoal("l_shoulder_roll", 30.0f),
                        new PoseJointGoal("l_arm_yaw", 10.0f),
                        new PoseJointGoal("l_elbow_pitch", -80.0f),
                        new PoseJointGoal("l_forearm_yaw", 25.0f),
                        new PoseJointGoal("l_wrist_pitch", 20.0f),
                        new PoseJointGoal("l_wrist_roll", 0.0f),
                        new PoseJointGoal("l_gripper", 0.0f),
                    }
                ),
                new PosePreset(
                    HelloPoseWaveName,
                    new List<PoseJointGoal>
                    {
                        new PoseJointGoal("r_shoulder_pitch", 0.0f),
                        new PoseJointGoal("r_shoulder_roll", 0.0f),
                        new PoseJointGoal("r_arm_yaw", 0.0f),
                        new PoseJointGoal("r_elbow_pitch", 0.0f),
                        new PoseJointGoal("r_forearm_yaw", 0.0f),
                        new PoseJointGoal("r_wrist_pitch", 0.0f),
                        new PoseJointGoal("r_wrist_roll", 0.0f),
                        new PoseJointGoal("r_gripper", 0.0f),
                        new PoseJointGoal("l_shoulder_pitch", -78.0f),
                        new PoseJointGoal("l_shoulder_roll", 35.0f),
                        new PoseJointGoal("l_arm_yaw", 15.0f),
                        new PoseJointGoal("l_elbow_pitch", -115.0f),
                        new PoseJointGoal("l_forearm_yaw", 40.0f),
                        new PoseJointGoal("l_wrist_pitch", 30.0f),
                        new PoseJointGoal("l_wrist_roll", 18.0f),
                        new PoseJointGoal("l_gripper", 0.0f),
                    }
                ),
                new PosePreset(
                    HelloPoseRightName,
                    new List<PoseJointGoal>
                    {
                        new PoseJointGoal("r_shoulder_pitch", -85.0f),
                        new PoseJointGoal("r_shoulder_roll", -30.0f),
                        new PoseJointGoal("r_arm_yaw", -10.0f),
                        new PoseJointGoal("r_elbow_pitch", -80.0f),
                        new PoseJointGoal("r_forearm_yaw", -25.0f),
                        new PoseJointGoal("r_wrist_pitch", 20.0f),
                        new PoseJointGoal("r_wrist_roll", 0.0f),
                        new PoseJointGoal("r_gripper", 0.0f),
                        new PoseJointGoal("l_shoulder_pitch", 0.0f),
                        new PoseJointGoal("l_shoulder_roll", 0.0f),
                        new PoseJointGoal("l_arm_yaw", 0.0f),
                        new PoseJointGoal("l_elbow_pitch", 0.0f),
                        new PoseJointGoal("l_forearm_yaw", 0.0f),
                        new PoseJointGoal("l_wrist_pitch", 0.0f),
                        new PoseJointGoal("l_wrist_roll", 0.0f),
                        new PoseJointGoal("l_gripper", 0.0f),
                    }
                ),
                new PosePreset(
                    HelloPoseRightWaveName,
                    new List<PoseJointGoal>
                    {
                        new PoseJointGoal("r_shoulder_pitch", -78.0f),
                        new PoseJointGoal("r_shoulder_roll", -35.0f),
                        new PoseJointGoal("r_arm_yaw", -15.0f),
                        new PoseJointGoal("r_elbow_pitch", -115.0f),
                        new PoseJointGoal("r_forearm_yaw", -40.0f),
                        new PoseJointGoal("r_wrist_pitch", 30.0f),
                        new PoseJointGoal("r_wrist_roll", -18.0f),
                        new PoseJointGoal("r_gripper", 0.0f),
                        new PoseJointGoal("l_shoulder_pitch", 0.0f),
                        new PoseJointGoal("l_shoulder_roll", 0.0f),
                        new PoseJointGoal("l_arm_yaw", 0.0f),
                        new PoseJointGoal("l_elbow_pitch", 0.0f),
                        new PoseJointGoal("l_forearm_yaw", 0.0f),
                        new PoseJointGoal("l_wrist_pitch", 0.0f),
                        new PoseJointGoal("l_wrist_roll", 0.0f),
                        new PoseJointGoal("l_gripper", 0.0f),
                    }
                ),
            };
        }

        private PosePreset FindPresetByName(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                return null;
            }

            for (int i = 0; i < _presetPoses.Count; i++)
            {
                PosePreset preset = _presetPoses[i];
                if (string.Equals(preset.Name, presetName, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            return null;
        }

        private sealed class PosePreset
        {
            public PosePreset(string name, List<PoseJointGoal> goals)
            {
                Name = name;
                Goals = goals ?? new List<PoseJointGoal>();
            }

            public string Name { get; }
            public List<PoseJointGoal> Goals { get; }
        }

        private sealed class PoseJointGoal
        {
            public PoseJointGoal(string jointName, float goalDegrees)
            {
                JointName = jointName;
                GoalDegrees = goalDegrees;
            }

            public string JointName { get; }
            public float GoalDegrees { get; }
        }

        private sealed class JointStateSnapshot
        {
            public JointStateSnapshot(bool? compliant, float? presentPosition)
            {
                Compliant = compliant;
                PresentPosition = presentPosition;
            }

            public bool? Compliant { get; }
            public float? PresentPosition { get; }
        }
    }
}
