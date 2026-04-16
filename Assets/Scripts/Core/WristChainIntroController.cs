using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using VRCombat.Player;

namespace VRCombat.Core
{
    [DisallowMultipleComponent]
    public sealed class WristChainIntroController : MonoBehaviour
    {
        const float ReleaseHeight = 0.18f;
        const float HapticsPulseInterval = 0.05f;
        const float AnchorForwardOffset = 0.015f;
        const float AnchorSideOffset = 0.025f;
        const float AnchorHeightOffset = 0.02f;
        const float ChainNominalLength = 0.92f;

        readonly List<GameObject> m_RuntimeObjects = new List<GameObject>();

        Transform m_TrackingSpace;
        Transform m_LeftHand;
        Transform m_RightHand;
        Transform m_LeftController;
        Transform m_RightController;
        Transform m_LeftActivePose;
        Transform m_RightActivePose;
        Transform m_LeftAnchor;
        Transform m_RightAnchor;
        Transform m_LeftChainRoot;
        Transform m_RightChainRoot;
        Transform m_LeftNailRoot;
        Transform m_RightNailRoot;
        Vector3 m_LeftStartWorldPosition;
        Vector3 m_RightStartWorldPosition;
        Vector3 m_LeftChainBaseScale;
        Vector3 m_RightChainBaseScale;
        Action<PlayerHandSide, Vector3, Quaternion> m_OnWristBroken;
        Action m_OnCompleted;
        Func<Transform, bool, GameObject> m_CreateChainVisual;
        float m_NextLeftPulseTime;
        float m_NextRightPulseTime;
        bool m_LeftBroken;
        bool m_RightBroken;
        bool m_IsActive;

        public bool IsActive => m_IsActive;

        public void Begin(
            Transform headTransform,
            Transform trackingSpaceTransform,
            Transform leftHandTransform,
            Transform rightHandTransform,
            Transform leftControllerTransform,
            Transform rightControllerTransform,
            Func<Transform, bool, GameObject> createChainVisual,
            Action<PlayerHandSide, Vector3, Quaternion> wristBrokenAction,
            Action completedAction)
        {
            Cleanup();
            if (headTransform == null)
                return;

            m_LeftHand = leftHandTransform;
            m_RightHand = rightHandTransform;
            m_LeftController = leftControllerTransform;
            m_RightController = rightControllerTransform;
            m_TrackingSpace = trackingSpaceTransform != null
                ? trackingSpaceTransform
                : headTransform.parent != null
                    ? headTransform.parent
                    : transform;
            m_CreateChainVisual = createChainVisual;
            m_OnWristBroken = wristBrokenAction;
            m_OnCompleted = completedAction;
            m_IsActive = true;
            TryGetCurrentPoseWorld(PlayerHandSide.Left, XRNode.LeftHand, m_LeftHand, m_LeftController, out m_LeftStartWorldPosition, out _, out m_LeftActivePose);
            TryGetCurrentPoseWorld(PlayerHandSide.Right, XRNode.RightHand, m_RightHand, m_RightController, out m_RightStartWorldPosition, out _, out m_RightActivePose);
            m_NextLeftPulseTime = 0f;
            m_NextRightPulseTime = 0f;
            InputSystem.ResumeHaptics();

            var flatForward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;

            var flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;
            if (flatRight.sqrMagnitude < 0.0001f)
                flatRight = Vector3.right;

            var leftWristPosition = m_LeftStartWorldPosition;
            var rightWristPosition = m_RightStartWorldPosition;

            m_LeftAnchor = CreateAnchor(
                "Left Chain Anchor",
                ResolveAnchorPosition(leftWristPosition, headTransform.position.y, flatForward, -flatRight));
            m_RightAnchor = CreateAnchor(
                "Right Chain Anchor",
                ResolveAnchorPosition(rightWristPosition, headTransform.position.y, flatForward, flatRight));

            m_LeftChainRoot = CreateChainVisualRoot("Left Wrist Chain");
            m_RightChainRoot = CreateChainVisualRoot("Right Wrist Chain");
            m_LeftNailRoot = CreateNailVisualRoot("Left Wrist Nail", m_LeftAnchor, flatForward);
            m_RightNailRoot = CreateNailVisualRoot("Right Wrist Nail", m_RightAnchor, flatForward);
        }

        public void Cleanup()
        {
            for (var i = 0; i < m_RuntimeObjects.Count; i++)
            {
                var runtimeObject = m_RuntimeObjects[i];
                if (runtimeObject != null)
                    Destroy(runtimeObject);
            }

            m_RuntimeObjects.Clear();
            m_TrackingSpace = null;
            m_LeftHand = null;
            m_RightHand = null;
            m_LeftController = null;
            m_RightController = null;
            m_LeftActivePose = null;
            m_RightActivePose = null;
            m_LeftAnchor = null;
            m_RightAnchor = null;
            m_LeftChainRoot = null;
            m_RightChainRoot = null;
            m_LeftNailRoot = null;
            m_RightNailRoot = null;
            m_LeftStartWorldPosition = Vector3.zero;
            m_RightStartWorldPosition = Vector3.zero;
            m_LeftChainBaseScale = Vector3.zero;
            m_RightChainBaseScale = Vector3.zero;
            m_OnWristBroken = null;
            m_OnCompleted = null;
            m_CreateChainVisual = null;
            m_NextLeftPulseTime = 0f;
            m_NextRightPulseTime = 0f;
            m_LeftBroken = false;
            m_RightBroken = false;
            m_IsActive = false;
        }

        void Update()
        {
            if (!m_IsActive)
                return;

            UpdateChain(
                PlayerHandSide.Left,
                XRNode.LeftHand,
                m_LeftAnchor,
                m_LeftChainRoot,
                m_LeftNailRoot,
                m_LeftStartWorldPosition,
                m_LeftHand,
                m_LeftController,
                ref m_LeftActivePose,
                ref m_LeftBroken,
                ref m_LeftChainBaseScale,
                ref m_NextLeftPulseTime);

            UpdateChain(
                PlayerHandSide.Right,
                XRNode.RightHand,
                m_RightAnchor,
                m_RightChainRoot,
                m_RightNailRoot,
                m_RightStartWorldPosition,
                m_RightHand,
                m_RightController,
                ref m_RightActivePose,
                ref m_RightBroken,
                ref m_RightChainBaseScale,
                ref m_NextRightPulseTime);

            if (!m_LeftBroken || !m_RightBroken)
                return;

            var completedAction = m_OnCompleted;
            Cleanup();
            completedAction?.Invoke();
        }

        Transform CreateAnchor(string name, Vector3 worldPosition)
        {
            var anchorObject = new GameObject(name);
            anchorObject.transform.SetParent(transform, false);
            anchorObject.transform.position = worldPosition;
            anchorObject.transform.rotation = Quaternion.identity;
            m_RuntimeObjects.Add(anchorObject);
            return anchorObject.transform;
        }

        Transform CreateChainVisualRoot(string name)
        {
            var chainRoot = new GameObject(name);
            chainRoot.transform.SetParent(transform, false);
            m_RuntimeObjects.Add(chainRoot);

            var visualRoot = m_CreateChainVisual?.Invoke(chainRoot.transform, false);
            if (visualRoot == null)
            {
                CreateFallbackChainVisual(chainRoot.transform);
                return chainRoot.transform;
            }

            return chainRoot.transform;
        }

        Transform CreateNailVisualRoot(string name, Transform anchorTransform, Vector3 flatForward)
        {
            if (anchorTransform == null)
                return null;

            var nailRoot = new GameObject(name);
            nailRoot.transform.SetParent(transform, false);
            nailRoot.transform.position = anchorTransform.position;
            nailRoot.transform.rotation = Quaternion.LookRotation(Vector3.down, flatForward);
            m_RuntimeObjects.Add(nailRoot);

            var visualRoot = m_CreateChainVisual?.Invoke(nailRoot.transform, true);
            if (visualRoot == null)
                CreateFallbackNailVisual(nailRoot.transform);

            return nailRoot.transform;
        }

        void UpdateChain(
            PlayerHandSide handSide,
            XRNode xrNode,
            Transform anchorTransform,
            Transform chainVisual,
            Transform nailRoot,
            Vector3 startWorldPosition,
            Transform handTransform,
            Transform controllerTransform,
            ref Transform activePose,
            ref bool broken,
            ref Vector3 chainBaseScale,
            ref float nextPulseTime)
        {
            if (broken || anchorTransform == null)
                return;

            if (!TryGetCurrentPoseWorld(
                    handSide,
                    xrNode,
                    handTransform,
                    controllerTransform,
                    out var wristPosition,
                    out var wristRotation,
                    out var poseTransform))
            {
                poseTransform = activePose;
                if (poseTransform != null)
                {
                    wristPosition = poseTransform.position;
                    wristRotation = poseTransform.rotation;
                }
            }

            if (poseTransform == null && wristPosition == Vector3.zero)
                return;

            activePose = poseTransform;
            var anchorPosition = anchorTransform.position;

            if (chainVisual != null)
                UpdateChainVisual(chainVisual, wristPosition, anchorPosition, ref chainBaseScale);

            if (nailRoot != null)
                nailRoot.position = anchorPosition;

            var upwardTravel = Mathf.Max(0f, wristPosition.y - startWorldPosition.y);
            var progress = Mathf.Clamp01(upwardTravel / ReleaseHeight);
            if (progress > 0.02f && Time.unscaledTime >= nextPulseTime)
            {
                SendHapticsPulse(xrNode, Mathf.Lerp(0.16f, 0.72f, progress), 0.06f);
                nextPulseTime = Time.unscaledTime + HapticsPulseInterval;
            }

            if (upwardTravel < ReleaseHeight)
                return;

            broken = true;
            if (chainVisual != null)
                chainVisual.gameObject.SetActive(false);
            if (nailRoot != null)
                nailRoot.gameObject.SetActive(false);

            SendHapticsPulse(xrNode, 0.95f, 0.14f);
            var spawnForward = Vector3.ProjectOnPlane(wristRotation * Vector3.forward, Vector3.up).normalized;
            if (spawnForward.sqrMagnitude < 0.001f)
                spawnForward = Vector3.forward;

            var spawnRotation = Quaternion.LookRotation(spawnForward, Vector3.up);
            m_OnWristBroken?.Invoke(handSide, wristPosition + spawnForward * 0.06f, spawnRotation);
        }

        Vector3 ResolveAnchorPosition(Vector3 wristPosition, float headHeight, Vector3 flatForward, Vector3 sideOffsetDirection)
        {
            var rayOrigin = wristPosition + Vector3.up * 0.25f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.point
                       + flatForward * AnchorForwardOffset
                       + sideOffsetDirection * AnchorSideOffset
                       + Vector3.up * AnchorHeightOffset;
            }

            var fallbackFloorY = headHeight - 1.3f;
            return new Vector3(
                wristPosition.x,
                fallbackFloorY + AnchorHeightOffset,
                wristPosition.z)
                   + flatForward * AnchorForwardOffset
                   + sideOffsetDirection * AnchorSideOffset;
        }

        bool TryGetCurrentPoseWorld(
            Transform handTransform,
            Transform controllerTransform,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out Transform activeTransform)
        {
            activeTransform = controllerTransform != null ? controllerTransform : handTransform;
            if (activeTransform == null)
            {
                worldPosition = Vector3.zero;
                worldRotation = Quaternion.identity;
                return false;
            }

            worldPosition = activeTransform.position;
            worldRotation = activeTransform.rotation;
            return true;
        }

        bool TryGetCurrentPoseWorld(
            PlayerHandSide handSide,
            XRNode xrNode,
            Transform handTransform,
            Transform controllerTransform,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out Transform activeTransform)
        {
            if (XrPoseWorldUtility.TryResolvePoseWorld(
                    xrNode,
                    m_TrackingSpace != null ? m_TrackingSpace : transform,
                    controllerTransform,
                    handTransform,
                    out worldPosition,
                    out worldRotation,
                    out activeTransform))
            {
                return true;
            }

            return TryGetCurrentPoseWorld(handTransform, controllerTransform, out worldPosition, out worldRotation, out activeTransform);
        }

        static void UpdateChainVisual(Transform chainVisual, Vector3 wristPosition, Vector3 anchorPosition, ref Vector3 chainBaseScale)
        {
            if (chainVisual == null)
                return;

            if (chainBaseScale == Vector3.zero)
                chainBaseScale = chainVisual.localScale;

            var direction = anchorPosition - wristPosition;
            var distance = Mathf.Max(0.08f, direction.magnitude);
            var up = Vector3.up;
            var forward = direction.normalized;
            var cross = Vector3.Cross(forward, up);
            if (cross.sqrMagnitude < 0.0001f)
                up = Vector3.forward;

            chainVisual.position = wristPosition;
            chainVisual.rotation = Quaternion.LookRotation(forward, up);
            chainVisual.localScale = new Vector3(
                chainBaseScale.x,
                chainBaseScale.y,
                chainBaseScale.z * Mathf.Max(0.35f, distance / ChainNominalLength));
        }

        static void SendHapticsPulse(XRNode xrNode, float amplitude, float durationSeconds)
        {
            amplitude = Mathf.Clamp01(amplitude);
            durationSeconds = Mathf.Max(0.01f, durationSeconds);
            if (amplitude <= 0f)
                return;

            var inputSystemController = xrNode == XRNode.RightHand
                ? XRController.rightHand
                : XRController.leftHand;
            if (inputSystemController is XRControllerWithRumble rumbleController)
                rumbleController.SendImpulse(amplitude, durationSeconds);

            var desiredUsageName = xrNode == XRNode.RightHand ? "RightHand" : "LeftHand";
            var inputSystemDevices = InputSystem.devices;
            for (var i = 0; i < inputSystemDevices.Count; i++)
            {
                var device = inputSystemDevices[i];
                if (device is not XRControllerWithRumble rumbleDevice)
                    continue;

                if (!InputSystemDeviceMatchesUsage(device, desiredUsageName))
                    continue;

                rumbleDevice.SendImpulse(amplitude, durationSeconds);
            }

            var xrDevice = InputDevices.GetDeviceAtXRNode(xrNode);
            if (xrDevice.isValid &&
                xrDevice.TryGetHapticCapabilities(out var capabilities) &&
                capabilities.supportsImpulse)
            {
                xrDevice.SendHapticImpulse(0u, amplitude, durationSeconds);
            }
        }

        static bool InputSystemDeviceMatchesUsage(UnityEngine.InputSystem.InputDevice device, string usageName)
        {
            if (device == null || string.IsNullOrWhiteSpace(usageName))
                return false;

            var usages = device.usages;
            foreach (var deviceUsage in usages)
            {
                if (string.Equals(deviceUsage.ToString(), usageName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var descriptor = $"{device.displayName} {device.name} {device.layout}";
            return string.Equals(usageName, "LeftHand", StringComparison.OrdinalIgnoreCase)
                ? descriptor.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0
                : descriptor.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void CreateFallbackChainVisual(Transform parent)
        {
            for (var i = 0; i < 5; i++)
            {
                var link = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                link.name = $"Fallback Chain Link {i + 1}";
                link.transform.SetParent(parent, false);
                link.transform.localPosition = new Vector3(0f, 0f, 0.08f + i * 0.09f);
                link.transform.localScale = Vector3.one * 0.055f;

                var collider = link.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
            }
        }

        static void CreateFallbackNailVisual(Transform parent)
        {
            var nailVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nailVisual.name = "Fallback Chain Nail";
            nailVisual.transform.SetParent(parent, false);
            nailVisual.transform.localPosition = new Vector3(0f, 0f, -0.06f);
            nailVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            nailVisual.transform.localScale = new Vector3(0.018f, 0.06f, 0.018f);

            var collider = nailVisual.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
        }
    }

    static class XrPoseWorldUtility
    {
        const float MinimumLocalPositionSqrMagnitude = 0.000001f;

        static readonly List<XRNodeState> s_NodeStates = new List<XRNodeState>(8);

        public static bool TryResolvePoseWorld(
            XRNode xrNode,
            Transform trackingSpace,
            Transform controllerTransform,
            Transform handTransform,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out Transform activeTransform)
        {
            return TryResolvePoseWorldDetailed(
                xrNode,
                trackingSpace,
                controllerTransform,
                handTransform,
                out worldPosition,
                out _,
                out worldRotation,
                out _,
                out activeTransform);
        }

        public static bool TryResolvePoseWorldDetailed(
            XRNode xrNode,
            Transform trackingSpace,
            Transform controllerTransform,
            Transform handTransform,
            out Vector3 worldPosition,
            out bool hasTrackedPosition,
            out Quaternion worldRotation,
            out bool hasTrackedRotation,
            out Transform activeTransform)
        {
            activeTransform = controllerTransform != null ? controllerTransform : handTransform;
            worldPosition = activeTransform != null ? activeTransform.position : Vector3.zero;
            worldRotation = activeTransform != null ? activeTransform.rotation : Quaternion.identity;
            hasTrackedPosition = false;
            hasTrackedRotation = false;

            if (!TryReadTrackedLocalPose(
                    xrNode,
                    out var localPosition,
                    out hasTrackedPosition,
                    out var localRotation,
                    out hasTrackedRotation))
            {
                return activeTransform != null;
            }

            var resolvedTrackingSpace = ResolveTrackingSpace(trackingSpace, controllerTransform, handTransform);
            if (hasTrackedPosition)
            {
                worldPosition = resolvedTrackingSpace != null
                    ? resolvedTrackingSpace.TransformPoint(localPosition)
                    : localPosition;
            }
            else if (controllerTransform != null)
            {
                worldPosition = controllerTransform.position;
            }
            else if (handTransform != null)
            {
                worldPosition = handTransform.position;
            }

            if (hasTrackedRotation)
            {
                worldRotation = resolvedTrackingSpace != null
                    ? resolvedTrackingSpace.rotation * localRotation
                    : localRotation;
            }
            else if (controllerTransform != null)
            {
                worldRotation = controllerTransform.rotation;
            }
            else if (handTransform != null)
            {
                worldRotation = handTransform.rotation;
            }

            return hasTrackedPosition || hasTrackedRotation || activeTransform != null;
        }

        static Transform ResolveTrackingSpace(Transform trackingSpace, Transform controllerTransform, Transform handTransform)
        {
            if (trackingSpace != null)
                return trackingSpace;

            if (controllerTransform != null && controllerTransform.parent != null)
                return controllerTransform.parent;

            if (handTransform != null && handTransform.parent != null)
                return handTransform.parent;

            return null;
        }

        static bool TryReadTrackedLocalPose(
            XRNode xrNode,
            out Vector3 localPosition,
            out bool hasLocalPosition,
            out Quaternion localRotation,
            out bool hasLocalRotation)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;

            var xrDevice = InputDevices.GetDeviceAtXRNode(xrNode);
            hasLocalPosition = xrDevice.isValid && xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out localPosition);
            hasLocalRotation = xrDevice.isValid && xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out localRotation);

            if (!hasLocalPosition || !hasLocalRotation)
            {
                s_NodeStates.Clear();
                InputTracking.GetNodeStates(s_NodeStates);
                for (var i = 0; i < s_NodeStates.Count; i++)
                {
                    var nodeState = s_NodeStates[i];
                    if (nodeState.nodeType != xrNode)
                        continue;

                    if (!hasLocalPosition && nodeState.TryGetPosition(out var nodePosition))
                    {
                        localPosition = nodePosition;
                        hasLocalPosition = true;
                    }

                    if (!hasLocalRotation && nodeState.TryGetRotation(out var nodeRotation))
                    {
                        localRotation = nodeRotation;
                        hasLocalRotation = true;
                    }

                    if (hasLocalPosition && hasLocalRotation)
                        break;
                }
            }

            return hasLocalPosition || hasLocalRotation || localPosition.sqrMagnitude > MinimumLocalPositionSqrMagnitude;
        }
    }
}
