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
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using VRCombat.Combat;
using VRCombat.Enemies;
using VRCombat.Player;
using VRCombat.UI;

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
        float m_KillZoneHorizontalRadius = 4.75f;

        [SerializeField]
        float m_KillZoneBelowCenter = 2.35f;

        [SerializeField]
        float m_KillZoneAboveCenter = 4.2f;

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
        readonly Dictionary<XRGrabInteractable, HandSide> m_EquippedHandByPickup = new Dictionary<XRGrabInteractable, HandSide>();
        readonly List<Renderer> m_LeftSuppressedHandRenderers = new List<Renderer>();
        readonly List<Renderer> m_RightSuppressedHandRenderers = new List<Renderer>();
        static readonly List<InputDevice> s_HandDeviceBuffer = new List<InputDevice>(4);
        static readonly List<InputDevice> s_ControllerDeviceBuffer = new List<InputDevice>(4);
        Coroutine m_DeathFlowRoutine;
        Vector3 m_KillZoneCenter;
        bool m_HasKillZoneCenter;
        float m_NextKillZoneGlobalSweepTime;
        RuntimeKillZoneBoundary m_RuntimeKillZoneBoundary;
        UnityEngine.InputSystem.InputAction m_RuntimePauseMenuAction;
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
        int m_LeftHandReplacementHoldCount;
        int m_RightHandReplacementHoldCount;
        float m_BootstrapStartedRealtime;
        float m_MovementVignetteStrength;
        TunnelingVignetteController m_TunnelingVignetteController;

        static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorShaderId = Shader.PropertyToID("_Color");
        const string MovementVignetteStrengthPrefKey = "vrcombat.movement_vignette_strength";
        const string MovementVignetteInitializedPrefKey = "vrcombat.movement_vignette_initialized_v2";
        const string PauseMapperActionTitle = "VR Combat Pause Menu";
        const float PauseToggleDebounceSeconds = 0.12f;
        const float CameraRecoveryGraceSeconds = 0.75f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureBootstrapperExists()
        {
            if (FindAnyObjectByType<VRCombatBootstrapper>() != null)
                return;

            var bootstrapperObject = new GameObject("VR Combat Bootstrapper");
            bootstrapperObject.AddComponent<VRCombatBootstrapper>();
        }

        IEnumerator Start()
        {
            m_BootstrapStartedRealtime = Time.realtimeSinceStartup;
            while (!TryResolvePlayerRig())
                yield return null;

            SetupPlayerDamageDetection();
            SetupMovementVignetteControl();
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

        bool TryResolvePlayerRig(bool forceRefresh = false)
        {
            if (!forceRefresh && m_PlayerCamera != null && m_PlayerRoot != null)
                return true;

            Camera resolvedCamera = null;
            Transform resolvedRoot = null;
            var resolvedRigType = "Unknown";

            if (TryFindActiveXrOriginRig(out var xrOrigin, out resolvedCamera, out resolvedRoot))
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

        static bool TryFindActiveXrOriginRig(out XROrigin selectedOrigin, out Camera selectedCamera, out Transform selectedRoot)
        {
            selectedOrigin = null;
            selectedCamera = null;
            selectedRoot = null;

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

                var score = 0;
                if (origin.name.IndexOf("xr origin", StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 4;
                if (candidateCamera.CompareTag("MainCamera"))
                    score += 2;
                if (origin.Camera != null)
                    score += 2;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                selectedOrigin = origin;
                selectedCamera = candidateCamera;
                selectedRoot = candidateRoot;
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
                if (origin.Camera != null)
                    score += 2;
                if (origin.CameraFloorOffsetObject != null)
                    score += 1;

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

            Debug.Log(
                $"[VRCombat] Selected rig={resolvedRigType}, camera={GetTransformPath(m_PlayerCamera != null ? m_PlayerCamera.transform : null)}, root={GetTransformPath(m_PlayerRoot)}, disabledRigStacks={disabledDescription}");
        }

        void ConfigureHandFirstInteraction()
        {
            if (m_PlayerRoot == null)
                return;

            m_LeftHandTransform = FindHandTransform("left");
            m_RightHandTransform = FindHandTransform("right");
            m_LeftControllerTransform = FindControllerTransform("left");
            m_RightControllerTransform = FindControllerTransform("right");

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
            if (m_PlayerRoot == null)
                return;

            if (m_LeftHandTransform != null)
            {
                var controllerTrackedLeft = IsTrackedControllerActive(InputDeviceCharacteristics.Left);
                var nonHandTrackedLeft = IsTrackedNonHandDeviceActive(InputDeviceCharacteristics.Left);
                var useControllerLeft = controllerTrackedLeft || nonHandTrackedLeft || !IsTrackedHandActive(InputDeviceCharacteristics.Left);
                if (useControllerLeft)
                {
                    if (!ReferenceEquals(m_LeftHandTransform, m_LeftControllerTransform) &&
                        TryGetControllerFallbackPose(InputDeviceCharacteristics.Left, XRNode.LeftHand, m_LeftControllerTransform, out var leftPosition, out var leftRotation))
                    {
                        m_LeftHandTransform.SetPositionAndRotation(leftPosition, leftRotation);
                    }
                }

                SetProxyActive(m_LeftHandProxyVisual, false);
            }

            if (m_RightHandTransform != null)
            {
                var controllerTrackedRight = IsTrackedControllerActive(InputDeviceCharacteristics.Right);
                var nonHandTrackedRight = IsTrackedNonHandDeviceActive(InputDeviceCharacteristics.Right);
                var useControllerRight = controllerTrackedRight || nonHandTrackedRight || !IsTrackedHandActive(InputDeviceCharacteristics.Right);
                if (useControllerRight)
                {
                    if (!ReferenceEquals(m_RightHandTransform, m_RightControllerTransform) &&
                        TryGetControllerFallbackPose(InputDeviceCharacteristics.Right, XRNode.RightHand, m_RightControllerTransform, out var rightPosition, out var rightRotation))
                    {
                        m_RightHandTransform.SetPositionAndRotation(rightPosition, rightRotation);
                    }
                }

                SetProxyActive(m_RightHandProxyVisual, false);
            }
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

                if (device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && isTracked)
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

                if (device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && isTracked)
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

                if (device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && !isTracked)
                    continue;

                if ((device.characteristics & InputDeviceCharacteristics.HandTracking) != 0)
                    continue;

                return true;
            }

            return false;
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var rawLeftMenuPressed = ReadRawLeftMenuButtonDown();
            LogPauseAttemptDiagnostics(runtimeActionPressed, rawLeftMenuPressed);
#endif
            return runtimeActionPressed;
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
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(m_RuntimePauseMenuAction, "<OculusTouchController>{LeftHand}/menu");
                m_RuntimePauseMenuAction.Enable();
            }

            ConfigureMetaControllerButtonsMapperPauseBinding();
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
                    Button = OVRInput.Button.Start,
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

            return m_RuntimePauseMenuAction != null && m_RuntimePauseMenuAction.WasPressedThisFrame();
        }

        static bool WasInputSystemButtonPressedThisFrame(UnityEngine.InputSystem.InputDevice device, string controlPath)
        {
            if (device == null || !device.added || string.IsNullOrWhiteSpace(controlPath))
                return false;

            var control = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(controlPath);
            return control != null && control.wasPressedThisFrame;
        }

        void LogPauseInputDiagnosticsAtStartup()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (m_HasLoggedPauseStartupDiagnostics)
                return;

            m_HasLoggedPauseStartupDiagnostics = true;
            var leftHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var hasXrMenuUsage = leftHandDevice.isValid &&
                (leftHandDevice.TryGetFeatureValue(CommonUsages.menuButton, out _)
                 || leftHandDevice.TryGetFeatureValue(new InputFeatureUsage<bool>("menu"), out _)
                 || leftHandDevice.TryGetFeatureValue(new InputFeatureUsage<bool>("start"), out _));

            var inputSystemLeft = UnityEngine.InputSystem.XR.XRController.leftHand;
            var hasInputSystemMenuControl = inputSystemLeft != null &&
                (inputSystemLeft.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("menu") != null
                 || inputSystemLeft.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("menuButton") != null);

            var xrDevices = new List<InputDevice>();
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
                $"[VRCombat] Pause diagnostics: actionEnabled={(m_RuntimePauseMenuAction != null && m_RuntimePauseMenuAction.enabled)}, leftXRDeviceValid={leftHandDevice.isValid}, leftXRMenuFeature={hasXrMenuUsage}, leftInputSystemMenuControl={hasInputSystemMenuControl}, xrDevices=[{xrDeviceSummary}], inputSystemDevices=[{inputSystemSummary}]");
#endif
        }

        static bool ReadRawLeftMenuButtonDown()
        {
            var leftHandDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (IsMenuSpecificButtonPressed(leftHandDevice))
                return true;

            var left = UnityEngine.InputSystem.XR.XRController.leftHand;
            if (WasInputSystemButtonPressedThisFrame(left, "menu")
                || WasInputSystemButtonPressedThisFrame(left, "menuButton"))
            {
                return true;
            }

            try
            {
                if (OVRInput.GetDown(OVRInput.Button.Start)
                    || OVRInput.GetDown(OVRInput.RawButton.Start))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        void LogPauseAttemptDiagnostics(bool runtimeActionPressed, bool rawLeftMenuPressed)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (m_HasLoggedFirstPauseAttempt || (!runtimeActionPressed && !rawLeftMenuPressed))
                return;

            m_HasLoggedFirstPauseAttempt = true;
            Debug.Log(
                $"[VRCombat] First pause attempt: runtimeActionPressed={runtimeActionPressed}, rawLeftMenuPressed={rawLeftMenuPressed}, actionEnabled={(m_RuntimePauseMenuAction != null && m_RuntimePauseMenuAction.enabled)}");
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
                (!device.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) || isTracked))
            {
                var gotPosition = device.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                var gotRotation = device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
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

                if (controllerDevice.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && !isTracked)
                    continue;

                var gotPosition = controllerDevice.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                var gotRotation = controllerDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
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

                if (trackedDevice.TryGetFeatureValue(CommonUsages.isTracked, out var isTracked) && !isTracked)
                    continue;

                var gotPosition = trackedDevice.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                var gotRotation = trackedDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
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

            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out var primaryPressed) && primaryPressed)
                return true;

            if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out var secondaryPressed) && secondaryPressed)
                return true;

            return false;
        }

        static bool IsMenuSpecificButtonPressed(InputDevice device)
        {
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(CommonUsages.menuButton, out var menuPressed) && menuPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("menu"), out var menuPressedByName) && menuPressedByName)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("applicationMenu"), out var applicationMenuPressed) && applicationMenuPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("appMenuButton"), out var appMenuPressed) && appMenuPressed)
                return true;

            if (device.TryGetFeatureValue(new InputFeatureUsage<bool>("start"), out var startPressed) && startPressed)
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

                RestoreSuppressedHandRenderers();
                DestroyRuntimeCombatObjects();
                DestroyLegacyStickObjects();
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
                ConfigureExistingGrabInteractables();
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
            m_KillZoneCenter = destinationPosition;
            m_HasKillZoneCenter = true;
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

        void InitializeKillZoneCenter()
        {
            if (m_HasKillZoneCenter)
            {
                EnsureRuntimeKillZoneBoundary();
                return;
            }

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

            var rigidbodyObject = rigidbody.gameObject;
            if (rigidbodyObject == null)
                return;

            m_RuntimeSpawnedObjects.Remove(rigidbodyObject);
            Destroy(rigidbodyObject);
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

            var flatForward = Vector3.ProjectOnPlane(m_PlayerCamera.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;

            var rightDirection = Vector3.Cross(Vector3.up, flatForward).normalized;
            if (rightDirection.sqrMagnitude < 0.001f)
                rightDirection = Vector3.right;

            TryResolveWallMount(rightDirection, mountY, out var bladeWallPoint, out var bladeWallNormal);
            TryResolveWallMount(-rightDirection, mountY, out var shieldWallPoint, out var shieldWallNormal);

            var bladeWallTangent = Vector3.Cross(Vector3.up, bladeWallNormal).normalized;
            if (bladeWallTangent.sqrMagnitude < 0.001f)
                bladeWallTangent = flatForward;

            var bladeBase = bladeWallPoint + bladeWallNormal * Mathf.Max(0.02f, m_LoadoutWallInset);
            var bladeFacing = Quaternion.LookRotation(-bladeWallNormal, Vector3.up);

            CreateWallBladePickup(
                "Wall Blade 1",
                bladeBase + bladeWallTangent * (m_LoadoutBladeSpacing * 0.5f),
                bladeFacing * Quaternion.Euler(-90f, 0f, 0f));

            CreateWallBladePickup(
                "Wall Blade 2",
                bladeBase - bladeWallTangent * (m_LoadoutBladeSpacing * 0.5f),
                bladeFacing * Quaternion.Euler(-90f, 0f, 0f));

            var shieldPosition = shieldWallPoint + shieldWallNormal * (Mathf.Max(0.02f, m_LoadoutWallInset) + 0.01f);
            var shieldFacing = Quaternion.LookRotation(-shieldWallNormal, Vector3.up);
            CreateWallShieldPickup("Wall Shield", shieldPosition, shieldFacing);
        }

        void TryResolveWallMount(Vector3 castDirection, float mountY, out Vector3 wallPoint, out Vector3 wallNormal)
        {
            castDirection = Vector3.ProjectOnPlane(castDirection, Vector3.up).normalized;
            if (castDirection.sqrMagnitude < 0.001f)
                castDirection = Vector3.right;

            var origin = m_PlayerCamera.transform.position;
            origin.y = mountY;

            if (Physics.Raycast(origin, castDirection, out var hit, Mathf.Max(1.5f, m_LoadoutWallSearchDistance), ~0, QueryTriggerInteraction.Ignore))
            {
                wallNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
                if (wallNormal.sqrMagnitude < 0.0001f)
                    wallNormal = -castDirection;

                wallPoint = hit.point;
                wallPoint.y = mountY;
                return;
            }

            wallNormal = -castDirection;
            wallPoint = origin + castDirection * Mathf.Max(0.6f, m_LoadoutFallbackDistance);
            wallPoint.y = mountY;
        }

        void CreateWallBladePickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            var bladeVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bladeVisual.name = "Blade";
            bladeVisual.transform.SetParent(root.transform, false);
            bladeVisual.transform.localPosition = new Vector3(0f, 0f, 0.22f);
            bladeVisual.transform.localRotation = Quaternion.identity;
            bladeVisual.transform.localScale = new Vector3(0.045f, 0.012f, 0.42f);
            ApplyMaterial(bladeVisual, GetOrCreatePickupMaterial());

            var handleVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handleVisual.name = "Handle";
            handleVisual.transform.SetParent(root.transform, false);
            handleVisual.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            handleVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            handleVisual.transform.localScale = new Vector3(0.024f, 0.1f, 0.024f);
            ApplyMaterial(handleVisual, GetOrCreatePickupMaterial());

            var bladeVisualCollider = bladeVisual.GetComponent<Collider>();
            if (bladeVisualCollider != null)
                Destroy(bladeVisualCollider);
            var handleVisualCollider = handleVisual.GetComponent<Collider>();
            if (handleVisualCollider != null)
                Destroy(handleVisualCollider);

            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.center = new Vector3(0f, 0f, 0.165f);
            collider.radius = 0.028f;
            collider.height = 0.44f;
            collider.contactOffset = 0.0065f;

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 0.95f;
            rigidbody.linearDamping = 0.1f;
            rigidbody.angularDamping = 0.14f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureGrabInteractable(grabInteractable, allowDynamicAttach: true);
            grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grabInteractable.throwOnDetach = false;
            grabInteractable.useDynamicAttach = false;
            grabInteractable.matchAttachPosition = false;
            grabInteractable.matchAttachRotation = false;
            grabInteractable.attachEaseInTime = 0f;

            var attachPoint = new GameObject("Attach Point");
            attachPoint.transform.SetParent(root.transform, false);
            attachPoint.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            attachPoint.transform.localRotation = Quaternion.identity;
            grabInteractable.attachTransform = attachPoint.transform;

            EnsureSwingWeapon(root, makeTriggerCollider: false, forceKinematic: false, defaultRadius: 0.08f);
            SetupWallMountedPickup(root, grabInteractable, collider);
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
            ConfigureGrabInteractable(grabInteractable, allowDynamicAttach: true);
            grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grabInteractable.throwOnDetach = false;
            grabInteractable.useDynamicAttach = false;
            grabInteractable.matchAttachPosition = false;
            grabInteractable.matchAttachRotation = false;
            grabInteractable.attachEaseInTime = 0f;

            var attachPoint = new GameObject("Attach Point");
            attachPoint.transform.SetParent(root.transform, false);
            attachPoint.transform.localPosition = new Vector3(0f, 0f, -0.04f);
            attachPoint.transform.localRotation = Quaternion.identity;
            grabInteractable.attachTransform = attachPoint.transform;

            SetupWallMountedPickup(root, grabInteractable, collider);
        }

        void SetupWallMountedPickup(GameObject pickupRoot, XRGrabInteractable grabInteractable, Collider holdCollider)
        {
            if (pickupRoot == null || grabInteractable == null)
                return;

            var rigidbody = pickupRoot.GetComponent<Rigidbody>();
            if (rigidbody == null)
                return;

            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            grabInteractable.selectEntered.AddListener(args =>
            {
                if (rigidbody == null)
                    return;

                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                if (holdCollider != null)
                    holdCollider.isTrigger = false;

                HandleHandReplacementEquipped(grabInteractable, args);
            });

            grabInteractable.selectExited.AddListener(args =>
            {
                HandleHandReplacementReleased(grabInteractable, args);

                if (rigidbody == null)
                    return;

                rigidbody.isKinematic = false;
                rigidbody.useGravity = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                if (holdCollider != null)
                    holdCollider.isTrigger = false;
            });
        }

        void HandleHandReplacementEquipped(XRGrabInteractable pickup, SelectEnterEventArgs args)
        {
            if (pickup == null)
                return;

            var side = ResolvePickupHandSide(args, pickup.transform.position);
            m_EquippedHandByPickup[pickup] = side;

            if (side == HandSide.Left)
            {
                m_LeftHandReplacementHoldCount++;
                if (m_LeftHandReplacementHoldCount == 1)
                    SetHandSideVisualSuppressed(HandSide.Left, true);
            }
            else if (side == HandSide.Right)
            {
                m_RightHandReplacementHoldCount++;
                if (m_RightHandReplacementHoldCount == 1)
                    SetHandSideVisualSuppressed(HandSide.Right, true);
            }
        }

        void HandleHandReplacementReleased(XRGrabInteractable pickup, SelectExitEventArgs args)
        {
            if (pickup == null)
                return;

            if (!m_EquippedHandByPickup.TryGetValue(pickup, out var side))
                side = ResolvePickupHandSide(args, pickup.transform.position);

            m_EquippedHandByPickup.Remove(pickup);

            if (side == HandSide.Left)
            {
                m_LeftHandReplacementHoldCount = Mathf.Max(0, m_LeftHandReplacementHoldCount - 1);
                if (m_LeftHandReplacementHoldCount == 0)
                    SetHandSideVisualSuppressed(HandSide.Left, false);
            }
            else if (side == HandSide.Right)
            {
                m_RightHandReplacementHoldCount = Mathf.Max(0, m_RightHandReplacementHoldCount - 1);
                if (m_RightHandReplacementHoldCount == 0)
                    SetHandSideVisualSuppressed(HandSide.Right, false);
            }
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

            if (suppressedList.Count > 0)
                return;

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

            CollectSuppressedHandRenderers(primary, suppressedList);
            if (!ReferenceEquals(primary, secondary))
                CollectSuppressedHandRenderers(secondary, suppressedList);
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
            m_LeftHandReplacementHoldCount = 0;
            m_RightHandReplacementHoldCount = 0;
            m_EquippedHandByPickup.Clear();
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

        void ConfigureGrabInteractable(XRGrabInteractable interactable, bool allowDynamicAttach)
        {
            if (interactable == null)
                return;

            interactable.trackPosition = true;
            interactable.trackRotation = true;
            interactable.throwOnDetach = true;
            interactable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            interactable.velocityDamping = 0.04f;
            interactable.velocityScale = 2.2f;
            interactable.angularVelocityDamping = 0.03f;
            interactable.angularVelocityScale = 2.4f;
            interactable.useDynamicAttach = allowDynamicAttach;
            interactable.matchAttachPosition = allowDynamicAttach;
            interactable.matchAttachRotation = false;
            interactable.attachEaseInTime = 0f;

            EnsureMaxPhysicalGrabDistanceFilter(interactable);
        }

        void ConfigureExistingGrabInteractables()
        {
            var grabInteractables = FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < grabInteractables.Length; i++)
                EnsureMaxPhysicalGrabDistanceFilter(grabInteractables[i]);
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
