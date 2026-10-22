using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Reachy.ControlApp
{
    public partial class ReachyRuntimeControlUI
    {
        [Header("Ethernet discovery")]
        [SerializeField] private bool autoDetectEthernetRobot = true;
        [SerializeField] private float ethernetDiscoveryIntervalSeconds = 15f;

        private Task<ReachyEthernetDiscovery.Result> _ethernetDiscoveryTask;
        private CancellationTokenSource _ethernetDiscoveryCancellation;
        private float _nextEthernetDiscoveryAt;
        private bool _initialEthernetDiscoveryCompleted;
        private bool _ethernetDiscoveryStopped;
        private string _ethernetDiscoveryStatus = "Ethernet discovery starts automatically in Play mode.";
        private string _ethernetSelectedHost = string.Empty;

        private bool IsInitialEthernetDiscoveryPending()
        {
            return autoDetectEthernetRobot && !_manualDisconnect && !_initialEthernetDiscoveryCompleted;
        }

        private void UpdateEthernetDiscovery()
        {
            if (_ethernetDiscoveryStopped) return;
            if (_ethernetDiscoveryTask != null && _ethernetDiscoveryTask.IsCompleted)
            {
                // Applying the result can start a connection; defer until the current attempt finishes.
                if (_isConnectAttemptInProgress) return;
                Task<ReachyEthernetDiscovery.Result> completed = _ethernetDiscoveryTask;
                _ethernetDiscoveryTask = null;
                _ethernetDiscoveryCancellation.Dispose();
                _ethernetDiscoveryCancellation = null;
                _initialEthernetDiscoveryCompleted = true;
                _nextEthernetDiscoveryAt = Time.unscaledTime + Mathf.Max(5f, ethernetDiscoveryIntervalSeconds);
                try
                {
                    ReachyEthernetDiscovery.Result result = completed.GetAwaiter().GetResult();
                    _ethernetDiscoveryStatus = result.Message;
                    if (autoDetectEthernetRobot && !_manualDisconnect && result.Found)
                    {
                        ApplyEthernetRobot(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    _ethernetDiscoveryStatus = autoDetectEthernetRobot
                        ? "Ethernet search timed out. No verified Reachy endpoint; discovery retries automatically."
                        : "Automatic Ethernet discovery disabled.";
                }
                catch (Exception exception) { _ethernetDiscoveryStatus = "Ethernet discovery failed: " + exception.Message; }
            }

            if (!autoDetectEthernetRobot || _manualDisconnect || _isConnectAttemptInProgress ||
                _ethernetDiscoveryTask != null || Time.unscaledTime < _nextEthernetDiscoveryAt) return;
            if (_client != null && _client.IsConnected && _connectedMode == ReachyControlMode.RealRobot &&
                string.Equals(_client.ConnectedHost, _ethernetSelectedHost, StringComparison.OrdinalIgnoreCase)) return;

            var endpoints = BuildConnectionCandidates(ReachyControlMode.RealRobot);
            string[] hosts = endpoints.Select(endpoint => endpoint.Host).Distinct().ToArray();
            int[] ports = endpoints.Select(endpoint => endpoint.Port).Concat(new[] { 50055, 3972 }).Distinct().ToArray();
            _ethernetDiscoveryStatus = "Searching Ethernet for Reachy...";
            _ethernetDiscoveryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            CancellationToken cancellation = _ethernetDiscoveryCancellation.Token;
            _ethernetDiscoveryTask = Task.Run(() => ReachyEthernetDiscovery.DiscoverAsync(hosts, ports, cancellation));
        }

        private void ApplyEthernetRobot(ReachyEthernetDiscovery.Result result)
        {
            bool alreadyConnected = _client != null && _client.IsConnected &&
                _connectedMode == ReachyControlMode.RealRobot &&
                string.Equals(_client.ConnectedHost, result.Host, StringComparison.OrdinalIgnoreCase) &&
                _client.ConnectedPort == result.Port;
            robotHost = result.Host;
            robotPort = result.Port;
            _ethernetSelectedHost = result.Host;
            mode = ReachyControlMode.RealRobot;
            _initialAutoConnectAttempted = true;
            _autoReconnectMode = ReachyControlMode.RealRobot;
            // Camera, microphone SSH, speaker and mobile base already derive their host from robotHost.
            LogConnectionEvent(ReachyControlMode.RealRobot, "ethernet-discovered", result.Message);
            if (alreadyConnected) return;

            StopActedSequence(updateStatus: false, reason: "Switching to Ethernet robot.", stopLoopingAnimation: false);
            StopLoopingAnimation(updateStatus: false, reason: "Switching to Ethernet robot.");
            StopAnimationCreatorPlayback(updateStatus: false, reason: "Switching to Ethernet robot.");
            StopVoiceMotionSequence(updateStatus: false, reason: "Switching to Ethernet robot.");
            StopVoiceShowMovementSequence(updateStatus: false, reason: "Switching to Ethernet robot.");
            StopVoiceHelloReturnTimer(updateStatus: false, reason: "Switching to Ethernet robot.");
            ResetAnimationCreatorLivePreviewState();
            TryStartConnectAttempt("Ethernet auto-connect", ReachyControlMode.RealRobot,
                Mathf.Max(1f, reconnectCooldownSeconds), ensureOneUiFrameBeforeConnect: true);
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
                _ethernetSelectedHost = string.Empty;
            }
            GUI.enabled = wasEnabled;
        }

        private void StopEthernetDiscovery()
        {
            _ethernetDiscoveryStopped = true;
            CancellationTokenSource cancellation = _ethernetDiscoveryCancellation;
            Task<ReachyEthernetDiscovery.Result> task = _ethernetDiscoveryTask;
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
