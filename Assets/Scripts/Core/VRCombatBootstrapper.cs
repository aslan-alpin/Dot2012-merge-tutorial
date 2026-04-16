using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Meta.XR.BuildingBlocks;
using UnityEngine;
using UnityEngine.Events;
using Unity.XR.CoreUtils;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Samples.Hands;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.InputSystem;
using VRCombat.Combat;
using VRCombat.Enemies;
using VRCombat.Player;
using VRCombat.UI;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

namespace VRCombat.Core
{
    [DisallowMultipleComponent]
    public class VRCombatBootstrapper : MonoBehaviour
    {
        enum HandSide
        {
            None,
            Left,
            Right
        }

        struct BladePickupLayout
        {
            public Vector3 GripColliderCenter;
            public float GripColliderHeight;
            public float GripColliderRadius;
            public Vector3 BodyColliderCenter;
            public float BodyColliderHeight;
            public float BodyColliderRadius;
            public Vector3 AttachLocalPosition;
        }

        struct DisplayWallCandidate
        {
            public Transform Transform;
            public Bounds Bounds;
            public Vector3 SurfacePoint;
            public Vector3 SurfaceNormal;
            public bool IsTransparent;
            public float Score;
        }

        [SerializeField]
        int m_StartingEnemiesPerWave = 5;

        [SerializeField]
        float m_InitialWaveDelaySeconds = 2f;

        [SerializeField]
        int m_EnemiesAddedPerWave = 2;

        [SerializeField]
        float m_StartingFlowRatePerSecond = 0.35f;

        [SerializeField]
        float m_FlowRateIncreasePerWave = 0.09f;

        [SerializeField]
        float m_TimeBetweenWavesSeconds = 5f;

        [SerializeField]
        int m_MaxHitsPerWave = 10;

        [SerializeField]
        float m_EnemySpawnRadius = 6f;

        [SerializeField]
        float m_EnemySpawnVerticalOffset = -0.45f;

        [SerializeField]
        float m_PlayerHitboxRadius = 0.24f;

        [SerializeField]
        float m_HandSwingColliderRadius = 0.09f;

        [SerializeField]
        float m_NearGrabRadius = 0.14f;

        [SerializeField]
        float m_NearGrabDistance = 0.78f;

        [SerializeField]
        float m_MaxPhysicalGrabDistance = 1.35f;

        [SerializeField]
        float m_UiFarInteractionDistance = 3f;

        [SerializeField]
        float m_LoadoutWallSearchDistance = 6f;

        [SerializeField]
        float m_LoadoutMountHeight = 1.22f;

        [SerializeField]
        float m_LoadoutWallInset = 0.14f;

        [SerializeField]
        float m_LoadoutBladeSpacing = 0.34f;

        [SerializeField]
        float m_LoadoutFallbackDistance = 1.65f;

        [SerializeField]
        float m_TeleportSpawnHeightOffset = 0.08f;

        [SerializeField]
        float m_KillZoneHorizontalRadius = 50.75f;

        [SerializeField]
        float m_KillZoneBelowCenter = 30.35f;

        [SerializeField]
        float m_KillZoneAboveCenter = 50.2f;

        [SerializeField]
        float m_KillZoneGlobalSweepInterval = 0.2f;

        [SerializeField]
        [Range(0f, 1f)]
        float m_DefaultMovementVignetteStrength = 0.08f;

        [SerializeField]
        Color m_PickupColor = new Color(0.95f, 0.64f, 0.2f, 1f);

        [SerializeField]
        Color m_ShieldColor = new Color(0.23f, 0.46f, 0.68f, 1f);

        [SerializeField]
        Color m_EnemyStartColor = new Color(0.2f, 0.85f, 0.35f, 1f);

        Camera m_PlayerCamera;
        Transform m_PlayerRoot;
        XRInputModalityManager m_InputModalityManager;
        Coroutine m_SpawnLoop;
        CombatHUDRuntime m_CombatHud;
        PlayerDamageReceiver m_PlayerDamageReceiver;
        Transform m_LeftHandTransform;
        Transform m_RightHandTransform;
        Transform m_LeftControllerTransform;
        Transform m_RightControllerTransform;
        GameObject m_LeftHandProxyVisual;
        GameObject m_RightHandProxyVisual;
        Material m_PickupMaterial;
        Material m_HandProxyMaterial;
        Material m_EnemyMaterial;
        Material m_ShieldMaterial;
        readonly List<CapsuleEnemy> m_ActiveWaveEnemies = new List<CapsuleEnemy>();
        readonly List<GameObject> m_RuntimeSpawnedObjects = new List<GameObject>();
        readonly List<Collider> m_ArenaSurfaceColliders = new List<Collider>();
        readonly Dictionary<XRGrabInteractable, HandSide> m_EquippedHandByPickup = new Dictionary<XRGrabInteractable, HandSide>();
        readonly HashSet<XRGrabInteractable> m_CombatPickupListenerRegistrations = new HashSet<XRGrabInteractable>();
        readonly List<Renderer> m_LeftSuppressedHandRenderers = new List<Renderer>();
        readonly List<Renderer> m_RightSuppressedHandRenderers = new List<Renderer>();
        readonly List<Collider> m_LeftSuppressedHandColliders = new List<Collider>();
        readonly List<Collider> m_RightSuppressedHandColliders = new List<Collider>();
        static readonly List<XRInputDevice> s_HandDeviceBuffer = new List<XRInputDevice>(4);
        static readonly List<XRInputDevice> s_ControllerDeviceBuffer = new List<XRInputDevice>(4);
        static readonly RaycastHit[] s_ScenePickupSupportHits = new RaycastHit[16];
        static readonly RaycastHit[] s_PlayerGroundHitBuffer = new RaycastHit[16];
        Coroutine m_DeathFlowRoutine;
        Vector3 m_KillZoneCenter;
        bool m_HasKillZoneCenter;
        bool m_KillZoneDerivedFromArena;
        float m_NextKillZoneGlobalSweepTime;
        RuntimeKillZoneBoundary m_RuntimeKillZoneBoundary;
        Collider m_RuntimeArenaTeleportCollider;
        InputAction m_RuntimePauseMenuAction;
        MetaSystemGestureDetector m_MetaMenuGestureDetector;
        float m_LastPauseToggleTime = -100f;
        int m_CurrentWave = 1;
        int m_HitsTakenThisWave;
        float m_CurrentFlowRate;
        bool m_IsGameOver;
        bool m_IsRestarting;
        bool m_IsPauseMenuOpen;
        bool m_HasLoggedPauseStartupDiagnostics;
        bool m_HasLoggedFirstPauseAttempt;
        bool m_HasLoggedCameraRecoveryAttempt;
        bool m_WasRawMenuButtonPressed;
        bool m_PendingMetaMenuGesture;
        float m_BootstrapStartedRealtime;
        float m_MovementVignetteStrength;
        TunnelingVignetteController m_TunnelingVignetteController;

        static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorShaderId = Shader.PropertyToID("_Color");
        const string MovementVignetteStrengthPrefKey = "vrcombat.movement_vignette_strength";
        const string MovementVignetteInitializedPrefKey = "vrcombat.movement_vignette_initialized_v2";
        const string PauseMapperActionTitle = "VR Combat Pause Menu";
        const string DaggerModelResourcePath = "CombatModels/Dagger_06";
        const string ChainModelResourcePath = "CombatModels/Chain_03";
        const string ArenaRootName = "Arena";
        const string AttachPointObjectName = "Attach Point";
        const string SwordNameToken = "sword";
        const float PauseToggleDebounceSeconds = 0.12f;
        const float CameraRecoveryGraceSeconds = 0.75f;
        const float ScenePickupSupportProbeDistance = 0.15f;
        const float ScenePickupSupportProbeLift = 0.02f;
        const float ArenaKillZoneHorizontalPadding = 4f;
        const float ArenaKillZoneBelowPadding = 4f;
        const float ArenaKillZoneAbovePadding = 8f;
        const float ArenaTeleportSurfaceThickness = 0.06f;
        const float ArenaTeleportSurfaceLift = 0.03f;
        const float ArenaTeleportFloorTopBand = 0.75f;
        const float ArenaGroundSnapProbeHeight = 1.4f;
        const float ArenaGroundSnapDistance = 4f;
        const float ArenaGroundSnapNormalThreshold = 0.55f;
        const float ArenaGroundSnapMaxStepUp = 0.75f;
        const float ArenaGroundSnapMaxStepDown = 1.6f;
        const float ArenaGroundSnapYOffset = 0.02f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureBootstrapperExists()
        {
            RenderSettings.fog = false;

            if (FindAnyObjectByType<VRCombatBootstrapper>() != null)
                return;

            var bootstrapperObject = new GameObject("VR Combat Bootstrapper");
            bootstrapperObject.AddComponent<VRCombatBootstrapper>();
        }

        void Awake()
        {
            RenderSettings.fog = false;
        }

        IEnumerator Start()
        {
            m_BootstrapStartedRealtime = Time.realtimeSinceStartup;
            while (!TryResolvePlayerRig())
                yield return null;

            EnablePlayerControls();
            SetupPlayerDamageDetection();
            SetupMovementVignetteControl();
            ConfigureHandFirstInteraction();
            SetupCombatHud();
            SetupPauseMenuInputActions();
            LogPauseInputDiagnosticsAtStartup();
            ValidateRigCoherency("startup");
            StartCoroutine(RestartRunRoutine(initialStartup: true));
        }

        void Update()
        {
            UpdateControllerFallbackHandMapping();

            if (!m_IsGameOver && !m_IsRestarting)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || ConsumeMenuButtonPress())
                    RequestPauseMenuToggle();
            }

            UpdateKillZoneState();

            if (!m_IsGameOver || m_IsRestarting)
                return;

            if (Input.GetKeyDown(KeyCode.R) || ReadAnyRestartButton())
                RestartCurrentScene();
        }

        void OnDestroy()
        {
            RestoreSuppressedHandRenderers();
            UnbindMetaMenuGestureDetector();
            if (m_RuntimePauseMenuAction != null)
            {
                m_RuntimePauseMenuAction.Disable();
                m_RuntimePauseMenuAction.Dispose();
                m_RuntimePauseMenuAction = null;
            }

            if (m_PlayerDamageReceiver == null)
                return;

            m_PlayerDamageReceiver.HealthChanged -= OnPlayerHealthChanged;
            m_PlayerDamageReceiver.DamageTaken -= OnPlayerDamageTaken;
            m_PlayerDamageReceiver.Died -= OnPlayerDied;
        }

        void EnablePlayerControls()
        {
            var inputActionAssets = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            foreach (var asset in inputActionAssets)
            {
                asset.Enable();
                Debug.Log($"[VRCombat] Enabled input action asset '{asset.name}'");
            }
        }

        bool TryResolvePlayerRig(bool forceRefresh = false)
        {
            if (!forceRefresh && m_PlayerCamera != null && m_PlayerRoot != null)
            {
                m_InputModalityManager ??= FindPreferredInputModalityManager(m_PlayerRoot);
                return true;
            }


            Camera resolvedCamera = null;
            Transform resolvedRoot = null;
            XRInputModalityManager resolvedInputModalityManager = null;
            var resolvedRigType = "Unknown";

            if (TryFindActiveXrOriginRig(out var xrOrigin, out resolvedCamera, out resolvedRoot, out resolvedInputModalityManager))
            {
                resolvedRigType = $"XROrigin ({xrOrigin.name})";
            }
            else if (TryFindActiveOvrCameraRig(out var ovrRig, out resolvedCamera, out resolvedRoot))
            {
                resolvedRigType = $"OVRCameraRig ({ovrRig.name})";
            }
            else
            {
                if (TryFindEnabledSceneCamera(out resolvedCamera))
                {
                    resolvedRoot = resolvedCamera.transform.root;
                    resolvedRigType = $"Fallback Camera ({resolvedCamera.name})";
                }
            }

            if ((resolvedCamera == null || resolvedRoot == null)
                && TryRecoverPlayerCamera(out var recoveredCamera, out var recoveredRoot, out var recoveredRigType))
            {
                resolvedCamera = recoveredCamera;
                resolvedRoot = recoveredRoot;
                resolvedRigType = recoveredRigType;
            }

            if (resolvedCamera == null || resolvedRoot == null)
                return false;

            m_PlayerCamera = resolvedCamera;
            m_PlayerRoot = resolvedRoot;
            m_InputModalityManager = resolvedInputModalityManager ?? FindPreferredInputModalityManager(m_PlayerRoot);
            SetupPauseMenuInputActions();

            var disabledRigPaths = DisableConflictingRigStacks(m_PlayerRoot);
            LogRigSelection(resolvedRigType, disabledRigPaths);
            return true;
        }

        static bool IsActiveAndEnabled(Component component)
        {
            return component != null && component.gameObject.activeInHierarchy;
        }

        static bool TryFindEnabledSceneCamera(out Camera selectedCamera)
        {
            selectedCamera = null;
            var allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bestScore = int.MinValue;
            for (var i = 0; i < allCameras.Length; i++)
            {
                var candidate = allCameras[i];
                if (!IsActiveAndEnabled(candidate) || !candidate.enabled)
                    continue;

                var score = 0;
                if (candidate.CompareTag("MainCamera"))
                    score += 4;
                if (candidate.stereoTargetEye != StereoTargetEyeMask.None)
                    score += 3;
                if (candidate.GetComponentInParent<XROrigin>() != null)
                    score += 2;
                if (candidate.GetComponentInParent<OVRCameraRig>() != null)
                    score += 2;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                selectedCamera = candidate;
            }

            return selectedCamera != null;
        }

        static bool TryFindActiveXrOriginRig(
            out XROrigin selectedOrigin,
            out Camera selectedCamera,
            out Transform selectedRoot,
            out XRInputModalityManager selectedInputModalityManager)
        {
            selectedOrigin = null;
            selectedCamera = null;
            selectedRoot = null;
            selectedInputModalityManager = null;

            var origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bestScore = int.MinValue;
            for (var i = 0; i < origins.Length; i++)
            {
                var origin = origins[i];
                if (!IsActiveAndEnabled(origin))
                    continue;

                var candidateCamera = origin.Camera;
                if (candidateCamera == null)
                    candidateCamera = FindFirstCameraInHierarchy(origin.transform, true);
                if (!IsActiveAndEnabled(candidateCamera) || !candidateCamera.enabled)
                    continue;

                var candidateRoot = origin.Origin != null ? origin.Origin.transform : origin.transform;
                if (candidateRoot == null || !candidateRoot.gameObject.activeInHierarchy)
                    continue;

                var candidateInputModalityManager = FindPreferredInputModalityManager(candidateRoot);
                var score = 0;
                if (origin.name.IndexOf("xr origin", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 4;
                if (origin.name.IndexOf("hands", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 2;
                if (candidateCamera.CompareTag("MainCamera"))
                    score += 2;
                if (origin.Camera != null)
                    score += 2;
                if (candidateInputModalityManager != null)
                    score += 5;
                if (candidateInputModalityManager != null &&
                    (candidateInputModalityManager.leftHand != null || candidateInputModalityManager.rightHand != null))
                {
                    score += 2;
                }

                if (score <= bestScore)
                    continue;

                bestScore = score;
                selectedOrigin = origin;
                selectedCamera = candidateCamera;
                selectedRoot = candidateRoot;
                selectedInputModalityManager = candidateInputModalityManager;
            }

            return selectedOrigin != null && selectedCamera != null && selectedRoot != null;
        }

        static bool TryFindActiveOvrCameraRig(out OVRCameraRig selectedRig, out Camera selectedCamera, out Transform selectedRoot)
        {
            selectedRig = null;
            selectedCamera = null;
            selectedRoot = null;

            var rigs = FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bestScore = int.MinValue;
            for (var i = 0; i < rigs.Length; i++)
            {
                var rig = rigs[i];
                if (!IsActiveAndEnabled(rig))
                    continue;

                var candidateCamera = rig.centerEyeAnchor != null
                    ? rig.centerEyeAnchor.GetComponent<Camera>()
                    : null;
                if (!IsActiveAndEnabled(candidateCamera) || !candidateCamera.enabled)
                    candidateCamera = FindFirstCameraInHierarchy(rig.transform, true);
                if (!IsActiveAndEnabled(candidateCamera) || !candidateCamera.enabled)
                    continue;

                var score = 0;
                if (rig.name.IndexOf("camera rig", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 3;
                if (rig.centerEyeAnchor != null && candidateCamera.transform == rig.centerEyeAnchor)
                    score += 3;
                if (candidateCamera.CompareTag("MainCamera"))
                    score += 1;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                selectedRig = rig;
                selectedCamera = candidateCamera;
                selectedRoot = rig.transform;
            }

            return selectedRig != null && selectedCamera != null && selectedRoot != null;
        }

        bool TryRecoverPlayerCamera(out Camera selectedCamera, out Transform selectedRoot, out string selectedRigType)
        {
            selectedCamera = null;
            selectedRoot = null;
            selectedRigType = "Unresolved";

            var allowCameraCreation = Time.realtimeSinceStartup - m_BootstrapStartedRealtime >= CameraRecoveryGraceSeconds;

            if (TryRecoverXrOriginCamera(allowCameraCreation, out selectedCamera, out selectedRoot, out selectedRigType))
            {
                LogCameraRecovery(selectedRigType);
                return true;
            }

            if (TryRecoverOvrCameraRigCamera(allowCameraCreation, out selectedCamera, out selectedRoot, out selectedRigType))
            {
                LogCameraRecovery(selectedRigType);
                return true;
            }

            if (allowCameraCreation && TryCreateEmergencyCamera(out selectedCamera, out selectedRoot, out selectedRigType))
            {
                LogCameraRecovery(selectedRigType);
                return true;
            }

            return false;
        }

        void LogCameraRecovery(string selectedRigType)
        {
            if (m_HasLoggedCameraRecoveryAttempt)
                return;

            m_HasLoggedCameraRecoveryAttempt = true;
            Debug.LogWarning($"[VRCombat] Camera recovery path engaged: {selectedRigType}");
        }

        static bool TryRecoverXrOriginCamera(
            bool allowCameraCreation,
            out Camera selectedCamera,
            out Transform selectedRoot,
            out string selectedRigType)
        {
            selectedCamera = null;
            selectedRoot = null;
            selectedRigType = "Unresolved";

            var origins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bestScore = int.MinValue;
            XROrigin bestOrigin = null;

            for (var i = 0; i < origins.Length; i++)
            {
                var origin = origins[i];
                if (origin == null || !origin.gameObject.activeInHierarchy)
                    continue;

                var score = 0;
                if (origin.name.IndexOf("xr origin", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 3;
                if (origin.name.IndexOf("hands", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 2;
                if (origin.Camera != null)
                    score += 2;
                if (origin.CameraFloorOffsetObject != null)
                    score += 1;
                if (FindPreferredInputModalityManager(origin.transform) != null)
                    score += 4;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestOrigin = origin;
            }

            if (bestOrigin == null)
                return false;

            var root = bestOrigin.Origin != null ? bestOrigin.Origin.transform : bestOrigin.transform;
            var camera = bestOrigin.Camera;
            if (camera == null)
                camera = FindFirstCameraInHierarchy(bestOrigin.transform, false);

            if (camera == null && allowCameraCreation)
            {
                var parent = bestOrigin.CameraFloorOffsetObject != null
                    ? bestOrigin.CameraFloorOffsetObject.transform
                    : (root != null ? root : bestOrigin.transform);
                camera = CreateRuntimeFallbackCamera(
                    parent,
                    "Main Camera (Runtime Recovery)",
                    parent == root ? new Vector3(0f, 1.65f, 0f) : Vector3.zero);
            }

            if (camera == null)
                return false;

            ActivateTransformHierarchy(camera.transform, bestOrigin.transform);
            camera.enabled = true;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            TryEnsureMainCameraTag(camera);
            EnsureCameraAudioListener(camera);
            bestOrigin.Camera = camera;

            selectedCamera = camera;
            selectedRoot = root != null ? root : bestOrigin.transform;
            selectedRigType = $"Recovered XROrigin ({bestOrigin.name})";
            return true;
        }

        static bool TryRecoverOvrCameraRigCamera(
            bool allowCameraCreation,
            out Camera selectedCamera,
            out Transform selectedRoot,
            out string selectedRigType)
        {
            selectedCamera = null;
            selectedRoot = null;
            selectedRigType = "Unresolved";

            var rigs = FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bestScore = int.MinValue;
            OVRCameraRig bestRig = null;

            for (var i = 0; i < rigs.Length; i++)
            {
                var rig = rigs[i];
                if (rig == null || !rig.gameObject.activeInHierarchy)
                    continue;

                var score = 0;
                if (rig.name.IndexOf("camera rig", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 3;
                if (rig.centerEyeAnchor != null)
                    score += 2;
                if (rig.trackingSpace != null)
                    score += 1;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestRig = rig;
            }

            if (bestRig == null)
                return false;

            var camera = bestRig.centerEyeAnchor != null
                ? bestRig.centerEyeAnchor.GetComponent<Camera>()
                : null;
            if (camera == null)
                camera = FindFirstCameraInHierarchy(bestRig.transform, false);

            if (camera == null && allowCameraCreation)
            {
                var parent = bestRig.centerEyeAnchor != null
                    ? bestRig.centerEyeAnchor
                    : (bestRig.trackingSpace != null ? bestRig.trackingSpace : bestRig.transform);
                camera = CreateRuntimeFallbackCamera(parent, "CenterEye Camera (Runtime Recovery)", Vector3.zero);
            }

            if (camera == null)
                return false;

            ActivateTransformHierarchy(camera.transform, bestRig.transform);
            camera.enabled = true;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            TryEnsureMainCameraTag(camera);
            EnsureCameraAudioListener(camera);

            selectedCamera = camera;
            selectedRoot = bestRig.transform;
            selectedRigType = $"Recovered OVRCameraRig ({bestRig.name})";
            return true;
        }

        static bool TryCreateEmergencyCamera(out Camera selectedCamera, out Transform selectedRoot, out string selectedRigType)
        {
            selectedCamera = null;
            selectedRoot = null;
            selectedRigType = "Unresolved";

            var emergencyRoot = new GameObject("Runtime Emergency XR Root");
            selectedRoot = emergencyRoot.transform;
            selectedRoot.position = Vector3.zero;
            selectedRoot.rotation = Quaternion.identity;

            selectedCamera = CreateRuntimeFallbackCamera(selectedRoot, "Main Camera (Emergency)", new Vector3(0f, 1.65f, 0f));
            if (selectedCamera == null)
            {
                Destroy(emergencyRoot);
                selectedRoot = null;
                return false;
            }

            selectedRigType = "Runtime Emergency Camera";
            return true;
        }

        static Camera CreateRuntimeFallbackCamera(Transform parent, string cameraName, Vector3 localPosition)
        {
            if (parent == null)
                return null;

            var cameraObject = new GameObject(cameraName);
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = localPosition;
            cameraObject.transform.localRotation = Quaternion.identity;
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            EnsureCameraAudioListener(camera);
            TryAttachTrackedPoseDriver(cameraObject);
            return camera;
        }

        static void TryAttachTrackedPoseDriver(GameObject cameraObject)
        {
            if (cameraObject == null)
                return;

            var trackedPoseDriverType = Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
            if (trackedPoseDriverType == null || cameraObject.GetComponent(trackedPoseDriverType) != null)
                return;

            cameraObject.AddComponent(trackedPoseDriverType);
        }

        static void EnsureCameraAudioListener(Camera camera)
        {
            if (camera == null)
                return;

            var listener = camera.GetComponent<AudioListener>();
            if (listener == null)
                listener = camera.gameObject.AddComponent<AudioListener>();
            listener.enabled = true;
        }

        static void TryEnsureMainCameraTag(Camera camera)
        {
            if (camera == null || camera.CompareTag("MainCamera"))
                return;

            camera.tag = "MainCamera";
        }

        static void ActivateTransformHierarchy(Transform target, Transform stopAt)
        {
            var current = target;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                    current.gameObject.SetActive(true);

                if (current == stopAt)
                    break;

                current = current.parent;
            }
        }

        static Camera FindFirstCameraInHierarchy(Transform root, bool requireEnabled)
        {
            if (root == null)
                return null;

            var cameras = root.GetComponentsInChildren<Camera>(true);
            for (var i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (camera == null)
                    continue;

                if (requireEnabled && (!IsActiveAndEnabled(camera) || !camera.enabled))
                    continue;

                return camera;
            }

            return null;
        }

        List<string> DisableConflictingRigStacks(Transform selectedRigRoot)
        {
            var disabledRigRoots = new List<string>();
            if (selectedRigRoot == null)
                return disabledRigRoots;

            var disabledCameras = new List<Camera>();
            var disabledListeners = new List<AudioListener>();

            var rigRoots = new HashSet<Transform>();
            var xrOrigins = FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < xrOrigins.Length; i++)
            {
                var origin = xrOrigins[i];
                if (origin == null)
                    continue;

                var root = origin.Origin != null ? origin.Origin.transform : origin.transform;
                if (root != null)
                    rigRoots.Add(root);
            }

            var ovrRigs = FindObjectsByType<OVRCameraRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < ovrRigs.Length; i++)
            {
                var rig = ovrRigs[i];
                if (rig != null)
                    rigRoots.Add(rig.transform);
            }

            foreach (var rigRoot in rigRoots)
            {
                if (rigRoot == null || rigRoot == selectedRigRoot)
                    continue;

                var disabledAny = false;
                var cameras = rigRoot.GetComponentsInChildren<Camera>(true);
                for (var i = 0; i < cameras.Length; i++)
                {
                    var camera = cameras[i];
                    if (camera == null || !camera.enabled || camera == m_PlayerCamera)
                        continue;

                    camera.enabled = false;
                    disabledCameras.Add(camera);
                    disabledAny = true;
                }

                var listeners = rigRoot.GetComponentsInChildren<AudioListener>(true);
                for (var i = 0; i < listeners.Length; i++)
                {
                    var listener = listeners[i];
                    if (listener == null || !listener.enabled)
                        continue;

                    listener.enabled = false;
                    disabledListeners.Add(listener);
                    disabledAny = true;
                }

                if (disabledAny)
                    disabledRigRoots.Add(GetTransformPath(rigRoot));
            }

            if (CountActiveEnabledCameras() == 0)
            {
                for (var i = 0; i < disabledCameras.Count; i++)
                {
                    if (disabledCameras[i] != null)
                        disabledCameras[i].enabled = true;
                }

                for (var i = 0; i < disabledListeners.Count; i++)
                {
                    if (disabledListeners[i] != null)
                        disabledListeners[i].enabled = true;
                }

                disabledRigRoots.Clear();
                Debug.LogWarning("[VRCombat] Rig suppression would disable all active cameras; rollback applied.");
            }

            return disabledRigRoots;
        }

        static int CountActiveEnabledCameras()
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var count = 0;
            for (var i = 0; i < cameras.Length; i++)
            {
                var camera = cameras[i];
                if (!IsActiveAndEnabled(camera) || !camera.enabled)
                    continue;

                count++;
            }

            return count;
        }

        static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            var parts = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", parts.ToArray());
        }

        void LogRigSelection(string resolvedRigType, List<string> disabledRigPaths)
        {
            var disabledDescription = disabledRigPaths == null || disabledRigPaths.Count == 0
                ? "none"
                : string.Join(", ", disabledRigPaths);
            var modalityPath = m_InputModalityManager != null
                ? GetTransformPath(m_InputModalityManager.transform)
                : "none";

            Debug.Log(
                $"[VRCombat] Selected rig={resolvedRigType}, camera={GetTransformPath(m_PlayerCamera != null ? m_PlayerCamera.transform : null)}, root={GetTransformPath(m_PlayerRoot)}, modalityManager={modalityPath}, disabledRigStacks={disabledDescription}");
        }

        void ConfigureHandFirstInteraction()
        {
            if (m_PlayerRoot == null)
                return;

            ResolveModalityManagedRigTransforms(
                out m_LeftHandTransform,
                out m_RightHandTransform,
                out m_LeftControllerTransform,
                out m_RightControllerTransform);

            m_LeftHandTransform ??= FindHandTransform("left");
            m_RightHandTransform ??= FindHandTransform("right");
            m_LeftControllerTransform ??= FindControllerTransform("left");
            m_RightControllerTransform ??= FindControllerTransform("right");

            if (m_LeftHandTransform == null)
            {
                m_LeftHandTransform = m_LeftControllerTransform != null
                    ? m_LeftControllerTransform
                    : EnsureRuntimeHandAnchor("Left Runtime Hand Anchor");
            }
            if (m_RightHandTransform == null)
            {
                m_RightHandTransform = m_RightControllerTransform != null
                    ? m_RightControllerTransform
                    : EnsureRuntimeHandAnchor("Right Runtime Hand Anchor");
            }

            m_LeftControllerTransform ??= m_LeftHandTransform;
            m_RightControllerTransform ??= m_RightHandTransform;

            m_LeftHandProxyVisual = EnsureRuntimeHandProxy(m_LeftHandTransform, "Left Runtime Hand Proxy");
            m_RightHandProxyVisual = EnsureRuntimeHandProxy(m_RightHandTransform, "Right Runtime Hand Proxy");

            var allBehaviours = m_PlayerRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (var i = 0; i < allBehaviours.Length; i++)
            {
                var behaviour = allBehaviours[i];
                if (behaviour == null)
                    continue;

                var typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("HandVisualizer", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TrySetMemberValue(behaviour, "m_DrawMeshes", true);
                    TrySetMemberValue(behaviour, "drawMeshes", true);
                }
            }

            ConfigureInteractorReach(allBehaviours);
        }

        void ConfigureInteractorReach(MonoBehaviour[] behaviours)
        {
            if (behaviours == null)
                return;

            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("NearFarInteractor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    TrySetMemberValue(behaviour, "enableFarCasting", true);
                    TrySetMemberValue(behaviour, "m_EnableFarCasting", true);
                    TrySetMemberValue(behaviour, "m_EnableNearCasting", true);
                    TrySetMemberValue(behaviour, "m_EnableUIInteraction", true);

                    if (TryGetMemberValue(behaviour, "m_NearInteractionCaster", out var nearCasterObject))
                        ConfigureNearCaster(nearCasterObject as Component);
                    else if (TryGetMemberValue(behaviour, "nearInteractionCaster", out nearCasterObject))
                        ConfigureNearCaster(nearCasterObject as Component);

                    if (TryGetMemberValue(behaviour, "m_FarInteractionCaster", out var farCasterObject))
                        ConfigureFarCaster(farCasterObject as Component);
                    else if (TryGetMemberValue(behaviour, "farInteractionCaster", out farCasterObject))
                        ConfigureFarCaster(farCasterObject as Component);
                }
            }
        }

        void ConfigureNearCaster(Component caster)
        {
            if (caster == null)
                return;

            TrySetMemberValue(caster, "m_CastRadius", m_NearGrabRadius);
            TrySetMemberValue(caster, "m_SphereCastRadius", m_NearGrabRadius);
            TrySetMemberValue(caster, "m_CastDistance", m_NearGrabDistance);

            if (caster is Behaviour casterBehaviour)
                casterBehaviour.enabled = true;
        }

        void ConfigureFarCaster(Component caster)
        {
            if (caster == null)
                return;

            TrySetMemberValue(caster, "m_CastDistance", Mathf.Max(m_UiFarInteractionDistance, m_NearGrabDistance + 0.1f));
            TrySetMemberValue(caster, "m_SphereCastRadius", Mathf.Min(m_NearGrabRadius * 0.55f, 0.08f));
            TrySetMemberValue(caster, "m_ConeCastAngle", 4.5f);

            if (caster is Behaviour casterBehaviour)
                casterBehaviour.enabled = true;
        }

        void DisableTutorialObjects()
        {
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var current = allTransforms[i];
                if (current == null)
                    continue;

                var lowerName = current.name.ToLowerInvariant();
                if (ShouldDisableTutorialObject(lowerName))
                    current.gameObject.SetActive(false);
            }

            var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < allBehaviours.Length; i++)
            {
                var behaviour = allBehaviours[i];
                if (behaviour == null)
                    continue;

                var typeName = behaviour.GetType().Name.ToLowerInvariant();
                if (typeName.Contains("callout") ||
                    typeName.Contains("tutorial") ||
                    typeName.Contains("stepmanager"))
                {
                    behaviour.gameObject.SetActive(false);
                }
            }
        }

        void UpdateControllerFallbackHandMapping()
        {
            SetProxyActive(m_LeftHandProxyVisual, false);
            SetProxyActive(m_RightHandProxyVisual, false);
        }

        void UpdateArenaGroundAlignment()
        {
            if (m_IsRestarting || m_PlayerRoot == null || m_PlayerCamera == null || m_ArenaSurfaceColliders.Count == 0)
                return;

            if (!TryGetArenaGroundHit(out var groundHit))
                return;

            var desiredRootY = groundHit.point.y + Mathf.Max(ArenaGroundSnapYOffset, m_TeleportSpawnHeightOffset);
            var currentRootY = m_PlayerRoot.position.y;
            var delta = desiredRootY - currentRootY;
            if (delta > ArenaGroundSnapMaxStepUp || delta < -ArenaGroundSnapMaxStepDown)
                return;

            if (Mathf.Abs(delta) <= 0.001f)
                return;

            var rootPosition = m_PlayerRoot.position;
            rootPosition.y = desiredRootY;
            m_PlayerRoot.position = rootPosition;
        }

        bool TryGetArenaGroundHit(out RaycastHit bestHit)
        {
            bestHit = default;
            if (m_PlayerCamera == null || m_ArenaSurfaceColliders.Count == 0)
                return false;

            var origin = m_PlayerCamera.transform.position + Vector3.up * ArenaGroundSnapProbeHeight;
            var distance = ArenaGroundSnapProbeHeight + ArenaGroundSnapDistance;
            var hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                s_PlayerGroundHitBuffer,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            var closestDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = s_PlayerGroundHitBuffer[i];
                var collider = hit.collider;
                if (collider == null || !m_ArenaSurfaceColliders.Contains(collider))
                    continue;

                if (hit.normal.y < ArenaGroundSnapNormalThreshold)
                    continue;

                if (hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                bestHit = hit;
            }

            return closestDistance < float.PositiveInfinity;
        }

        static bool IsTrackedHandActive(InputDeviceCharacteristics handednessFlag)
        {
            s_HandDeviceBuffer.Clear();
            var desiredCharacteristics =
                InputDeviceCharacteristics.HandTracking |
                InputDeviceCharacteristics.TrackedDevice |
                handednessFlag;

            InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, s_HandDeviceBuffer);
            for (var i = 0; i < s_HandDeviceBuffer.Count; i++)
            {
                var device = s_HandDeviceBuffer[i];
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) && isTracked)
                    return true;
            }

            return false;
        }

        static bool IsTrackedControllerActive(InputDeviceCharacteristics handednessFlag)
        {
            s_ControllerDeviceBuffer.Clear();
            var desiredCharacteristics =
                InputDeviceCharacteristics.Controller |
                InputDeviceCharacteristics.TrackedDevice |
                handednessFlag;

            InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, s_ControllerDeviceBuffer);
            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                var device = s_ControllerDeviceBuffer[i];
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) && isTracked)
                    return true;
            }

            return false;
        }

        static bool IsTrackedNonHandDeviceActive(InputDeviceCharacteristics handednessFlag)
        {
            s_ControllerDeviceBuffer.Clear();
            var desiredCharacteristics = InputDeviceCharacteristics.TrackedDevice | handednessFlag;
            InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, s_ControllerDeviceBuffer);
            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                var device = s_ControllerDeviceBuffer[i];
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) && !isTracked)
                    continue;

                if ((device.characteristics & InputDeviceCharacteristics.HandTracking) != 0)
                    continue;

                return true;
            }

            return false;
        }

        static bool TryGetResolvedControllerDevice(
            InputDeviceCharacteristics handednessFlag,
            XRNode handNode,
            Transform controllerTransform,
            out XRInputDevice resolvedDevice)
        {
            resolvedDevice = default;

            s_ControllerDeviceBuffer.Clear();
            var desiredCharacteristics =
                InputDeviceCharacteristics.Controller |
                InputDeviceCharacteristics.TrackedDevice |
                handednessFlag;
            InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, s_ControllerDeviceBuffer);

            if (TryGetClosestDeviceToTransform(s_ControllerDeviceBuffer, controllerTransform, out resolvedDevice))
                return true;

            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                var device = s_ControllerDeviceBuffer[i];
                if (!device.isValid)
                    continue;

                resolvedDevice = device;
                return true;
            }

            resolvedDevice = InputDevices.GetDeviceAtXRNode(handNode);
            return resolvedDevice.isValid;
        }

        static bool TryGetClosestDeviceToTransform(List<XRInputDevice> devices, Transform targetTransform, out XRInputDevice resolvedDevice)
        {
            resolvedDevice = default;
            if (devices == null || devices.Count == 0 || targetTransform == null)
                return false;

            var bestDistance = float.PositiveInfinity;
            var foundAny = false;
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) && !isTracked)
                    continue;

                if (!device.TryGetFeatureValue(XRCommonUsages.devicePosition, out var devicePosition))
                    continue;

                var distance = (devicePosition - targetTransform.position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                resolvedDevice = device;
                foundAny = true;
            }

            return foundAny;
        }

        GameObject EnsureRuntimeHandProxy(Transform handTransform, string proxyName)
        {
            if (handTransform == null)
                return null;

            var existing = handTransform.Find(proxyName);
            if (existing != null)
                return existing.gameObject;

            var proxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            proxy.name = proxyName;
            proxy.transform.SetParent(handTransform, false);
            proxy.transform.localPosition = new Vector3(0f, -0.015f, 0.05f);
            proxy.transform.localRotation = Quaternion.identity;
            proxy.transform.localScale = new Vector3(0.115f, 0.065f, 0.145f);

            var proxyCollider = proxy.GetComponent<Collider>();
            if (proxyCollider != null)
                Destroy(proxyCollider);

            var proxyMaterial = GetOrCreateHandProxyMaterial();
            ApplyMaterial(proxy, proxyMaterial);
            proxy.SetActive(false);
            return proxy;
        }

        Transform EnsureRuntimeHandAnchor(string anchorName)
        {
            if (m_PlayerRoot == null || string.IsNullOrWhiteSpace(anchorName))
                return null;

            var existing = m_PlayerRoot.Find(anchorName);
            if (existing != null)
                return existing;

            var anchorObject = new GameObject(anchorName);
            anchorObject.transform.SetParent(m_PlayerRoot, false);
            anchorObject.transform.position = m_PlayerCamera != null ? m_PlayerCamera.transform.position : m_PlayerRoot.position;
            anchorObject.transform.rotation = Quaternion.identity;
            return anchorObject.transform;
        }

        static void SetProxyActive(GameObject proxy, bool shouldBeActive)
        {
            if (proxy == null || proxy.activeSelf == shouldBeActive)
                return;

            proxy.SetActive(shouldBeActive);
        }

        bool ReadAnyRestartButton()
        {
            return ReadRestartButtonForNode(XRNode.LeftHand) || ReadRestartButtonForNode(XRNode.RightHand);
        }

        bool ConsumeMenuButtonPress()
        {
            var runtimeActionPressed = ReadRuntimePauseMenuActionDown();
            var rawMenuPressed = ReadRawMenuButtonDown()
                || ReadGenericInputSystemPauseButtonDown()
                || ConsumeMetaMenuGesturePress();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            LogPauseAttemptDiagnostics(runtimeActionPressed, rawMenuPressed);
#endif
            return runtimeActionPressed || rawMenuPressed;
        }

        void RequestPauseMenuToggle()
        {
            if (m_IsGameOver || m_IsRestarting)
                return;

            if (Time.unscaledTime - m_LastPauseToggleTime < PauseToggleDebounceSeconds)
                return;

            m_LastPauseToggleTime = Time.unscaledTime;
            SetPauseMenuOpen(!m_IsPauseMenuOpen);
        }

        void SetupPauseMenuInputActions()
        {
            if (m_RuntimePauseMenuAction == null)
            {
                m_RuntimePauseMenuAction = new UnityEngine.InputSystem.InputAction(
                    "Runtime Pause Menu",
                    UnityEngine.InputSystem.InputActionType.Button);

                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<XRController>{LeftHand}/menu");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<XRController>{LeftHand}/menuButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<XRController>{LeftHand}/systemButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<XRController>{LeftHand}/start");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/menu");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/menuButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/systemButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/start");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestTouchPlusController>{LeftHand}/menu");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestTouchPlusController>{LeftHand}/menuButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestTouchPlusController>{LeftHand}/systemButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestTouchPlusController>{LeftHand}/start");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestProTouchController>{LeftHand}/menu");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestProTouchController>{LeftHand}/menuButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestProTouchController>{LeftHand}/systemButton");
                // Quest 3 Touch Plus specific - secondaryButton is the hamburger/menu button on left controller
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<XRController>{LeftHand}/secondaryButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/secondaryButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestTouchPlusController>{LeftHand}/secondaryButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<MetaQuestTouchPlusController>{LeftHand}/menu");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<MetaQuestTouchPlusController>{LeftHand}/menuButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<MetaQuestTouchPlusController>{LeftHand}/secondaryButton");
                // Thumbstick click fallback
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<XRController>{LeftHand}/primary2DAxisClick");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<XRController>{LeftHand}/thumbstickClicked");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/primary2DAxisClick");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/thumbstickClicked");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestTouchPlusController>{LeftHand}/primary2DAxisClick");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestTouchPlusController>{LeftHand}/thumbstickClicked");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestProTouchController>{LeftHand}/primary2DAxisClick");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<QuestProTouchController>{LeftHand}/thumbstickClicked");
            }

            if (!m_RuntimePauseMenuAction.enabled)
                m_RuntimePauseMenuAction.Enable();

            ConfigureMetaControllerButtonsMapperPauseBinding();
            RebindMetaMenuGestureDetector();
            m_PendingMetaMenuGesture = false;
        }

        void ConfigureMetaControllerButtonsMapperPauseBinding()
        {
            var mappers = FindObjectsByType<ControllerButtonsMapper>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < mappers.Length; i++)
            {
                var mapper = mappers[i];
                if (mapper == null)
                    continue;

                if (mapper.ButtonClickActions == null)
                    mapper.ButtonClickActions = new List<ControllerButtonsMapper.ButtonClickAction>();

                var actions = mapper.ButtonClickActions;
                var actionIndex = -1;
                for (var actionOffset = 0; actionOffset < actions.Count; actionOffset++)
                {
                    var existingAction = actions[actionOffset];
                    if (!string.Equals(existingAction.Title, PauseMapperActionTitle, StringComparison.Ordinal))
                        continue;

                    actionIndex = actionOffset;
                    break;
                }

                var callbackEvent = new UnityEvent();
                callbackEvent.AddListener(OnControllerButtonsMapperPausePressed);

                var pauseAction = new ControllerButtonsMapper.ButtonClickAction
                {
                    Title = PauseMapperActionTitle,
                    Button = OVRInput.Button.PrimaryThumbstick,
                    ButtonMode = ControllerButtonsMapper.ButtonClickAction.ButtonClickMode.OnButtonDown,
                    Callback = callbackEvent
                };

#if ENABLE_INPUT_SYSTEM && UNITY_NEW_INPUT_SYSTEM_INSTALLED
                if (m_RuntimePauseMenuAction != null)
                    pauseAction.InputActionReference = UnityEngine.InputSystem.InputActionReference.Create(m_RuntimePauseMenuAction);
#endif

                if (actionIndex >= 0)
                    actions[actionIndex] = pauseAction;
                else
                    actions.Add(pauseAction);

                RefreshControllerButtonsMapperInputSubscriptions(mapper);
            }
        }

        static void RefreshControllerButtonsMapperInputSubscriptions(ControllerButtonsMapper mapper)
        {
            if (mapper == null || !mapper.isActiveAndEnabled)
                return;

            mapper.enabled = false;
            mapper.enabled = true;
        }

        void OnControllerButtonsMapperPausePressed()
        {
            RequestPauseMenuToggle();
        }

        bool ReadRuntimePauseMenuActionDown()
        {
            if (m_RuntimePauseMenuAction == null)
                SetupPauseMenuInputActions();

            return m_RuntimePauseMenuAction != null &&
                (m_RuntimePauseMenuAction.WasPressedThisFrame() || m_RuntimePauseMenuAction.WasPerformedThisFrame());
        }

        bool ReadGenericInputSystemPauseButtonDown()
        {
            var inputSystemDevices = UnityEngine.InputSystem.InputSystem.devices;
            for (var i = 0; i < inputSystemDevices.Count; i++)
            {
                var device = inputSystemDevices[i];
                if (device == null || !device.added)
                    continue;

                if (WasAnyMenuControlPressedThisFrame(device))
                    return true;

                if (IsLikelyLeftHandInputSystemDevice(device) && WasAnyThumbstickPauseControlPressedThisFrame(device))
                    return true;
            }

            return false;
        }

        static bool WasAnyMenuControlPressedThisFrame(UnityEngine.InputSystem.InputDevice device)
        {
            return WasInputSystemButtonPressedThisFrame(device, "menu")
                || WasInputSystemButtonPressedThisFrame(device, "menuButton")
                || WasInputSystemButtonPressedThisFrame(device, "systemButton")
                || WasInputSystemButtonPressedThisFrame(device, "start");
        }

        static bool WasAnyThumbstickPauseControlPressedThisFrame(UnityEngine.InputSystem.InputDevice device)
        {
            return WasInputSystemButtonPressedThisFrame(device, "primary2DAxisClick")
                || WasInputSystemButtonPressedThisFrame(device, "thumbstickClicked")
                || WasInputSystemButtonPressedThisFrame(device, "joystickClicked");
        }

        static bool WasInputSystemButtonPressedThisFrame(UnityEngine.InputSystem.InputDevice device, string controlPath)
        {
            if (device == null || !device.added || string.IsNullOrWhiteSpace(controlPath))
                return false;

            var buttonControl = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(controlPath);
            return buttonControl != null && buttonControl.wasPressedThisFrame;
        }

        static bool HasInputSystemUsage(UnityEngine.InputSystem.InputDevice device, string usageName)
        {
            if (device == null || string.IsNullOrWhiteSpace(usageName))
                return false;

            var usages = device.usages;
            for (var i = 0; i < usages.Count; i++)
            {
                if (string.Equals(usages[i].ToString(), usageName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool IsLikelyLeftHandInputSystemDevice(UnityEngine.InputSystem.InputDevice device)
        {
            if (device == null)
                return false;

            if (HasInputSystemUsage(device, "LeftHand"))
                return true;

            var descriptor = $"{device.displayName} {device.name} {device.layout}";
            return descriptor.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool ConsumeMetaMenuGesturePress()
        {
            if (!m_PendingMetaMenuGesture)
                return false;

            m_PendingMetaMenuGesture = false;
            return true;
        }

        void RebindMetaMenuGestureDetector()
        {
            var detector = FindAnyObjectByType<MetaSystemGestureDetector>(FindObjectsInactive.Include);
            if (detector == m_MetaMenuGestureDetector)
                return;

            UnbindMetaMenuGestureDetector();
            m_MetaMenuGestureDetector = detector;
            if (m_MetaMenuGestureDetector != null)
                m_MetaMenuGestureDetector.menuPressed.AddListener(OnMetaMenuGesturePressed);
        }

        void UnbindMetaMenuGestureDetector()
        {
            if (m_MetaMenuGestureDetector == null)
                return;

            m_MetaMenuGestureDetector.menuPressed.RemoveListener(OnMetaMenuGesturePressed);
            m_MetaMenuGestureDetector = null;
        }

        void OnMetaMenuGesturePressed()
        {
            m_PendingMetaMenuGesture = true;
        }

        static bool IsInputSystemButtonPressed(UnityEngine.InputSystem.InputDevice device, string controlPath)
        {
            if (device == null || !device.added || string.IsNullOrWhiteSpace(controlPath))
                return false;

            var control = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(controlPath);
            return control != null && control.isPressed;
        }

        static bool HasInputSystemButtonControl(UnityEngine.InputSystem.InputDevice device, string controlPath)
        {
            if (device == null || !device.added || string.IsNullOrWhiteSpace(controlPath))
                return false;

            return device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(controlPath) != null;
        }

        static bool HasInputSystemPauseMenuControl(UnityEngine.InputSystem.InputDevice device)
        {
            return HasInputSystemButtonControl(device, "menu")
                || HasInputSystemButtonControl(device, "menuButton")
                || HasInputSystemButtonControl(device, "systemButton")
                || HasInputSystemButtonControl(device, "start");
        }

        static bool IsInputSystemPauseFallbackPressed(UnityEngine.InputSystem.InputDevice device)
        {
            return IsInputSystemButtonPressed(device, "primary2DAxisClick")
                || IsInputSystemButtonPressed(device, "thumbstickClicked")
                || IsInputSystemButtonPressed(device, "joystickClicked");
        }

        void LogPauseInputDiagnosticsAtStartup()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (m_HasLoggedPauseStartupDiagnostics)
                return;

            m_HasLoggedPauseStartupDiagnostics = true;
            var leftHandDevice = TryGetResolvedControllerDevice(
                InputDeviceCharacteristics.Left,
                XRNode.LeftHand,
                m_LeftControllerTransform,
                out var resolvedLeftDevice)
                ? resolvedLeftDevice
                : InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var hasXrMenuUsage = leftHandDevice.isValid &&
                HasMenuSpecificButtonUsage(leftHandDevice);

            var inputSystemLeft = UnityEngine.InputSystem.XR.XRController.leftHand;
            var hasInputSystemMenuControl = HasInputSystemPauseMenuControl(inputSystemLeft);

            var xrDevices = new List<XRInputDevice>();
            InputDevices.GetDevices(xrDevices);
            var xrDeviceSummary = new StringBuilder();
            for (var i = 0; i < xrDevices.Count; i++)
            {
                if (i > 0)
                    xrDeviceSummary.Append(" | ");
                var xrDevice = xrDevices[i];
                xrDeviceSummary.Append(xrDevice.isValid ? $"{xrDevice.name}:{xrDevice.characteristics}" : "<invalid>");
            }

            var inputSystemSummary = new StringBuilder();
            var inputSystemDevices = UnityEngine.InputSystem.InputSystem.devices;
            for (var i = 0; i < inputSystemDevices.Count; i++)
            {
                if (i > 0)
                    inputSystemSummary.Append(" | ");
                var device = inputSystemDevices[i];
                inputSystemSummary.Append($"{device.displayName}/{device.layout}");
            }

            Debug.Log(
                $"[VRCombat] Pause diagnostics: actionEnabled={(m_RuntimePauseMenuAction != null && m_RuntimePauseMenuAction.enabled)}, leftController={GetTransformPath(m_LeftControllerTransform)}, leftXRDeviceValid={leftHandDevice.isValid}, leftXRMenuFeature={hasXrMenuUsage}, leftInputSystemMenuControl={hasInputSystemMenuControl}, xrDevices=[{xrDeviceSummary}], inputSystemDevices=[{inputSystemSummary}]");
#endif
        }

        bool ReadRawMenuButtonDown()
        {
            var isPressed = IsRawMenuButtonPressed();
            var wasPressed = m_WasRawMenuButtonPressed;
            m_WasRawMenuButtonPressed = isPressed;
            return isPressed && !wasPressed;
        }

        bool IsRawMenuButtonPressed()
        {
            if (TryGetResolvedControllerDevice(
                    InputDeviceCharacteristics.Left,
                    XRNode.LeftHand,
                    m_LeftControllerTransform,
                    out var resolvedLeftDevice))
            {
                if (IsMenuSpecificButtonPressed(resolvedLeftDevice) || IsPauseFallbackThumbstickPressed(resolvedLeftDevice))
                    return true;
            }

            if (IsAnyMenuSpecificButtonPressed(
                    InputDeviceCharacteristics.Left |
                    InputDeviceCharacteristics.Controller |
                    InputDeviceCharacteristics.TrackedDevice))
            {
                return true;
            }

            if (IsAnyPauseFallbackThumbstickPressed(
                    InputDeviceCharacteristics.Left |
                    InputDeviceCharacteristics.Controller |
                    InputDeviceCharacteristics.TrackedDevice))
            {
                return true;
            }

            if (IsAnyMenuSpecificButtonPressed(
                    InputDeviceCharacteristics.Left |
                    InputDeviceCharacteristics.TrackedDevice))
            {
                return true;
            }

            var left = UnityEngine.InputSystem.XR.XRController.leftHand;
            if (IsInputSystemButtonPressed(left, "menu")
                || IsInputSystemButtonPressed(left, "menuButton")
                || IsInputSystemButtonPressed(left, "systemButton")
                || IsInputSystemButtonPressed(left, "start")
                || IsInputSystemPauseFallbackPressed(left))
            {
                return true;
            }

            var inputSystemDevices = UnityEngine.InputSystem.InputSystem.devices;
            for (var i = 0; i < inputSystemDevices.Count; i++)
            {
                var device = inputSystemDevices[i];
                if (device == null)
                    continue;

                var descriptor = $"{device.displayName} {device.name} {device.layout}";
                if (descriptor.IndexOf("left", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (IsInputSystemButtonPressed(device, "menu")
                    || IsInputSystemButtonPressed(device, "menuButton")
                    || IsInputSystemButtonPressed(device, "systemButton")
                    || IsInputSystemButtonPressed(device, "start")
                    || IsInputSystemPauseFallbackPressed(device))
                {
                    return true;
                }
            }

            try
            {
                // Standard menu buttons
                if (OVRInput.Get(OVRInput.Button.Start)
                    || OVRInput.Get(OVRInput.RawButton.Start))
                    return true;

                // Quest 3 left controller hamburger/menu button (Button.Three on left hand)
                if (OVRInput.Get(OVRInput.Button.Three, OVRInput.Controller.LTouch)
                    || OVRInput.Get(OVRInput.RawButton.Y))
                    return true;

                // Also check for Menu button explicitly
                if (OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.LTouch))
                    return true;

                if (OVRInput.Get(OVRInput.Button.PrimaryThumbstick)
                    || OVRInput.Get(OVRInput.RawButton.LThumbstick))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        static bool IsAnyMenuSpecificButtonPressed(InputDeviceCharacteristics desiredCharacteristics)
        {
            s_ControllerDeviceBuffer.Clear();
            InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, s_ControllerDeviceBuffer);
            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                if (IsMenuSpecificButtonPressed(s_ControllerDeviceBuffer[i]))
                    return true;
            }

            return false;
        }

        static bool IsAnyPauseFallbackThumbstickPressed(InputDeviceCharacteristics desiredCharacteristics)
        {
            s_ControllerDeviceBuffer.Clear();
            InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, s_ControllerDeviceBuffer);
            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                if (IsPauseFallbackThumbstickPressed(s_ControllerDeviceBuffer[i]))
                    return true;
            }

            return false;
        }

        void LogPauseAttemptDiagnostics(bool runtimeActionPressed, bool rawMenuPressed)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (m_HasLoggedFirstPauseAttempt || (!runtimeActionPressed && !rawMenuPressed))
                return;

            m_HasLoggedFirstPauseAttempt = true;
            Debug.Log(
                $"[VRCombat] First pause attempt: runtimeActionPressed={runtimeActionPressed}, rawMenuPressed={rawMenuPressed}, actionEnabled={(m_RuntimePauseMenuAction != null && m_RuntimePauseMenuAction.enabled)}");
#endif
        }

        static bool ShouldDisableTutorialObject(string lowerName)
        {
            if (string.IsNullOrEmpty(lowerName))
                return false;

            return lowerName.Contains("callout")
                || lowerName.Contains("tutorial")
                || lowerName.Contains("button function")
                || lowerName.Contains("buttonfunction")
                || lowerName.Contains("affordance callout");
        }

        static bool TryGetFallbackHandPose(XRNode handNode, Transform controllerTransform, out Vector3 position, out Quaternion rotation)
        {
            var device = InputDevices.GetDeviceAtXRNode(handNode);
            if (device.isValid &&
                (!device.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) || isTracked))
            {
                var gotPosition = device.TryGetFeatureValue(XRCommonUsages.devicePosition, out position);
                var gotRotation = device.TryGetFeatureValue(XRCommonUsages.deviceRotation, out rotation);
                if (gotPosition || gotRotation)
                {
                    if (!gotPosition && controllerTransform != null)
                        position = controllerTransform.position;
                    if (!gotRotation && controllerTransform != null)
                        rotation = controllerTransform.rotation;
                    return true;
                }
            }

            if (controllerTransform != null)
            {
                position = controllerTransform.position;
                rotation = controllerTransform.rotation;
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        static bool TryGetControllerFallbackPose(
            InputDeviceCharacteristics handednessFlag,
            XRNode handNode,
            Transform controllerTransform,
            out Vector3 position,
            out Quaternion rotation)
        {
            s_ControllerDeviceBuffer.Clear();
            var desiredCharacteristics =
                InputDeviceCharacteristics.Controller |
                InputDeviceCharacteristics.TrackedDevice |
                handednessFlag;

            InputDevices.GetDevicesWithCharacteristics(desiredCharacteristics, s_ControllerDeviceBuffer);
            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                var controllerDevice = s_ControllerDeviceBuffer[i];
                if (!controllerDevice.isValid)
                    continue;

                if (controllerDevice.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) && !isTracked)
                    continue;

                var gotPosition = controllerDevice.TryGetFeatureValue(XRCommonUsages.devicePosition, out position);
                var gotRotation = controllerDevice.TryGetFeatureValue(XRCommonUsages.deviceRotation, out rotation);
                if (gotPosition || gotRotation)
                {
                    if (!gotPosition && controllerTransform != null)
                        position = controllerTransform.position;
                    if (!gotRotation && controllerTransform != null)
                        rotation = controllerTransform.rotation;
                    return true;
                }
            }

            s_ControllerDeviceBuffer.Clear();
            var broadCharacteristics = InputDeviceCharacteristics.TrackedDevice | handednessFlag;
            InputDevices.GetDevicesWithCharacteristics(broadCharacteristics, s_ControllerDeviceBuffer);
            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                var trackedDevice = s_ControllerDeviceBuffer[i];
                if (!trackedDevice.isValid)
                    continue;

                if (trackedDevice.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) && !isTracked)
                    continue;

                var gotPosition = trackedDevice.TryGetFeatureValue(XRCommonUsages.devicePosition, out position);
                var gotRotation = trackedDevice.TryGetFeatureValue(XRCommonUsages.deviceRotation, out rotation);
                if (gotPosition || gotRotation)
                {
                    if (!gotPosition && controllerTransform != null)
                        position = controllerTransform.position;
                    if (!gotRotation && controllerTransform != null)
                        rotation = controllerTransform.rotation;
                    return true;
                }
            }

            return TryGetFallbackHandPose(handNode, controllerTransform, out position, out rotation);
        }

        static bool ReadRestartButtonForNode(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(XRCommonUsages.primaryButton, out var primaryPressed) && primaryPressed)
                return true;

            if (device.TryGetFeatureValue(XRCommonUsages.secondaryButton, out var secondaryPressed) && secondaryPressed)
                return true;

            return false;
        }

        static bool HasMenuSpecificButtonUsage(XRInputDevice device)
        {
            if (!device.isValid)
                return false;

            return device.TryGetFeatureValue(XRCommonUsages.menuButton, out _)
                || device.TryGetFeatureValue(new InputFeatureUsage<bool>("menu"), out _)
                || device.TryGetFeatureValue(new InputFeatureUsage<bool>("applicationMenu"), out _)
                || device.TryGetFeatureValue(new InputFeatureUsage<bool>("appMenuButton"), out _)
                || device.TryGetFeatureValue(new InputFeatureUsage<bool>("start"), out _);
        }

        static bool IsMenuSpecificButtonPressed(XRInputDevice device)
        {
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(XRCommonUsages.menuButton, out var menuPressed) && menuPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("menu"), out var menuPressedByName) && menuPressedByName)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("applicationMenu"), out var applicationMenuPressed) && applicationMenuPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("appMenuButton"), out var appMenuPressed) && appMenuPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("start"), out var startPressed) && startPressed)
                return true;

            // Quest 3 Touch Plus specific - hamburger/menu button mappings
            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("menuButton"), out var menuButtonPressed) && menuButtonPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("systemButton"), out var systemButtonPressed) && systemButtonPressed)
                return true;

            return false;
        }

        static bool IsPauseFallbackThumbstickPressed(XRInputDevice device)
        {
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(XRCommonUsages.primary2DAxisClick, out var thumbstickPressed) && thumbstickPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("primary2DAxisClick"), out var thumbstickPressedByName) && thumbstickPressedByName)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("thumbstickClicked"), out var thumbstickClicked) && thumbstickClicked)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("joystickClicked"), out var joystickClicked) && joystickClicked)
                return true;

            return false;
        }

        void SetupPlayerDamageDetection()
        {
            var cameraObject = m_PlayerCamera.gameObject;
            var vignetteFeedback = cameraObject.GetComponent<DamageVignetteFeedback>();
            if (vignetteFeedback == null)
                vignetteFeedback = cameraObject.AddComponent<DamageVignetteFeedback>();

            var hurtboxTransform = m_PlayerDamageReceiver != null ? m_PlayerDamageReceiver.transform : null;
            if (hurtboxTransform == null)
            {
                var existingReceiver = FindAnyObjectByType<PlayerDamageReceiver>(FindObjectsInactive.Include);
                if (existingReceiver != null)
                    hurtboxTransform = existingReceiver.transform;
            }

            if (hurtboxTransform == null)
                hurtboxTransform = m_PlayerRoot.Find("Player Hurtbox");

            if (hurtboxTransform == null)
            {
                var hurtboxObject = new GameObject("Player Hurtbox");
                hurtboxTransform = hurtboxObject.transform;
            }
            if (hurtboxTransform.parent != m_PlayerRoot)
                hurtboxTransform.SetParent(m_PlayerRoot, false);

            var hurtboxGameObject = hurtboxTransform.gameObject;

            var follower = hurtboxGameObject.GetComponent<PlayerHurtboxFollower>();
            if (follower == null)
                follower = hurtboxGameObject.AddComponent<PlayerHurtboxFollower>();
            follower.Configure(m_PlayerCamera.transform, m_PlayerHitboxRadius);

            var capsuleCollider = hurtboxGameObject.GetComponent<CapsuleCollider>();
            if (capsuleCollider == null)
                capsuleCollider = hurtboxGameObject.AddComponent<CapsuleCollider>();
            capsuleCollider.isTrigger = true;
            capsuleCollider.direction = 1;
            capsuleCollider.radius = m_PlayerHitboxRadius;

            var rigidbody = hurtboxGameObject.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = hurtboxGameObject.AddComponent<Rigidbody>();

            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            m_PlayerDamageReceiver = hurtboxGameObject.GetComponent<PlayerDamageReceiver>();
            if (m_PlayerDamageReceiver == null)
                m_PlayerDamageReceiver = hurtboxGameObject.AddComponent<PlayerDamageReceiver>();

            m_PlayerDamageReceiver.SetDamageFeedback(vignetteFeedback);
            m_PlayerDamageReceiver.SetPlayerRootTransform(m_PlayerRoot);
            EnsureRuntimeKillZoneBoundary();
        }

        void SetupMovementVignetteControl()
        {
            if (m_PlayerRoot != null)
                m_TunnelingVignetteController = m_PlayerRoot.GetComponentInChildren<TunnelingVignetteController>(true);
            if (m_TunnelingVignetteController == null)
                m_TunnelingVignetteController = FindAnyObjectByType<TunnelingVignetteController>(FindObjectsInactive.Include);

            var initialized = PlayerPrefs.GetInt(MovementVignetteInitializedPrefKey, 0) == 1;
            if (!initialized)
            {
                m_MovementVignetteStrength = Mathf.Clamp01(m_DefaultMovementVignetteStrength);
                PlayerPrefs.SetFloat(MovementVignetteStrengthPrefKey, m_MovementVignetteStrength);
                PlayerPrefs.SetInt(MovementVignetteInitializedPrefKey, 1);
                PlayerPrefs.Save();
            }
            else
            {
                m_MovementVignetteStrength = Mathf.Clamp01(
                    PlayerPrefs.GetFloat(MovementVignetteStrengthPrefKey, m_DefaultMovementVignetteStrength));
            }

            ApplyMovementVignetteStrength(m_MovementVignetteStrength);
        }

        void SetMovementVignetteStrength(float normalizedStrength)
        {
            m_MovementVignetteStrength = Mathf.Clamp01(normalizedStrength);
            ApplyMovementVignetteStrength(m_MovementVignetteStrength);
            PlayerPrefs.SetFloat(MovementVignetteStrengthPrefKey, m_MovementVignetteStrength);
            PlayerPrefs.Save();
        }

        void ApplyMovementVignetteStrength(float normalizedStrength)
        {
            if (m_TunnelingVignetteController == null)
                return;

            var strength = Mathf.Clamp01(normalizedStrength);
            var apertureSize = Mathf.Lerp(1f, 0.3f, strength);
            var feathering = Mathf.Lerp(0f, 0.24f, strength);
            var easeTime = Mathf.Lerp(0f, 0.22f, strength);

            var defaultParameters = m_TunnelingVignetteController.defaultParameters ?? new VignetteParameters();
            defaultParameters.apertureSize = apertureSize;
            defaultParameters.featheringEffect = feathering;
            defaultParameters.easeInTime = easeTime;
            defaultParameters.easeOutTime = easeTime;
            defaultParameters.easeOutDelayTime = 0f;
            defaultParameters.easeInTimeLock = strength > 0.001f;
            defaultParameters.vignetteColor = Color.black;
            defaultParameters.vignetteColorBlend = Color.black;
            m_TunnelingVignetteController.defaultParameters = defaultParameters;

            var providers = m_TunnelingVignetteController.locomotionVignetteProviders;
            if (providers == null)
                return;

            for (var i = 0; i < providers.Count; i++)
            {
                var provider = providers[i];
                if (provider == null)
                    continue;

                provider.enabled = true;
                provider.overrideDefaultParameters = true;
                if (provider.overrideParameters == null)
                    provider.overrideParameters = new VignetteParameters();

                provider.overrideParameters.CopyFrom(defaultParameters);
            }
        }

        void SetupCombatHud()
        {
            var hudObject = new GameObject("Combat HUD Runtime");
            m_CombatHud = hudObject.AddComponent<CombatHUDRuntime>();
            m_CombatHud.Initialize(
                RestartCurrentScene,
                QuitGame,
                () => SetPauseMenuOpen(false),
                SetMovementVignetteStrength,
                m_MovementVignetteStrength,
                m_PlayerCamera,
                m_PlayerCamera.transform);

            if (m_PlayerDamageReceiver == null)
                return;

            m_PlayerDamageReceiver.HealthChanged += OnPlayerHealthChanged;
            m_PlayerDamageReceiver.DamageTaken += OnPlayerDamageTaken;
            m_PlayerDamageReceiver.Died += OnPlayerDied;

            OnPlayerHealthChanged(m_PlayerDamageReceiver.CurrentHealth, m_PlayerDamageReceiver.MaxHealth);
        }

        void RebindResolvedRigDependencies()
        {
            if (m_PlayerRoot == null || m_PlayerCamera == null)
                return;

            SetupPlayerDamageDetection();
            SetupMovementVignetteControl();
            ConfigureHandFirstInteraction();
            SetupPauseMenuInputActions();
            m_CombatHud?.SetViewAnchor(m_PlayerCamera, m_PlayerCamera.transform);
        }

        void ValidateRigCoherency(string phase)
        {
            if (m_PlayerRoot == null || m_PlayerCamera == null)
                return;

            if (IsRigCoherent())
            {
                m_PlayerDamageReceiver?.SetKnockbackEnabled(true);
                return;
            }

            Debug.LogWarning($"[VRCombat] Rig coherency mismatch during {phase}. Attempting one rig re-resolve.");
            if (TryResolvePlayerRig(forceRefresh: true))
                RebindResolvedRigDependencies();

            if (IsRigCoherent())
            {
                m_PlayerDamageReceiver?.SetKnockbackEnabled(true);
                return;
            }

            m_PlayerDamageReceiver?.SetKnockbackEnabled(false);
            Debug.LogError($"[VRCombat] Rig coherency still invalid during {phase}. Knockback disabled to prevent camera/controller desync.");
        }

        bool IsRigCoherent()
        {
            if (!IsTransformUnderRoot(m_PlayerCamera != null ? m_PlayerCamera.transform : null, m_PlayerRoot))
                return false;
            if (!IsTransformUnderRoot(m_PlayerDamageReceiver != null ? m_PlayerDamageReceiver.transform : null, m_PlayerRoot))
                return false;
            if (!IsTransformUnderRoot(m_LeftHandTransform, m_PlayerRoot))
                return false;
            if (!IsTransformUnderRoot(m_RightHandTransform, m_PlayerRoot))
                return false;
            if (!IsTransformUnderRoot(m_LeftControllerTransform, m_PlayerRoot))
                return false;
            if (!IsTransformUnderRoot(m_RightControllerTransform, m_PlayerRoot))
                return false;

            return true;
        }

        static bool IsTransformUnderRoot(Transform target, Transform root)
        {
            if (target == null || root == null)
                return true;

            return target == root || target.IsChildOf(root);
        }

        void OnPlayerHealthChanged(float currentHealth, float maxHealth)
        {
            m_CombatHud?.SetHealth(currentHealth, maxHealth);
        }

        void OnPlayerDamageTaken(float damageAmount)
        {
            if (m_IsGameOver || m_IsRestarting || damageAmount <= 0f)
                return;

            m_HitsTakenThisWave++;
            if (m_HitsTakenThisWave >= Mathf.Max(1, m_MaxHitsPerWave))
                m_PlayerDamageReceiver?.ForceKill();
        }

        void OnPlayerDied()
        {
            if (m_IsGameOver)
                return;

            SetPauseMenuOpen(false);
            m_IsGameOver = true;
            if (m_SpawnLoop != null)
                StopCoroutine(m_SpawnLoop);

            FreezeAllEnemiesForDeath();
            Time.timeScale = 0f;
            m_CombatHud?.HideDeathPanel();
            m_CombatHud?.ShowBanner("You were overwhelmed.", 1.2f);
            m_CombatHud?.FadeToBlack(0.55f);

            if (m_DeathFlowRoutine != null)
                StopCoroutine(m_DeathFlowRoutine);
            m_DeathFlowRoutine = StartCoroutine(DeathUiFlow());
        }

        void RestartCurrentScene()
        {
            if (!isActiveAndEnabled || m_IsRestarting)
                return;

            SetPauseMenuOpen(false);
            StartCoroutine(RestartRunRoutine(initialStartup: false));
        }

        void QuitGame()
        {
            SetPauseMenuOpen(false);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void SetPauseMenuOpen(bool isOpen)
        {
            if (m_IsGameOver || m_IsRestarting)
                isOpen = false;

            if (m_IsPauseMenuOpen == isOpen)
                return;

            m_IsPauseMenuOpen = isOpen;
            m_CombatHud?.SetPauseMenuVisible(isOpen);
            if (m_CombatHud != null)
                m_CombatHud.SetMovementVignetteStrength(m_MovementVignetteStrength, notify: false);

            if (isOpen)
                Time.timeScale = 0f;
            else if (!m_IsGameOver && !m_IsRestarting)
                Time.timeScale = 1f;
        }

        IEnumerator DeathUiFlow()
        {
            yield return new WaitForSecondsRealtime(0.65f);
            m_CombatHud?.ShowDeathPanel(m_CurrentWave);
            m_DeathFlowRoutine = null;
        }

        IEnumerator RestartRunRoutine(bool initialStartup)
        {
            m_IsRestarting = true;
            try
            {
                SetPauseMenuOpen(false);
                if (!initialStartup && m_DeathFlowRoutine != null)
                {
                    StopCoroutine(m_DeathFlowRoutine);
                    m_DeathFlowRoutine = null;
                }

                Time.timeScale = 1f;
                if (m_SpawnLoop != null)
                    StopCoroutine(m_SpawnLoop);
                m_SpawnLoop = null;
                m_HasLoggedFirstPauseAttempt = false;
                m_PendingMetaMenuGesture = false;

                RestoreSuppressedHandRenderers();
                DestroyRuntimeCombatObjects();
                DestroyLegacyStickObjects();
                ConfigureArenaSurface();
                ResetSceneAuthoredObjects();
                CapsuleEnemy.ClearRuntimeDecals();
                m_ActiveWaveEnemies.Clear();
                m_NextKillZoneGlobalSweepTime = 0f;

                yield return null;

                m_CurrentWave = 1;
                m_CurrentFlowRate = m_StartingFlowRatePerSecond;
                m_HitsTakenThisWave = 0;
                m_IsGameOver = false;
                m_PlayerDamageReceiver?.ResetState();
                if (m_PlayerDamageReceiver != null)
                    OnPlayerHealthChanged(m_PlayerDamageReceiver.CurrentHealth, m_PlayerDamageReceiver.MaxHealth);

                m_CombatHud?.ResetForRestart();
                ConfigureHandFirstInteraction();
                SetupPauseMenuInputActions();
                ConfigureSceneAuthoredSwordPickups();
                ConfigureExistingGrabInteractables();
                EnsureSceneAuthoredResettables();
                DisableTutorialObjects();
                MovePlayerToTeleportAnchor();
                yield return null;
                MovePlayerToTeleportAnchor();
                InitializeKillZoneCenter();
                SetupControllerSwingDamage();
                ValidateRigCoherency("restart");
                SpawnWallMountedLoadout();
                m_SpawnLoop = StartCoroutine(WaveLoop());
            }
            finally
            {
                m_IsRestarting = false;
            }
        }

        void ResetSceneAuthoredObjects()
        {
            var sceneAuthoredResettables = FindObjectsByType<SceneAuthoredResettable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < sceneAuthoredResettables.Length; i++)
            {
                var sceneAuthoredResettable = sceneAuthoredResettables[i];
                if (sceneAuthoredResettable != null)
                    sceneAuthoredResettable.ResetToInitialState();
            }

            ResetSceneAuthoredPickups();
            ResetSceneAuthoredWeapons();
        }

        void ResetSceneAuthoredPickups()
        {
            var sceneAuthoredPickups = FindObjectsByType<SceneAuthoredPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < sceneAuthoredPickups.Length; i++)
            {
                var sceneAuthoredPickup = sceneAuthoredPickups[i];
                if (sceneAuthoredPickup != null)
                    sceneAuthoredPickup.ResetToInitialState();
            }
        }

        void ResetSceneAuthoredWeapons()
        {
            var flintlocks = FindObjectsByType<FlintlockWeapon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < flintlocks.Length; i++)
            {
                var flintlock = flintlocks[i];
                if (flintlock != null)
                    flintlock.ResetWeaponState();
            }
        }

        void ConfigureArenaSurface()
        {
            if (!TryFindArenaRoot(out var arenaRoot))
                return;

            m_ArenaSurfaceColliders.Clear();

            // First, collect colliders from MeshFilters with renderers (original behavior)
            var meshFilters = arenaRoot.GetComponentsInChildren<MeshFilter>(true);
            for (var i = 0; i < meshFilters.Length; i++)
            {
                var meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                var renderer = meshFilter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled)
                    continue;

                var meshCollider = meshFilter.GetComponent<MeshCollider>();
                if (meshCollider == null)
                    meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                meshCollider.enabled = true;
                if (!m_ArenaSurfaceColliders.Contains(meshCollider))
                    m_ArenaSurfaceColliders.Add(meshCollider);
            }

            // Also collect any existing enabled non-trigger colliders under the arena
            // This ensures floor colliders without renderers are also included
            var existingColliders = arenaRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < existingColliders.Length; i++)
            {
                var collider = existingColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                // Skip colliders already added
                if (m_ArenaSurfaceColliders.Contains(collider))
                    continue;

                // Include BoxColliders, MeshColliders, and other non-trigger colliders as potential floor surfaces
                m_ArenaSurfaceColliders.Add(collider);
            }

            TryConfigureKillZoneFromArena(arenaRoot);

            if (TryFindPrimaryTeleportationArea(out var teleportationArea))
                ConfigureArenaTeleportationArea(arenaRoot, teleportationArea);
        }

        bool TryFindArenaRoot(out Transform arenaRoot)
        {
            arenaRoot = null;
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null || candidate.parent != null)
                    continue;

                if (string.Equals(candidate.name, ArenaRootName, StringComparison.Ordinal))
                {
                    arenaRoot = candidate;
                    return true;
                }
            }

            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null || candidate.parent != null)
                    continue;

                if (string.Equals(candidate.name, ArenaRootName, StringComparison.OrdinalIgnoreCase))
                {
                    arenaRoot = candidate;
                    return true;
                }
            }

            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null || candidate.parent != null)
                    continue;

                if (candidate.name.StartsWith(ArenaRootName, StringComparison.OrdinalIgnoreCase))
                {
                    arenaRoot = candidate;
                    return true;
                }
            }

            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null || candidate.parent != null)
                    continue;

                if (candidate.name.IndexOf(ArenaRootName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    arenaRoot = candidate;
                    return true;
                }
            }

            return false;
        }

        static bool TryFindPrimaryTeleportationArea(out TeleportationArea teleportationArea)
        {
            teleportationArea = null;
            var teleportationAreas = FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < teleportationAreas.Length; i++)
            {
                var candidate = teleportationAreas[i];
                if (candidate == null)
                    continue;

                if (candidate.name.IndexOf("teleport area", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    teleportationArea = candidate;
                    return true;
                }

                if (teleportationArea == null)
                    teleportationArea = candidate;
            }

            return teleportationArea != null;
        }

        void ConfigureArenaTeleportationArea(Transform arenaRoot, TeleportationArea teleportationArea)
        {
            if (arenaRoot == null || teleportationArea == null)
                return;

            teleportationArea.teleportationProvider = EnsureTeleportationProvider();
            if (m_RuntimeArenaTeleportCollider != null)
                m_RuntimeArenaTeleportCollider.enabled = false;

            if (teleportationArea.colliders.Count == 0)
            {
                for (var i = 0; i < m_ArenaSurfaceColliders.Count; i++)
                {
                    var collider = m_ArenaSurfaceColliders[i];
                    if (collider == null || !collider.enabled || collider.isTrigger)
                        continue;

                    teleportationArea.colliders.Add(collider);
                }
            }

            // Restrict teleport hits to walkable faces when the arena mesh includes walls.
            TrySetMemberValue(teleportationArea, "filterSelectionByHitNormal", true);
            TrySetMemberValue(teleportationArea, "m_FilterSelectionByHitNormal", true);
            TrySetMemberValue(teleportationArea, "upNormalToleranceDegrees", 45f);
            TrySetMemberValue(teleportationArea, "m_UpNormalToleranceDegrees", 45f);
        }

        Collider EnsureRuntimeArenaTeleportCollider(GameObject teleportAreaObject)
        {
            if (m_RuntimeArenaTeleportCollider != null)
                return m_RuntimeArenaTeleportCollider;

            var teleportSurfaceObject = new GameObject("Runtime Arena Teleport Surface");
            teleportSurfaceObject.transform.SetParent(teleportAreaObject != null ? teleportAreaObject.transform : transform, false);
            teleportSurfaceObject.layer = teleportAreaObject != null ? teleportAreaObject.layer : gameObject.layer;

            var collider = teleportSurfaceObject.AddComponent<BoxCollider>();
            collider.enabled = false;
            m_RuntimeArenaTeleportCollider = collider;
            return collider;
        }

        TeleportationProvider EnsureTeleportationProvider()
        {
            var provider = FindAnyObjectByType<TeleportationProvider>(FindObjectsInactive.Include);
            if (provider != null)
                return provider;

            var locomotionHost = m_PlayerRoot != null ? m_PlayerRoot.gameObject : gameObject;
            var locomotionObject = locomotionHost.transform.Find("Runtime Locomotion")?.gameObject;
            if (locomotionObject == null)
            {
                locomotionObject = new GameObject("Runtime Locomotion");
                locomotionObject.transform.SetParent(locomotionHost.transform, false);
                m_RuntimeSpawnedObjects.Add(locomotionObject);
            }

            var xrOrigin = locomotionHost.GetComponentInParent<XROrigin>();
            if (xrOrigin == null && m_PlayerCamera != null)
                xrOrigin = m_PlayerCamera.GetComponentInParent<XROrigin>();

            var bodyTransformer = locomotionObject.GetComponent<XRBodyTransformer>() ?? locomotionObject.AddComponent<XRBodyTransformer>();
            if (xrOrigin != null)
                bodyTransformer.xrOrigin = xrOrigin;

            var mediator = locomotionObject.GetComponent<LocomotionMediator>() ?? locomotionObject.AddComponent<LocomotionMediator>();
            provider = locomotionObject.GetComponent<TeleportationProvider>() ?? locomotionObject.AddComponent<TeleportationProvider>();
            provider.mediator = mediator;
            return provider;
        }

        void ConfigureSceneAuthoredSwordPickups()
        {
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var root = allTransforms[i];
                if (root == null || root.parent != null || !IsSceneAuthoredSwordRoot(root))
                    continue;

                if (root.GetComponent<SceneAuthoredPickup>() != null)
                    continue;

                if (root.GetComponentInChildren<XRGrabInteractable>(true) != null)
                    continue;

                ConfigureSceneAuthoredSwordPickup(root.gameObject);
            }
        }

        bool IsSceneAuthoredSwordRoot(Transform root)
        {
            if (root == null || m_RuntimeSpawnedObjects.Contains(root.gameObject))
                return false;

            var allTransforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate != null && candidate.name.IndexOf(SwordNameToken, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        void ConfigureSceneAuthoredSwordPickup(GameObject swordRoot)
        {
            if (swordRoot == null || !TryCreateSceneBladePickupLayout(swordRoot.transform, out var bladeLayout))
            {
                if (swordRoot != null)
                    Debug.LogWarning($"[VRCombat] Could not configure scene sword '{swordRoot.name}' because no render bounds were found.");
                return;
            }

            var startMode = DetectSceneAuthoredPickupStartMode(swordRoot.transform);
            StripImportedColliders(swordRoot);
            DisableImportedAnimation(swordRoot);
            CreateBladeColliders(swordRoot.transform, bladeLayout, out var gripCollider, out var bodyCollider);

            var rigidbody = swordRoot.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = swordRoot.AddComponent<Rigidbody>();
            ConfigureBladePickupRigidbody(rigidbody);

            var grabInteractable = swordRoot.GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
                grabInteractable = swordRoot.AddComponent<XRGrabInteractable>();
            ConfigureBladeGrabInteractable(swordRoot, grabInteractable, gripCollider, bladeLayout.AttachLocalPosition);

            EnsureSwingWeapon(
                swordRoot,
                makeTriggerCollider: false,
                forceKinematic: false,
                defaultRadius: Mathf.Max(0.06f, bodyCollider.radius * 1.5f));
            SetupSceneAuthoredPickup(swordRoot, grabInteractable, startMode);
        }

        SceneAuthoredPickupStartMode DetectSceneAuthoredPickupStartMode(Transform pickupRoot)
        {
            if (pickupRoot == null || !TryGetTransformWorldBounds(pickupRoot, out var bounds))
                return SceneAuthoredPickupStartMode.Mounted;

            var rayOrigin = new Vector3(bounds.center.x, bounds.min.y + ScenePickupSupportProbeLift, bounds.center.z);
            var hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                s_ScenePickupSupportHits,
                ScenePickupSupportProbeDistance + ScenePickupSupportProbeLift,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hitCount; i++)
            {
                var hit = s_ScenePickupSupportHits[i];
                if (!IsSupportColliderHit(pickupRoot, hit.collider))
                    continue;

                return SceneAuthoredPickupStartMode.Loose;
            }

            return SceneAuthoredPickupStartMode.Mounted;
        }

        static bool IsSupportColliderHit(Transform pickupRoot, Collider collider)
        {
            if (pickupRoot == null || collider == null || !collider.enabled || collider.isTrigger)
                return false;

            var colliderTransform = collider.transform;
            return colliderTransform != pickupRoot && !colliderTransform.IsChildOf(pickupRoot);
        }

        void FreezeAllEnemiesForDeath()
        {
            var allEnemies = FindObjectsByType<CapsuleEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < allEnemies.Length; i++)
            {
                var enemy = allEnemies[i];
                if (enemy != null)
                    enemy.FreezeForGameOver();
            }
        }

        void DestroyRuntimeCombatObjects()
        {
            for (var i = 0; i < m_RuntimeSpawnedObjects.Count; i++)
            {
                var runtimeObject = m_RuntimeSpawnedObjects[i];
                if (runtimeObject != null)
                    Destroy(runtimeObject);
            }

            m_RuntimeSpawnedObjects.Clear();
            m_RuntimeKillZoneBoundary = null;
        }

        void DestroyLegacyStickObjects()
        {
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var current = allTransforms[i];
                if (current == null)
                    continue;

                var lowerName = current.name.ToLowerInvariant();
                if (!lowerName.Contains("swing pickup") && !lowerName.Contains("floppy tube"))
                    continue;

                if (current.gameObject == gameObject)
                    continue;

                Destroy(current.gameObject);
            }
        }

        void MovePlayerToTeleportAnchor()
        {
            if (m_PlayerRoot == null || m_PlayerCamera == null)
                return;

            if (!TryGetTeleportStartPose(out var destinationPosition, out var destinationRotation))
                return;

            var currentHeadPosition = m_PlayerCamera.transform.position;
            var currentForward = Vector3.ProjectOnPlane(m_PlayerCamera.transform.forward, Vector3.up);
            var targetForward = Vector3.ProjectOnPlane(destinationRotation * Vector3.forward, Vector3.up);
            if (currentForward.sqrMagnitude > 0.0001f && targetForward.sqrMagnitude > 0.0001f)
            {
                var yawDelta = Vector3.SignedAngle(currentForward.normalized, targetForward.normalized, Vector3.up);
                m_PlayerRoot.RotateAround(currentHeadPosition, Vector3.up, yawDelta);
            }

            // Keep the horizontal camera-to-rig offset so standing origin stays aligned,
            // but never apply headset height as a downward offset to rig root.
            var rootToHead = m_PlayerCamera.transform.position - m_PlayerRoot.position;
            var horizontalHeadOffset = new Vector3(rootToHead.x, 0f, rootToHead.z);
            var targetRootPosition = destinationPosition - horizontalHeadOffset;
            targetRootPosition.y = destinationPosition.y + Mathf.Max(0.02f, m_TeleportSpawnHeightOffset);
            m_PlayerRoot.position = targetRootPosition;

            if (!m_KillZoneDerivedFromArena)
            {
                m_KillZoneCenter = destinationPosition;
                m_HasKillZoneCenter = true;
            }

            EnsureRuntimeKillZoneBoundary();
        }

        static bool TryGetTeleportStartPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            var anchors = FindObjectsByType<TeleportationAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (anchors == null || anchors.Length == 0)
                return false;

            TeleportationAnchor bestAnchor = null;
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor == null)
                    continue;

                var lowerName = anchor.name.ToLowerInvariant();
                if (lowerName.Contains("start") || lowerName.Contains("spawn"))
                {
                    bestAnchor = anchor;
                    break;
                }

                if (bestAnchor == null && lowerName.Contains("teleport anchor"))
                    bestAnchor = anchor;
                else if (bestAnchor == null)
                    bestAnchor = anchor;
            }

            if (bestAnchor == null)
                return false;

            var anchorTransform = bestAnchor.teleportAnchorTransform != null ? bestAnchor.teleportAnchorTransform : bestAnchor.transform;
            if (anchorTransform == null)
                return false;

            position = anchorTransform.position;
            rotation = anchorTransform.rotation;
            return true;
        }

        void SetupControllerSwingDamage()
        {
            var leftSource = ResolveHandCombatAnchor(m_LeftHandTransform, "left");
            if (leftSource == null)
                leftSource = m_LeftControllerTransform;

            var rightSource = ResolveHandCombatAnchor(m_RightHandTransform, "right");
            if (rightSource == null)
                rightSource = m_RightControllerTransform;

            EnsureHandDamageHitbox(leftSource, "Left Runtime Hand Damage Hitbox");
            EnsureHandDamageHitbox(rightSource, "Right Runtime Hand Damage Hitbox");

            if (leftSource != null && rightSource != null)
                return;

            var allTransforms = m_PlayerRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                var lowerName = candidate.name.ToLowerInvariant();
                if (!lowerName.Contains("controller"))
                    continue;

                if (leftSource == null && lowerName.Contains("left"))
                    EnsureHandDamageHitbox(candidate, "Left Runtime Hand Damage Hitbox");

                if (rightSource == null && lowerName.Contains("right"))
                    EnsureHandDamageHitbox(candidate, "Right Runtime Hand Damage Hitbox");
            }
        }

        Transform ResolveHandCombatAnchor(Transform handRoot, string handedness)
        {
            if (handRoot == null)
                return null;

            var sideToken = string.IsNullOrWhiteSpace(handedness) ? string.Empty : handedness.ToLowerInvariant();
            var bestRenderer = FindBestHandCombatRenderer(handRoot.GetComponentsInChildren<Renderer>(true), sideToken);
            if (bestRenderer != null)
                return bestRenderer.transform;

            if (m_PlayerRoot == null)
                return handRoot;

            var allRenderers = m_PlayerRoot.GetComponentsInChildren<Renderer>(true);
            bestRenderer = FindBestHandCombatRenderer(allRenderers, sideToken);
            return bestRenderer != null ? bestRenderer.transform : handRoot;
        }

        static Renderer FindBestHandCombatRenderer(Renderer[] renderers, string sideToken)
        {
            Renderer bestRenderer = null;
            var bestScore = int.MinValue;
            if (renderers == null)
                return null;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var lowerName = renderer.name.ToLowerInvariant();
                if (lowerName.Contains("proxy") ||
                    lowerName.Contains("line") ||
                    lowerName.Contains("ray") ||
                    lowerName.Contains("reticle") ||
                    lowerName.Contains("teleport"))
                {
                    continue;
                }

                var score = 0;
                if (renderer is SkinnedMeshRenderer)
                    score += 8;
                if (lowerName.Contains("hand visualizer"))
                    score += 6;
                if (lowerName.Contains("hand"))
                    score += 4;
                if (!string.IsNullOrEmpty(sideToken))
                    score += lowerName.Contains(sideToken) ? 5 : -3;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRenderer = renderer;
                }
            }

            return bestRenderer;
        }

        void EnsureHandDamageHitbox(Transform source, string hitboxName)
        {
            if (source == null || string.IsNullOrWhiteSpace(hitboxName))
                return;

            var existing = source.Find(hitboxName);
            GameObject hitboxObject;
            if (existing != null)
            {
                hitboxObject = existing.gameObject;
            }
            else
            {
                hitboxObject = new GameObject(hitboxName);
                hitboxObject.transform.SetParent(source, false);
            }

            hitboxObject.layer = source.gameObject.layer;
            hitboxObject.transform.localPosition = Vector3.zero;
            hitboxObject.transform.localRotation = Quaternion.identity;
            hitboxObject.transform.localScale = Vector3.one;

            var rigidbody = hitboxObject.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = hitboxObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var sphereCollider = hitboxObject.GetComponent<SphereCollider>();
            if (sphereCollider == null)
                sphereCollider = hitboxObject.AddComponent<SphereCollider>();
            sphereCollider.isTrigger = true;
            sphereCollider.radius = Mathf.Max(0.05f, m_HandSwingColliderRadius * 1.1f);

            var damageDealer = hitboxObject.GetComponent<SwingDamageDealer>();
            if (damageDealer == null)
                damageDealer = hitboxObject.AddComponent<SwingDamageDealer>();
            damageDealer.SetDamageGate(null);
            damageDealer.Configure(6.4f, 0.01f, 1.55f, 0.06f, 0.15f);
        }

        bool TryConfigureKillZoneFromArena()
        {
            return TryFindArenaRoot(out var arenaRoot) && TryConfigureKillZoneFromArena(arenaRoot);
        }

        bool TryConfigureKillZoneFromArena(Transform arenaRoot)
        {
            if (!TryGetArenaWorldBounds(arenaRoot, out var arenaBounds))
                return false;

            m_KillZoneCenter = arenaBounds.center;
            m_KillZoneHorizontalRadius = Mathf.Max(2f, Mathf.Max(arenaBounds.extents.x, arenaBounds.extents.z) + ArenaKillZoneHorizontalPadding);
            m_KillZoneBelowCenter = Mathf.Max(0.5f, arenaBounds.extents.y + ArenaKillZoneBelowPadding);
            m_KillZoneAboveCenter = Mathf.Max(2f, arenaBounds.extents.y + ArenaKillZoneAbovePadding);
            m_HasKillZoneCenter = true;
            m_KillZoneDerivedFromArena = true;
            EnsureRuntimeKillZoneBoundary();
            return true;
        }

        bool TryGetArenaWorldBounds(Transform arenaRoot, out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;

            for (var i = 0; i < m_ArenaSurfaceColliders.Count; i++)
            {
                var collider = m_ArenaSurfaceColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (hasBounds)
                return true;

            if (arenaRoot == null)
                return false;

            var renderers = arenaRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        void InitializeKillZoneCenter()
        {
            if (TryConfigureKillZoneFromArena())
                return;

            if (m_HasKillZoneCenter)
            {
                EnsureRuntimeKillZoneBoundary();
                return;
            }

            m_KillZoneDerivedFromArena = false;
            if (m_PlayerRoot != null)
            {
                m_KillZoneCenter = m_PlayerRoot.position;
                m_HasKillZoneCenter = true;
                EnsureRuntimeKillZoneBoundary();
                return;
            }

            if (m_PlayerCamera != null)
            {
                m_KillZoneCenter = m_PlayerCamera.transform.position;
                m_HasKillZoneCenter = true;
                EnsureRuntimeKillZoneBoundary();
            }
        }

        void UpdateKillZoneState()
        {
            if (!m_HasKillZoneCenter)
                InitializeKillZoneCenter();

            EnsureRuntimeKillZoneBoundary();

            if (m_IsGameOver || !m_HasKillZoneCenter)
                return;

            if (m_PlayerDamageReceiver != null && !m_PlayerDamageReceiver.IsDead)
            {
                var playerPosition = m_PlayerCamera != null
                    ? m_PlayerCamera.transform.position
                    : m_PlayerDamageReceiver.transform.position;

                if (IsOutsideKillZone(playerPosition))
                    m_PlayerDamageReceiver.ForceKill();
            }

            m_ActiveWaveEnemies.RemoveAll(enemy => enemy == null);
            for (var i = 0; i < m_ActiveWaveEnemies.Count; i++)
            {
                var enemy = m_ActiveWaveEnemies[i];
                if (enemy == null)
                    continue;

                if (!IsOutsideKillZone(enemy.transform.position))
                    continue;

                KillEnemyFromKillZone(enemy);
            }

            if (Time.unscaledTime < m_NextKillZoneGlobalSweepTime)
                return;

            m_NextKillZoneGlobalSweepTime = Time.unscaledTime + Mathf.Max(0.05f, m_KillZoneGlobalSweepInterval);
            SweepAllEnemiesForKillZone();
            SweepRuntimeObjectsForKillZone();
        }

        void EnsureRuntimeKillZoneBoundary()
        {
            if (!m_HasKillZoneCenter)
                return;

            if (m_RuntimeKillZoneBoundary == null)
            {
                var boundaryObject = new GameObject("Runtime Kill Zone Boundary");
                var boundaryRigidbody = boundaryObject.AddComponent<Rigidbody>();
                boundaryRigidbody.isKinematic = true;
                boundaryRigidbody.useGravity = false;
                boundaryRigidbody.detectCollisions = true;
                boundaryRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                boundaryObject.AddComponent<BoxCollider>().isTrigger = true;
                m_RuntimeKillZoneBoundary = boundaryObject.AddComponent<RuntimeKillZoneBoundary>();
                RegisterRuntimeObject(boundaryObject);
            }

            m_RuntimeKillZoneBoundary.transform.SetPositionAndRotation(m_KillZoneCenter, Quaternion.identity);
            m_RuntimeKillZoneBoundary.Configure(
                m_PlayerDamageReceiver,
                OnKillZonePlayerExited,
                OnKillZoneEnemyExited,
                OnKillZoneRigidbodyExited);

            var horizontalRadius = Mathf.Max(2f, m_KillZoneHorizontalRadius);
            var below = Mathf.Max(0.5f, m_KillZoneBelowCenter);
            var above = Mathf.Max(2f, m_KillZoneAboveCenter);
            var boundaryCollider = m_RuntimeKillZoneBoundary.GetComponent<BoxCollider>();
            if (boundaryCollider != null)
            {
                boundaryCollider.size = new Vector3(horizontalRadius * 2f, below + above, horizontalRadius * 2f);
                boundaryCollider.center = new Vector3(0f, (above - below) * 0.5f, 0f);
                boundaryCollider.isTrigger = true;
            }
        }

        void OnKillZonePlayerExited(PlayerDamageReceiver playerDamageReceiver)
        {
            if (playerDamageReceiver == null || playerDamageReceiver.IsDead)
                return;

            playerDamageReceiver.ForceKill();
        }

        void OnKillZoneEnemyExited(CapsuleEnemy enemy)
        {
            KillEnemyFromKillZone(enemy);
        }

        void OnKillZoneRigidbodyExited(Rigidbody rigidbody)
        {
            if (rigidbody == null)
                return;

            if (rigidbody.GetComponentInParent<PlayerDamageReceiver>() != null)
                return;

            if (rigidbody.GetComponentInParent<CapsuleEnemy>() != null)
                return;

            var runtimeRoot = ResolveRuntimeRootObject(rigidbody.transform);
            if (runtimeRoot == null)
                return;

            if (ShouldPreserveRuntimePickup(runtimeRoot) || !IsOutsideKillZone(runtimeRoot.transform.position))
                return;

            m_RuntimeSpawnedObjects.Remove(runtimeRoot);
            Destroy(runtimeRoot);
        }

        void SweepAllEnemiesForKillZone()
        {
            var allEnemies = FindObjectsByType<CapsuleEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < allEnemies.Length; i++)
            {
                var enemy = allEnemies[i];
                if (enemy == null || !IsOutsideKillZone(enemy.transform.position))
                    continue;

                KillEnemyFromKillZone(enemy);
            }
        }

        void SweepRuntimeObjectsForKillZone()
        {
            for (var i = m_RuntimeSpawnedObjects.Count - 1; i >= 0; i--)
            {
                var runtimeObject = m_RuntimeSpawnedObjects[i];
                if (runtimeObject == null)
                {
                    m_RuntimeSpawnedObjects.RemoveAt(i);
                    continue;
                }

                if (!IsOutsideKillZone(runtimeObject.transform.position))
                    continue;

                if (ShouldPreserveRuntimePickup(runtimeObject))
                    continue;

                if (runtimeObject.TryGetComponent<CapsuleEnemy>(out var enemy))
                {
                    KillEnemyFromKillZone(enemy);
                    continue;
                }

                if (runtimeObject.TryGetComponent<Rigidbody>(out _))
                {
                    Destroy(runtimeObject);
                    m_RuntimeSpawnedObjects.RemoveAt(i);
                }
            }
        }

        void KillEnemyFromKillZone(CapsuleEnemy enemy)
        {
            if (enemy == null)
                return;

            m_ActiveWaveEnemies.Remove(enemy);
            enemy.ApplyDamage(9999f, enemy.transform.position, gameObject);
        }

        bool IsOutsideKillZone(Vector3 worldPosition)
        {
            var horizontalOffset = new Vector2(worldPosition.x - m_KillZoneCenter.x, worldPosition.z - m_KillZoneCenter.z);
            if (horizontalOffset.magnitude > Mathf.Max(2f, m_KillZoneHorizontalRadius))
                return true;

            if (worldPosition.y < m_KillZoneCenter.y - Mathf.Max(0.5f, m_KillZoneBelowCenter))
                return true;

            if (worldPosition.y > m_KillZoneCenter.y + Mathf.Max(2f, m_KillZoneAboveCenter))
                return true;

            return false;
        }

        void SpawnWallMountedLoadout()
        {
            if (m_PlayerCamera == null)
                return;

            var floorY = m_PlayerRoot != null
                ? m_PlayerRoot.position.y
                : m_PlayerCamera.transform.position.y - 1.4f;
            var mountY = floorY + Mathf.Max(0.8f, m_LoadoutMountHeight);
            if (!TryResolveDisplayWallMount(mountY, out var wallPoint, out var wallNormal))
            {
                var fallbackForward = Vector3.ProjectOnPlane(m_PlayerCamera.transform.forward, Vector3.up).normalized;
                if (fallbackForward.sqrMagnitude < 0.001f)
                    fallbackForward = Vector3.forward;

                wallNormal = -fallbackForward;
                wallPoint = m_PlayerCamera.transform.position + fallbackForward * Mathf.Max(0.6f, m_LoadoutFallbackDistance);
                wallPoint.y = mountY;
                Debug.LogWarning("[VRCombat] Could not resolve an environment wall for the loadout. Falling back to a camera-forward mount.");
            }

            var wallRight = Vector3.Cross(Vector3.up, wallNormal).normalized;
            if (wallRight.sqrMagnitude < 0.001f)
            {
                wallRight = Vector3.ProjectOnPlane(m_PlayerCamera.transform.right, Vector3.up).normalized;
                if (wallRight.sqrMagnitude < 0.001f)
                    wallRight = Vector3.right;
            }

            var mountCenter = wallPoint + wallNormal * Mathf.Max(0.02f, m_LoadoutWallInset);
            var bladeFacing = Quaternion.LookRotation(-wallNormal, Vector3.up);
            var bladeRotation = bladeFacing * Quaternion.Euler(-90f, 0f, 0f);

            CreateWallBladePickup(
                "Wall Blade 1",
                mountCenter - wallRight * (m_LoadoutBladeSpacing * 0.55f),
                bladeRotation);

            CreateWallBladePickup(
                "Wall Blade 2",
                mountCenter + wallRight * (m_LoadoutBladeSpacing * 0.55f),
                bladeRotation);

            CreateWallChainPickup(
                "Wall Chain",
                mountCenter + Vector3.up * 0.2f,
                bladeRotation);

            CreateWallShieldPickup(
                "Wall Shield",
                mountCenter + wallRight * (m_LoadoutBladeSpacing * 1.65f),
                bladeFacing);
        }

        bool TryResolveDisplayWallMount(float mountY, out Vector3 wallPoint, out Vector3 wallNormal)
        {
            wallPoint = default;
            wallNormal = default;
            if (m_PlayerCamera == null || !TryFindTemplateEnvironmentRoot(out var environmentRoot))
                return false;

            var cameraPosition = m_PlayerCamera.transform.position;
            var flatCameraForward = Vector3.ProjectOnPlane(m_PlayerCamera.transform.forward, Vector3.up).normalized;
            if (flatCameraForward.sqrMagnitude < 0.001f)
                flatCameraForward = Vector3.forward;

            var allTransforms = environmentRoot.GetComponentsInChildren<Transform>(true);
            var bestCandidate = default(DisplayWallCandidate);
            var hasCandidate = false;
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (!IsDisplayWallCandidate(candidate))
                    continue;

                if (!TryBuildDisplayWallCandidate(candidate, mountY, cameraPosition, flatCameraForward, out var wallCandidate))
                    continue;

                if (!hasCandidate || wallCandidate.Score > bestCandidate.Score)
                {
                    bestCandidate = wallCandidate;
                    hasCandidate = true;
                }
            }

            if (!hasCandidate)
                return false;

            wallPoint = bestCandidate.SurfacePoint;
            wallNormal = bestCandidate.SurfaceNormal;
            return true;
        }

        bool TryFindTemplateEnvironmentRoot(out Transform environmentRoot)
        {
            environmentRoot = null;
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null)
                    continue;

                if (candidate.name.IndexOf("Template Environment", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    environmentRoot = candidate;
                    return true;
                }
            }

            return false;
        }

        static bool IsDisplayWallCandidate(Transform candidate)
        {
            if (candidate == null)
                return false;

            return candidate.name.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool TryBuildDisplayWallCandidate(
            Transform candidate,
            float mountY,
            Vector3 cameraPosition,
            Vector3 flatCameraForward,
            out DisplayWallCandidate wallCandidate)
        {
            wallCandidate = default;
            if (candidate == null || !TryGetTransformWorldBounds(candidate, out var bounds))
                return false;

            if (bounds.size.y < 0.75f || Mathf.Max(bounds.size.x, bounds.size.z) < 0.75f)
                return false;

            var flatToCamera = Vector3.ProjectOnPlane(cameraPosition - bounds.center, Vector3.up);
            if (flatToCamera.sqrMagnitude < 0.001f)
                flatToCamera = -flatCameraForward;

            var surfaceNormal = Vector3.ProjectOnPlane(candidate.forward, Vector3.up).normalized;
            if (surfaceNormal.sqrMagnitude < 0.001f)
                surfaceNormal = flatToCamera.normalized;

            if (Vector3.Dot(surfaceNormal, flatToCamera.normalized) < 0f)
                surfaceNormal = -surfaceNormal;

            var facingScore = Vector3.Dot(surfaceNormal, flatToCamera.normalized);
            if (facingScore <= 0.1f)
                return false;

            var surfacePoint = bounds.center + surfaceNormal * GetBoundsExtentAlongDirection(bounds, surfaceNormal);
            var minMountY = bounds.min.y + 0.18f;
            var maxMountY = bounds.max.y - 0.18f;
            surfacePoint.y = minMountY < maxMountY
                ? Mathf.Clamp(mountY, minMountY, maxMountY)
                : bounds.center.y;

            var toSurface = surfacePoint - cameraPosition;
            var flatToSurface = Vector3.ProjectOnPlane(toSurface, Vector3.up);
            if (flatToSurface.sqrMagnitude < 0.001f)
                return false;

            var maxSearchDistance = Mathf.Max(1.5f, m_LoadoutWallSearchDistance);
            if (flatToSurface.magnitude > maxSearchDistance)
                return false;

            var forwardScore = Vector3.Dot(flatCameraForward, flatToSurface.normalized);
            if (forwardScore <= 0.05f)
                return false;

            var distance = toSurface.magnitude;
            var targetDistance = Mathf.Max(0.8f, m_LoadoutFallbackDistance);
            var distanceScore = 1f - Mathf.Clamp01(Mathf.Abs(distance - targetDistance) / Mathf.Max(0.5f, targetDistance));
            var sizeScore = Mathf.Clamp01(Mathf.Max(bounds.size.x, bounds.size.z) / 4f);
            var isTransparent = candidate.name.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0;

            wallCandidate = new DisplayWallCandidate
            {
                Transform = candidate,
                Bounds = bounds,
                SurfacePoint = surfacePoint,
                SurfaceNormal = surfaceNormal,
                IsTransparent = isTransparent,
                Score = forwardScore * 2.5f + facingScore * 2f + distanceScore + sizeScore + (isTransparent ? -0.5f : 0.25f)
            };
            return true;
        }

        static bool TryGetTransformWorldBounds(Transform candidate, out Bounds bounds)
        {
            bounds = default;
            if (candidate == null)
                return false;

            var hasBounds = false;
            var colliders = candidate.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            if (hasBounds)
                return true;

            var renderers = candidate.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        static float GetBoundsExtentAlongDirection(Bounds bounds, Vector3 direction)
        {
            direction = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            return Vector3.Dot(bounds.extents, direction);
        }

        void CreateWallBladePickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            if (!TryCreateDaggerVisual(root.transform, out var bladeLayout))
                CreateFallbackBladeVisual(root.transform, out bladeLayout);

            CreateBladeColliders(root.transform, bladeLayout, out var gripCollider, out var bodyCollider);

            var rigidbody = root.AddComponent<Rigidbody>();
            ConfigureBladePickupRigidbody(rigidbody);

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureBladeGrabInteractable(root, grabInteractable, gripCollider, bladeLayout.AttachLocalPosition);

            EnsureSwingWeapon(root, makeTriggerCollider: false, forceKinematic: false, defaultRadius: Mathf.Max(0.06f, bodyCollider.radius * 1.5f));
            SetupWallMountedPickup(root, grabInteractable, keepKinematicWhileHeld: true);
        }

        void CreateWallChainPickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var nailRoot = new GameObject($"{pickupName} Nail");
            nailRoot.transform.position = worldPosition;
            nailRoot.transform.rotation = worldRotation;
            RegisterRuntimeObject(nailRoot);

            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            var hasImportedChainVisual = TryCreateChainVisual(root.transform, out _, showOnlyNail: false);
            if (!TryCreateChainVisual(nailRoot.transform, out _, showOnlyNail: true))
                CreateFallbackChainNailVisual(nailRoot.transform);

            if (!hasImportedChainVisual)
            {
                var handleVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                handleVisual.name = "Chain Handle";
                handleVisual.transform.SetParent(root.transform, false);
                handleVisual.transform.localPosition = new Vector3(0f, 0f, -0.055f);
                handleVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                handleVisual.transform.localScale = new Vector3(0.023f, 0.09f, 0.023f);
                ApplyMaterial(handleVisual, GetOrCreatePickupMaterial());

                var handleVisualCollider = handleVisual.GetComponent<Collider>();
                if (handleVisualCollider != null)
                    Destroy(handleVisualCollider);

                CreateFallbackChainVisual(root.transform);
            }

            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.center = new Vector3(0f, 0f, -0.055f);
            collider.radius = 0.03f;
            collider.height = 0.2f;
            collider.contactOffset = 0.0065f;

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 1.2f;
            rigidbody.linearDamping = 0.08f;
            rigidbody.angularDamping = 0.12f;
            rigidbody.solverIterations = 18;
            rigidbody.solverVelocityIterations = 8;
            rigidbody.maxAngularVelocity = 40f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: true,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;

            var attachPoint = new GameObject("Attach Point");
            attachPoint.transform.SetParent(root.transform, false);
            attachPoint.transform.localPosition = new Vector3(0f, 0f, -0.055f);
            attachPoint.transform.localRotation = Quaternion.identity;
            grabInteractable.attachTransform = attachPoint.transform;

            var riggedChainWeapon = root.AddComponent<RiggedChainWeapon>();
            var initialized = riggedChainWeapon.Initialize(
                rigidbody,
                collider,
                boneStep: 2,
                segmentRadius: 0.016f,
                tipRadius: 0.05f,
                segmentMass: 0.03f,
                baseDamage: 15f,
                minSwingSpeed: 0.55f,
                maxSwingSpeedForScaling: 9f,
                hitCooldownSeconds: 0.08f,
                rootSpring: 400f,
                segmentSpring: 200f,
                damper: 20f);

            if (!initialized)
                EnsureSwingWeapon(root, makeTriggerCollider: false, forceKinematic: false, defaultRadius: 0.1f);

            SetupWallMountedPickup(root, grabInteractable, riggedChainWeapon, keepKinematicWhileHeld: true);
        }

        void CreateWallShieldPickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            var shieldDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shieldDisk.name = "Shield Disk";
            shieldDisk.transform.SetParent(root.transform, false);
            shieldDisk.transform.localPosition = Vector3.zero;
            shieldDisk.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shieldDisk.transform.localScale = new Vector3(0.3f, 0.032f, 0.3f);
            ApplyMaterial(shieldDisk, GetOrCreateShieldMaterial());

            var shieldHandle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shieldHandle.name = "Shield Handle";
            shieldHandle.transform.SetParent(root.transform, false);
            shieldHandle.transform.localPosition = new Vector3(0f, 0f, -0.04f);
            shieldHandle.transform.localRotation = Quaternion.identity;
            shieldHandle.transform.localScale = new Vector3(0.12f, 0.042f, 0.04f);
            ApplyMaterial(shieldHandle, GetOrCreatePickupMaterial());

            var diskCollider = shieldDisk.GetComponent<Collider>();
            if (diskCollider != null)
                Destroy(diskCollider);
            var handleCollider = shieldHandle.GetComponent<Collider>();
            if (handleCollider != null)
                Destroy(handleCollider);

            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0f, -0.012f);
            collider.size = new Vector3(0.44f, 0.44f, 0.08f);
            collider.contactOffset = 0.0065f;

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 2.5f;
            rigidbody.linearDamping = 0.2f;
            rigidbody.angularDamping = 0.22f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: true,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;

            var attachPoint = new GameObject("Attach Point");
            attachPoint.transform.SetParent(root.transform, false);
            attachPoint.transform.localPosition = new Vector3(0f, 0f, -0.04f);
            attachPoint.transform.localRotation = Quaternion.identity;
            grabInteractable.attachTransform = attachPoint.transform;

            SetupWallMountedPickup(root, grabInteractable, keepKinematicWhileHeld: true);
        }

        void SetupWallMountedPickup(
            GameObject pickupRoot,
            XRGrabInteractable grabInteractable,
            RiggedChainWeapon riggedChainWeapon = null,
            bool keepKinematicWhileHeld = false)
        {
            var mountedPickup = EnsureRuntimeMountedPickup(pickupRoot, riggedChainWeapon, keepKinematicWhileHeld);
            if (mountedPickup == null || grabInteractable == null)
                return;

            mountedPickup.SetState(RuntimeMountedPickupState.Mounted);
            AddPickupSelectListeners(grabInteractable, mountedPickup);
        }

        void SetupSceneAuthoredPickup(
            GameObject pickupRoot,
            XRGrabInteractable grabInteractable,
            SceneAuthoredPickupStartMode startMode)
        {
            if (pickupRoot == null || grabInteractable == null)
                return;

            var rigidbody = pickupRoot.GetComponent<Rigidbody>();
            var mountedPickup = EnsureRuntimeMountedPickup(pickupRoot, riggedChainWeapon: null, keepKinematicWhileHeld: true);
            if (rigidbody == null || mountedPickup == null)
                return;

            var sceneAuthoredPickup = pickupRoot.GetComponent<SceneAuthoredPickup>();
            if (sceneAuthoredPickup == null)
                sceneAuthoredPickup = pickupRoot.AddComponent<SceneAuthoredPickup>();
            sceneAuthoredPickup.Configure(rigidbody, mountedPickup, startMode);
            sceneAuthoredPickup.ResetToInitialState();
            AddPickupSelectListeners(grabInteractable, mountedPickup);
        }

        RuntimeMountedPickup EnsureRuntimeMountedPickup(
            GameObject pickupRoot,
            RiggedChainWeapon riggedChainWeapon,
            bool keepKinematicWhileHeld)
        {
            if (pickupRoot == null)
                return null;

            var rigidbody = pickupRoot.GetComponent<Rigidbody>();
            if (rigidbody == null)
                return null;

            var mountedPickup = pickupRoot.GetComponent<RuntimeMountedPickup>();
            if (mountedPickup == null)
                mountedPickup = pickupRoot.AddComponent<RuntimeMountedPickup>();
            mountedPickup.Configure(rigidbody, riggedChainWeapon, keepKinematicWhileHeld);
            return mountedPickup;
        }

        void AddPickupSelectListeners(XRGrabInteractable grabInteractable, RuntimeMountedPickup mountedPickup)
        {
            if (grabInteractable == null)
                return;

            m_CombatPickupListenerRegistrations.RemoveWhere(candidate => candidate == null);
            if (!m_CombatPickupListenerRegistrations.Add(grabInteractable))
                return;

            grabInteractable.selectEntered.AddListener(args =>
            {
                if (mountedPickup != null)
                    mountedPickup.SetState(RuntimeMountedPickupState.Held);
                var side = HandleHandReplacementEquipped(grabInteractable, args);
                SetHandSideVisualSuppressed(side, true);
                SetHandSideCollidersSuppressed(side, true);
            });

            grabInteractable.selectExited.AddListener(args =>
            {
                var side = HandleHandReplacementReleased(grabInteractable, args);
                SetHandSideVisualSuppressed(side, false);
                SetHandSideCollidersSuppressed(side, false);
                if (mountedPickup != null)
                    mountedPickup.SetState(RuntimeMountedPickupState.Dropped);
            });
        }

        HandSide HandleHandReplacementEquipped(XRGrabInteractable pickup, SelectEnterEventArgs args)
        {
            if (pickup == null)
                return HandSide.None;

            var side = ResolvePickupHandSide(args, pickup.transform.position);
            if (side == HandSide.None)
                m_EquippedHandByPickup.Remove(pickup);
            else
                m_EquippedHandByPickup[pickup] = side;

            return side;
        }

        HandSide HandleHandReplacementReleased(XRGrabInteractable pickup, SelectExitEventArgs args)
        {
            if (pickup == null)
                return HandSide.None;

            m_EquippedHandByPickup.TryGetValue(pickup, out var side);
            m_EquippedHandByPickup.Remove(pickup);
            return side;
        }

        HandSide ResolvePickupHandSide(BaseInteractionEventArgs args, Vector3 pickupPosition)
        {
            var interactorTransform = (args?.interactorObject as Component)?.transform;
            if (interactorTransform != null)
            {
                var name = interactorTransform.name.ToLowerInvariant();
                if (name.Contains("left"))
                    return HandSide.Left;
                if (name.Contains("right"))
                    return HandSide.Right;
            }

            var leftHasReference = TryGetSideReferencePosition(HandSide.Left, out var leftReference);
            var rightHasReference = TryGetSideReferencePosition(HandSide.Right, out var rightReference);
            var comparisonPosition = interactorTransform != null ? interactorTransform.position : pickupPosition;

            if (leftHasReference && rightHasReference)
            {
                var leftDistance = (comparisonPosition - leftReference).sqrMagnitude;
                var rightDistance = (comparisonPosition - rightReference).sqrMagnitude;
                return leftDistance <= rightDistance ? HandSide.Left : HandSide.Right;
            }

            if (leftHasReference)
                return HandSide.Left;
            if (rightHasReference)
                return HandSide.Right;

            return HandSide.None;
        }

        bool TryGetSideReferencePosition(HandSide side, out Vector3 position)
        {
            position = Vector3.zero;
            Transform primary;
            Transform secondary;
            switch (side)
            {
                case HandSide.Left:
                    primary = m_LeftHandTransform;
                    secondary = m_LeftControllerTransform;
                    break;
                case HandSide.Right:
                    primary = m_RightHandTransform;
                    secondary = m_RightControllerTransform;
                    break;
                default:
                    return false;
            }

            if (primary != null)
            {
                position = primary.position;
                return true;
            }

            if (secondary != null)
            {
                position = secondary.position;
                return true;
            }

            return false;
        }

        bool IsCombatPickupInteractable(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable == null)
                return false;

            if (grabInteractable.GetComponentInParent<CapsuleEnemy>() != null)
                return false;

            if (grabInteractable.GetComponentInParent<FlintlockWeapon>() != null)
                return true;

            if (grabInteractable.GetComponentInParent<SceneAuthoredPickup>() != null)
                return true;

            if (grabInteractable.GetComponentInParent<RuntimeMountedPickup>() != null)
                return true;

            if (grabInteractable.GetComponentInParent<RiggedChainWeapon>() != null)
                return true;

            if (grabInteractable.GetComponentInChildren<SwingDamageDealer>(true) != null)
                return true;

            var lowerName = grabInteractable.name.ToLowerInvariant();
            return lowerName.Contains("sword")
                || lowerName.Contains("shield")
                || lowerName.Contains("flintlock")
                || lowerName.Contains("chain");
        }

        void SetHandSideVisualSuppressed(HandSide side, bool suppress)
        {
            if (side == HandSide.None)
                return;

            var suppressedList = side == HandSide.Left ? m_LeftSuppressedHandRenderers : m_RightSuppressedHandRenderers;
            if (!suppress)
            {
                RestoreRendererList(suppressedList);
                return;
            }

            // Don't early return if list is non-empty - try to collect more in case transforms weren't ready before
            Transform primary;
            Transform secondary;
            if (side == HandSide.Left)
            {
                primary = m_LeftHandTransform;
                secondary = m_LeftControllerTransform;
            }
            else
            {
                primary = m_RightHandTransform;
                secondary = m_RightControllerTransform;
            }

            // Try to refresh hand transforms if they're null
            if (primary == null && secondary == null)
            {
                RefreshHandTransformsIfNeeded();
                if (side == HandSide.Left)
                {
                    primary = m_LeftHandTransform;
                    secondary = m_LeftControllerTransform;
                }
                else
                {
                    primary = m_RightHandTransform;
                    secondary = m_RightControllerTransform;
                }
            }

            CollectSuppressedHandRenderers(primary, suppressedList);
            if (!ReferenceEquals(primary, secondary))
                CollectSuppressedHandRenderers(secondary, suppressedList);
        }

        void RefreshHandTransformsIfNeeded()
        {
            if (m_PlayerRoot == null)
                return;

            if (m_LeftHandTransform == null || m_RightHandTransform == null)
            {
                ResolveModalityManagedRigTransforms(
                    out var leftHand,
                    out var rightHand,
                    out var leftController,
                    out var rightController);

                m_LeftHandTransform ??= leftHand ?? FindHandTransform("left");
                m_RightHandTransform ??= rightHand ?? FindHandTransform("right");
                m_LeftControllerTransform ??= leftController ?? FindControllerTransform("left") ?? m_LeftHandTransform;
                m_RightControllerTransform ??= rightController ?? FindControllerTransform("right") ?? m_RightHandTransform;
            }
        }

        void SetHandSideCollidersSuppressed(HandSide side, bool suppress)
        {
            if (side == HandSide.None)
                return;

            var suppressedList = side == HandSide.Left ? m_LeftSuppressedHandColliders : m_RightSuppressedHandColliders;
            if (!suppress)
            {
                RestoreColliderList(suppressedList);
                return;
            }

            // Don't early return if list is non-empty - try to collect more in case transforms weren't ready before
            Transform primary;
            Transform secondary;
            if (side == HandSide.Left)
            {
                primary = m_LeftHandTransform;
                secondary = m_LeftControllerTransform;
            }
            else
            {
                primary = m_RightHandTransform;
                secondary = m_RightControllerTransform;
            }

            // Try to refresh hand transforms if they're null
            if (primary == null && secondary == null)
            {
                RefreshHandTransformsIfNeeded();
                if (side == HandSide.Left)
                {
                    primary = m_LeftHandTransform;
                    secondary = m_LeftControllerTransform;
                }
                else
                {
                    primary = m_RightHandTransform;
                    secondary = m_RightControllerTransform;
                }
            }

            CollectSuppressedHandColliders(primary, suppressedList);
            if (!ReferenceEquals(primary, secondary))
                CollectSuppressedHandColliders(secondary, suppressedList);
        }

        static void CollectSuppressedHandColliders(Transform sourceRoot, List<Collider> suppressedList)
        {
            if (sourceRoot == null || suppressedList == null)
                return;

            var colliders = sourceRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;

                // Do not suppress trigger colliders for interaction
                if (collider.isTrigger)
                    continue;

                collider.enabled = false;
                suppressedList.Add(collider);
            }
        }

        static void CollectSuppressedHandRenderers(Transform sourceRoot, List<Renderer> suppressedList)
        {
            if (sourceRoot == null || suppressedList == null)
                return;

            var renderers = sourceRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (ShouldIgnoreHandRendererSuppression(renderer))
                    continue;

                renderer.enabled = false;
                suppressedList.Add(renderer);
            }
        }

        void RestoreSuppressedHandRenderers()
        {
            RestoreRendererList(m_LeftSuppressedHandRenderers);
            RestoreRendererList(m_RightSuppressedHandRenderers);
            RestoreSuppressedHandColliders();
            m_EquippedHandByPickup.Clear();
        }

        void RestoreSuppressedHandColliders()
        {
            RestoreColliderList(m_LeftSuppressedHandColliders);
            RestoreColliderList(m_RightSuppressedHandColliders);
        }

        static void RestoreColliderList(List<Collider> colliders)
        {
            if (colliders == null)
                return;

            for (var i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = true;
            }

            colliders.Clear();
        }

        static void RestoreRendererList(List<Renderer> renderers)
        {
            if (renderers == null)
                return;

            for (var i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = true;
            }

            renderers.Clear();
        }

        static bool ShouldIgnoreHandRendererSuppression(Renderer renderer)
        {
            if (renderer == null)
                return true;

            var lowerName = renderer.name.ToLowerInvariant();
            if (lowerName.Contains("line") ||
                lowerName.Contains("ray") ||
                lowerName.Contains("reticle") ||
                lowerName.Contains("teleport") ||
                lowerName.Contains("pointer"))
            {
                return true;
            }

            return false;
        }

        IEnumerator WaveLoop()
        {
            if (m_InitialWaveDelaySeconds > 0f)
                yield return new WaitForSeconds(m_InitialWaveDelaySeconds);

            m_CurrentWave = 1;

            while (!m_IsGameOver)
            {
                m_HitsTakenThisWave = 0;
                var enemiesThisWave = Mathf.Max(1, m_StartingEnemiesPerWave + (m_CurrentWave - 1) * m_EnemiesAddedPerWave);
                m_CurrentFlowRate = Mathf.Max(
                    0.08f,
                    m_StartingFlowRatePerSecond + (m_CurrentWave - 1) * m_FlowRateIncreasePerWave);

                var spawnInterval = 1f / m_CurrentFlowRate;
                m_CombatHud?.SetWaveInfo(m_CurrentWave, enemiesThisWave, m_CurrentFlowRate);
                m_CombatHud?.ShowBanner($"Wave {m_CurrentWave} starting", 2f);

                m_ActiveWaveEnemies.Clear();
                for (var i = 0; i < enemiesThisWave; i++)
                {
                    if (m_IsGameOver)
                        yield break;

                    var enemy = SpawnSingleEnemy();
                    if (enemy != null)
                        m_ActiveWaveEnemies.Add(enemy);

                    var remainingToSpawn = enemiesThisWave - (i + 1);
                    var aliveNow = AliveEnemyCount();
                    m_CombatHud?.SetWaveInfo(m_CurrentWave, aliveNow + remainingToSpawn, m_CurrentFlowRate);
                    yield return new WaitForSeconds(spawnInterval);
                }

                while (!m_IsGameOver)
                {
                    var aliveNow = AliveEnemyCount();
                    m_CombatHud?.SetWaveInfo(m_CurrentWave, aliveNow, m_CurrentFlowRate);
                    if (aliveNow <= 0)
                        break;

                    yield return null;
                }

                if (m_IsGameOver)
                    yield break;

                var nextWave = m_CurrentWave + 1;
                m_PlayerDamageReceiver?.RestoreFullHealth();
                m_CombatHud?.ShowBanner($"Wave cleared. Wave {nextWave} incoming", 2.2f);
                yield return new WaitForSeconds(m_TimeBetweenWavesSeconds);
                m_CurrentWave = nextWave;
            }
        }

        CapsuleEnemy SpawnSingleEnemy()
        {
            var position = GetEnemySpawnPosition();

            var enemyObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemyObject.name = "Capsule Enemy";
            enemyObject.transform.position = position;
            enemyObject.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
            ApplyMaterial(enemyObject, GetOrCreateEnemyMaterial());

            var rigidbody = enemyObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = true;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var enemy = enemyObject.AddComponent<CapsuleEnemy>();
            enemy.SetTarget(m_PlayerCamera.transform);
            enemy.ConfigureGrabDistance(m_MaxPhysicalGrabDistance);
            RegisterRuntimeObject(enemyObject);
            return enemy;
        }

        int AliveEnemyCount()
        {
            m_ActiveWaveEnemies.RemoveAll(enemy => enemy == null);
            return m_ActiveWaveEnemies.Count;
        }

        Vector3 GetEnemySpawnPosition()
        {
            var center = m_PlayerCamera.transform.position;
            center.y += m_EnemySpawnVerticalOffset;

            var randomAngleDegrees = UnityEngine.Random.Range(0f, 360f);
            var direction3D = Quaternion.AngleAxis(randomAngleDegrees, Vector3.up) * Vector3.forward;
            var randomDirection = new Vector2(direction3D.x, direction3D.z).normalized;
            if (randomDirection.sqrMagnitude < 0.001f)
                randomDirection = Vector2.up;

            var randomRadius = m_EnemySpawnRadius + UnityEngine.Random.Range(-1f, 1f);
            return center + new Vector3(randomDirection.x, 0f, randomDirection.y) * randomRadius;
        }

        Material GetOrCreatePickupMaterial()
        {
            if (m_PickupMaterial != null)
                return m_PickupMaterial;

            m_PickupMaterial = CreateRuntimeLitMaterial("Runtime Pickup Material", m_PickupColor);
            return m_PickupMaterial;
        }

        Material GetOrCreateHandProxyMaterial()
        {
            if (m_HandProxyMaterial != null)
                return m_HandProxyMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader == null)
                return GetOrCreatePickupMaterial();

            m_HandProxyMaterial = new Material(shader) { name = "Runtime Hand Proxy Material" };
            SetMaterialColor(m_HandProxyMaterial, new Color(0.96f, 0.76f, 0.63f, 1f));
            return m_HandProxyMaterial;
        }

        Material GetOrCreateEnemyMaterial()
        {
            if (m_EnemyMaterial != null)
                return m_EnemyMaterial;

            m_EnemyMaterial = CreateRuntimeLitMaterial("Runtime Enemy Material", m_EnemyStartColor);
            return m_EnemyMaterial;
        }

        Material GetOrCreateShieldMaterial()
        {
            if (m_ShieldMaterial != null)
                return m_ShieldMaterial;

            m_ShieldMaterial = CreateRuntimeLitMaterial("Runtime Shield Material", m_ShieldColor);
            return m_ShieldMaterial;
        }

        void RegisterRuntimeObject(GameObject runtimeObject)
        {
            if (runtimeObject == null)
                return;

            m_RuntimeSpawnedObjects.Add(runtimeObject);
        }

        GameObject ResolveRuntimeRootObject(Transform transformCandidate)
        {
            var current = transformCandidate;
            while (current != null)
            {
                var detachedOwner = current.GetComponent<DetachedRuntimeOwner>();
                if (detachedOwner != null && detachedOwner.OwnerRoot != null)
                    return detachedOwner.OwnerRoot;

                if (m_RuntimeSpawnedObjects.Contains(current.gameObject))
                    return current.gameObject;

                current = current.parent;
            }

            return transformCandidate != null ? transformCandidate.gameObject : null;
        }

        static bool ShouldPreserveRuntimePickup(GameObject runtimeObject)
        {
            if (runtimeObject == null)
                return false;

            return runtimeObject.GetComponent<XRGrabInteractable>() != null;
        }

        static Material CreateRuntimeLitMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");

            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogError("Could not find a compatible runtime shader for spawned combat objects.");
                return null;
            }

            var material = new Material(shader) { name = name };
            SetMaterialColor(material, color);
            return material;
        }

        static void ApplyMaterial(GameObject gameObject, Material material)
        {
            if (gameObject == null || material == null)
                return;

            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
                return;

            renderer.enabled = true;
            renderer.sharedMaterial = material;
        }

        void ConfigureBladePickupRigidbody(Rigidbody rigidbody)
        {
            if (rigidbody == null)
                return;

            rigidbody.mass = 0.95f;
            rigidbody.linearDamping = 0.06f;
            rigidbody.angularDamping = 0.08f;
            rigidbody.solverIterations = 12;
            rigidbody.solverVelocityIterations = 4;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void ConfigureBladeGrabInteractable(
            GameObject pickupRoot,
            XRGrabInteractable grabInteractable,
            Collider gripCollider,
            Vector3 attachLocalPosition)
        {
            if (pickupRoot == null || grabInteractable == null)
                return;

            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: true,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;
            grabInteractable.colliders.Clear();
            if (gripCollider != null)
                grabInteractable.colliders.Add(gripCollider);

            var attachTransform = grabInteractable.attachTransform;
            if (attachTransform == null)
            {
                attachTransform = pickupRoot.transform.Find(AttachPointObjectName);
                if (attachTransform == null)
                {
                    var attachPointObject = new GameObject(AttachPointObjectName);
                    attachTransform = attachPointObject.transform;
                }
            }

            attachTransform.SetParent(pickupRoot.transform, false);
            attachTransform.localPosition = attachLocalPosition;
            attachTransform.localRotation = Quaternion.identity;
            grabInteractable.attachTransform = attachTransform;
        }

        bool TryCreateDaggerVisual(Transform parent, out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();
            if (!TryInstantiateRuntimeModelVisual(
                    DaggerModelResourcePath,
                    "Dagger Visual",
                    parent,
                    out var visualRoot))
            {
                return false;
            }

            AlignLongestVisualAxisToForward(visualRoot.transform);
            UniformScaleVisualToLength(visualRoot.transform, 0.6f);
            if (!TryBuildBladePickupLayout(visualRoot.transform, alignGripToOrigin: true, out bladeLayout))
                bladeLayout = CreateDefaultBladePickupLayout();
            return true;
        }

        bool TryCreateChainVisual(Transform parent, out GameObject visualRoot, bool showOnlyNail)
        {
            visualRoot = null;
            if (!TryInstantiateRuntimeModelVisual(
                    ChainModelResourcePath,
                    "Chain Visual",
                    parent,
                    out visualRoot))
            {
                return false;
            }

            AlignLongestVisualAxisToForward(visualRoot.transform);
            UniformScaleVisualToLength(visualRoot.transform, 0.92f);
            AlignChainRigToGrip(visualRoot.transform, 0.02f);
            SetChainVisualPartVisibility(visualRoot, showOnlyNail);
            return true;
        }

        void SetChainVisualPartVisibility(GameObject visualRoot, bool showOnlyNail)
        {
            if (visualRoot == null)
                return;

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var isNailRenderer = IsNamedChainVisualPart(renderer.transform, "nail");
                renderer.enabled = showOnlyNail ? isNailRenderer : !isNailRenderer;
            }
        }

        static bool IsNamedChainVisualPart(Transform transformCandidate, string nameToken)
        {
            if (transformCandidate == null || string.IsNullOrWhiteSpace(nameToken))
                return false;

            var current = transformCandidate;
            while (current != null)
            {
                if (current.name.IndexOf(nameToken, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                current = current.parent;
            }

            return false;
        }

        bool TryInstantiateRuntimeModelVisual(string resourcePath, string visualName, Transform parent, out GameObject visualRoot)
        {
            visualRoot = null;
            if (parent == null || string.IsNullOrWhiteSpace(resourcePath))
                return false;

            var modelPrefab = Resources.Load<GameObject>(resourcePath);
            if (modelPrefab == null)
            {
                Debug.LogWarning($"Could not load runtime combat model at Resources path '{resourcePath}'.");
                return false;
            }

            visualRoot = Instantiate(modelPrefab, parent, false);
            visualRoot.name = visualName;
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;
            visualRoot.transform.localScale = Vector3.one;

            StripImportedColliders(visualRoot);
            DisableImportedAnimation(visualRoot);
            RuntimeCombatModelMaterialBinder.Apply(visualRoot, GetOrCreatePickupMaterial());
            return true;
        }

        bool TryCreateSceneBladePickupLayout(Transform pickupRoot, out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();
            if (pickupRoot == null || !TryGetVisualBoundsRelativeToReference(pickupRoot, pickupRoot, out var bounds))
                return false;

            bladeLayout = BuildBladePickupLayoutFromBounds(bounds, alignGripToOrigin: false);
            return true;
        }

        void CreateFallbackBladeVisual(Transform parent, out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();

            var bladeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bladeVisual.name = "Blade";
            bladeVisual.transform.SetParent(parent, false);
            bladeVisual.transform.localPosition = bladeLayout.BodyColliderCenter;
            bladeVisual.transform.localRotation = Quaternion.identity;
            bladeVisual.transform.localScale = new Vector3(
                bladeLayout.BodyColliderRadius * 1.8f,
                bladeLayout.BodyColliderRadius * 0.8f,
                bladeLayout.BodyColliderHeight);
            ApplyMaterial(bladeVisual, GetOrCreatePickupMaterial());

            var handleVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handleVisual.name = "Handle";
            handleVisual.transform.SetParent(parent, false);
            handleVisual.transform.localPosition = Vector3.zero;
            handleVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            handleVisual.transform.localScale = new Vector3(
                bladeLayout.GripColliderRadius,
                bladeLayout.GripColliderHeight * 0.5f,
                bladeLayout.GripColliderRadius);
            ApplyMaterial(handleVisual, GetOrCreatePickupMaterial());

            var bladeVisualCollider = bladeVisual.GetComponent<Collider>();
            if (bladeVisualCollider != null)
                Destroy(bladeVisualCollider);

            var handleVisualCollider = handleVisual.GetComponent<Collider>();
            if (handleVisualCollider != null)
                Destroy(handleVisualCollider);
        }

        void CreateFallbackChainVisual(Transform parent)
        {
            for (var i = 0; i < 5; i++)
            {
                var link = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                link.name = $"Fallback Chain Link {i + 1}";
                link.transform.SetParent(parent, false);
                link.transform.localPosition = new Vector3(0f, 0f, 0.06f + i * 0.09f);
                link.transform.localRotation = Quaternion.identity;
                link.transform.localScale = Vector3.one * 0.055f;
                ApplyMaterial(link, GetOrCreatePickupMaterial());

                var collider = link.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
            }
        }

        void CreateFallbackChainNailVisual(Transform parent)
        {
            var nailVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nailVisual.name = "Fallback Chain Nail";
            nailVisual.transform.SetParent(parent, false);
            nailVisual.transform.localPosition = new Vector3(0f, 0f, -0.11f);
            nailVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            nailVisual.transform.localScale = new Vector3(0.018f, 0.06f, 0.018f);
            ApplyMaterial(nailVisual, GetOrCreatePickupMaterial());

            var collider = nailVisual.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
        }

        void StripImportedColliders(GameObject visualRoot)
        {
            if (visualRoot == null)
                return;

            var colliders = visualRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    Destroy(colliders[i]);
            }
        }

        void DisableImportedAnimation(GameObject visualRoot)
        {
            if (visualRoot == null)
                return;

            var animator = visualRoot.GetComponentInChildren<Animator>(true);
            if (animator != null)
                animator.enabled = false;

            var animation = visualRoot.GetComponentInChildren<Animation>(true);
            if (animation != null)
                animation.enabled = false;
        }

        void AssignFallbackMaterialToUntexturedRenderers(GameObject visualRoot, Material fallbackMaterial)
        {
            if (visualRoot == null || fallbackMaterial == null)
                return;

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var j = 0; j < materials.Length; j++)
                {
                    if (materials[j] != null)
                        continue;

                    materials[j] = fallbackMaterial;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        BladePickupLayout CreateDefaultBladePickupLayout()
        {
            return new BladePickupLayout
            {
                GripColliderCenter = Vector3.zero,
                GripColliderHeight = 0.11f,
                GripColliderRadius = 0.022f,
                BodyColliderCenter = new Vector3(0f, 0f, 0.19f),
                BodyColliderHeight = 0.34f,
                BodyColliderRadius = 0.015f,
                AttachLocalPosition = Vector3.zero
            };
        }

        bool TryBuildBladePickupLayout(
            Transform visualRoot,
            bool alignGripToOrigin,
            out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();
            var referenceTransform = visualRoot != null && visualRoot.parent != null ? visualRoot.parent : visualRoot;
            if (visualRoot == null || !TryGetVisualBoundsRelativeToReference(visualRoot, referenceTransform, out var bounds))
                return false;

            if (alignGripToOrigin)
                visualRoot.localPosition += -GetBladeGripCenter(bounds);

            bladeLayout = BuildBladePickupLayoutFromBounds(bounds, alignGripToOrigin);
            return true;
        }

        static Vector3 GetBladeGripCenter(Bounds bounds)
        {
            var length = Mathf.Max(0.18f, bounds.size.z);
            var gripLength = Mathf.Clamp(length * 0.22f, 0.08f, 0.14f);
            return new Vector3(bounds.center.x, bounds.center.y, bounds.min.z + gripLength * 0.5f);
        }

        static BladePickupLayout BuildBladePickupLayoutFromBounds(Bounds bounds, bool alignGripToOrigin)
        {
            const float importedBladeBodyColliderBackshift = 0.15f;

            var length = Mathf.Max(0.18f, bounds.size.z);
            var gripLength = Mathf.Clamp(length * 0.22f, 0.08f, 0.14f);
            var gripCenter = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z + gripLength * 0.5f);
            var gripBaseZ = alignGripToOrigin ? 0f : gripCenter.z;
            var gripCenterX = alignGripToOrigin ? 0f : gripCenter.x;
            var gripCenterY = alignGripToOrigin ? 0f : gripCenter.y;
            var crossSection = Mathf.Max(bounds.size.x, bounds.size.y);
            var gripRadius = Mathf.Clamp(crossSection * 0.28f, 0.016f, 0.028f);
            var bladeRadius = Mathf.Clamp(crossSection * 0.18f, 0.009f, gripRadius * 0.8f);
            var frontEdgeZ = alignGripToOrigin ? bounds.max.z - gripCenter.z : bounds.max.z;
            var bodyStartZ = gripBaseZ + gripLength * 0.25f;
            var bodyEndZ = Mathf.Max(bodyStartZ + 0.12f, frontEdgeZ - gripLength * 0.06f);
            var bodyHeight = Mathf.Max(0.12f, bodyEndZ - bodyStartZ);

            return new BladePickupLayout
            {
                GripColliderCenter = new Vector3(gripCenterX, gripCenterY, gripBaseZ),
                GripColliderHeight = Mathf.Max(gripLength, gripRadius * 2.05f),
                GripColliderRadius = gripRadius,
                BodyColliderCenter = new Vector3(
                    gripCenterX,
                    gripCenterY,
                    bodyStartZ + bodyHeight * 0.5f - importedBladeBodyColliderBackshift),
                BodyColliderHeight = bodyHeight,
                BodyColliderRadius = bladeRadius,
                AttachLocalPosition = new Vector3(gripCenterX, gripCenterY, gripBaseZ)
            };
        }

        void CreateBladeColliders(
            Transform parent,
            BladePickupLayout bladeLayout,
            out CapsuleCollider gripCollider,
            out CapsuleCollider bodyCollider)
        {
            gripCollider = CreateCapsuleCollider(
                parent,
                "Grip Collider",
                bladeLayout.GripColliderCenter,
                bladeLayout.GripColliderRadius,
                bladeLayout.GripColliderHeight);

            bodyCollider = CreateCapsuleCollider(
                parent,
                "Body Collider",
                bladeLayout.BodyColliderCenter,
                bladeLayout.BodyColliderRadius,
                bladeLayout.BodyColliderHeight);
        }

        static CapsuleCollider CreateCapsuleCollider(
            Transform parent,
            string colliderName,
            Vector3 localPosition,
            float radius,
            float height)
        {
            var colliderRoot = new GameObject(colliderName);
            colliderRoot.transform.SetParent(parent, false);
            colliderRoot.transform.localPosition = localPosition;
            colliderRoot.transform.localRotation = Quaternion.identity;

            var capsuleCollider = colliderRoot.AddComponent<CapsuleCollider>();
            capsuleCollider.direction = 2;
            capsuleCollider.radius = Mathf.Max(0.005f, radius);
            capsuleCollider.height = Mathf.Max(capsuleCollider.radius * 2.05f, height);
            capsuleCollider.contactOffset = 0.005f;
            return capsuleCollider;
        }

        void AlignLongestVisualAxisToForward(Transform visualRoot)
        {
            if (visualRoot == null || !TryGetVisualLocalBounds(visualRoot, out var bounds))
                return;

            var size = bounds.size;
            if (size.x >= size.y && size.x >= size.z)
            {
                visualRoot.localRotation *= Quaternion.Euler(0f, 90f, 0f);
                return;
            }

            if (size.y >= size.x && size.y >= size.z)
                visualRoot.localRotation *= Quaternion.Euler(-90f, 0f, 0f);
        }

        void UniformScaleVisualToLength(Transform visualRoot, float targetLength)
        {
            if (visualRoot == null || targetLength <= 0f || !TryGetVisualLocalBounds(visualRoot, out var bounds))
                return;

            var currentLength = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (currentLength <= 0.0001f)
                return;

            var scaleFactor = targetLength / currentLength;
            visualRoot.localScale *= scaleFactor;
        }

        void CenterVisualAndPlaceBackEdge(Transform visualRoot, float backEdgePosition)
        {
            if (visualRoot == null || !TryGetVisualLocalBounds(visualRoot, out var bounds))
                return;

            visualRoot.localPosition += new Vector3(
                -bounds.center.x,
                -bounds.center.y,
                backEdgePosition - bounds.min.z);
        }

        void AlignChainRigToGrip(Transform visualRoot, float gripForwardOffset)
        {
            if (visualRoot == null)
                return;

            if (!TryCollectOrderedChainBones(visualRoot, out var chainBones) || chainBones.Count < 2)
            {
                CenterVisualAndPlaceBackEdge(visualRoot, 0.02f);
                return;
            }

            var firstBone = chainBones[0];
            var lastBone = chainBones[chainBones.Count - 1];
            var firstBoneLocal = visualRoot.InverseTransformPoint(firstBone.position);
            var lastBoneLocal = visualRoot.InverseTransformPoint(lastBone.position);
            if (lastBoneLocal.z < firstBoneLocal.z)
            {
                visualRoot.localRotation *= Quaternion.Euler(0f, 180f, 0f);
                firstBoneLocal = visualRoot.InverseTransformPoint(firstBone.position);
            }

            visualRoot.localPosition += new Vector3(
                -firstBoneLocal.x,
                -firstBoneLocal.y,
                gripForwardOffset - firstBoneLocal.z);
        }

        bool TryCollectOrderedChainBones(Transform searchRoot, out List<Transform> chainBones)
        {
            chainBones = new List<Transform>();
            if (searchRoot == null)
                return false;

            var allTransforms = searchRoot.GetComponentsInChildren<Transform>(true);
            Transform armature = null;
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null)
                    continue;

                if (candidate.name.ToLowerInvariant().Contains("armature"))
                {
                    armature = candidate;
                    break;
                }
            }

            Transform rootBone = null;
            var shallowestDepth = int.MaxValue;
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (!IsChainBoneTransform(candidate))
                    continue;

                if (armature != null && !candidate.IsChildOf(armature))
                    continue;

                if (IsChainBoneTransform(candidate.parent))
                    continue;

                var depth = GetTransformDepth(candidate);
                if (depth < shallowestDepth)
                {
                    shallowestDepth = depth;
                    rootBone = candidate;
                }
            }

            var current = rootBone;
            while (current != null)
            {
                chainBones.Add(current);
                current = GetPrimaryChainBoneChild(current);
            }

            return chainBones.Count > 0;
        }

        static bool IsChainBoneTransform(Transform transformCandidate)
        {
            if (transformCandidate == null)
                return false;

            var lowerName = transformCandidate.name.ToLowerInvariant();
            return lowerName.Contains("bone") && !lowerName.Contains("_end");
        }

        static Transform GetPrimaryChainBoneChild(Transform parentBone)
        {
            if (parentBone == null)
                return null;

            Transform bestChild = null;
            for (var i = 0; i < parentBone.childCount; i++)
            {
                var child = parentBone.GetChild(i);
                if (!IsChainBoneTransform(child))
                    continue;

                if (bestChild == null || string.CompareOrdinal(child.name, bestChild.name) < 0)
                    bestChild = child;
            }

            return bestChild;
        }

        static int GetTransformDepth(Transform transformCandidate)
        {
            var depth = 0;
            while (transformCandidate != null)
            {
                depth++;
                transformCandidate = transformCandidate.parent;
            }

            return depth;
        }

        bool TryGetVisualLocalBounds(Transform visualRoot, out Bounds bounds)
        {
            return TryGetVisualBoundsRelativeToReference(visualRoot, visualRoot, out bounds);
        }

        static bool TryGetVisualBoundsRelativeToReference(Transform visualRoot, Transform referenceTransform, out Bounds bounds)
        {
            bounds = default;
            if (visualRoot == null || referenceTransform == null)
                return false;

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                var rendererBounds = renderer.bounds;
                var corners = GetBoundsCorners(rendererBounds);
                for (var j = 0; j < corners.Length; j++)
                {
                    var localCorner = referenceTransform.InverseTransformPoint(corners[j]);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localCorner);
                    }
                }
            }

            return hasBounds;
        }

        static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z)
            };
        }

        static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty(BaseColorShaderId))
                material.SetColor(BaseColorShaderId, color);

            if (material.HasProperty(ColorShaderId))
                material.SetColor(ColorShaderId, color);
        }

        Transform FindHandTransform(string handedness)
        {
            if (m_PlayerRoot == null || string.IsNullOrWhiteSpace(handedness))
                return null;

            var needle = handedness.ToLowerInvariant();
            Transform fallback = null;
            var allTransforms = m_PlayerRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var transformCandidate = allTransforms[i];
                var lowerName = transformCandidate.name.ToLowerInvariant();
                if (!lowerName.Contains(needle))
                    continue;

                if (!lowerName.Contains("hand"))
                    continue;

                if (lowerName.Contains("controller") || lowerName.Contains("visual") || lowerName.Contains("model"))
                    continue;

                if (lowerName == $"{needle} hand")
                    return transformCandidate;

                fallback ??= transformCandidate;
            }

            return fallback;
        }

        Transform FindControllerTransform(string handedness)
        {
            if (m_PlayerRoot == null || string.IsNullOrWhiteSpace(handedness))
                return null;

            var needle = handedness.ToLowerInvariant();
            var allTransforms = m_PlayerRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var transformCandidate = allTransforms[i];
                var lowerName = transformCandidate.name.ToLowerInvariant();
                if (!lowerName.Contains(needle))
                    continue;

                if (lowerName.Contains("controller") && !lowerName.Contains("visual"))
                    return transformCandidate;
            }

            return null;
        }

        void ResolveModalityManagedRigTransforms(
            out Transform leftHandTransform,
            out Transform rightHandTransform,
            out Transform leftControllerTransform,
            out Transform rightControllerTransform)
        {
            leftHandTransform = null;
            rightHandTransform = null;
            leftControllerTransform = null;
            rightControllerTransform = null;

            m_InputModalityManager ??= FindPreferredInputModalityManager(m_PlayerRoot);
            if (m_InputModalityManager == null)
                return;

            TryResolveModalityTransform(m_InputModalityManager.leftHand, out leftHandTransform);
            TryResolveModalityTransform(m_InputModalityManager.rightHand, out rightHandTransform);

            if (!TryResolveModalityTransform(m_InputModalityManager.leftController, out leftControllerTransform))
                leftControllerTransform = leftHandTransform;

            if (!TryResolveModalityTransform(m_InputModalityManager.rightController, out rightControllerTransform))
                rightControllerTransform = rightHandTransform;
        }

        bool TryResolveModalityTransform(GameObject modalityObject, out Transform resolvedTransform)
        {
            resolvedTransform = null;
            if (modalityObject == null || m_PlayerRoot == null)
                return false;

            var candidateTransform = modalityObject.transform;
            if (candidateTransform == null || !modalityObject.scene.IsValid() || !modalityObject.scene.isLoaded)
                return false;

            if (!IsTransformUnderRoot(candidateTransform, m_PlayerRoot))
                return false;

            resolvedTransform = candidateTransform;
            return true;
        }

        static XRInputModalityManager FindPreferredInputModalityManager(Transform root)
        {
            if (root == null)
                return null;

            var managers = root.GetComponentsInChildren<XRInputModalityManager>(true);
            XRInputModalityManager bestManager = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < managers.Length; i++)
            {
                var manager = managers[i];
                if (manager == null)
                    continue;

                var score = 0;
                if (IsActiveAndEnabled(manager))
                    score += 2;
                if (manager.leftHand != null)
                    score += 2;
                if (manager.rightHand != null)
                    score += 2;
                if (manager.leftController != null)
                    score += 1;
                if (manager.rightController != null)
                    score += 1;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestManager = manager;
            }

            return bestManager;
        }

        void ConfigureGrabInteractable(
            XRGrabInteractable interactable,
            bool allowDynamicAttach,
            XRBaseInteractable.MovementType movementType = XRBaseInteractable.MovementType.VelocityTracking)
        {
            if (interactable == null)
                return;

            interactable.trackPosition = true;
            interactable.trackRotation = true;
            interactable.throwOnDetach = true;
            interactable.movementType = movementType;
            interactable.velocityDamping = 0.9f;
            interactable.velocityScale = 1.1f;
            interactable.angularVelocityDamping = 0.9f;
            interactable.angularVelocityScale = 1.1f;
            interactable.useDynamicAttach = allowDynamicAttach;
            interactable.matchAttachPosition = allowDynamicAttach;
            interactable.matchAttachRotation = allowDynamicAttach;
            interactable.snapToColliderVolume = allowDynamicAttach;
            interactable.reinitializeDynamicAttachEverySingleGrab = allowDynamicAttach;
            interactable.attachEaseInTime = allowDynamicAttach ? 0.035f : 0f;
            interactable.smoothPosition = true;
            interactable.smoothPositionAmount = movementType == XRBaseInteractable.MovementType.Instantaneous ? 18f : 14f;
            interactable.tightenPosition = movementType == XRBaseInteractable.MovementType.Instantaneous ? 0.92f : 0.8f;
            interactable.smoothRotation = true;
            interactable.smoothRotationAmount = movementType == XRBaseInteractable.MovementType.Instantaneous ? 18f : 14f;
            interactable.tightenRotation = movementType == XRBaseInteractable.MovementType.Instantaneous ? 0.92f : 0.82f;
            interactable.smoothScale = false;

            EnsureMaxPhysicalGrabDistanceFilter(interactable);
        }

        void ConfigureExistingGrabInteractables()
        {
            var grabInteractables = FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < grabInteractables.Length; i++)
            {
                var grabInteractable = grabInteractables[i];
                EnsureMaxPhysicalGrabDistanceFilter(grabInteractable);
                if (!IsCombatPickupInteractable(grabInteractable))
                    continue;

                AddPickupSelectListeners(
                    grabInteractable,
                    grabInteractable.GetComponent<RuntimeMountedPickup>() ?? grabInteractable.GetComponentInParent<RuntimeMountedPickup>());
            }
        }

        void EnsureSceneAuthoredResettables()
        {
            var sceneAuthoredRoots = new HashSet<GameObject>();

            var rigidbodies = FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < rigidbodies.Length; i++)
            {
                var rigidbody = rigidbodies[i];
                var resetRoot = GetSceneAuthoredResetRoot(rigidbody != null ? rigidbody.transform : null);
                if (resetRoot != null)
                    sceneAuthoredRoots.Add(resetRoot.gameObject);
            }

            var grabInteractables = FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < grabInteractables.Length; i++)
            {
                var grabInteractable = grabInteractables[i];
                var resetRoot = GetSceneAuthoredResetRoot(grabInteractable != null ? grabInteractable.transform : null);
                if (resetRoot != null)
                    sceneAuthoredRoots.Add(resetRoot.gameObject);
            }

            foreach (var root in sceneAuthoredRoots)
            {
                if (root == null)
                    continue;

                var resettable = root.GetComponent<SceneAuthoredResettable>();
                if (resettable == null)
                    resettable = root.AddComponent<SceneAuthoredResettable>();

                resettable.Configure(root.GetComponent<Rigidbody>());
            }
        }

        Transform GetSceneAuthoredResetRoot(Transform source)
        {
            if (source == null)
                return null;

            Transform selectedRoot = null;
            var current = source;
            while (current != null)
            {
                if (current.GetComponent<Rigidbody>() != null || current.GetComponent<XRGrabInteractable>() != null)
                    selectedRoot = current;

                current = current.parent;
            }

            if (selectedRoot == null || m_RuntimeSpawnedObjects.Contains(selectedRoot.gameObject))
                return null;

            return selectedRoot;
        }

        void EnsureMaxPhysicalGrabDistanceFilter(XRGrabInteractable interactable)
        {
            if (interactable == null)
                return;

            var filter = interactable.GetComponent<MaxGrabDistanceSelectFilter>();
            if (filter == null)
                filter = interactable.gameObject.AddComponent<MaxGrabDistanceSelectFilter>();

            filter.Configure(m_MaxPhysicalGrabDistance);
            AddSelectFilterIfMissing(interactable, filter);
        }

        static void AddSelectFilterIfMissing(XRBaseInteractable interactable, IXRSelectFilter filter)
        {
            if (interactable == null || filter == null)
                return;

            var existingFilters = new List<IXRSelectFilter>();
            interactable.selectFilters.GetAll(existingFilters);
            for (var i = 0; i < existingFilters.Count; i++)
            {
                if (ReferenceEquals(existingFilters[i], filter))
                    return;
            }

            interactable.selectFilters.Add(filter);
        }

        void EnsureSwingWeapon(GameObject target, bool makeTriggerCollider, bool forceKinematic, float defaultRadius)
        {
            var rigidbody = target.GetComponent<Rigidbody>();
            if (rigidbody == null)
                rigidbody = target.AddComponent<Rigidbody>();

            if (forceKinematic)
            {
                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                var sphereCollider = target.GetComponent<SphereCollider>();
                if (sphereCollider == null)
                    sphereCollider = target.AddComponent<SphereCollider>();

                sphereCollider.radius = Mathf.Max(sphereCollider.radius, defaultRadius);
                sphereCollider.isTrigger = makeTriggerCollider;
            }
            else
            {
                rigidbody.isKinematic = false;
                rigidbody.useGravity = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

                var collider = target.GetComponent<Collider>();
                if (collider != null)
                    collider.isTrigger = makeTriggerCollider;
            }

            var damageDealer = target.GetComponent<SwingDamageDealer>();
            if (damageDealer == null)
                damageDealer = target.AddComponent<SwingDamageDealer>();
            damageDealer.SetDamageGate(null);
        }

        static bool TryGetMemberValue(object target, string memberName, out object value)
        {
            value = null;
            if (target == null || string.IsNullOrEmpty(memberName))
                return false;

            var targetType = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var propertyInfo = targetType.GetProperty(memberName, flags);
            if (propertyInfo != null)
            {
                value = propertyInfo.GetValue(target);
                return true;
            }

            var fieldInfo = targetType.GetField(memberName, flags);
            if (fieldInfo != null)
            {
                value = fieldInfo.GetValue(target);
                return true;
            }

            return false;
        }

        static bool TrySetMemberValue(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
                return false;

            var targetType = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var propertyInfo = targetType.GetProperty(memberName, flags);
            if (propertyInfo != null && propertyInfo.CanWrite)
            {
                var convertedValue = ConvertValue(value, propertyInfo.PropertyType);
                if (convertedValue != null || !propertyInfo.PropertyType.IsValueType)
                {
                    propertyInfo.SetValue(target, convertedValue);
                    return true;
                }
            }

            var fieldInfo = targetType.GetField(memberName, flags);
            if (fieldInfo != null)
            {
                var convertedValue = ConvertValue(value, fieldInfo.FieldType);
                if (convertedValue != null || !fieldInfo.FieldType.IsValueType)
                {
                    fieldInfo.SetValue(target, convertedValue);
                    return true;
                }
            }

            return false;
        }

        static object ConvertValue(object value, Type targetType)
        {
            if (targetType == null)
                return null;

            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (nonNullableType.IsInstanceOfType(value))
                return value;

            try
            {
                if (nonNullableType.IsEnum)
                {
                    if (value is string enumString)
                        return Enum.Parse(nonNullableType, enumString, ignoreCase: true);

                    return Enum.ToObject(nonNullableType, value);
                }

                return Convert.ChangeType(value, nonNullableType);
            }
            catch
            {
                return null;
            }
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(Rigidbody))]
    class RuntimeKillZoneBoundary : MonoBehaviour
    {
        PlayerDamageReceiver m_PlayerDamageReceiver;
        Action<PlayerDamageReceiver> m_OnPlayerExited;
        Action<CapsuleEnemy> m_OnEnemyExited;
        Action<Rigidbody> m_OnRigidbodyExited;

        public void Configure(
            PlayerDamageReceiver playerDamageReceiver,
            Action<PlayerDamageReceiver> onPlayerExited,
            Action<CapsuleEnemy> onEnemyExited,
            Action<Rigidbody> onRigidbodyExited)
        {
            m_PlayerDamageReceiver = playerDamageReceiver;
            m_OnPlayerExited = onPlayerExited;
            m_OnEnemyExited = onEnemyExited;
            m_OnRigidbodyExited = onRigidbodyExited;
        }

        void OnTriggerExit(Collider other)
        {
            if (other == null)
                return;

            var player = other.GetComponentInParent<PlayerDamageReceiver>();
            if (player != null)
            {
                if (m_PlayerDamageReceiver == null || ReferenceEquals(player, m_PlayerDamageReceiver))
                    m_OnPlayerExited?.Invoke(player);
                return;
            }

            var enemy = other.GetComponentInParent<CapsuleEnemy>();
            if (enemy != null)
            {
                m_OnEnemyExited?.Invoke(enemy);
                return;
            }

            var rigidbody = other.attachedRigidbody;
            if (rigidbody != null && !rigidbody.isKinematic)
                m_OnRigidbodyExited?.Invoke(rigidbody);
        }
    }
}
