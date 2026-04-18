using System;
using System.Collections.Generic;

using UnityEngine;

namespace Reachy.ControlApp
{
    internal sealed class ReachyAnimationCreator : IDisposable
    {
        private sealed class JointBinding
        {
            public string Name;
            public global::Reachy.Motor Motor;
            public Transform Transform;
            public ArticulationBody Articulation;
            public float Offset;
            public bool IsDirect;
            public float MinDegrees;
            public float MaxDegrees;
        }

        private const string SourceRootObjectName = "Reachy";
        private const float JointDragSensitivity = 0.35f;
        private static readonly Color SelectionHighlightColor = new Color(1f, 0.94f, 0.35f, 0.85f);
        private static readonly Color SelectionEmissionColor = new Color(0.18f, 0.12f, 0.01f, 1f);

        private readonly Dictionary<string, JointBinding> _jointBindings =
            new Dictionary<string, JointBinding>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Collider, string> _colliderToJoint =
            new Dictionary<Collider, string>();
        private readonly Dictionary<string, List<Renderer>> _jointToRenderers =
            new Dictionary<string, List<Renderer>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Transform, string> _transformToJoint =
            new Dictionary<Transform, string>();
        private readonly Dictionary<string, float> _currentPose =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Renderer> _highlightedRenderers = new List<Renderer>();
        private readonly List<string> _jointOrder = new List<string>();
        private readonly MaterialPropertyBlock _highlightPropertyBlock = new MaterialPropertyBlock();

        private GameObject _sourceRoot;
        private global::Reachy.ReachyController _sourceController;
        private Transform _headTransform;
        private Rect _screenRect;
        private Vector3 _neckDegrees;
        private bool _draggingJoint;
        private Vector2 _dragStartMouseGuiPosition;
        private float _dragStartDegrees;
        private bool _selectionHighlightVisible;

        public bool IsReady => _sourceRoot != null;
        public Texture PreviewTexture => null;
        public string Status { get; private set; } =
            "Use the scene Reachy in the middle. Click joints there after enabling pose capture.";
        public string SelectedJointName { get; private set; } = string.Empty;
        public IReadOnlyList<string> JointNames => _jointOrder;

        public void Dispose()
        {
            _selectionHighlightVisible = false;
            ClearJointSelectionHighlight();
            _sourceRoot = null;
            _sourceController = null;
            _headTransform = null;
            _jointBindings.Clear();
            _colliderToJoint.Clear();
            _jointToRenderers.Clear();
            _transformToJoint.Clear();
            _currentPose.Clear();
            _highlightedRenderers.Clear();
            _jointOrder.Clear();
            SelectedJointName = string.Empty;
            Status = "Animation creator helper disposed.";
        }

        public bool EnsureInitialized(out string message)
        {
            if (IsReady)
            {
                message = "Animation creator is bound to the scene Reachy.";
                return true;
            }

            _sourceRoot = GameObject.Find(SourceRootObjectName);
            if (_sourceRoot == null)
            {
                message = $"Could not find '{SourceRootObjectName}' in the active scene.";
                Status = message;
                return false;
            }

            _sourceController = _sourceRoot.GetComponent<global::Reachy.ReachyController>();
            _headTransform = _sourceController != null && _sourceController.head != null
                ? _sourceController.head.transform
                : null;

            BuildBindings();
            BuildColliderMappings();
            BuildRendererMappings();
            InitializePoseFromScene();
            ApplyJointSelectionHighlight();

            message = "Animation creator is using the original scene Reachy.";
            Status = "Using the original scene Reachy. Create a pose, then click and drag joints in the center scene.";
            return true;
        }

        public void SetScreenRect(Rect screenRect)
        {
            _screenRect = screenRect;
        }

        public void SetSelectionHighlightVisible(bool visible)
        {
            if (_selectionHighlightVisible == visible)
            {
                return;
            }

            _selectionHighlightVisible = visible;
            ApplyJointSelectionHighlight();
        }

        public void UpdateInteraction(bool allowPoseEditing, bool mirrorLimbEdits)
        {
            if (!IsReady || _screenRect.width <= 1f || _screenRect.height <= 1f)
            {
                return;
            }

            Vector2 mouseGuiPosition = GetMouseGuiPosition();
            bool pointerInsideActiveArea = _screenRect.Contains(mouseGuiPosition);
            if (!_draggingJoint && !pointerInsideActiveArea)
            {
                return;
            }

            HandleJointDragging(allowPoseEditing, mirrorLimbEdits, pointerInsideActiveArea, mouseGuiPosition);
        }

        public Dictionary<string, float> CapturePose()
        {
            RefreshCurrentPoseFromScene();
            return new Dictionary<string, float>(_currentPose, StringComparer.OrdinalIgnoreCase);
        }

        public bool SetPose(IReadOnlyDictionary<string, float> pose)
        {
            if (pose == null || pose.Count == 0 || !IsReady)
            {
                return false;
            }

            bool appliedAny = false;
            for (int i = 0; i < _jointOrder.Count; i++)
            {
                string jointName = _jointOrder[i];
                if (pose.TryGetValue(jointName, out float degrees))
                {
                    appliedAny |= SetJointTarget(jointName, degrees);
                }
            }

            return appliedAny;
        }

        public bool SetJointTarget(string jointName, float degrees)
        {
            if (string.IsNullOrWhiteSpace(jointName))
            {
                return false;
            }

            if (TrySetNeckJointTarget(jointName, degrees))
            {
                return true;
            }

            if (!_jointBindings.TryGetValue(jointName, out JointBinding binding) ||
                binding == null)
            {
                return false;
            }

            float clamped = Mathf.Clamp(degrees, binding.MinDegrees, binding.MaxDegrees);
            float articulationDegrees = ToArticulationDegrees(clamped, binding.Offset, binding.IsDirect);

            if (binding.Motor != null)
            {
                binding.Motor.targetPosition = articulationDegrees;
                binding.Motor.isCompliant = false;
            }

            if (binding.Articulation != null)
            {
                ArticulationDrive drive = binding.Articulation.xDrive;
                drive.target = articulationDegrees;
                binding.Articulation.xDrive = drive;
            }

            _currentPose[jointName] = clamped;
            return true;
        }

        public bool SetJointTargetFromEditor(string jointName, float degrees, bool mirrorLimbEdits)
        {
            bool appliedAny = SetJointTarget(jointName, degrees);
            if (mirrorLimbEdits)
            {
                appliedAny |= ApplyMirroredJointTarget(jointName, degrees);
            }

            return appliedAny;
        }

        public bool TryGetJointTarget(string jointName, out float degrees)
        {
            RefreshCurrentPoseFromScene();
            if (_currentPose.TryGetValue(jointName, out degrees))
            {
                return true;
            }

            degrees = 0f;
            return false;
        }

        public bool TryGetJointRange(string jointName, out float minDegrees, out float maxDegrees)
        {
            if (_jointBindings.TryGetValue(jointName, out JointBinding binding) && binding != null)
            {
                minDegrees = binding.MinDegrees;
                maxDegrees = binding.MaxDegrees;
                return true;
            }

            GetFallbackJointRange(jointName, out minDegrees, out maxDegrees);
            return !string.IsNullOrWhiteSpace(jointName);
        }

        public bool SelectJoint(string jointName)
        {
            if (string.IsNullOrWhiteSpace(jointName))
            {
                SelectedJointName = string.Empty;
                ApplyJointSelectionHighlight();
                return false;
            }

            if (_jointBindings.ContainsKey(jointName) || IsNeckJoint(jointName))
            {
                SelectedJointName = jointName;
                ApplyJointSelectionHighlight();
                Status = $"Selected {GetDisplayName(jointName)} on the scene Reachy.";
                return true;
            }

            return false;
        }

        public static string GetDisplayName(string jointName)
        {
            string trimmed = (jointName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "Joint";
            }

            if (trimmed.StartsWith("l_", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "left " + trimmed.Substring(2);
            }
            else if (trimmed.StartsWith("r_", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "right " + trimmed.Substring(2);
            }

            string[] tokens = trimmed.Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Length <= 0)
                {
                    continue;
                }

                string lower = tokens[i].ToLowerInvariant();
                tokens[i] = char.ToUpperInvariant(lower[0]) + lower.Substring(1);
            }

            return string.Join(" ", tokens);
        }

        private void BuildBindings()
        {
            _jointBindings.Clear();
            _jointOrder.Clear();
            _transformToJoint.Clear();
            _currentPose.Clear();

            if (_sourceController == null || _sourceController.motors == null)
            {
                return;
            }

            for (int i = 0; i < _sourceController.motors.Length; i++)
            {
                global::Reachy.Motor motor = _sourceController.motors[i];
                if (motor == null || string.IsNullOrWhiteSpace(motor.name))
                {
                    continue;
                }

                string jointName = motor.name.Trim();
                ArticulationBody articulation = motor.gameObject != null
                    ? motor.gameObject.GetComponent<ArticulationBody>()
                    : null;

                GetJointRange(articulation, motor.offset, motor.isDirect, jointName, out float minDegrees, out float maxDegrees);

                _jointBindings[jointName] = new JointBinding
                {
                    Name = jointName,
                    Motor = motor,
                    Transform = motor.gameObject != null ? motor.gameObject.transform : null,
                    Articulation = articulation,
                    Offset = motor.offset,
                    IsDirect = motor.isDirect,
                    MinDegrees = minDegrees,
                    MaxDegrees = maxDegrees
                };

                if (!_jointOrder.Contains(jointName))
                {
                    _jointOrder.Add(jointName);
                }

                if (motor.gameObject != null)
                {
                    _transformToJoint[motor.gameObject.transform] = jointName;
                }
            }

            AddNeckJoint("neck_roll", -60f, 60f);
            AddNeckJoint("neck_pitch", -35f, 35f);
            AddNeckJoint("neck_yaw", -20f, 20f);
        }

        private void BuildColliderMappings()
        {
            _colliderToJoint.Clear();
            Collider[] colliders = _sourceRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                string jointName = ResolveJointNameForTransform(collider.transform);
                if (string.IsNullOrWhiteSpace(jointName))
                {
                    continue;
                }

                _colliderToJoint[collider] = jointName;
            }
        }

        private void BuildRendererMappings()
        {
            _jointToRenderers.Clear();
            if (_sourceRoot == null)
            {
                return;
            }

            Renderer[] renderers = _sourceRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Transform rendererTransform = renderer.transform;
                if (_headTransform != null &&
                    (rendererTransform == _headTransform || rendererTransform.IsChildOf(_headTransform)))
                {
                    AddRendererMapping("neck_roll", renderer);
                    AddRendererMapping("neck_pitch", renderer);
                    AddRendererMapping("neck_yaw", renderer);
                    continue;
                }

                string jointName = ResolveJointNameForTransform(rendererTransform);
                if (string.IsNullOrWhiteSpace(jointName))
                {
                    continue;
                }

                AddRendererMapping(jointName, renderer);
            }
        }

        private void AddRendererMapping(string jointName, Renderer renderer)
        {
            if (string.IsNullOrWhiteSpace(jointName) || renderer == null)
            {
                return;
            }

            if (!_jointToRenderers.TryGetValue(jointName, out List<Renderer> renderers))
            {
                renderers = new List<Renderer>();
                _jointToRenderers[jointName] = renderers;
            }

            if (!renderers.Contains(renderer))
            {
                renderers.Add(renderer);
            }
        }

        private void InitializePoseFromScene()
        {
            RefreshCurrentPoseFromScene();
            for (int i = 0; i < _jointOrder.Count; i++)
            {
                string jointName = _jointOrder[i];
                if (_currentPose.TryGetValue(jointName, out float degrees))
                {
                    SetJointTarget(jointName, degrees);
                }
            }
        }

        private void RefreshCurrentPoseFromScene()
        {
            for (int i = 0; i < _jointOrder.Count; i++)
            {
                string jointName = _jointOrder[i];
                if (IsNeckJoint(jointName))
                {
                    continue;
                }

                if (!_jointBindings.TryGetValue(jointName, out JointBinding binding) || binding == null)
                {
                    continue;
                }

                float internalDegrees = 0f;
                if (binding.Articulation != null)
                {
                    internalDegrees = binding.Articulation.xDrive.target;
                }
                else if (binding.Motor != null)
                {
                    internalDegrees = binding.Motor.targetPosition;
                }

                _currentPose[jointName] = ToUserDegrees(internalDegrees, binding.Offset, binding.IsDirect);
            }

            _currentPose["neck_roll"] = _neckDegrees.x;
            _currentPose["neck_pitch"] = _neckDegrees.y;
            _currentPose["neck_yaw"] = _neckDegrees.z;
        }

        private void HandleJointDragging(
            bool allowPoseEditing,
            bool mirrorLimbEdits,
            bool pointerInsideActiveArea,
            Vector2 mouseGuiPosition)
        {
            if (!_draggingJoint && Input.GetMouseButtonDown(0) && pointerInsideActiveArea)
            {
                if (!allowPoseEditing)
                {
                    Status = "Create a new pose first to enable joint editing.";
                    return;
                }

                if (!TryPickJoint(out string jointName))
                {
                    return;
                }

                RefreshCurrentPoseFromScene();
                SelectedJointName = jointName;
                ApplyJointSelectionHighlight();
                _draggingJoint = true;
                _dragStartMouseGuiPosition = mouseGuiPosition;
                _dragStartDegrees = _currentPose.TryGetValue(jointName, out float currentDegrees) ? currentDegrees : 0f;
                Status = $"Editing {GetDisplayName(jointName)} on the scene Reachy.";
            }

            if (_draggingJoint && Input.GetMouseButton(0) && !string.IsNullOrWhiteSpace(SelectedJointName))
            {
                Vector2 delta = mouseGuiPosition - _dragStartMouseGuiPosition;
                float targetDegrees = _dragStartDegrees + ComputeDragDegrees(SelectedJointName, delta);
                SetJointTargetFromEditor(SelectedJointName, targetDegrees, mirrorLimbEdits);
            }

            if (_draggingJoint && Input.GetMouseButtonUp(0))
            {
                _draggingJoint = false;
                if (!string.IsNullOrWhiteSpace(SelectedJointName))
                {
                    Status = $"Adjusted {GetDisplayName(SelectedJointName)}.";
                }
            }
        }

        private bool TryPickJoint(out string jointName)
        {
            jointName = string.Empty;
            Camera interactionCamera = GetInteractionCamera();
            if (interactionCamera == null)
            {
                Status = "No active scene camera was found for joint picking.";
                return false;
            }

            Ray ray = interactionCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (_colliderToJoint.TryGetValue(hits[i].collider, out string mappedJointName) &&
                    !string.IsNullOrWhiteSpace(mappedJointName))
                {
                    jointName = mappedJointName;
                    return true;
                }
            }

            return false;
        }

        private Camera GetInteractionCamera()
        {
            if (Camera.main != null && Camera.main.enabled && Camera.main.gameObject.activeInHierarchy)
            {
                return Camera.main;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera != null && camera.enabled && camera.gameObject.activeInHierarchy)
                {
                    return camera;
                }
            }

            return null;
        }

        private string ResolveJointNameForTransform(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                if (_transformToJoint.TryGetValue(current, out string jointName))
                {
                    return jointName;
                }

                if (_headTransform != null && (current == _headTransform || current.IsChildOf(_headTransform)))
                {
                    return "neck_yaw";
                }

                current = current.parent;
            }

            return string.Empty;
        }

        private bool ApplyMirroredJointTarget(string jointName, float sourceDegrees)
        {
            if (!TryGetMirroredJointName(jointName, out string mirroredJointName) ||
                !_jointBindings.ContainsKey(mirroredJointName))
            {
                return false;
            }

            return SetJointTarget(mirroredJointName, GetMirroredJointDegrees(jointName, sourceDegrees));
        }

        public static bool TryGetMirroredJointName(string jointName, out string mirroredJointName)
        {
            string trimmed = (jointName ?? string.Empty).Trim();
            if (trimmed.StartsWith("l_", StringComparison.OrdinalIgnoreCase))
            {
                mirroredJointName = "r_" + trimmed.Substring(2);
                return true;
            }

            if (trimmed.StartsWith("r_", StringComparison.OrdinalIgnoreCase))
            {
                mirroredJointName = "l_" + trimmed.Substring(2);
                return true;
            }

            mirroredJointName = string.Empty;
            return false;
        }

        private static float GetMirroredJointDegrees(string jointName, float sourceDegrees)
        {
            string trimmed = (jointName ?? string.Empty).Trim();
            if (trimmed.IndexOf("roll", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("yaw", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return -sourceDegrees;
            }

            return sourceDegrees;
        }

        private void ApplyJointSelectionHighlight()
        {
            ClearJointSelectionHighlight();
            if (!_selectionHighlightVisible || string.IsNullOrWhiteSpace(SelectedJointName))
            {
                return;
            }

            if (!_jointToRenderers.TryGetValue(SelectedJointName, out List<Renderer> renderers) ||
                renderers == null)
            {
                return;
            }

            _highlightPropertyBlock.Clear();
            _highlightPropertyBlock.SetColor("_BaseColor", SelectionHighlightColor);
            _highlightPropertyBlock.SetColor("_Color", SelectionHighlightColor);
            _highlightPropertyBlock.SetColor("_EmissionColor", SelectionEmissionColor);
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.SetPropertyBlock(_highlightPropertyBlock);
                _highlightedRenderers.Add(renderer);
            }
        }

        private void ClearJointSelectionHighlight()
        {
            if (_highlightedRenderers.Count <= 0)
            {
                return;
            }

            _highlightPropertyBlock.Clear();
            for (int i = 0; i < _highlightedRenderers.Count; i++)
            {
                Renderer renderer = _highlightedRenderers[i];
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(_highlightPropertyBlock);
                }
            }

            _highlightedRenderers.Clear();
        }

        private bool TrySetNeckJointTarget(string jointName, float degrees)
        {
            if (!IsNeckJoint(jointName))
            {
                return false;
            }

            GetFallbackJointRange(jointName, out float minDegrees, out float maxDegrees);
            float clamped = Mathf.Clamp(degrees, minDegrees, maxDegrees);
            switch (jointName)
            {
                case "neck_roll":
                    _neckDegrees.x = clamped;
                    break;
                case "neck_pitch":
                    _neckDegrees.y = clamped;
                    break;
                case "neck_yaw":
                    _neckDegrees.z = clamped;
                    break;
            }

            Quaternion neckRotation =
                Quaternion.Euler(Vector3.forward * -_neckDegrees.z) *
                Quaternion.Euler(Vector3.up * -_neckDegrees.x) *
                Quaternion.Euler(Vector3.right * _neckDegrees.y);

            if (_sourceController != null)
            {
                _sourceController.HandleHeadOrientation(neckRotation);
            }
            else if (_headTransform != null)
            {
                _headTransform.localRotation = neckRotation;
            }

            _currentPose[jointName] = clamped;
            return true;
        }

        private void AddNeckJoint(string jointName, float minDegrees, float maxDegrees)
        {
            if (!_jointOrder.Contains(jointName))
            {
                _jointOrder.Add(jointName);
            }

            _jointBindings[jointName] = new JointBinding
            {
                Name = jointName,
                Motor = null,
                Transform = _headTransform,
                Articulation = null,
                Offset = 0f,
                IsDirect = true,
                MinDegrees = minDegrees,
                MaxDegrees = maxDegrees
            };

            _currentPose[jointName] = 0f;
        }

        private void GetJointRange(
            ArticulationBody articulation,
            float offset,
            bool isDirect,
            string jointName,
            out float minDegrees,
            out float maxDegrees)
        {
            if (articulation == null)
            {
                GetFallbackJointRange(jointName, out minDegrees, out maxDegrees);
                return;
            }

            ArticulationDrive drive = articulation.xDrive;
            float convertedMin = ToUserDegrees(drive.lowerLimit, offset, isDirect);
            float convertedMax = ToUserDegrees(drive.upperLimit, offset, isDirect);
            minDegrees = Mathf.Min(convertedMin, convertedMax);
            maxDegrees = Mathf.Max(convertedMin, convertedMax);

            if (Mathf.Abs(maxDegrees - minDegrees) < 0.01f)
            {
                GetFallbackJointRange(jointName, out minDegrees, out maxDegrees);
            }
        }

        private static bool IsNeckJoint(string jointName)
        {
            return string.Equals(jointName, "neck_roll", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(jointName, "neck_pitch", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(jointName, "neck_yaw", StringComparison.OrdinalIgnoreCase);
        }

        private static float ToArticulationDegrees(float userDegrees, float offset, bool isDirect)
        {
            float articulationDegrees = userDegrees + offset;
            if (!isDirect)
            {
                articulationDegrees *= -1f;
            }

            return articulationDegrees;
        }

        private static float ToUserDegrees(float articulationDegrees, float offset, bool isDirect)
        {
            float userDegrees = isDirect ? articulationDegrees : -articulationDegrees;
            return userDegrees - offset;
        }

        private static Vector2 GetMouseGuiPosition()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        }

        private static float ComputeDragDegrees(string jointName, Vector2 mouseDelta)
        {
            if (string.IsNullOrWhiteSpace(jointName))
            {
                return 0f;
            }

            if (jointName.IndexOf("pitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                jointName.IndexOf("gripper", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return -mouseDelta.y * JointDragSensitivity;
            }

            return mouseDelta.x * JointDragSensitivity;
        }

        private static void GetFallbackJointRange(string jointName, out float minDegrees, out float maxDegrees)
        {
            switch (jointName)
            {
                case "l_shoulder_pitch":
                case "r_shoulder_pitch":
                    minDegrees = -95f;
                    maxDegrees = 20f;
                    break;
                case "l_shoulder_roll":
                    minDegrees = -20f;
                    maxDegrees = 90f;
                    break;
                case "r_shoulder_roll":
                    minDegrees = -90f;
                    maxDegrees = 20f;
                    break;
                case "l_arm_yaw":
                case "r_arm_yaw":
                    minDegrees = -60f;
                    maxDegrees = 60f;
                    break;
                case "l_elbow_pitch":
                case "r_elbow_pitch":
                    minDegrees = -130f;
                    maxDegrees = 5f;
                    break;
                case "neck_roll":
                    minDegrees = -60f;
                    maxDegrees = 60f;
                    break;
                case "neck_pitch":
                    minDegrees = -35f;
                    maxDegrees = 35f;
                    break;
                case "neck_yaw":
                    minDegrees = -20f;
                    maxDegrees = 20f;
                    break;
                case "l_wrist_roll":
                case "r_wrist_roll":
                    minDegrees = -90f;
                    maxDegrees = 90f;
                    break;
                case "l_gripper":
                case "r_gripper":
                    minDegrees = -30f;
                    maxDegrees = 30f;
                    break;
                default:
                    minDegrees = -180f;
                    maxDegrees = 180f;
                    break;
            }
        }
    }
}
