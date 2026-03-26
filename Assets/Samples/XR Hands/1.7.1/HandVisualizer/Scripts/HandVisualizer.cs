using System.Collections.Generic;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace UnityEngine.XR.Hands.Samples.VisualizerSample
{
    // Hand rig setups can differ between platforms. In these cases, the HandVisualizer supports displaying unique hands on a per-platform basis.
    // If you would like to customize the hand meshes that are displayed by the HandVisualizer, based on the platform you are using,
    // you will need to replace the rigged hand mesh references assigned to the corresponding fields for that platform.
    // For Meta Quest devices, assign your rigged hand meshes to the "m_MetaQuestLeftHandMesh" & "m_MetaQuestRightHandMesh" fields.
    // For Android XR devices, assign your rigged hand meshes to the "m_AndroidXRLeftHandMesh" & "m_AndroidXRRightHandMesh" fields.
    // The rigged hand meshes that are assigned for a given platform will be displayed when that platform is detected,
    // and any other rigged hand meshes assigned for other undetected platforms will not be displayed.

    /// <summary>
    /// This component visualizes the hand joints and mesh for the left and right hands.
    /// </summary>
    public class HandVisualizer : MonoBehaviour
    {
        /// <summary>
        /// The type of velocity to visualize.
        /// </summary>
        public enum VelocityType
        {
            /// <summary>
            /// Visualize the linear velocity of the joint.
            /// </summary>
            Linear,

            /// <summary>
            /// Visualize the angular velocity of the joint.
            /// </summary>
            Angular,

            /// <summary>
            /// Do not visualize velocity.
            /// </summary>
            None,
        }

        [SerializeField]
        [Tooltip("If this is enabled, this component will enable the Input System internal feature flag 'USE_OPTIMIZED_CONTROLS'. You must have at least version 1.5.0 of the Input System and have its backend enabled for this to take effect.")]
        bool m_UseOptimizedControls;

        [SerializeField, FormerlySerializedAs("m_LeftHandMesh")]
        [Tooltip("References either a prefab or a GameObject in the scene that will be used to visualize the left hand.")]
        GameObject m_MetaQuestLeftHandMesh;

        [SerializeField, FormerlySerializedAs("m_RightHandMesh")]
        [Tooltip("References either a prefab or a GameObject in the scene that will be used to visualize the right hand.")]
        GameObject m_MetaQuestRightHandMesh;

        [SerializeField]
        [Tooltip("References either a prefab or a GameObject in the scene that will be used to visualize the left hand on Android XR devices." +
                 "<br><br><b>Instructions for how to setup and use these meshes can be found at the top of the <b>HandVisualizer.cs class</b>")]
        GameObject m_AndroidXRLeftHandMesh;

        [SerializeField]
        [Tooltip("References either a prefab or a GameObject in the scene that will be used to visualize the right hand on Android XR devices." +
                 "<br><br><b>Instructions for how to setup and use these meshes can be found at the top of the <b>HandVisualizer.cs class</b>")]
        GameObject m_AndroidXRRightHandMesh;

        [SerializeField]
        [Tooltip("(Optional) If this is set, the hand meshes will be assigned this material.")]
        Material m_HandMeshMaterial;

        [SerializeField]
        [Tooltip("Tells the Hand Visualizer to draw the meshes for the hands.")]
        bool m_DrawMeshes;
        bool m_PreviousDrawMeshes;

        /// <summary>
        /// Tells the Hand Visualizer to draw the meshes for the hands.
        /// </summary>
        public bool drawMeshes
        {
            get => m_DrawMeshes;
            set => m_DrawMeshes = value;
        }

        [SerializeField]
        [Tooltip("The prefab that will be used to visualize the joints for debugging.")]
        GameObject m_DebugDrawPrefab;

        [SerializeField]
        [Tooltip("Tells the Hand Visualizer to draw the debug joints for the hands.")]
        bool m_DebugDrawJoints;
        bool m_PreviousDebugDrawJoints;

        /// <summary>
        /// Tells the Hand Visualizer to draw the debug joints for the hands.
        /// </summary>
        public bool debugDrawJoints
        {
            get => m_DebugDrawJoints;
            set => m_DebugDrawJoints = value;
        }

        [SerializeField]
        [Tooltip("Prefab to use for visualizing the velocity.")]
        GameObject m_VelocityPrefab;

        [SerializeField]
        [Tooltip("The type of velocity to visualize.")]
        VelocityType m_VelocityType;
        VelocityType m_PreviousVelocityType;

        /// <summary>
        /// The type of velocity to visualize.
        /// </summary>
        public VelocityType velocityType
        {
            get => m_VelocityType;
            set => m_VelocityType = value;
        }

        XRHandSubsystem m_Subsystem;
        HandGameObjects m_LeftHandGameObjects;
        HandGameObjects m_RightHandGameObjects;

        static readonly List<XRHandSubsystem> s_SubsystemsReuse = new List<XRHandSubsystem>();

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected void Awake()
        {
#if ENABLE_INPUT_SYSTEM
            if (m_UseOptimizedControls)
                InputSystem.InputSystem.settings.SetInternalFeatureFlag("USE_OPTIMIZED_CONTROLS", true);
#endif // ENABLE_INPUT_SYSTEM
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected void OnEnable()
        {
            if (m_Subsystem == null)
                return;

            UpdateRenderingVisibility(m_LeftHandGameObjects, m_Subsystem.leftHand.isTracked);
            UpdateRenderingVisibility(m_RightHandGameObjects, m_Subsystem.rightHand.isTracked);
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected void OnDisable()
        {
            if (m_Subsystem != null)
            {
                m_Subsystem.trackingAcquired -= OnTrackingAcquired;
                m_Subsystem.trackingLost -= OnTrackingLost;
                m_Subsystem.updatedHands -= OnUpdatedHands;
                m_Subsystem = null;
            }

            UpdateRenderingVisibility(m_LeftHandGameObjects, false);
            UpdateRenderingVisibility(m_RightHandGameObjects, false);
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected void OnDestroy()
        {
            if (m_LeftHandGameObjects != null)
            {
                m_LeftHandGameObjects.OnDestroy();
                m_LeftHandGameObjects = null;
            }

            if (m_RightHandGameObjects != null)
            {
                m_RightHandGameObjects.OnDestroy();
                m_RightHandGameObjects = null;
            }
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected void Update()
        {
            if (m_Subsystem != null && m_Subsystem.running)
            {
                UpdateControllerFallbackVisuals();
                return;
            }

            SubsystemManager.GetSubsystems(s_SubsystemsReuse);
            var foundRunningHandSubsystem = false;
            for (var i = 0; i < s_SubsystemsReuse.Count; ++i)
            {
                var handSubsystem = s_SubsystemsReuse[i];
                if (handSubsystem.running)
                {
                    UnsubscribeHandSubsystem();
                    m_Subsystem = handSubsystem;
                    foundRunningHandSubsystem = true;
                    break;
                }
            }

            if (!foundRunningHandSubsystem)
                return;

            GameObject selectedLeftHandMesh = null, selectedRightHandMesh = null;
            if (m_Subsystem.detectedHandMeshLayout == XRDetectedHandMeshLayout.OpenXRAndroidXR)
            {
                selectedLeftHandMesh = m_AndroidXRLeftHandMesh;
                selectedRightHandMesh = m_AndroidXRRightHandMesh;
            }
            else
            {
                selectedLeftHandMesh = m_MetaQuestLeftHandMesh;
                selectedRightHandMesh = m_MetaQuestRightHandMesh;
            }

            if (m_LeftHandGameObjects == null)
            {
                m_LeftHandGameObjects = new HandGameObjects(
                    Handedness.Left,
                    transform,
                    selectedLeftHandMesh,
                    m_HandMeshMaterial,
                    m_DebugDrawPrefab,
                    m_VelocityPrefab);
            }

            if (m_RightHandGameObjects == null)
            {
                m_RightHandGameObjects = new HandGameObjects(
                    Handedness.Right,
                    transform,
                    selectedRightHandMesh,
                    m_HandMeshMaterial,
                    m_DebugDrawPrefab,
                    m_VelocityPrefab);
            }

            UpdateRenderingVisibility(m_LeftHandGameObjects, m_Subsystem.leftHand.isTracked);
            UpdateRenderingVisibility(m_RightHandGameObjects, m_Subsystem.rightHand.isTracked);

            m_PreviousDrawMeshes = m_DrawMeshes;
            m_PreviousDebugDrawJoints = m_DebugDrawJoints;
            m_PreviousVelocityType = m_VelocityType;

            SubscribeHandSubsystem();
            UpdateControllerFallbackVisuals();
        }

        void SubscribeHandSubsystem()
        {
            if (m_Subsystem == null)
                return;

            m_Subsystem.trackingAcquired += OnTrackingAcquired;
            m_Subsystem.trackingLost += OnTrackingLost;
            m_Subsystem.updatedHands += OnUpdatedHands;
        }

        void UnsubscribeHandSubsystem()
        {
            if (m_Subsystem == null)
                return;

            m_Subsystem.trackingAcquired -= OnTrackingAcquired;
            m_Subsystem.trackingLost -= OnTrackingLost;
            m_Subsystem.updatedHands -= OnUpdatedHands;
        }

        void UpdateRenderingVisibility(HandGameObjects handGameObjects, bool isTracked)
        {
            if (handGameObjects == null)
                return;

            handGameObjects.ToggleDrawMesh(m_DrawMeshes && (isTracked || handGameObjects.HasTrackedControllerPose()));
            handGameObjects.ToggleDebugDrawJoints(m_DebugDrawJoints && isTracked);
            handGameObjects.SetVelocityType(isTracked ? m_VelocityType : VelocityType.None);
        }

        void UpdateControllerFallbackVisuals()
        {
            if (m_LeftHandGameObjects != null && (m_Subsystem == null || !m_Subsystem.leftHand.isTracked))
            {
                UpdateRenderingVisibility(m_LeftHandGameObjects, false);
                m_LeftHandGameObjects.TryUpdateFromController();
            }

            if (m_RightHandGameObjects != null && (m_Subsystem == null || !m_Subsystem.rightHand.isTracked))
            {
                UpdateRenderingVisibility(m_RightHandGameObjects, false);
                m_RightHandGameObjects.TryUpdateFromController();
            }
        }

        void OnTrackingAcquired(XRHand hand)
        {
            switch (hand.handedness)
            {
                case Handedness.Left:
                    UpdateRenderingVisibility(m_LeftHandGameObjects, true);
                    break;

                case Handedness.Right:
                    UpdateRenderingVisibility(m_RightHandGameObjects, true);
                    break;
            }
        }

        void OnTrackingLost(XRHand hand)
        {
            switch (hand.handedness)
            {
                case Handedness.Left:
                    UpdateRenderingVisibility(m_LeftHandGameObjects, false);
                    break;

                case Handedness.Right:
                    UpdateRenderingVisibility(m_RightHandGameObjects, false);
                    break;
            }
        }

        void OnUpdatedHands(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags, XRHandSubsystem.UpdateType updateType)
        {
            // We have no game logic depending on the Transforms, so early out here
            // (add game logic before this return here, directly querying from
            // subsystem.leftHand and subsystem.rightHand using GetJoint on each hand)
            if (updateType == XRHandSubsystem.UpdateType.Dynamic)
                return;

            bool leftHandTracked = subsystem.leftHand.isTracked;
            bool rightHandTracked = subsystem.rightHand.isTracked;

            if (m_PreviousDrawMeshes != m_DrawMeshes)
            {
                m_LeftHandGameObjects.ToggleDrawMesh(m_DrawMeshes);
                m_RightHandGameObjects.ToggleDrawMesh(m_DrawMeshes);
                m_PreviousDrawMeshes = m_DrawMeshes;
            }

            if (m_PreviousDebugDrawJoints != m_DebugDrawJoints)
            {
                m_LeftHandGameObjects.ToggleDebugDrawJoints(m_DebugDrawJoints && leftHandTracked);
                m_RightHandGameObjects.ToggleDebugDrawJoints(m_DebugDrawJoints && rightHandTracked);
                m_PreviousDebugDrawJoints = m_DebugDrawJoints;
            }

            if (m_PreviousVelocityType != m_VelocityType)
            {
                m_LeftHandGameObjects.SetVelocityType(leftHandTracked ? m_VelocityType : VelocityType.None);
                m_RightHandGameObjects.SetVelocityType(rightHandTracked ? m_VelocityType : VelocityType.None);
                m_PreviousVelocityType = m_VelocityType;
            }

            if (leftHandTracked)
            {
                m_LeftHandGameObjects.UpdateJoints(
                    subsystem.leftHand,
                    (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != 0,
                    m_DebugDrawJoints,
                    m_VelocityType);
            }
            else
            {
                m_LeftHandGameObjects.TryUpdateFromController();
            }

            if (rightHandTracked)
            {
                m_RightHandGameObjects.UpdateJoints(
                    subsystem.rightHand,
                    (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != 0,
                    m_DebugDrawJoints,
                    m_VelocityType);
            }
            else
            {
                m_RightHandGameObjects.TryUpdateFromController();
            }
        }

        class HandGameObjects
        {
            GameObject m_HandRoot;
            GameObject m_DrawJointsParent;

            GameObject[] m_DrawJoints = new GameObject[XRHandJointID.EndMarker.ToIndex()];
            GameObject[] m_VelocityParents = new GameObject[XRHandJointID.EndMarker.ToIndex()];
            LineRenderer[] m_Lines = new LineRenderer[XRHandJointID.EndMarker.ToIndex()];
            JointVisualizer[] m_JointVisualizers = new JointVisualizer[XRHandJointID.EndMarker.ToIndex()];

            static Vector3[] s_LinePointsReuse = new Vector3[2];
            XRHandMeshController m_MeshController;
            XRHandSkeletonDriver m_SkeletonDriver;
            readonly Handedness m_Handedness;
            readonly Dictionary<XRHandJointID, Quaternion> m_InitialJointRotations = new Dictionary<XRHandJointID, Quaternion>();
            const float k_LineWidth = 0.005f;

            public HandGameObjects(
                Handedness handedness,
                Transform parent,
                GameObject meshPrefab,
                Material meshMaterial,
                GameObject debugDrawPrefab,
                GameObject velocityPrefab)
            {
                void AssignJoint(
                    XRHandJointID jointId,
                    Transform jointDrivenTransform,
                    Transform drawJointsParent)
                {
                    var jointIndex = jointId.ToIndex();
                    m_DrawJoints[jointIndex] = Instantiate(debugDrawPrefab);
                    m_DrawJoints[jointIndex].transform.parent = drawJointsParent;
                    m_DrawJoints[jointIndex].name = jointId.ToString();

                    m_VelocityParents[jointIndex] = Instantiate(velocityPrefab);
                    m_VelocityParents[jointIndex].transform.parent = jointDrivenTransform;

                    m_Lines[jointIndex] = m_DrawJoints[jointIndex].GetComponent<LineRenderer>();
                    m_Lines[jointIndex].startWidth = m_Lines[jointIndex].endWidth = k_LineWidth;
                    s_LinePointsReuse[0] = s_LinePointsReuse[1] = jointDrivenTransform.position;
                    m_Lines[jointIndex].SetPositions(s_LinePointsReuse);

                    if (m_DrawJoints[jointIndex].TryGetComponent<JointVisualizer>(out var jointVisualizer))
                        m_JointVisualizers[jointIndex] = jointVisualizer;
                }

                m_Handedness = handedness;
                var isSceneObject = meshPrefab.scene.IsValid();
                m_HandRoot = isSceneObject ? meshPrefab : Instantiate(meshPrefab, parent);
                m_HandRoot.SetActive(false); // Deactivate so that added components do not run OnEnable before they are finished being set up

                m_HandRoot.transform.localPosition = Vector3.zero;
                m_HandRoot.transform.localRotation = Quaternion.identity;

                var handEvents = m_HandRoot.GetComponent<XRHandTrackingEvents>();
                if (handEvents == null)
                {
                    handEvents = m_HandRoot.AddComponent<XRHandTrackingEvents>();
                    handEvents.updateType = XRHandTrackingEvents.UpdateTypes.Dynamic;
                    handEvents.handedness = handedness;
                }

                m_MeshController = m_HandRoot.GetComponent<XRHandMeshController>();
                if (m_MeshController == null)
                {
                    m_MeshController = m_HandRoot.AddComponent<XRHandMeshController>();
                    for (var childIndex = 0; childIndex < m_HandRoot.transform.childCount; ++childIndex)
                    {
                        var childTransform = m_HandRoot.transform.GetChild(childIndex);
                        if (childTransform.TryGetComponent<SkinnedMeshRenderer>(out var renderer))
                            m_MeshController.handMeshRenderer = renderer;
                    }

                    m_MeshController.handTrackingEvents = handEvents;
                }

                m_MeshController.showMeshWhenTrackingIsAcquired = true;
                m_MeshController.hideMeshWhenTrackingIsLost = false;

                if (meshMaterial != null)
                {
                    m_MeshController.handMeshRenderer.sharedMaterial = meshMaterial;
                }

                m_SkeletonDriver = m_HandRoot.GetComponent<XRHandSkeletonDriver>();
                if (m_SkeletonDriver == null)
                {
                    m_SkeletonDriver = m_HandRoot.AddComponent<XRHandSkeletonDriver>();
                    m_SkeletonDriver.jointTransformReferences = new List<JointToTransformReference>();
                    Transform root = null;
                    for (var childIndex = 0; childIndex < m_HandRoot.transform.childCount; ++childIndex)
                    {
                        var child = m_HandRoot.transform.GetChild(childIndex);
                        if (child.gameObject.name.EndsWith(XRHandJointID.Wrist.ToString()))
                            root = child;
                    }

                    m_SkeletonDriver.rootTransform = root;
                    XRHandSkeletonDriverUtility.FindJointsFromRoot(m_SkeletonDriver);
                    m_SkeletonDriver.InitializeFromSerializedReferences();
                    m_SkeletonDriver.handTrackingEvents = handEvents;
                }

                m_DrawJointsParent = new GameObject();
                m_DrawJointsParent.transform.parent = parent;
                m_DrawJointsParent.transform.localPosition = Vector3.zero;
                m_DrawJointsParent.transform.localRotation = Quaternion.identity;
                m_DrawJointsParent.name = handedness + "HandDebugDrawJoints";

                for (var i = 0; i < m_SkeletonDriver.jointTransformReferences.Count; i++)
                {
                    var jointTransformReference = m_SkeletonDriver.jointTransformReferences[i];
                    var jointTransform = jointTransformReference.jointTransform;
                    var jointID = jointTransformReference.xrHandJointID;
                    AssignJoint(jointID, jointTransform, m_DrawJointsParent.transform);
                    if (jointTransform != null)
                        m_InitialJointRotations[jointID] = jointTransform.localRotation;
                }

                m_HandRoot.SetActive(true);
            }

            public void OnDestroy()
            {
                Destroy(m_HandRoot);
                m_HandRoot = null;

                for (var jointIndex = 0; jointIndex < m_DrawJoints.Length; ++jointIndex)
                {
                    Destroy(m_DrawJoints[jointIndex]);
                    m_DrawJoints[jointIndex] = null;
                }

                for (var jointIndex = 0; jointIndex < m_VelocityParents.Length; ++jointIndex)
                {
                    Destroy(m_VelocityParents[jointIndex]);
                    m_VelocityParents[jointIndex] = null;
                }

                Destroy(m_DrawJointsParent);
                m_DrawJointsParent = null;
            }

            public void ToggleDrawMesh(bool drawMesh)
            {
                if (m_MeshController == null)
                    return;

                m_MeshController.enabled = drawMesh;
                if (m_MeshController.handMeshRenderer != null)
                    m_MeshController.handMeshRenderer.enabled = drawMesh;
            }

            public void ToggleDebugDrawJoints(bool debugDrawJoints)
            {
                for (int jointIndex = 0; jointIndex < m_DrawJoints.Length; ++jointIndex)
                {
                    ToggleRenderers<MeshRenderer>(debugDrawJoints, m_DrawJoints[jointIndex].transform);
                    m_Lines[jointIndex].enabled = debugDrawJoints;
                }

                m_Lines[0].enabled = false;
            }

            public void SetVelocityType(VelocityType velocityType)
            {
                for (int jointIndex = 0; jointIndex < m_VelocityParents.Length; ++jointIndex)
                    ToggleRenderers<LineRenderer>(velocityType != VelocityType.None, m_VelocityParents[jointIndex].transform);
            }

            public bool HasTrackedControllerPose()
            {
                var device = InputDevices.GetDeviceAtXRNode(m_Handedness == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand);
                if (!device.isValid)
                    return false;

                if (device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && !isTracked)
                    return false;

                return device.TryGetFeatureValue(CommonUsages.devicePosition, out _) ||
                       device.TryGetFeatureValue(CommonUsages.deviceRotation, out _);
            }

            public bool TryUpdateFromController()
            {
                if (m_HandRoot == null)
                    return false;

                var node = m_Handedness == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand;
                var device = InputDevices.GetDeviceAtXRNode(node);
                if (!device.isValid)
                    return false;

                if (device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && !isTracked)
                    return false;

                var gotPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out var position);
                var gotRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation);
                if (!gotPosition && !gotRotation)
                    return false;

                if (m_SkeletonDriver != null)
                    m_SkeletonDriver.enabled = false;

                if (gotPosition)
                    m_HandRoot.transform.localPosition = position;
                if (gotRotation)
                    m_HandRoot.transform.localRotation = rotation;

                if (m_MeshController != null && m_MeshController.handMeshRenderer != null && m_MeshController.enabled)
                    m_MeshController.handMeshRenderer.enabled = true;

                var grip = ReadAnalog(device, CommonUsages.grip, CommonUsages.gripButton);
                var trigger = ReadAnalog(device, CommonUsages.trigger, CommonUsages.triggerButton);
                var gripDrivenCurl = Mathf.Clamp01(grip);
                var triggerDrivenCurl = Mathf.Clamp01(trigger);
                var thumbCurl = Mathf.Clamp01(grip);

                for (var i = 0; i < m_SkeletonDriver.jointTransformReferences.Count; i++)
                {
                    var jointRef = m_SkeletonDriver.jointTransformReferences[i];
                    var jointTransform = jointRef.jointTransform;
                    if (jointTransform == null)
                        continue;

                    if (!m_InitialJointRotations.TryGetValue(jointRef.xrHandJointID, out var initialRotation))
                        continue;

                    jointTransform.localRotation = initialRotation * GetControllerJointRotationOffset(
                        jointRef.xrHandJointID,
                        triggerDrivenCurl,
                        gripDrivenCurl,
                        thumbCurl);
                }

                return true;
            }

            public void UpdateJoints(
                XRHand hand,
                bool areJointsTracked,
                bool debugDrawJoints,
                VelocityType velocityType)
            {
                if (m_SkeletonDriver != null)
                    m_SkeletonDriver.enabled = true;

                if (!areJointsTracked)
                    return;

                var wristPose = Pose.identity;
                var parentIndex = XRHandJointID.Wrist.ToIndex();
                UpdateJoint(debugDrawJoints, velocityType, hand.GetJoint(XRHandJointID.Wrist), ref wristPose, ref parentIndex);
                UpdateJoint(debugDrawJoints, velocityType, hand.GetJoint(XRHandJointID.Palm), ref wristPose, ref parentIndex, false);

                for (var fingerIndex = (int)XRHandFingerID.Thumb;
                    fingerIndex <= (int)XRHandFingerID.Little;
                    ++fingerIndex)
                {
                    var parentPose = wristPose;
                    var fingerId = (XRHandFingerID)fingerIndex;
                    parentIndex = XRHandJointID.Wrist.ToIndex();

                    var jointIndexBack = fingerId.GetBackJointID().ToIndex();
                    for (var jointIndex = fingerId.GetFrontJointID().ToIndex();
                        jointIndex <= jointIndexBack;
                        ++jointIndex)
                    {
                        UpdateJoint(debugDrawJoints, velocityType, hand.GetJoint(XRHandJointIDUtility.FromIndex(jointIndex)), ref parentPose, ref parentIndex);
                    }
                }
            }

            void UpdateJoint(
                bool debugDrawJoints,
                VelocityType velocityType,
                XRHandJoint joint,
                ref Pose parentPose,
                ref int parentIndex,
                bool cacheParentPose = true)
            {
                if (joint.id == XRHandJointID.Invalid)
                    return;

                var jointIndex = joint.id.ToIndex();
                m_JointVisualizers[jointIndex].NotifyTrackingState(joint.trackingState);

                if (!joint.TryGetPose(out var pose))
                    return;

                m_DrawJoints[jointIndex].transform.localPosition = pose.position;
                m_DrawJoints[jointIndex].transform.localRotation = pose.rotation;

                if (debugDrawJoints && joint.id != XRHandJointID.Wrist)
                {
                    s_LinePointsReuse[0] = m_DrawJoints[parentIndex].transform.position;
                    s_LinePointsReuse[1] = m_DrawJoints[jointIndex].transform.position;
                    m_Lines[jointIndex].SetPositions(s_LinePointsReuse);
                }

                if (cacheParentPose)
                {
                    parentPose = pose;
                    parentIndex = jointIndex;
                }

                if (velocityType != VelocityType.None && m_VelocityParents[jointIndex].TryGetComponent<LineRenderer>(out var renderer))
                {
                    m_VelocityParents[jointIndex].transform.localPosition = Vector3.zero;
                    m_VelocityParents[jointIndex].transform.localRotation = Quaternion.identity;

                    s_LinePointsReuse[0] = s_LinePointsReuse[1] = m_VelocityParents[jointIndex].transform.position;
                    if (velocityType == VelocityType.Linear)
                    {
                        if (joint.TryGetLinearVelocity(out var velocity))
                            s_LinePointsReuse[1] += velocity;
                    }
                    else if (velocityType == VelocityType.Angular)
                    {
                        if (joint.TryGetAngularVelocity(out var velocity))
                            s_LinePointsReuse[1] += 0.05f * velocity.normalized;
                    }

                    renderer.SetPositions(s_LinePointsReuse);
                }
            }

            static void ToggleRenderers<TRenderer>(bool toggle, Transform rendererTransform)
                where TRenderer : Renderer
            {
                if (rendererTransform.TryGetComponent<TRenderer>(out var renderer))
                    renderer.enabled = toggle;

                for (var childIndex = 0; childIndex < rendererTransform.childCount; ++childIndex)
                    ToggleRenderers<TRenderer>(toggle, rendererTransform.GetChild(childIndex));
            }

            static float ReadAnalog(InputDevice device, InputFeatureUsage<float> analogUsage, InputFeatureUsage<bool> buttonUsage)
            {
                var analog = 0f;
                if (!device.TryGetFeatureValue(analogUsage, out analog))
                    analog = 0f;

                if (device.TryGetFeatureValue(buttonUsage, out var pressed) && pressed)
                    analog = Mathf.Max(analog, 1f);

                return analog;
            }

            static Quaternion GetControllerJointRotationOffset(
                XRHandJointID jointId,
                float triggerCurl,
                float gripCurl,
                float thumbCurl)
            {
                if (jointId >= XRHandJointID.IndexMetacarpal && jointId <= XRHandJointID.IndexTip)
                    return Quaternion.Euler(GetFingerCurlAngle(jointId, triggerCurl), 0f, 0f);

                if (jointId >= XRHandJointID.MiddleMetacarpal && jointId <= XRHandJointID.LittleTip)
                    return Quaternion.Euler(GetFingerCurlAngle(jointId, gripCurl), 0f, 0f);

                if (jointId >= XRHandJointID.ThumbMetacarpal && jointId <= XRHandJointID.ThumbTip)
                    return Quaternion.Euler(GetThumbCurlAngle(jointId, thumbCurl), 0f, 0f);

                return Quaternion.identity;
            }

            static float GetFingerCurlAngle(XRHandJointID jointId, float curl)
            {
                var angle = jointId switch
                {
                    XRHandJointID.IndexMetacarpal => 6f,
                    XRHandJointID.MiddleMetacarpal => 5f,
                    XRHandJointID.RingMetacarpal => 5f,
                    XRHandJointID.LittleMetacarpal => 8f,
                    XRHandJointID.IndexProximal => 42f,
                    XRHandJointID.MiddleProximal => 46f,
                    XRHandJointID.RingProximal => 48f,
                    XRHandJointID.LittleProximal => 54f,
                    XRHandJointID.IndexIntermediate => 58f,
                    XRHandJointID.MiddleIntermediate => 64f,
                    XRHandJointID.RingIntermediate => 66f,
                    XRHandJointID.LittleIntermediate => 70f,
                    XRHandJointID.IndexDistal => 70f,
                    XRHandJointID.MiddleDistal => 74f,
                    XRHandJointID.RingDistal => 76f,
                    XRHandJointID.LittleDistal => 82f,
                    XRHandJointID.IndexTip => 14f,
                    XRHandJointID.MiddleTip => 12f,
                    XRHandJointID.RingTip => 12f,
                    XRHandJointID.LittleTip => 14f,
                    _ => 0f,
                };

                return curl * angle;
            }

            static float GetThumbCurlAngle(XRHandJointID jointId, float curl)
            {
                var angle = jointId switch
                {
                    XRHandJointID.ThumbMetacarpal => 12f,
                    XRHandJointID.ThumbProximal => 24f,
                    XRHandJointID.ThumbDistal => 34f,
                    XRHandJointID.ThumbTip => 12f,
                    _ => 0f,
                };

                return curl * angle;
            }
        }
    }
}
