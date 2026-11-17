using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Reachy.ControlApp
{
    public partial class ReachyRuntimeControlUI
    {
        private const float EthernetStartupWaitSeconds = 120f;
        private const float EthernetRestartCooldownSeconds = 120f;

        [Header("Ethernet discovery")]
        [SerializeField] private bool autoDetectEthernetRobot = true;
        [SerializeField] private float ethernetDiscoveryIntervalSeconds = 15f;

        private sealed class EthernetCheck
        {
            internal ReachyEthernetDiscovery.Result Discovery;
            internal EthernetSshCheck Ssh;
        }

        [Serializable]
        private sealed class EthernetSshCheck
        {
            public string status;
            public int jointCount;
            public string identity;
            public string hostname;
            public string linkLocal;
            public string[] addresses;
        }

        [Serializable]
        private sealed class EthernetSshRequest
        {
            public ReachyEthernetDiscovery.SshRoute[] routes;
            public string user;
            public string password;
            public string knownHostsPath;
            public string identity;
            public string action;
        }

        private Task<EthernetCheck> _ethernetDiscoveryTask;
        private CancellationTokenSource _ethernetDiscoveryCancellation;
        private float _nextEthernetDiscoveryAt;
        private float _nextEthernetLinkCheckAt;
        private float _ethernetLinkSince;
        private float _nextEthernetRestartAt;
        private bool _ethernetLinkUp;
        private bool _ethernetRestartUsed;
        private bool _ethernetNetworkRepairUsed;
        private string _pendingEthernetRecovery;
        private EthernetSshCheck _ethernetRobotHint;
        private bool _initialEthernetDiscoveryCompleted;
        private bool _ethernetDiscoveryStopped;
        private string _ethernetDiscoveryStatus = "Ethernet discovery starts automatically in Play mode.";
        private string _lastEthernetDiagnostic;
        private string _ethernetSelectedHost = string.Empty;
        private int _ethernetSelectedPort;

        private bool IsInitialEthernetDiscoveryPending()
        {
            if (!autoDetectEthernetRobot || _manualDisconnect) return false;
            return !_initialEthernetDiscoveryCompleted || _ethernetLinkUp &&
                (Time.unscaledTime - _ethernetLinkSince < EthernetStartupWaitSeconds || mode == ReachyControlMode.RealRobot);
        }

        private bool ShouldUseEthernetReconnect()
        {
            return autoDetectEthernetRobot && !_manualDisconnect &&
                _autoReconnectMode == ReachyControlMode.RealRobot &&
                (_ethernetLinkUp || !string.IsNullOrEmpty(_ethernetSelectedHost));
        }

        private void UpdateEthernetDiscovery()
        {
            if (_ethernetDiscoveryStopped) return;
            float now = Time.unscaledTime;
            if (now >= _nextEthernetLinkCheckAt)
            {
                _nextEthernetLinkCheckAt = now + 1f;
                bool linkUp = ReachyEthernetDiscovery.GetActiveWiredAdapters().Any();
                if (linkUp != _ethernetLinkUp)
                {
                    _ethernetLinkUp = linkUp;
                    _ethernetLinkSince = now;
                    _nextEthernetDiscoveryAt = 0f;
                    if (!linkUp)
                    {
                        _pendingEthernetRecovery = null;
                        _ethernetNetworkRepairUsed = false;
                        _ethernetDiscoveryCancellation?.Cancel();
                        _ethernetDiscoveryStatus = "Ethernet disconnected. Waiting for the cable.";
                    }
                }
                if (!linkUp)
                {
                    _initialEthernetDiscoveryCompleted = true;
                    if (autoDetectEthernetRobot) _ethernetDiscoveryStatus = "No active Ethernet link. Watching for the cable.";
                }
            }
            if (_manualDisconnect) _ethernetDiscoveryCancellation?.Cancel();

            if (_ethernetDiscoveryTask != null && _ethernetDiscoveryTask.IsCompleted)
            {
                // Applying the result can start a connection; defer until the current attempt finishes.
                if (_isConnectAttemptInProgress) return;
                Task<EthernetCheck> completed = _ethernetDiscoveryTask;
                _ethernetDiscoveryTask = null;
                _ethernetDiscoveryCancellation.Dispose();
                _ethernetDiscoveryCancellation = null;
                _initialEthernetDiscoveryCompleted = true;
                _nextEthernetDiscoveryAt = now + (_ethernetLinkUp && !IsConnectedToSelectedEthernet() &&
                    now - _ethernetLinkSince < EthernetStartupWaitSeconds
                    ? 3f : Mathf.Max(5f, ethernetDiscoveryIntervalSeconds));
                try
                {
                    EthernetCheck result = completed.GetAwaiter().GetResult();
                    if (autoDetectEthernetRobot && !_manualDisconnect && _ethernetLinkUp)
                    {
                        ConsumeEthernetCheck(result);
                        if (_lastEthernetDiagnostic != _ethernetDiscoveryStatus)
                        {
                            _lastEthernetDiagnostic = _ethernetDiscoveryStatus;
                            Debug.Log("[Reachy Ethernet] " + _ethernetDiscoveryStatus);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (autoDetectEthernetRobot && !_manualDisconnect && _ethernetLinkUp)
                        _ethernetDiscoveryStatus = "Ethernet check timed out. Retrying automatically.";
                }
                catch (Exception exception) { _ethernetDiscoveryStatus = "Ethernet discovery failed: " + exception.Message; }
            }

            if (!autoDetectEthernetRobot || _manualDisconnect || !_ethernetLinkUp || _isConnectAttemptInProgress ||
                _ethernetDiscoveryTask != null || now < _nextEthernetDiscoveryAt) return;

            string knownHosts = Path.Combine(Application.persistentDataPath, "ReachyControlApp", "ethernet_known_hosts");
            string hintPath = knownHosts + ".hint.json";
            if (_ethernetRobotHint == null && File.Exists(hintPath))
            {
                try { _ethernetRobotHint = JsonUtility.FromJson<EthernetSshCheck>(File.ReadAllText(hintPath)); }
                catch (Exception) { /* A cache is optional; discovery still runs. */ }
            }
            var hint = _ethernetRobotHint;
            var endpoints = BuildConnectionCandidates(ReachyControlMode.RealRobot);
            string[] hosts = endpoints.Select(endpoint => endpoint.Host).Concat(hint?.addresses ?? Array.Empty<string>())
                .Concat(new[] { hint?.hostname }).Where(host => !string.IsNullOrWhiteSpace(host)).Distinct().ToArray();
            int[] ports = endpoints.Select(endpoint => endpoint.Port).Concat(new[] { 50055, 3972 }).Distinct().ToArray();
            string selected = _ethernetSelectedHost;
            int selectedPort = _ethernetSelectedPort;
            bool realConnected = _client != null && _client.IsConnected && _connectedMode == ReachyControlMode.RealRobot;
            bool monitorSelected = IsConnectedToSelectedEthernet();
            string script = ResolveLocalAssetRelativePath("ReachyControlApp/LocalVoiceAgent/verify_reachy_ethernet.py");
            string directory = Path.GetDirectoryName(script) ?? Application.dataPath;
            string[] interpreters = BuildPythonCommandCandidates(directory, localAiAgentSidecarPythonCommand, true, new[] { script }).ToArray();
            string user = string.IsNullOrWhiteSpace(robotSshUser) ? "reachy" : robotSshUser.Trim();
            string password = robotSshPassword ?? string.Empty;
            string action = _pendingEthernetRecovery;
            _pendingEthernetRecovery = null;
            if (realConnected) action = null;
            if (action == "sdk")
            {
                // Reserve the attempt before the helper runs, including if its reply is lost.
                _ethernetRestartUsed = true;
                _nextEthernetRestartAt = now + EthernetRestartCooldownSeconds;
            }
            if (action == "network") _ethernetNetworkRepairUsed = true;
            if (!monitorSelected && string.IsNullOrEmpty(selected) && !_initialEthernetDiscoveryCompleted)
                _ethernetDiscoveryStatus = "Ethernet connected. Waiting for Reachy to boot...";
            _ethernetDiscoveryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            CancellationToken cancellation = _ethernetDiscoveryCancellation.Token;
            _ethernetDiscoveryTask = Task.Run(async () =>
            {
                var source = monitorSelected ? ReachyEthernetDiscovery.FindWiredSource(selected) : null;
                ReachyEthernetDiscovery.Result discovery = null;
                if (source != null)
                    discovery = await ReachyEthernetDiscovery.VerifyServiceAsync(selected, selectedPort, source.Name, cancellation);
                else if (!monitorSelected)
                    discovery = await ReachyEthernetDiscovery.DiscoverAsync(hosts, ports, cancellation);

                var check = new EthernetCheck { Discovery = discovery };
                if (discovery?.Ambiguous == true && hint == null) return check;
                if (File.Exists(knownHosts) && !string.IsNullOrEmpty(password))
                {
                    var routes = await ReachyEthernetDiscovery.FindManagementRoutesAsync(
                        new[] { discovery?.Host, selected }.Concat(hosts), hint?.linkLocal, cancellation);
                    check.Ssh = RunEthernetSshCheck(script, interpreters, new EthernetSshRequest
                    {
                        routes = routes, user = user, password = password, knownHostsPath = knownHosts,
                        identity = hint?.identity, action = action
                    }, cancellation);

                    // Use the verified robot's current wired address instead of
                    // assuming the previous IPv4 or the first SDK reply is its own.
                    if (check.Ssh?.addresses != null && !string.IsNullOrEmpty(check.Ssh.identity) &&
                        (discovery?.Found != true || !check.Ssh.addresses.Contains(discovery.Host)))
                    {
                        foreach (string address in check.Ssh.addresses.Take(4))
                        {
                            source = ReachyEthernetDiscovery.FindWiredSource(address);
                            if (source == null) continue;
                            check.Discovery = await ReachyEthernetDiscovery.VerifyServiceAsync(address, 50055, source.Name, cancellation);
                            if (check.Discovery != null) break;
                        }
                        if (check.Discovery?.Found != true || !check.Ssh.addresses.Contains(check.Discovery.Host))
                            check.Discovery = new ReachyEthernetDiscovery.Result { Message = "Verified Reachy over SSH. Waiting for its wired SDK endpoint." };
                    }
                }
                return check;
            });
        }

        private bool IsConnectedToSelectedEthernet()
        {
            return _client != null && _client.IsConnected && _connectedMode == ReachyControlMode.RealRobot &&
                string.Equals(_client.ConnectedHost, _ethernetSelectedHost, StringComparison.OrdinalIgnoreCase) &&
                _client.ConnectedPort == _ethernetSelectedPort;
        }

        private void ConsumeEthernetCheck(EthernetCheck check)
        {
            string status = check.Ssh?.status;
            RememberEthernetRobot(check.Ssh);
            bool sdkReady = check.Discovery?.Found == true;
            if (IsConnectedToSelectedEthernet() && (!sdkReady || status == "controller_missing" ||
                status == "no_sample" || status == "key_mismatch"))
            {
                _client.Disconnect();
                _connectedMode = null;
                StopEthernetMotion("Ethernet joint data unavailable.");
                _nextEthernetDiscoveryAt = 0f;
                SetStatus("Ethernet robot unavailable", "Waiting for verified joint data before reconnecting.");
            }
            _ethernetDiscoveryStatus = check.Discovery?.Message ?? "Waiting for Reachy on Ethernet.";
            switch (status)
            {
                case "ready":
                    _ethernetRestartUsed = false;
                    if (sdkReady) _ethernetNetworkRepairUsed = false;
                    _ethernetDiscoveryStatus = sdkReady
                        ? check.Discovery.Message + " Live ROS data verified for " + check.Ssh.jointCount + " joints."
                        : "Reachy publishes live joints, but its SDK is not ready. Retrying.";
                    break;
                case "network_missing":
                case "network_starting":
                case "network_requested":
                case "network_denied":
                    if (status == "network_missing" && !_ethernetNetworkRepairUsed)
                    {
                        _pendingEthernetRecovery = "network";
                        _nextEthernetDiscoveryAt = 0f;
                    }
                    if (status == "network_requested") _nextEthernetDiscoveryAt = Time.unscaledTime + 4f;
                    _ethernetDiscoveryStatus = status == "network_requested" ? "Requested Reachy's direct Ethernet profile. Waiting for IPv4..."
                        : status == "network_denied" ? "Reachy answered over IPv6, but IPv4 recovery was not confirmed."
                        : status == "network_starting" ? "Reachy answered over IPv6. Waiting for its Ethernet IPv4 address..."
                        : _ethernetNetworkRepairUsed ? "Reachy answered over IPv6, but IPv4 is still unavailable after a repair attempt."
                        : "Reachy answered over IPv6. Checking its direct Ethernet profile...";
                    return;
                case "restarted":
                case "restart_failed":
                    _ethernetRestartUsed = true;
                    _nextEthernetRestartAt = Time.unscaledTime + EthernetRestartCooldownSeconds;
                    _nextEthernetDiscoveryAt = Time.unscaledTime + 4f;
                    _ethernetDiscoveryStatus = status == "restarted"
                        ? "Restarted Reachy's SDK after detecting a missing controller. Waiting for live joints..."
                        : "Reachy's SDK restart failed. Check the robot's SDK service.";
                    return;
                case "controller_missing":
                case "no_sample":
                case "starting":
                    _ethernetDiscoveryStatus = status == "starting" ? "Reachy is starting. Waiting for live joints..."
                        : status == "no_sample" ? "Reachy is not publishing fresh joint data. Retrying."
                        : _ethernetRestartUsed ? "Reachy's controller is still missing after one restart. Check the SDK service."
                        : "Reachy's controller is missing. Waiting for SDK recovery...";
                    if (status == "controller_missing" && !_ethernetRestartUsed &&
                        Time.unscaledTime >= _nextEthernetRestartAt &&
                        !(_client != null && _client.IsConnected && _connectedMode == ReachyControlMode.RealRobot))
                    {
                        _pendingEthernetRecovery = "sdk";
                        _nextEthernetDiscoveryAt = 0f;
                    }
                    return;
                case "key_mismatch":
                    _ethernetDiscoveryStatus = "Reachy's SSH host key changed. Verify the robot's identity before updating the trusted key.";
                    return;
                case "ambiguous":
                case "trusted_robot_unavailable":
                    _ethernetDiscoveryStatus = status == "ambiguous" ? "Multiple trusted Reachys are reachable. Select the intended robot manually."
                        : "The previously verified Reachy is not answering over this Ethernet link.";
                    return;
            }

            if (status != "ready" && check.Discovery?.Ambiguous != true)
            {
                string detail = status == "untrusted" ? "No trusted SSH key."
                    : status == "auth_failed" ? "SSH credentials were rejected."
                    : status == "ssh_unavailable" ? "SSH is not ready."
                    : "SSH live-joint check is unavailable.";
                _ethernetDiscoveryStatus += " " + detail + " Automatic SDK recovery unavailable.";
            }
            if (sdkReady) ApplyEthernetRobot(check.Discovery);
            else if (status == "ssh_unavailable" && !sdkReady)
                _ethernetDiscoveryStatus = "Ethernet link is up, but Reachy's operating system is not answering over SSH or SDK. " +
                    "If this persists after boot, check Reachy's computer or restart it with its power button.";
        }

        private void RememberEthernetRobot(EthernetSshCheck robot)
        {
            if (robot == null || string.IsNullOrEmpty(robot.identity)) return;
            var previous = _ethernetRobotHint;
            var hint = new EthernetSshCheck
            {
                identity = robot.identity,
                hostname = string.IsNullOrEmpty(robot.hostname) ? previous?.hostname : robot.hostname,
                linkLocal = string.IsNullOrEmpty(robot.linkLocal) ? previous?.linkLocal : robot.linkLocal,
                addresses = robot.addresses?.Length > 0 ? robot.addresses : previous?.addresses
            };
            string json = JsonUtility.ToJson(hint);
            if (previous != null && json == JsonUtility.ToJson(previous)) return;
            _ethernetRobotHint = hint;
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "ReachyControlApp");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "ethernet_known_hosts.hint.json"), json);
            }
            catch (Exception) { /* The current session can still use the cache. */ }
        }

        private static EthernetSshCheck RunEthernetSshCheck(string script, string[] interpreters,
            EthernetSshRequest request, CancellationToken cancellation)
        {
            if (!File.Exists(script)) return new EthernetSshCheck { status = "dependency_missing" };
            string workingDirectory = Path.GetDirectoryName(script);
            foreach (string interpreter in interpreters)
            {
                cancellation.ThrowIfCancellationRequested();
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = interpreter,
                    Arguments = "-B \"" + script + "\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                ConfigureBundledPortablePythonEnvironment(startInfo, interpreter, workingDirectory);
                try
                {
                    using (var process = System.Diagnostics.Process.Start(startInfo))
                    {
                        if (process == null) continue;
                        try
                        {
                            Task<string> output = process.StandardOutput.ReadToEndAsync();
                            _ = process.StandardError.ReadToEndAsync();
                            process.StandardInput.Write(JsonUtility.ToJson(request));
                            process.StandardInput.Close();
                            while (!process.WaitForExit(100)) cancellation.ThrowIfCancellationRequested();
                            cancellation.ThrowIfCancellationRequested();
                            if (process.ExitCode != 0) continue;
                            var result = JsonUtility.FromJson<EthernetSshCheck>(output.GetAwaiter().GetResult());
                            if (result != null && result.status != "dependency_missing") return result;
                        }
                        finally
                        {
                            if (!process.HasExited) process.Kill();
                        }
                    }
                }
                catch (System.ComponentModel.Win32Exception) { /* Try another installed Python. */ }
                catch (IOException) { return new EthernetSshCheck { status = "inconclusive" }; }
            }
            return new EthernetSshCheck { status = "dependency_missing" };
        }

        private void ApplyEthernetRobot(ReachyEthernetDiscovery.Result result)
        {
            if (ReachyEthernetDiscovery.FindWiredSource(result.Host) == null) return;
            bool alreadyConnected = _client != null && _client.IsConnected &&
                _connectedMode == ReachyControlMode.RealRobot &&
                string.Equals(_client.ConnectedHost, result.Host, StringComparison.OrdinalIgnoreCase) &&
                _client.ConnectedPort == result.Port;
            if (alreadyConnected && IsConnectedToSelectedEthernet()) return;
            robotHost = result.Host;
            robotPort = result.Port;
            _ethernetSelectedHost = result.Host;
            _ethernetSelectedPort = result.Port;
            mode = ReachyControlMode.RealRobot;
            _initialAutoConnectAttempted = true;
            _autoReconnectMode = ReachyControlMode.RealRobot;
            // Camera, microphone SSH, speaker and mobile base already derive their host from robotHost.
            LogConnectionEvent(ReachyControlMode.RealRobot, "ethernet-discovered", result.Message);
            if (alreadyConnected) return;

            StopEthernetMotion("Switching to Ethernet robot.");
            TryStartConnectAttempt("Ethernet auto-connect", ReachyControlMode.RealRobot,
                Mathf.Max(1f, reconnectCooldownSeconds), ensureOneUiFrameBeforeConnect: true,
                ethernetEndpoint: new Endpoint { Host = result.Host, Port = result.Port });
        }

        private void StopEthernetMotion(string reason)
        {
            StopActedSequence(updateStatus: false, reason: reason, stopLoopingAnimation: false);
            StopLoopingAnimation(updateStatus: false, reason: reason);
            StopAnimationCreatorPlayback(updateStatus: false, reason: reason);
            StopVoiceMotionSequence(updateStatus: false, reason: reason);
            StopVoiceShowMovementSequence(updateStatus: false, reason: reason);
            StopVoiceHelloReturnTimer(updateStatus: false, reason: reason);
            ResetAnimationCreatorLivePreviewState();
        }

        private void DrawEthernetDiscoveryControls()
        {
            bool previous = autoDetectEthernetRobot;
            autoDetectEthernetRobot = GUILayout.Toggle(autoDetectEthernetRobot,
                "Automatically use Reachy on Ethernet");
            if (previous != autoDetectEthernetRobot)
            {
                if (autoDetectEthernetRobot)
                {
                    _nextEthernetDiscoveryAt = 0f;
                    _ethernetLinkSince = Time.unscaledTime;
                    _manualDisconnect = false;
                    _ethernetSelectedHost = string.Empty;
                }
                else
                {
                    _ethernetDiscoveryCancellation?.Cancel();
                    _ethernetDiscoveryStatus = "Automatic Ethernet discovery disabled.";
                }
            }
            GUILayout.Label(_ethernetDiscoveryStatus);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && _ethernetDiscoveryTask == null && !_isConnectAttemptInProgress;
            if (GUILayout.Button("Find Reachy on Ethernet"))
            {
                autoDetectEthernetRobot = true;
                _manualDisconnect = false;
                _nextEthernetDiscoveryAt = 0f;
                _ethernetLinkSince = Time.unscaledTime;
                _ethernetSelectedHost = string.Empty;
            }
            GUI.enabled = wasEnabled;
        }

        private void StopEthernetDiscovery()
        {
            _ethernetDiscoveryStopped = true;
            CancellationTokenSource cancellation = _ethernetDiscoveryCancellation;
            Task<EthernetCheck> task = _ethernetDiscoveryTask;
            _ethernetDiscoveryCancellation = null;
            _ethernetDiscoveryTask = null;
            if (cancellation == null) return;
            cancellation.Cancel();
            if (task == null) cancellation.Dispose();
            else _ = task.ContinueWith(completed =>
            {
                if (completed.IsFaulted) { var ignored = completed.Exception; }
                cancellation.Dispose();
            }, TaskScheduler.Default);
        }
    }
}
