using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Meta.XR.BuildingBlocks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.XR.CoreUtils;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Samples.Hands;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.InputSystem;
using VRCombat.Combat;
using VRCombat.Enemies;
using VRCombat.Environment;
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

        enum DebugComboDirection
        {
            Up,
            Down,
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

        struct ShieldPickupLayout
        {
            public Vector3 ColliderCenter;
            public Vector3 ColliderSize;
            public Vector3 AttachLocalPosition;
            public Quaternion AttachLocalRotation;
        }

        enum BladeGripEnd
        {
            Rear,
            Front
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
        Transform m_PlayerTrackingSpace;
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
        Material m_FlintlockBulletMaterial;
        Material m_HandProxyMaterial;
        Material m_EnemyMaterial;
        Material m_ShieldMaterial;
        RunProgressionController m_RunProgressionController;
        PlayerSpellLoadout m_PlayerSpellLoadout;
        WristChainIntroController m_WristChainIntroController;
        [SerializeField] ArenaOpeningEncounter m_IntroOpeningEncounter;
        readonly List<ArenaOpeningEncounter> m_ArenaEncounters = new List<ArenaOpeningEncounter>();
        readonly Dictionary<int, ArenaOpeningEncounter> m_WaveUnlockEncounters = new Dictionary<int, ArenaOpeningEncounter>();
        readonly List<CapsuleEnemy> m_ActiveWaveEnemies = new List<CapsuleEnemy>();
        readonly Dictionary<CapsuleEnemy, float> m_EnemyKillZoneGraceUntil = new Dictionary<CapsuleEnemy, float>();
        readonly List<GameObject> m_RuntimeSpawnedObjects = new List<GameObject>();
        readonly List<CardTableRuntime> m_RuntimeCardTables = new List<CardTableRuntime>();
        readonly List<Collider> m_ArenaSurfaceColliders = new List<Collider>();
        readonly List<Collider> m_ArenaPlatformSurfaceColliders = new List<Collider>();
        readonly Dictionary<XRGrabInteractable, HandSide> m_EquippedHandByPickup = new Dictionary<XRGrabInteractable, HandSide>();
        readonly Dictionary<XRGrabInteractable, Dictionary<Transform, int>> m_HeldPickupOriginalLayers = new Dictionary<XRGrabInteractable, Dictionary<Transform, int>>();
        readonly HashSet<XRGrabInteractable> m_CombatPickupListenerRegistrations = new HashSet<XRGrabInteractable>();
        readonly List<Behaviour> m_ManagedLocomotionBehaviours = new List<Behaviour>();
        readonly Dictionary<Behaviour, bool> m_LocomotionDefaultEnabledStates = new Dictionary<Behaviour, bool>();
        readonly Dictionary<Behaviour, float> m_LocomotionBaseMoveSpeed = new Dictionary<Behaviour, float>();
        readonly List<Renderer> m_LeftSuppressedHandRenderers = new List<Renderer>();
        readonly List<Renderer> m_RightSuppressedHandRenderers = new List<Renderer>();
        readonly List<Collider> m_LeftSuppressedHandColliders = new List<Collider>();
        readonly List<Collider> m_RightSuppressedHandColliders = new List<Collider>();
        int m_LeftSuppressedRendererHoldCount;
        int m_RightSuppressedRendererHoldCount;
        int m_LeftSuppressedColliderHoldCount;
        int m_RightSuppressedColliderHoldCount;
        static readonly List<XRInputDevice> s_HandDeviceBuffer = new List<XRInputDevice>(4);
        static readonly List<XRInputDevice> s_ControllerDeviceBuffer = new List<XRInputDevice>(4);
        static readonly RaycastHit[] s_ScenePickupSupportHits = new RaycastHit[16];
        static readonly RaycastHit[] s_PlayerGroundHitBuffer = new RaycastHit[16];
        static readonly RaycastHit[] s_ArenaSurfaceHitBuffer = new RaycastHit[96];
        static readonly Collider[] s_SpawnClearanceBuffer = new Collider[96];
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
        int m_DeferredNextWave = -1;
        bool m_IsGameOver;
        bool m_IsRestarting;
        bool m_IsWaitingForEncounterResume;
        bool m_IsPauseMenuOpen;
        bool m_IsVictoryMenuOpen;
        bool m_HasLoggedPauseStartupDiagnostics;
        bool m_HasLoggedFirstPauseAttempt;
        bool m_HasLoggedCameraRecoveryAttempt;
        bool m_RunStarted;
        bool m_HasStartedWaveLoop;
        bool m_EndlessModeActive;
        bool m_HasShownVictoryChoice;
        bool m_ShouldContinueAfterVictory;
        bool m_HasLoggedWavePlatformSpawnFailure;
        bool m_UsesAuthoredEncounterProgression;
        bool m_LastUpgradeSelectionActive;
        bool m_HasGrantedIntroChainWeapon;
        float m_SpawnProtectionUntilTime;
        int m_DebugMenuComboIndex;
        float m_LastDebugMenuComboInputTime = -100f;
        bool m_WasXrDebugComboAxisPressed;
        DebugComboDirection m_LastXrDebugComboAxisDirection;
        bool m_WasRawMenuButtonPressed;
        bool m_PendingMetaMenuGesture;
        float m_BootstrapStartedRealtime;
        float m_MovementVignetteStrength;
        TunnelingVignetteController m_TunnelingVignetteController;
        Vector3 m_CustomRunStartPosition;
        Quaternion m_CustomRunStartRotation = Quaternion.identity;
        bool m_HasCustomRunStartPose;
        ArenaOpeningEncounter m_PendingWaveResumeEncounter;
        CapsuleEnemy m_MilestoneKeyCarrier;

        static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorShaderId = Shader.PropertyToID("_Color");
        static readonly string[] s_PlatformSpawnRejectedNameTokens =
        {
            "corridor",
            "hall",
            "gate",
            "wall",
            "door",
            "bridge",
            "stair",
            "ramp",
            "pillar",
            "column",
            "ceiling",
            "trigger",
            "helper",
            "lock",
            "padlock"
        };
        static readonly string[] s_LegacyArenaPickupNames =
        {
            "sword",
            "Flintlock",
            "Sword.001",
            "MahmutCanKovan_01",
            "Goblin"
        };

        static readonly string[] s_DefaultCardTableReferenceNames =
        {
            "sword",
            "Sword.001",
            "MahmutCanKovan_01"
        };

        static readonly Vector3[] s_FallbackCardTablePositions =
        {
            new Vector3(-3.8736424f, 0f, 3.0679731f),
            new Vector3(-1.298f, 0f, 2.702f),
            new Vector3(-0.8007756f, 0f, 1.7452291f)
        };

        const string MovementVignetteStrengthPrefKey = "vrcombat.movement_vignette_strength";
        const string MovementVignetteInitializedPrefKey = "vrcombat.movement_vignette_initialized_v2";
        const string PauseMapperActionTitle = "VR Combat Pause Menu";
        const string DaggerModelResourcePath = "CombatModels/Dagger_06";
        const string ChainModelResourcePath = "CombatModels/Chain_03";
        const string FlintlockModelResourcePath = "CombatModels/Flintlock";
        const string GoblinModelResourcePath = "CombatModels/Goblin";
        const string BossGoblinModelResourcePath = "CombatModels/HobGoblin";
        const string MaceModelResourcePath = "CombatModels/gurz1";
        const string ShieldModelResourcePath = "CombatModels/shield";
        const string SpearModelResourcePath = "CombatModels/spear";
        const string SwordModelResourcePath = "CombatModels/sword";
        const string ArenaRootName = "Arena";
        const string AttachPointObjectName = "Attach Point";
        const string SwordNameToken = "sword";
        const float ChainJointRootSpring = 240f;
        const float ChainJointSegmentSpring = 160f;
        const float ChainJointDamper = 4f;
        const int EnemyXpReward = 25;
        static readonly Vector3 RunStartHeadPosition = new Vector3(-28.07f, 1.35f, -52.44f);
        static readonly Quaternion RunStartHeadRotation = Quaternion.identity;
        const float PlayerEyeHeightOffsetMeters = 0.30f;
        const float PauseToggleDebounceSeconds = 0.12f;
        const float SpawnProtectionSeconds = 2f;
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
        const float ArenaWalkableSampleInset = 0.35f;
        const int ArenaWalkableSampleGridResolution = 11;
        const int EnemySpawnResolutionAttempts = 14;
        const float EnemySpawnRetryRadius = 1.9f;
        const float EnemySpawnClearanceSkin = 0.035f;
        const float EnemySpawnKillZoneGraceSeconds = 0.45f;
        const float PlayerKillZoneGroundProbeHeight = 1.1f;
        const float PlayerKillZoneGroundProbeDistance = 4.5f;
        const float PlayerKillZoneExpansionHorizontalPadding = 4f;
        const float PlayerKillZoneExpansionBelowPadding = 2f;
        const float PlayerKillZoneExpansionAbovePadding = 3f;
        const float PlayerKillZoneMaxGroundDrop = 2.5f;
        const float PlayerKillZoneMaxGroundSlopeAngle = 75f;
        const string RuntimeOutdoorSunName = "Runtime Outdoor Sun";
        const string ArenaPrimaryPlatformObjectName = "Platform";
        const int BossWaveNumber = 15;
        const float BossGoblinTargetHeight = 3.2f;
        const float BossGoblinCapsuleRadius = 0.72f;
        const float BossGoblinCapsuleHeight = 3.2f;
        const float RuntimeOutdoorShadowDistance = 90f;
        const float DebugMenuComboTimeoutSeconds = 2f;
        static readonly int[] s_WaveEncounterMilestones = { 5, 10 };
        static readonly DebugComboDirection[] s_DebugMenuComboSequence =
        {
            DebugComboDirection.Up,
            DebugComboDirection.Up,
            DebugComboDirection.Down,
            DebugComboDirection.Down,
            DebugComboDirection.Left,
            DebugComboDirection.Right,
            DebugComboDirection.Left,
            DebugComboDirection.Right
        };
        static readonly string[] s_ArenaPlatformObjectNames =
        {
            ArenaPrimaryPlatformObjectName
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureBootstrapperExists()
        {
            ConfigureSceneLighting();

            if (FindAnyObjectByType<VRCombatBootstrapper>() != null)
                return;

            var bootstrapperObject = new GameObject("VR Combat Bootstrapper");
            bootstrapperObject.AddComponent<VRCombatBootstrapper>();
        }

        void Awake()
        {
            ConfigureSceneLighting();
        }

        static void ConfigureSceneLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.38f, 0.44f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.27f, 0.25f, 0.22f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.17f, 0.15f, 1f);
            RenderSettings.ambientIntensity = 0.88f;
            RenderSettings.reflectionIntensity = 0.42f;
            RenderSettings.fog = false;

            var keyLight = ResolvePrimaryDirectionalLight();
            var createdRuntimeLight = false;
            if (keyLight == null)
            {
                keyLight = CreateRuntimeDirectionalLight();
                createdRuntimeLight = true;
            }

            ConfigurePrimaryDirectionalLight(keyLight, createdRuntimeLight);
            RenderSettings.sun = keyLight;
            ConfigureRuntimeShadowDistance();
        }

        static void ConfigureRuntimeShadowDistance()
        {
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, RuntimeOutdoorShadowDistance);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset universalPipelineAsset)
                universalPipelineAsset.shadowDistance = Mathf.Max(universalPipelineAsset.shadowDistance, RuntimeOutdoorShadowDistance);
        }

        static Light ResolvePrimaryDirectionalLight()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
                return RenderSettings.sun;

            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Light fallbackDirectional = null;
            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null || light.type != LightType.Directional)
                    continue;

                if (fallbackDirectional == null)
                    fallbackDirectional = light;

                if (light.enabled && light.gameObject.activeInHierarchy)
                    return light;
            }

            return fallbackDirectional;
        }

        static Light CreateRuntimeDirectionalLight()
        {
            var lightObject = new GameObject(RuntimeOutdoorSunName);
            return lightObject.AddComponent<Light>();
        }

        static void ConfigurePrimaryDirectionalLight(Light keyLight, bool createdRuntimeLight)
        {
            if (keyLight == null)
                return;

            keyLight.enabled = true;
            keyLight.type = LightType.Directional;
            if (createdRuntimeLight)
            {
                keyLight.color = new Color(1f, 0.95f, 0.84f, 1f);
                keyLight.intensity = 1.45f;
                keyLight.transform.rotation = Quaternion.Euler(55f, -36f, 6f);
            }
            else
            {
                keyLight.intensity = Mathf.Clamp(keyLight.intensity <= 0f ? 1.45f : keyLight.intensity, 1.15f, 1.6f);
                if (keyLight.color.maxColorComponent <= 0.01f)
                    keyLight.color = new Color(1f, 0.95f, 0.84f, 1f);
            }

            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = Mathf.Clamp(keyLight.shadowStrength <= 0f ? 0.48f : keyLight.shadowStrength, 0.32f, 0.58f);
            keyLight.bounceIntensity = Mathf.Clamp(keyLight.bounceIntensity <= 0f ? 1.05f : keyLight.bounceIntensity, 0.95f, 1.25f);
            keyLight.renderMode = LightRenderMode.ForcePixel;
        }

        IEnumerator Start()
        {
            m_BootstrapStartedRealtime = Time.realtimeSinceStartup;
            while (!TryResolvePlayerRig())
                yield return null;

            EnablePlayerControls();
            ConfigureControllerLocomotionActionManagers();
            SetupPlayerDamageDetection();
            SetupMovementVignetteControl();
            ConfigureHandFirstInteraction();
            SetupCombatHud();
            EnsureRunSystems();
            SetupPauseMenuInputActions();
            LogPauseInputDiagnosticsAtStartup();
            ValidateRigCoherency("startup");
            StartCoroutine(RestartRunRoutine(initialStartup: true));
        }

        void Update()
        {
            UpdateControllerFallbackHandMapping();
            MonitorUpgradePauseState();

            if (!m_IsGameOver && !m_IsRestarting && !m_IsVictoryMenuOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || ConsumeMenuButtonPress())
                    RequestPauseMenuToggle();

                if (m_IsPauseMenuOpen)
                    UpdatePauseMenuShortcuts();
            }

            UpdateKillZoneState();

            if (!m_IsGameOver || m_IsRestarting)
                return;

            if (Input.GetKeyDown(KeyCode.R) || ReadAnyRestartButton())
                RestartCurrentScene();
        }

        void OnDestroy()
        {
            if (m_RunProgressionController != null)
                m_RunProgressionController.ProgressionChanged -= HandleRunProgressionChanged;

            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter != null)
                    encounter.EncounterCompleted -= HandleArenaOpeningEncounterCompleted;
            }

            if (m_MilestoneKeyCarrier != null)
                m_MilestoneKeyCarrier.Died -= HandleMilestoneKeyCarrierDied;

            RestoreSuppressedHandRenderers();
            UnbindMetaMenuGestureDetector();
            if (m_RuntimePauseMenuAction != null)
            {
                m_RuntimePauseMenuAction.performed -= OnRuntimePauseMenuActionPerformed;
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

        void ConfigureControllerLocomotionActionManagers()
        {
            if (m_PlayerRoot == null)
                return;

            var actionManagers = m_PlayerRoot.GetComponentsInChildren<ControllerInputActionManager>(true);
            for (var i = 0; i < actionManagers.Length; i++)
            {
                if (actionManagers[i] != null)
                    actionManagers[i].allowTeleportWithSmoothMotion = true;
            }
        }

        bool TryResolvePlayerRig(bool forceRefresh = false)
        {
            if (!forceRefresh && m_PlayerCamera != null && m_PlayerRoot != null)
            {
                m_InputModalityManager ??= FindPreferredInputModalityManager(m_PlayerRoot);
                m_PlayerTrackingSpace ??= ResolveTrackingSpaceForResolvedRig(m_PlayerCamera, m_PlayerRoot);
                return true;
            }


            Camera resolvedCamera = null;
            Transform resolvedRoot = null;
            Transform resolvedTrackingSpace = null;
            XRInputModalityManager resolvedInputModalityManager = null;
            var resolvedRigType = "Unknown";

            if (TryFindActiveXrOriginRig(out var xrOrigin, out resolvedCamera, out resolvedRoot, out resolvedInputModalityManager))
            {
                resolvedRigType = $"XROrigin ({xrOrigin.name})";
                resolvedTrackingSpace = ResolveTrackingSpace(xrOrigin, resolvedCamera, resolvedRoot);
            }
            else if (TryFindActiveOvrCameraRig(out var ovrRig, out resolvedCamera, out resolvedRoot))
            {
                resolvedRigType = $"OVRCameraRig ({ovrRig.name})";
                resolvedTrackingSpace = ResolveTrackingSpace(ovrRig, resolvedCamera, resolvedRoot);
            }
            else
            {
                if (TryFindEnabledSceneCamera(out resolvedCamera))
                {
                    resolvedRoot = resolvedCamera.transform.root;
                    resolvedRigType = $"Fallback Camera ({resolvedCamera.name})";
                    resolvedTrackingSpace = ResolveFallbackTrackingSpace(resolvedCamera, resolvedRoot);
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
            m_PlayerTrackingSpace = resolvedTrackingSpace != null
                ? resolvedTrackingSpace
                : ResolveTrackingSpaceForResolvedRig(m_PlayerCamera, m_PlayerRoot);
            m_InputModalityManager = resolvedInputModalityManager ?? FindPreferredInputModalityManager(m_PlayerRoot);
            SetupPauseMenuInputActions();

            var disabledRigPaths = DisableConflictingRigStacks(m_PlayerRoot);
            LogRigSelection(resolvedRigType, disabledRigPaths);
            return true;
        }

        static Transform ResolveTrackingSpace(XROrigin xrOrigin, Camera camera, Transform playerRoot)
        {
            if (xrOrigin == null)
                return ResolveFallbackTrackingSpace(camera, playerRoot);

            if (xrOrigin.CameraFloorOffsetObject != null)
                return xrOrigin.CameraFloorOffsetObject.transform;

            if (xrOrigin.Origin != null)
                return xrOrigin.Origin.transform;

            return ResolveFallbackTrackingSpace(camera, playerRoot ?? xrOrigin.transform);
        }

        static Transform ResolveTrackingSpace(OVRCameraRig ovrRig, Camera camera, Transform playerRoot)
        {
            if (ovrRig == null)
                return ResolveFallbackTrackingSpace(camera, playerRoot);

            if (ovrRig.trackingSpace != null)
                return ovrRig.trackingSpace;

            return ResolveFallbackTrackingSpace(camera, playerRoot ?? ovrRig.transform);
        }

        static Transform ResolveFallbackTrackingSpace(Camera camera, Transform playerRoot)
        {
            if (camera != null && camera.transform.parent != null)
                return camera.transform.parent;

            return playerRoot;
        }

        static Transform ResolveTrackingSpaceForResolvedRig(Camera camera, Transform playerRoot)
        {
            if (camera != null)
            {
                var xrOrigin = camera.GetComponentInParent<XROrigin>();
                if (xrOrigin != null)
                    return ResolveTrackingSpace(xrOrigin, camera, playerRoot);

                var ovrRig = camera.GetComponentInParent<OVRCameraRig>();
                if (ovrRig != null)
                    return ResolveTrackingSpace(ovrRig, camera, playerRoot);
            }

            if (playerRoot != null)
            {
                var xrOrigin = playerRoot.GetComponentInParent<XROrigin>();
                if (xrOrigin != null)
                    return ResolveTrackingSpace(xrOrigin, camera, playerRoot);

                var ovrRig = playerRoot.GetComponentInParent<OVRCameraRig>();
                if (ovrRig != null)
                    return ResolveTrackingSpace(ovrRig, camera, playerRoot);
            }

            return ResolveFallbackTrackingSpace(camera, playerRoot);
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
            return TryGetArenaGroundHit(origin, distance, out bestHit);
        }

        bool TryGetArenaGroundHit(Vector3 origin, float distance, out RaycastHit bestHit)
        {
            return TryGetArenaGroundHit(origin, distance, m_ArenaSurfaceColliders, float.PositiveInfinity, out bestHit);
        }

        bool TryGetArenaGroundHit(
            Vector3 origin,
            float distance,
            IReadOnlyList<Collider> allowedColliders,
            float maxSurfaceY,
            out RaycastHit bestHit)
        {
            bestHit = default;
            if (allowedColliders == null || allowedColliders.Count == 0)
                return false;

            if (TryRaycastAllowedArenaColliders(origin, distance, allowedColliders, maxSurfaceY, out bestHit))
                return true;

            var hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                s_ArenaSurfaceHitBuffer,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            var closestDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = s_ArenaSurfaceHitBuffer[i];
                var collider = hit.collider;
                if (collider == null || !ContainsCollider(allowedColliders, collider))
                    continue;

                if (m_PlayerRoot != null && collider.transform.IsChildOf(m_PlayerRoot))
                    continue;

                if (collider.GetComponentInParent<XRBaseInteractable>() != null || collider.GetComponentInParent<XRGrabInteractable>() != null)
                    continue;

                if (hit.normal.y < ArenaGroundSnapNormalThreshold)
                    continue;

                if (hit.point.y > maxSurfaceY)
                    continue;

                if (hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                bestHit = hit;
            }

            return closestDistance < float.PositiveInfinity;
        }

        bool TryRaycastAllowedArenaColliders(
            Vector3 origin,
            float distance,
            IReadOnlyList<Collider> allowedColliders,
            float maxSurfaceY,
            out RaycastHit bestHit)
        {
            bestHit = default;
            if (allowedColliders == null || allowedColliders.Count == 0)
                return false;

            var ray = new Ray(origin, Vector3.down);
            var closestDistance = float.PositiveInfinity;
            for (var i = 0; i < allowedColliders.Count; i++)
            {
                var collider = allowedColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (m_PlayerRoot != null && collider.transform.IsChildOf(m_PlayerRoot))
                    continue;

                if (collider.GetComponentInParent<XRBaseInteractable>() != null || collider.GetComponentInParent<XRGrabInteractable>() != null)
                    continue;

                if (!collider.Raycast(ray, out var hit, distance))
                    continue;

                if (hit.normal.y < ArenaGroundSnapNormalThreshold)
                    continue;

                if (hit.point.y > maxSurfaceY)
                    continue;

                if (hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                bestHit = hit;
            }

            return closestDistance < float.PositiveInfinity;
        }

        static bool ContainsCollider(IReadOnlyList<Collider> colliders, Collider candidate)
        {
            if (colliders == null || candidate == null)
                return false;

            for (var i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] == candidate)
                    return true;
            }

            return false;
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
                || ReadOvrPauseButtonDown()
                || ReadGenericInputSystemPauseButtonDown()
                || ConsumeMetaMenuGesturePress();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            LogPauseAttemptDiagnostics(runtimeActionPressed, rawMenuPressed);
#endif
            return runtimeActionPressed || rawMenuPressed;
        }

        void RequestPauseMenuToggle()
        {
            if (m_IsGameOver || m_IsRestarting || m_IsVictoryMenuOpen)
                return;

            if (Time.unscaledTime - m_LastPauseToggleTime < PauseToggleDebounceSeconds)
                return;

            m_LastPauseToggleTime = Time.unscaledTime;
            SetPauseMenuOpen(!m_IsPauseMenuOpen);
        }

        void UpdatePauseMenuShortcuts()
        {
            if (WasPauseRestartShortcutPressedThisFrame())
            {
                RestartCurrentScene();
                return;
            }

            if (UpdateDebugMenuComboShortcut())
                return;

            var vignetteStep = ReadPauseVignetteStepThisFrame();
            if (!Mathf.Approximately(vignetteStep, 0f))
                m_CombatHud?.AdjustPauseMenuVignette(vignetteStep);
        }

        bool UpdateDebugMenuComboShortcut()
        {
            if (!m_IsPauseMenuOpen || m_CombatHud == null)
            {
                m_DebugMenuComboIndex = 0;
                return false;
            }

            if (!TryReadDebugComboDirectionThisFrame(out var direction))
                return false;

            if (Time.unscaledTime - m_LastDebugMenuComboInputTime > DebugMenuComboTimeoutSeconds)
                m_DebugMenuComboIndex = 0;

            m_LastDebugMenuComboInputTime = Time.unscaledTime;
            var expectedDirection = s_DebugMenuComboSequence[m_DebugMenuComboIndex];
            if (direction == expectedDirection)
            {
                m_DebugMenuComboIndex++;
                if (m_DebugMenuComboIndex < s_DebugMenuComboSequence.Length)
                    return false;

                m_DebugMenuComboIndex = 0;
                m_CombatHud.ToggleDebugPanel();
                m_CombatHud.ShowBanner("Debug menu", 0.8f);
                return true;
            }

            m_DebugMenuComboIndex = direction == s_DebugMenuComboSequence[0] ? 1 : 0;
            return false;
        }

        bool TryReadDebugComboDirectionThisFrame(out DebugComboDirection direction)
        {
            direction = DebugComboDirection.Up;
            var keyboard = Keyboard.current;
            if (Input.GetKeyDown(KeyCode.UpArrow) ||
                Input.GetKeyDown(KeyCode.W) ||
                (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)))
            {
                direction = DebugComboDirection.Up;
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow) ||
                Input.GetKeyDown(KeyCode.S) ||
                (keyboard != null && (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)))
            {
                direction = DebugComboDirection.Down;
                return true;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow) ||
                Input.GetKeyDown(KeyCode.A) ||
                (keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)))
            {
                direction = DebugComboDirection.Left;
                return true;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow) ||
                Input.GetKeyDown(KeyCode.D) ||
                (keyboard != null && (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)))
            {
                direction = DebugComboDirection.Right;
                return true;
            }

            var gamepad = Gamepad.current;
            if (gamepad == null)
                return TryReadXrDebugComboDirection(out direction);

            if (gamepad.dpad.up.wasPressedThisFrame || gamepad.leftStick.up.wasPressedThisFrame)
            {
                direction = DebugComboDirection.Up;
                return true;
            }

            if (gamepad.dpad.down.wasPressedThisFrame || gamepad.leftStick.down.wasPressedThisFrame)
            {
                direction = DebugComboDirection.Down;
                return true;
            }

            if (gamepad.dpad.left.wasPressedThisFrame || gamepad.leftStick.left.wasPressedThisFrame)
            {
                direction = DebugComboDirection.Left;
                return true;
            }

            if (gamepad.dpad.right.wasPressedThisFrame || gamepad.leftStick.right.wasPressedThisFrame)
            {
                direction = DebugComboDirection.Right;
                return true;
            }

            return TryReadXrDebugComboDirection(out direction);
        }

        bool TryReadXrDebugComboDirection(out DebugComboDirection direction)
        {
            if (!TryReadDominantXrThumbstickDirection(out direction))
            {
                m_WasXrDebugComboAxisPressed = false;
                return false;
            }

            if (m_WasXrDebugComboAxisPressed && direction == m_LastXrDebugComboAxisDirection)
                return false;

            m_WasXrDebugComboAxisPressed = true;
            m_LastXrDebugComboAxisDirection = direction;
            return true;
        }

        static bool TryReadDominantXrThumbstickDirection(out DebugComboDirection direction)
        {
            direction = DebugComboDirection.Up;
            var bestMagnitude = 0.68f;
            var foundDirection = false;

            TryReadDominantXrThumbstickDirectionForNode(XRNode.LeftHand, ref direction, ref bestMagnitude, ref foundDirection);
            TryReadDominantXrThumbstickDirectionForNode(XRNode.RightHand, ref direction, ref bestMagnitude, ref foundDirection);
            return foundDirection;
        }

        static void TryReadDominantXrThumbstickDirectionForNode(
            XRNode node,
            ref DebugComboDirection direction,
            ref float bestMagnitude,
            ref bool foundDirection)
        {
            s_ControllerDeviceBuffer.Clear();
            InputDevices.GetDevicesAtXRNode(node, s_ControllerDeviceBuffer);
            for (var i = 0; i < s_ControllerDeviceBuffer.Count; i++)
            {
                var device = s_ControllerDeviceBuffer[i];
                if (!device.isValid || !device.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out var axis))
                    continue;

                var horizontal = Mathf.Abs(axis.x);
                var vertical = Mathf.Abs(axis.y);
                var magnitude = Mathf.Max(horizontal, vertical);
                if (magnitude <= bestMagnitude)
                    continue;

                bestMagnitude = magnitude;
                foundDirection = true;
                if (vertical >= horizontal)
                    direction = axis.y >= 0f ? DebugComboDirection.Up : DebugComboDirection.Down;
                else
                    direction = axis.x >= 0f ? DebugComboDirection.Right : DebugComboDirection.Left;
            }
        }

        static bool WasPauseRestartShortcutPressedThisFrame()
        {
            return Input.GetKeyDown(KeyCode.R) ||
                   (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ||
                   (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);
        }

        static float ReadPauseVignetteStepThisFrame()
        {
            const float step = 0.1f;
            var keyboard = Keyboard.current;
            if (Input.GetKeyDown(KeyCode.LeftArrow) ||
                Input.GetKeyDown(KeyCode.A) ||
                (keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)))
            {
                return -step;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow) ||
                Input.GetKeyDown(KeyCode.D) ||
                (keyboard != null && (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)))
            {
                return step;
            }

            var gamepad = Gamepad.current;
            if (gamepad == null)
                return 0f;

            if (gamepad.dpad.left.wasPressedThisFrame || gamepad.leftStick.left.wasPressedThisFrame)
                return -step;
            if (gamepad.dpad.right.wasPressedThisFrame || gamepad.leftStick.right.wasPressedThisFrame)
                return step;

            return 0f;
        }

        void SetupPauseMenuInputActions()
        {
            if (m_RuntimePauseMenuAction == null)
            {
                m_RuntimePauseMenuAction = new UnityEngine.InputSystem.InputAction(
                    "Runtime Pause Menu",
                    UnityEngine.InputSystem.InputActionType.Button);

                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(
                    m_RuntimePauseMenuAction,
                    "<Keyboard>/escape");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(
                    m_RuntimePauseMenuAction,
                    "<Gamepad>/startButton");

                var controllerLayouts = new[]
                {
                    "XRController",
                    "OculusTouchController",
                    "QuestTouchPlusController",
                    "QuestProTouchController",
                    "MetaQuestTouchPlusController"
                };
                var handUsages = new[] { "LeftHand", "RightHand" };
                var menuControls = new[] { "menu", "menuButton", "systemButton", "start", "startButton", "secondaryButton" };
                var thumbstickControls = new[] { "primary2DAxisClick", "thumbstickClicked", "joystickClicked" };

                for (var layoutIndex = 0; layoutIndex < controllerLayouts.Length; layoutIndex++)
                {
                    var layout = controllerLayouts[layoutIndex];
                    for (var controlIndex = 0; controlIndex < menuControls.Length; controlIndex++)
                    {
                        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(
                            m_RuntimePauseMenuAction,
                            $"<{layout}>/{menuControls[controlIndex]}");
                    }

                    for (var controlIndex = 0; controlIndex < thumbstickControls.Length; controlIndex++)
                    {
                        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(
                            m_RuntimePauseMenuAction,
                            $"<{layout}>/{thumbstickControls[controlIndex]}");
                    }

                    for (var handIndex = 0; handIndex < handUsages.Length; handIndex++)
                    {
                        var handUsage = handUsages[handIndex];
                        for (var controlIndex = 0; controlIndex < menuControls.Length; controlIndex++)
                        {
                            UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(
                                m_RuntimePauseMenuAction,
                                $"<{layout}>{{{handUsage}}}/{menuControls[controlIndex]}");
                        }

                        for (var controlIndex = 0; controlIndex < thumbstickControls.Length; controlIndex++)
                        {
                            UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(
                                m_RuntimePauseMenuAction,
                                $"<{layout}>{{{handUsage}}}/{thumbstickControls[controlIndex]}");
                        }
                    }
                }

                // Quest Touch Plus reports the hamburger/menu button as secondaryButton on the left controller.
                for (var layoutIndex = 0; layoutIndex < controllerLayouts.Length; layoutIndex++)
                {
                    UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(
                        m_RuntimePauseMenuAction,
                        $"<{controllerLayouts[layoutIndex]}>{{LeftHand}}/secondaryButton");
                }
            }

            m_RuntimePauseMenuAction.performed -= OnRuntimePauseMenuActionPerformed;
            m_RuntimePauseMenuAction.performed += OnRuntimePauseMenuActionPerformed;
            if (!m_RuntimePauseMenuAction.enabled)
                m_RuntimePauseMenuAction.Enable();

            ConfigureMetaControllerButtonsMapperPauseBinding();
            RebindMetaMenuGestureDetector();
            m_PendingMetaMenuGesture = false;
        }

        void OnRuntimePauseMenuActionPerformed(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            RequestPauseMenuToggle();
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

                if (IsLikelyHandInputSystemDevice(device) && WasAnyThumbstickPauseControlPressedThisFrame(device))
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

        static bool IsLikelyHandInputSystemDevice(UnityEngine.InputSystem.InputDevice device)
        {
            if (device == null)
                return false;

            if (HasInputSystemUsage(device, "LeftHand") || HasInputSystemUsage(device, "RightHand"))
                return true;

            var descriptor = $"{device.displayName} {device.name} {device.layout}";
            return descriptor.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   descriptor.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0;
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

        static bool IsInputSystemPauseButtonPressed(UnityEngine.InputSystem.InputDevice device)
        {
            return IsInputSystemButtonPressed(device, "menu")
                || IsInputSystemButtonPressed(device, "menuButton")
                || IsInputSystemButtonPressed(device, "systemButton")
                || IsInputSystemButtonPressed(device, "start")
                || IsInputSystemPauseFallbackPressed(device);
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

        static bool ReadOvrPauseButtonDown()
        {
            try
            {
                return OVRInput.GetDown(OVRInput.Button.Start) ||
                       OVRInput.GetDown(OVRInput.RawButton.Start) ||
                       OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch) ||
                       OVRInput.GetDown(OVRInput.RawButton.Y) ||
                       OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch) ||
                       OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick) ||
                       OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick) ||
                       OVRInput.GetDown(OVRInput.RawButton.LThumbstick) ||
                       OVRInput.GetDown(OVRInput.RawButton.RThumbstick);
            }
            catch
            {
                return false;
            }
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

            if (TryGetResolvedControllerDevice(
                    InputDeviceCharacteristics.Right,
                    XRNode.RightHand,
                    m_RightControllerTransform,
                    out var resolvedRightDevice))
            {
                if (IsMenuSpecificButtonPressed(resolvedRightDevice) || IsPauseFallbackThumbstickPressed(resolvedRightDevice))
                    return true;
            }

            if (IsAnyXrPauseButtonPressedForHand(InputDeviceCharacteristics.Left) ||
                IsAnyXrPauseButtonPressedForHand(InputDeviceCharacteristics.Right))
                return true;

            if (IsInputSystemPauseButtonPressed(UnityEngine.InputSystem.XR.XRController.leftHand) ||
                IsInputSystemPauseButtonPressed(UnityEngine.InputSystem.XR.XRController.rightHand))
                return true;

            var inputSystemDevices = UnityEngine.InputSystem.InputSystem.devices;
            for (var i = 0; i < inputSystemDevices.Count; i++)
            {
                var device = inputSystemDevices[i];
                if (device == null)
                    continue;

                if (!IsLikelyHandInputSystemDevice(device) && !HasInputSystemPauseMenuControl(device))
                    continue;

                if (IsInputSystemPauseButtonPressed(device))
                    return true;
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
                    || OVRInput.Get(OVRInput.Button.SecondaryThumbstick)
                    || OVRInput.Get(OVRInput.RawButton.LThumbstick)
                    || OVRInput.Get(OVRInput.RawButton.RThumbstick))
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

        static bool IsAnyXrPauseButtonPressedForHand(InputDeviceCharacteristics handednessFlag)
        {
            var controllerCharacteristics =
                handednessFlag |
                InputDeviceCharacteristics.Controller |
                InputDeviceCharacteristics.TrackedDevice;
            if (IsAnyMenuSpecificButtonPressed(controllerCharacteristics) ||
                IsAnyPauseFallbackThumbstickPressed(controllerCharacteristics))
            {
                return true;
            }

            var controllerOnlyCharacteristics = handednessFlag | InputDeviceCharacteristics.Controller;
            if (IsAnyMenuSpecificButtonPressed(controllerOnlyCharacteristics) ||
                IsAnyPauseFallbackThumbstickPressed(controllerOnlyCharacteristics))
            {
                return true;
            }

            var trackedOnlyCharacteristics = handednessFlag | InputDeviceCharacteristics.TrackedDevice;
            return IsAnyMenuSpecificButtonPressed(trackedOnlyCharacteristics) ||
                   IsAnyPauseFallbackThumbstickPressed(trackedOnlyCharacteristics);
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
                ContinueEndlessMode,
                SetMovementVignetteStrength,
                m_MovementVignetteStrength,
                m_PlayerCamera,
                m_PlayerCamera.transform,
                DebugGrantUpgrade,
                DebugGrantWeapon,
                DebugUnlockSpell,
                DebugGrantAllWeapons,
                DebugKillAllEnemies,
                DebugSetWaveNumber);

            if (m_PlayerDamageReceiver == null)
                return;

            m_PlayerDamageReceiver.HealthChanged += OnPlayerHealthChanged;
            m_PlayerDamageReceiver.DamageTaken += OnPlayerDamageTaken;
            m_PlayerDamageReceiver.Died += OnPlayerDied;

            OnPlayerHealthChanged(m_PlayerDamageReceiver.CurrentHealth, m_PlayerDamageReceiver.MaxHealth);
        }

        void EnsureRunSystems()
        {
            if (m_RunProgressionController == null)
                m_RunProgressionController = GetComponent<RunProgressionController>() ?? gameObject.AddComponent<RunProgressionController>();

            m_RunProgressionController.ProgressionChanged -= HandleRunProgressionChanged;
            m_RunProgressionController.ProgressionChanged += HandleRunProgressionChanged;
            m_RunProgressionController.Initialize(m_CombatHud);

            if (m_PlayerSpellLoadout == null)
                m_PlayerSpellLoadout = GetComponent<PlayerSpellLoadout>() ?? gameObject.AddComponent<PlayerSpellLoadout>();

            m_PlayerSpellLoadout.Configure(
                m_RunProgressionController,
                m_PlayerRoot,
                m_PlayerTrackingSpace != null ? m_PlayerTrackingSpace : m_PlayerRoot,
                m_PlayerCamera != null ? m_PlayerCamera.transform : m_PlayerRoot,
                m_LeftHandTransform,
                m_RightHandTransform,
                m_LeftControllerTransform,
                m_RightControllerTransform,
                IsSpellHandOccupied);

            if (m_WristChainIntroController == null)
                m_WristChainIntroController = GetComponent<WristChainIntroController>() ?? gameObject.AddComponent<WristChainIntroController>();

            RefreshManagedLocomotionBehaviours();
            ApplyPlayerSpeedMultiplier();
            m_LastUpgradeSelectionActive = m_RunProgressionController.IsUpgradeSelectionActive;
            RefreshRunInteractionState();
            ResolveArenaEncounters();
        }

        void ResolveArenaEncounters()
        {
            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter != null)
                    encounter.EncounterCompleted -= HandleArenaOpeningEncounterCompleted;
            }

            m_ArenaEncounters.Clear();
            m_WaveUnlockEncounters.Clear();

            var seenIds = new HashSet<int>();
            RegisterArenaEncounter(m_IntroOpeningEncounter, seenIds);

            var sceneEncounters = FindObjectsByType<ArenaOpeningEncounter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < sceneEncounters.Length; i++)
                RegisterArenaEncounter(sceneEncounters[i], seenIds);

            SortArenaEncountersForProgression();
            m_UsesAuthoredEncounterProgression = CountActiveArenaEncounters() > 1;

            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter == null)
                    continue;

                encounter.EncounterCompleted += HandleArenaOpeningEncounterCompleted;

                var unlockWave = encounter.UnlockAfterWave;
                if (unlockWave <= 0)
                    continue;

                if (encounter.GateToUnlock == null)
                {
                    Debug.LogWarning(
                        $"[VRCombat] Encounter '{encounter.name}' is linked to milestone wave {unlockWave}, but it has no gate assigned to unlock.",
                        encounter);
                }

                if (m_WaveUnlockEncounters.ContainsKey(unlockWave))
                {
                    Debug.LogWarning(
                        $"[VRCombat] Multiple encounters are configured for unlock wave {unlockWave}. Using the first encountered instance.",
                        encounter);
                    continue;
                }

                m_WaveUnlockEncounters.Add(unlockWave, encounter);
            }
        }

        void RegisterArenaEncounter(ArenaOpeningEncounter encounter, HashSet<int> seenIds)
        {
            if (encounter == null)
                return;

            if (!seenIds.Add(encounter.GetInstanceID()))
                return;

            m_ArenaEncounters.Add(encounter);
        }

        void SortArenaEncountersForProgression()
        {
            var startPosition = GetResolvedRunStartHeadPosition();
            m_ArenaEncounters.Sort((left, right) => CompareArenaEncounterProgressionOrder(left, right, startPosition));
        }

        int CountActiveArenaEncounters()
        {
            var activeCount = 0;
            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter != null && encounter.isActiveAndEnabled)
                    activeCount++;
            }

            return activeCount;
        }

        static int CompareArenaEncounterProgressionOrder(
            ArenaOpeningEncounter left,
            ArenaOpeningEncounter right,
            Vector3 startPosition)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            var waveCompare = left.UnlockAfterWave.CompareTo(right.UnlockAfterWave);
            if (waveCompare != 0)
                return waveCompare;

            var leftOffset = Vector3.ProjectOnPlane(left.transform.position - startPosition, Vector3.up);
            var rightOffset = Vector3.ProjectOnPlane(right.transform.position - startPosition, Vector3.up);
            var distanceCompare = leftOffset.sqrMagnitude.CompareTo(rightOffset.sqrMagnitude);
            if (distanceCompare != 0)
                return distanceCompare;

            return string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        }

        static bool ShouldAutoStartEncounter(ArenaOpeningEncounter encounter)
        {
            return ShouldAutoStartEncounter(encounter, maxUnlockAfterWave: 0);
        }

        static bool ShouldAutoStartEncounter(ArenaOpeningEncounter encounter, int maxUnlockAfterWave)
        {
            return encounter != null &&
                encounter.StartMode == EncounterStartMode.AutoOnRunStart &&
                encounter.UnlockAfterWave <= Mathf.Max(0, maxUnlockAfterWave);
        }

        bool HasEncounterThatStartsOrResumesWaves()
        {
            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter != null &&
                    encounter.isActiveAndEnabled &&
                    encounter.StartsOrResumesWavesOnCompletion)
                {
                    return true;
                }
            }

            return false;
        }

        bool BeginAutoStartEncounters()
        {
            if (m_UsesAuthoredEncounterProgression)
                return BeginNextAuthoredEncounterAfter(null);

            var beganAny = false;
            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter == null ||
                    !encounter.isActiveAndEnabled ||
                    !ShouldAutoStartEncounter(encounter))
                {
                    continue;
                }

                encounter.BeginEncounter(this);
                beganAny = true;
            }

            return beganAny;
        }

        bool BeginNextAuthoredEncounterAfter(ArenaOpeningEncounter completedEncounter)
        {
            var startIndex = 0;
            if (completedEncounter != null)
            {
                var completedIndex = m_ArenaEncounters.IndexOf(completedEncounter);
                startIndex = completedIndex >= 0 ? completedIndex + 1 : 0;
            }

            var maxUnlockAfterWave = GetAuthoredProgressionUnlockedWave(completedEncounter);
            for (var i = startIndex; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter == null ||
                    !encounter.isActiveAndEnabled ||
                    encounter.HasBegun ||
                    encounter.HasCompleted ||
                    !ShouldAutoStartEncounter(encounter, maxUnlockAfterWave))
                {
                    continue;
                }

                encounter.BeginEncounter(this);
                if (m_IsWaitingForEncounterResume &&
                    (m_PendingWaveResumeEncounter == null || ReferenceEquals(m_PendingWaveResumeEncounter, completedEncounter)))
                {
                    m_PendingWaveResumeEncounter = encounter;
                }

                return true;
            }

            return false;
        }

        int GetAuthoredProgressionUnlockedWave(ArenaOpeningEncounter completedEncounter)
        {
            var unlockedWave = m_HasStartedWaveLoop ? m_CurrentWave : 0;
            if (completedEncounter != null)
                unlockedWave = Mathf.Max(unlockedWave, completedEncounter.UnlockAfterWave);

            return Mathf.Max(0, unlockedWave);
        }

        bool HasPendingAutoStartEncounters()
        {
            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter == null ||
                    !encounter.isActiveAndEnabled ||
                    !ShouldAutoStartEncounter(encounter))
                {
                    continue;
                }

                if (encounter.HasBegun && !encounter.HasCompleted)
                    return true;
            }

            return false;
        }

        void ResetAllArenaEncounters()
        {
            for (var i = 0; i < m_ArenaEncounters.Count; i++)
            {
                var encounter = m_ArenaEncounters[i];
                if (encounter != null)
                    encounter.ResetEncounter();
            }
        }

        void HandleRunProgressionChanged()
        {
            ApplyPlayerSpeedMultiplier();
            RefreshRunInteractionState();
        }

        void MonitorUpgradePauseState()
        {
            if (m_RunProgressionController == null)
                return;

            var isUpgradeSelectionActive = m_RunProgressionController.IsUpgradeSelectionActive;
            if (isUpgradeSelectionActive == m_LastUpgradeSelectionActive)
                return;

            m_LastUpgradeSelectionActive = isUpgradeSelectionActive;
            RefreshRunInteractionState();
        }

        void RefreshRunInteractionState()
        {
            var hasRunInput = m_RunStarted &&
                              !m_IsRestarting &&
                              !m_IsGameOver &&
                              !m_IsPauseMenuOpen &&
                              !m_IsVictoryMenuOpen &&
                              (m_WristChainIntroController == null || !m_WristChainIntroController.IsActive) &&
                              (m_RunProgressionController == null || !m_RunProgressionController.IsUpgradeSelectionActive);

            SetLocomotionEnabled(hasRunInput);
            m_PlayerSpellLoadout?.SetSpellsEnabled(hasRunInput);
        }

        void RefreshManagedLocomotionBehaviours()
        {
            var previousDefaultStates = new Dictionary<Behaviour, bool>(m_LocomotionDefaultEnabledStates);
            var previousBaseMoveSpeed = new Dictionary<Behaviour, float>(m_LocomotionBaseMoveSpeed);
            m_ManagedLocomotionBehaviours.Clear();
            m_LocomotionDefaultEnabledStates.Clear();
            m_LocomotionBaseMoveSpeed.Clear();

            var locomotionProviders = FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < locomotionProviders.Length; i++)
                RegisterLocomotionBehaviour(locomotionProviders[i], previousDefaultStates, previousBaseMoveSpeed);

            var teleportationAreas = FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < teleportationAreas.Length; i++)
                RegisterLocomotionBehaviour(teleportationAreas[i], previousDefaultStates, previousBaseMoveSpeed);

            if (m_PlayerRoot != null)
            {
                var behaviours = m_PlayerRoot.GetComponentsInChildren<Behaviour>(true);
                for (var i = 0; i < behaviours.Length; i++)
                {
                    var behaviour = behaviours[i];
                    if (behaviour == null || !ShouldTrackLocomotionBehaviour(behaviour))
                        continue;

                    RegisterLocomotionBehaviour(behaviour, previousDefaultStates, previousBaseMoveSpeed);
                }
            }
        }

        void RegisterLocomotionBehaviour(
            Behaviour behaviour,
            IReadOnlyDictionary<Behaviour, bool> previousDefaultStates,
            IReadOnlyDictionary<Behaviour, float> previousBaseMoveSpeed)
        {
            if (behaviour == null || m_ManagedLocomotionBehaviours.Contains(behaviour))
                return;

            m_ManagedLocomotionBehaviours.Add(behaviour);
            if (previousDefaultStates != null && previousDefaultStates.TryGetValue(behaviour, out var defaultEnabled))
                m_LocomotionDefaultEnabledStates[behaviour] = defaultEnabled;
            else
                m_LocomotionDefaultEnabledStates[behaviour] = behaviour.enabled;

            if (previousBaseMoveSpeed != null && previousBaseMoveSpeed.TryGetValue(behaviour, out var baseMoveSpeed))
            {
                m_LocomotionBaseMoveSpeed[behaviour] = baseMoveSpeed;
            }
            else if (TryGetMemberValue(behaviour, "moveSpeed", out var moveSpeedValue) && moveSpeedValue is float moveSpeed)
            {
                m_LocomotionBaseMoveSpeed[behaviour] = moveSpeed;
            }
        }

        static bool ShouldTrackLocomotionBehaviour(Behaviour behaviour)
        {
            if (behaviour == null)
                return false;

            if (behaviour is LocomotionProvider || behaviour is TeleportationArea)
                return true;

            var type = behaviour.GetType();
            var typeName = type.Name;
            var typeNamespace = type.Namespace ?? string.Empty;
            return typeName.IndexOf("MoveProvider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeNamespace.IndexOf("Locomotion", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void SetLocomotionEnabled(bool enabled)
        {
            for (var i = 0; i < m_ManagedLocomotionBehaviours.Count; i++)
            {
                var behaviour = m_ManagedLocomotionBehaviours[i];
                if (behaviour == null)
                    continue;

                var defaultEnabled = !m_LocomotionDefaultEnabledStates.TryGetValue(behaviour, out var wasEnabled) || wasEnabled;
                behaviour.enabled = enabled && defaultEnabled;
            }
        }

        void ApplyPlayerSpeedMultiplier()
        {
            var speedMultiplier = m_RunProgressionController != null
                ? m_RunProgressionController.GetPlayerSpeedMultiplier()
                : 1f;

            foreach (var pair in m_LocomotionBaseMoveSpeed)
            {
                if (pair.Key == null)
                    continue;

                TrySetMemberValue(pair.Key, "moveSpeed", pair.Value * speedMultiplier);
            }
        }

        bool IsSpellHandOccupied(PlayerHandSide handSide)
        {
            var bootstrapperHandSide = handSide == PlayerHandSide.Left ? HandSide.Left : HandSide.Right;
            foreach (var pair in m_EquippedHandByPickup)
            {
                if (pair.Key == null)
                    continue;

                if (pair.Value == bootstrapperHandSide)
                    return true;
            }

            return false;
        }

        void BeginWristChainIntro()
        {
            if (m_PlayerCamera == null ||
                (m_LeftControllerTransform == null && m_LeftHandTransform == null) ||
                (m_RightControllerTransform == null && m_RightHandTransform == null) ||
                m_WristChainIntroController == null)
            {
                HandleWristChainsBroken();
                return;
            }

            m_CombatHud?.ShowBanner("Lift each controller to tear the chains free", 90f);
            m_WristChainIntroController.Begin(
                m_PlayerCamera.transform,
                m_PlayerCamera.transform.parent != null ? m_PlayerCamera.transform.parent : m_PlayerRoot,
                m_LeftHandTransform,
                m_RightHandTransform,
                m_LeftControllerTransform,
                m_RightControllerTransform,
                CreateIntroChainVisual,
                HandleWristChainBroken,
                HandleWristChainsBroken);

            RefreshRunInteractionState();
        }

        void HandleWristChainBroken(PlayerHandSide handSide, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            if (m_IsRestarting || m_IsGameOver)
                return;

            CreateLooseChainPickup("Freed Chain", spawnPosition, spawnRotation);

            if (!m_HasGrantedIntroChainWeapon)
            {
                m_RunProgressionController?.RegisterWeaponAcquired(WeaponKind.Chain);
                m_HasGrantedIntroChainWeapon = true;
            }

            var freedSideText = handSide == PlayerHandSide.Left ? "Left" : "Right";
            m_CombatHud?.ShowBanner($"{freedSideText} wrist free. Lift the other controller higher.", 90f);
        }

        void HandleWristChainsBroken()
        {
            if (m_IsRestarting || m_IsGameOver)
                return;

            Time.timeScale = 1f;
            m_RunStarted = true;
            if (!m_HasGrantedIntroChainWeapon)
            {
                m_RunProgressionController?.RegisterWeaponAcquired(WeaponKind.Chain);
                SpawnStartingChainWeapon();
                m_HasGrantedIntroChainWeapon = true;
            }
            if (m_SpawnLoop != null)
            {
                StopCoroutine(m_SpawnLoop);
                m_SpawnLoop = null;
            }
            m_CombatHud?.ShowBanner("Chains broken", 1.1f);
            RefreshManagedLocomotionBehaviours();
            ApplyPlayerSpeedMultiplier();
            SetLocomotionEnabled(true);
            m_PlayerSpellLoadout?.SetSpellsEnabled(true);
            RefreshRunInteractionState();

            ResolveArenaEncounters();
            if (!m_UsesAuthoredEncounterProgression && !HasEncounterThatStartsOrResumesWaves())
            {
                Debug.LogWarning(
                    "[VRCombat] No encounter is configured to start or resume waves on completion. Starting the runtime wave loop directly.",
                    this);
            }

            if (BeginAutoStartEncounters())
                return;

            if (!m_HasStartedWaveLoop)
                StartWaveLoop();
        }

        void HandleArenaOpeningEncounterCompleted(ArenaOpeningEncounter encounter)
        {
            if (m_IsRestarting || m_IsGameOver || !m_RunStarted || encounter == null)
                return;

            if (m_UsesAuthoredEncounterProgression)
            {
                m_PlayerDamageReceiver?.RestoreFullHealth();
                if (encounter.StartsOrResumesWavesOnCompletion)
                {
                    if (!m_HasStartedWaveLoop)
                    {
                        StartWaveLoop();
                        return;
                    }
                }

                if (BeginNextAuthoredEncounterAfter(encounter))
                {
                    m_CombatHud?.ShowBanner("Next arena fight ready.", 1.5f);
                    return;
                }

                if (m_IsWaitingForEncounterResume)
                {
                    if (!encounter.StartsOrResumesWavesOnCompletion &&
                        (m_PendingWaveResumeEncounter == null || ReferenceEquals(encounter, m_PendingWaveResumeEncounter)))
                    {
                        Debug.LogWarning(
                            $"[VRCombat] Encounter '{encounter.name}' completed during a wave milestone, but no follow-up auto-start encounter was available. Resuming waves to avoid stalling progression.",
                            encounter);
                    }

                    m_IsWaitingForEncounterResume = false;
                    m_PendingWaveResumeEncounter = null;
                    return;
                }

                m_CombatHud?.ShowBanner("Arena route cleared.", 1.5f);
                return;
            }

            if (!m_HasStartedWaveLoop)
            {
                if (encounter.StartsOrResumesWavesOnCompletion)
                    StartWaveLoop();
                else
                {
                    Debug.LogWarning(
                        $"[VRCombat] Encounter '{encounter.name}' completed without a wave-resume signal. Waves remain paused until an encounter or trigger resumes them.",
                        encounter);
                }

                return;
            }

            if (!m_IsWaitingForEncounterResume || !ReferenceEquals(encounter, m_PendingWaveResumeEncounter))
                return;

            if (!encounter.StartsOrResumesWavesOnCompletion)
            {
                Debug.LogWarning(
                    $"[VRCombat] Encounter '{encounter.name}' completed while waves were paused, but StartsOrResumesWavesOnCompletion is disabled.",
                    encounter);
                return;
            }

            m_IsWaitingForEncounterResume = false;
            m_PendingWaveResumeEncounter = null;
        }

        void StartWaveLoop()
        {
            if (m_SpawnLoop != null)
                return;

            m_HasStartedWaveLoop = true;
            m_SpawnLoop = StartCoroutine(WaveLoop());
            Debug.Log($"[VRCombat] Wave loop started at wave {m_CurrentWave}.", this);
        }

        void SpawnStartingChainWeapon()
        {
            var rightHand = m_RightControllerTransform != null ? m_RightControllerTransform : m_RightHandTransform;
            var referenceTransform = rightHand != null ? rightHand : m_PlayerCamera != null ? m_PlayerCamera.transform : null;
            if (referenceTransform == null)
                return;

            var spawnForward = Vector3.ProjectOnPlane(referenceTransform.forward, Vector3.up).normalized;
            if (spawnForward.sqrMagnitude < 0.001f)
                spawnForward = Vector3.forward;

            var spawnPosition = referenceTransform.position + spawnForward * 0.14f - Vector3.up * 0.04f;
            var spawnRotation = Quaternion.LookRotation(spawnForward, Vector3.up);
            SpawnCardRewardWeapon(WeaponKind.Chain, spawnPosition, spawnRotation);
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
            EnsureRunSystems();
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
            m_IsVictoryMenuOpen = false;
            m_IsGameOver = true;
            m_RunStarted = false;
            if (m_SpawnLoop != null)
                StopCoroutine(m_SpawnLoop);

            FreezeAllEnemiesForDeath();
            Time.timeScale = 0f;
            m_CombatHud?.HideVictoryPanel();
            m_CombatHud?.HideDeathPanel();
            m_CombatHud?.ShowBanner("You were overwhelmed.", 1.2f);
            m_CombatHud?.FadeToBlack(0.55f);

            if (m_DeathFlowRoutine != null)
                StopCoroutine(m_DeathFlowRoutine);
            m_DeathFlowRoutine = StartCoroutine(DeathUiFlow());
            RefreshRunInteractionState();
        }

        void RestartCurrentScene()
        {
            if (!isActiveAndEnabled || m_IsRestarting)
                return;

            m_IsVictoryMenuOpen = false;
            m_CombatHud?.HideVictoryPanel();
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

        void DebugGrantUpgrade(UpgradeKind upgradeKind)
        {
            if (m_RunProgressionController == null)
                return;

            if (!m_RunProgressionController.GrantUpgradeForDebug(upgradeKind))
                return;

            var upgradeName = RunCatalog.TryGetUpgrade(upgradeKind, out var definition)
                ? definition.DisplayName
                : upgradeKind.ToString();
            m_CombatHud?.ShowBanner($"Debug upgrade: {upgradeName}", 0.9f);
        }

        void DebugGrantWeapon(WeaponKind weaponKind)
        {
            DebugGrantWeaponAtSlot(weaponKind, 0, showBanner: true);
        }

        void DebugGrantAllWeapons()
        {
            var weaponKinds = new[]
            {
                WeaponKind.Chain,
                WeaponKind.Dagger,
                WeaponKind.Flintlock,
                WeaponKind.Mace,
                WeaponKind.Shield,
                WeaponKind.Spear,
                WeaponKind.Sword
            };

            for (var i = 0; i < weaponKinds.Length; i++)
                DebugGrantWeaponAtSlot(weaponKinds[i], i, showBanner: false);

            m_CombatHud?.ShowBanner("Debug weapons granted", 0.9f);
        }

        void DebugGrantWeaponAtSlot(WeaponKind weaponKind, int slotIndex, bool showBanner)
        {
            if (weaponKind == WeaponKind.None)
                return;

            if (TryGetDebugRewardPose(slotIndex, out var spawnPosition, out var spawnRotation))
                SpawnCardRewardWeapon(weaponKind, spawnPosition, spawnRotation);
            else
                m_RunProgressionController?.RegisterWeaponAcquired(weaponKind);

            if (!showBanner)
                return;

            var weaponName = RunCatalog.TryGetWeapon(weaponKind, out var definition)
                ? definition.DisplayName
                : weaponKind.ToString();
            m_CombatHud?.ShowBanner($"Debug weapon: {weaponName}", 0.9f);
        }

        void DebugUnlockSpell(SpellKind spellKind)
        {
            if (spellKind == SpellKind.None || m_RunProgressionController == null)
                return;

            m_RunProgressionController.UnlockSpell(spellKind);
            m_PlayerSpellLoadout?.SetSpellsEnabled(true);
            var spellName = RunCatalog.TryGetSpell(spellKind, out var definition)
                ? definition.DisplayName
                : spellKind.ToString();
            m_CombatHud?.ShowBanner($"Debug spell: {spellName}", 0.9f);
        }

        bool TryGetDebugRewardPose(int slotIndex, out Vector3 position, out Quaternion rotation)
        {
            var referenceTransform = m_PlayerCamera != null ? m_PlayerCamera.transform : transform;
            var forward = Vector3.ProjectOnPlane(referenceTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            var right = Vector3.ProjectOnPlane(referenceTransform.right, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;

            var column = slotIndex % 4;
            var row = slotIndex / 4;
            position = referenceTransform.position +
                       forward * (1.15f + row * 0.28f) +
                       right * ((column - 1.5f) * 0.34f) -
                       Vector3.up * 1.15f;
            rotation = Quaternion.LookRotation(forward, Vector3.up);

            if (TryResolveLooseGroundSpawnPosition(position, 0.18f, 0.45f, out var groundedPosition))
                position = groundedPosition;

            return true;
        }

        void DebugKillAllEnemies()
        {
            DebugKillAllEnemies(showBanner: true);
        }

        void DebugKillAllEnemies(bool showBanner)
        {
            var allEnemies = FindObjectsByType<CapsuleEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var killedCount = 0;
            for (var i = 0; i < allEnemies.Length; i++)
            {
                var enemy = allEnemies[i];
                if (enemy == null)
                    continue;

                m_ActiveWaveEnemies.Remove(enemy);
                enemy.ApplyDamage(999999f, enemy.transform.position + Vector3.up * 1f, gameObject);
                killedCount++;
            }

            if (showBanner)
                m_CombatHud?.ShowBanner($"Debug killed {killedCount} enemies", 0.9f);
        }

        void DebugSetWaveNumber(int waveNumber)
        {
            var clampedWave = Mathf.Clamp(waveNumber, 1, 999);
            if (m_SpawnLoop != null)
            {
                StopCoroutine(m_SpawnLoop);
                m_SpawnLoop = null;
            }

            if (m_MilestoneKeyCarrier != null)
                m_MilestoneKeyCarrier.Died -= HandleMilestoneKeyCarrierDied;

            m_PendingWaveResumeEncounter = null;
            m_MilestoneKeyCarrier = null;
            m_DeferredNextWave = -1;
            m_IsWaitingForEncounterResume = false;
            m_IsVictoryMenuOpen = false;
            m_ShouldContinueAfterVictory = false;
            m_EndlessModeActive = clampedWave > BossWaveNumber;
            m_HasShownVictoryChoice = clampedWave > BossWaveNumber;
            m_CurrentWave = clampedWave;
            m_CurrentFlowRate = Mathf.Max(
                0.08f,
                m_StartingFlowRatePerSecond + (m_CurrentWave - 1) * m_FlowRateIncreasePerWave);

            DebugKillAllEnemies(showBanner: false);
            m_ActiveWaveEnemies.Clear();
            m_HitsTakenThisWave = 0;
            m_RunStarted = true;
            m_HasStartedWaveLoop = false;
            StartWaveLoop();

            m_CombatHud?.SetWaveInfo(m_CurrentWave, 0, m_CurrentFlowRate);
            m_CombatHud?.ShowBanner($"Debug wave set to {m_CurrentWave}", 1f);
        }

        void SetPauseMenuOpen(bool isOpen)
        {
            if (m_IsGameOver || m_IsRestarting || m_IsVictoryMenuOpen)
                isOpen = false;

            if (m_IsPauseMenuOpen == isOpen)
                return;

            m_IsPauseMenuOpen = isOpen;
            m_CombatHud?.SetPauseMenuVisible(isOpen);
            if (m_CombatHud != null)
                m_CombatHud.SetMovementVignetteStrength(m_MovementVignetteStrength, notify: false);

            if (isOpen)
            {
                Time.timeScale = 0f;
                m_CombatHud?.FocusPauseMenu();
            }
            else if (!m_IsGameOver &&
                     !m_IsRestarting &&
                     (m_RunProgressionController == null || !m_RunProgressionController.IsUpgradeSelectionActive))
            {
                Time.timeScale = 1f;
            }

            RefreshRunInteractionState();
        }

        void ContinueEndlessMode()
        {
            if (!m_IsVictoryMenuOpen || m_IsRestarting || m_IsGameOver)
                return;

            m_EndlessModeActive = true;
            m_ShouldContinueAfterVictory = true;
            m_IsVictoryMenuOpen = false;
            m_CombatHud?.HideVictoryPanel();
            Time.timeScale = 1f;
            RefreshRunInteractionState();
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
                m_HasLoggedWavePlatformSpawnFailure = false;
                m_EndlessModeActive = false;
                m_HasShownVictoryChoice = false;
                m_IsVictoryMenuOpen = false;
                m_ShouldContinueAfterVictory = false;

                RestoreSuppressedHandRenderers();
                DestroyRuntimeCombatObjects();
                DestroyLegacyStickObjects();
                ConfigureArenaSurface();
                ResetSceneAuthoredObjects();
                CapsuleEnemy.ClearRuntimeDecals();
                m_ActiveWaveEnemies.Clear();
                m_EnemyKillZoneGraceUntil.Clear();
                m_NextKillZoneGlobalSweepTime = 0f;
                m_RunStarted = false;
                m_HasStartedWaveLoop = false;
                m_UsesAuthoredEncounterProgression = false;
                m_HasGrantedIntroChainWeapon = false;
                m_IsWaitingForEncounterResume = false;
                m_PendingWaveResumeEncounter = null;
                m_DeferredNextWave = -1;
                if (m_MilestoneKeyCarrier != null)
                    m_MilestoneKeyCarrier.Died -= HandleMilestoneKeyCarrierDied;
                m_MilestoneKeyCarrier = null;
                m_SpawnProtectionUntilTime = Time.unscaledTime + SpawnProtectionSeconds;
                m_WristChainIntroController?.Cleanup();

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
                ConfigureControllerLocomotionActionManagers();
                EnsureRunSystems();
                m_RunProgressionController?.ResetRun();
                ResetAllArenaEncounters();
                HideLegacyArenaCenterObjects();
                ConfigureSceneAuthoredSwordPickups();
                ConfigureExistingGrabInteractables();
                EnsureSceneAuthoredResettables();
                DisableTutorialObjects();
                ResolveRunStartPose();
                MovePlayerToTeleportAnchor();
                yield return null;
                MovePlayerToTeleportAnchor();
                InitializeKillZoneCenter();
                SetupControllerSwingDamage();
                InitializeCardTables();
                ValidateRigCoherency("restart");
                RefreshRunInteractionState();
                BeginWristChainIntro();
            }
            finally
            {
                m_IsRestarting = false;
                RefreshRunInteractionState();
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
            TryFindArenaRoot(out var arenaRoot);
            var hasScenePlatform = TryFindArenaPlatformRoot(arenaRoot, out var scenePlatformRoot);
            if (arenaRoot == null && hasScenePlatform && scenePlatformRoot.parent != null)
                arenaRoot = scenePlatformRoot.parent;
            if (arenaRoot == null && !hasScenePlatform)
                return;

            m_ArenaSurfaceColliders.Clear();
            m_ArenaPlatformSurfaceColliders.Clear();
            var candidateColliders = new List<Collider>();

            var colliderSearchRoot = arenaRoot != null ? arenaRoot : scenePlatformRoot;
            var meshFilters = colliderSearchRoot.GetComponentsInChildren<MeshFilter>(true);
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
                if (IsArenaWalkableCandidate(meshCollider, arenaRoot) && !candidateColliders.Contains(meshCollider))
                    candidateColliders.Add(meshCollider);
            }

            var existingColliders = colliderSearchRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < existingColliders.Length; i++)
            {
                var collider = existingColliders[i];
                if (!IsArenaWalkableCandidate(collider, arenaRoot) || candidateColliders.Contains(collider))
                    continue;

                candidateColliders.Add(collider);
            }

            CollectArenaWalkableSurfaces(candidateColliders);
            CollectArenaPlatformSpawnSurfaces(candidateColliders);
            if (hasScenePlatform)
            {
                var foundPrimaryPlatform = IsPrimaryArenaPlatform(scenePlatformRoot);
                if (foundPrimaryPlatform || m_ArenaPlatformSurfaceColliders.Count == 0)
                {
                    m_ArenaPlatformSurfaceColliders.Clear();
                    AddPlatformRootSurfaces(scenePlatformRoot);
                }
            }

            if (arenaRoot != null)
                TryConfigureKillZoneFromArena(arenaRoot);

            if (arenaRoot != null && TryFindPrimaryTeleportationArea(out var teleportationArea))
                ConfigureArenaTeleportationArea(arenaRoot, teleportationArea);
        }

        void AddPlatformRootSurfaces(Transform platformRoot)
        {
            if (platformRoot == null)
                return;

            var meshFilters = platformRoot.GetComponentsInChildren<MeshFilter>(true);
            for (var i = 0; i < meshFilters.Length; i++)
            {
                var meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                var meshCollider = meshFilter.GetComponent<MeshCollider>();
                if (meshCollider == null)
                    meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                meshCollider.isTrigger = false;
                meshCollider.enabled = true;
            }

            var colliders = platformRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (!IsArenaPlatformSpawnSurfaceCandidate(collider))
                    continue;

                AddArenaPlatformSurface(collider);
                if (!m_ArenaSurfaceColliders.Contains(collider))
                    m_ArenaSurfaceColliders.Add(collider);
            }

            if (m_ArenaPlatformSurfaceColliders.Count > 0)
                Debug.Log($"[VRCombat] Using arena platform '{GetTransformPath(platformRoot)}' for wave spawns.", this);
        }

        static bool IsPrimaryArenaPlatform(Transform platformRoot)
        {
            return platformRoot != null &&
                   string.Equals(platformRoot.name, ArenaPrimaryPlatformObjectName, StringComparison.OrdinalIgnoreCase);
        }

        bool IsArenaWalkableCandidate(Collider collider, Transform arenaRoot)
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
                return false;

            var colliderTransform = collider.transform;
            if (arenaRoot != null && colliderTransform != arenaRoot && !colliderTransform.IsChildOf(arenaRoot))
                return false;

            if (colliderTransform.GetComponentInParent<XRBaseInteractable>() != null ||
                colliderTransform.GetComponentInParent<XRGrabInteractable>() != null ||
                colliderTransform.GetComponentInParent<TeleportationArea>() != null ||
                colliderTransform.GetComponentInParent<GateController>() != null ||
                colliderTransform.GetComponentInParent<Padlock>() != null ||
                colliderTransform.GetComponentInParent<ArenaOpeningEncounter>() != null ||
                colliderTransform.GetComponentInParent<TriggerSpawner>() != null ||
                colliderTransform.GetComponentInParent<RuntimeKillZoneBoundary>() != null)
            {
                return false;
            }

            if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                return false;

            return !HasAnyNameToken(
                colliderTransform,
                "wall",
                "gate",
                "door",
                "teleport",
                "trigger",
                "kill",
                "boundary",
                "spawn",
                "encounter",
                "padlock",
                "lock",
                "pillar",
                "column",
                "ceiling",
                "helper");
        }

        void CollectArenaWalkableSurfaces(List<Collider> candidateColliders)
        {
            if (!TryGetBoundsFromColliders(candidateColliders, out var sampleBounds))
                return;

            var insetX = Mathf.Min(ArenaWalkableSampleInset, sampleBounds.extents.x * 0.45f);
            var insetZ = Mathf.Min(ArenaWalkableSampleInset, sampleBounds.extents.z * 0.45f);
            var sampleMinX = sampleBounds.min.x + insetX;
            var sampleMaxX = sampleBounds.max.x - insetX;
            var sampleMinZ = sampleBounds.min.z + insetZ;
            var sampleMaxZ = sampleBounds.max.z - insetZ;
            var probeY = sampleBounds.max.y + ArenaGroundSnapProbeHeight + 0.75f;
            var probeDistance = sampleBounds.size.y + ArenaGroundSnapProbeHeight + ArenaGroundSnapDistance + 2f;
            var floorCeilingY = sampleBounds.min.y + Mathf.Max(0.35f, ArenaTeleportFloorTopBand);

            for (var xIndex = 0; xIndex < ArenaWalkableSampleGridResolution; xIndex++)
            {
                var tx = ArenaWalkableSampleGridResolution <= 1 ? 0.5f : xIndex / (ArenaWalkableSampleGridResolution - 1f);
                var sampleX = Mathf.Lerp(sampleMinX, sampleMaxX, tx);
                for (var zIndex = 0; zIndex < ArenaWalkableSampleGridResolution; zIndex++)
                {
                    var tz = ArenaWalkableSampleGridResolution <= 1 ? 0.5f : zIndex / (ArenaWalkableSampleGridResolution - 1f);
                    var sampleZ = Mathf.Lerp(sampleMinZ, sampleMaxZ, tz);
                    if (!TryGetArenaGroundHit(
                            new Vector3(sampleX, probeY, sampleZ),
                            probeDistance,
                            candidateColliders,
                            floorCeilingY,
                            out var hit))
                    {
                        continue;
                    }

                    if (!m_ArenaSurfaceColliders.Contains(hit.collider))
                        m_ArenaSurfaceColliders.Add(hit.collider);
                }
            }

            if (m_ArenaSurfaceColliders.Count > 0)
                return;

            for (var i = 0; i < candidateColliders.Count; i++)
            {
                var collider = candidateColliders[i];
                if (collider == null || collider.bounds.max.y > floorCeilingY)
                    continue;

                if (!m_ArenaSurfaceColliders.Contains(collider))
                    m_ArenaSurfaceColliders.Add(collider);
            }
        }

        void CollectArenaPlatformSpawnSurfaces(List<Collider> candidateColliders)
        {
            m_ArenaPlatformSurfaceColliders.Clear();
            if (candidateColliders == null)
                return;

            TryGetBoundsFromColliders(candidateColliders, out var arenaCandidateBounds);
            var bestFallbackCollider = default(Collider);
            var bestFallbackScore = float.NegativeInfinity;

            for (var i = 0; i < candidateColliders.Count; i++)
            {
                var collider = candidateColliders[i];
                if (!IsArenaPlatformSpawnSurfaceCandidate(collider))
                    continue;

                if (HasColliderOrMeshNameToken(collider, "platform") ||
                    HasColliderOrMeshNameToken(collider, "grid") ||
                    HasColliderOrMeshLocalNameToken(collider, "floor"))
                {
                    AddArenaPlatformSurface(collider);
                }
            }

            if (m_ArenaPlatformSurfaceColliders.Count > 0)
                return;

            for (var i = 0; i < candidateColliders.Count; i++)
            {
                var collider = candidateColliders[i];
                if (!IsArenaPlatformSpawnSurfaceCandidate(collider))
                    continue;

                var fallbackScore = ScoreArenaPlatformFallbackSurface(collider, arenaCandidateBounds);
                if (fallbackScore <= bestFallbackScore)
                    continue;

                bestFallbackScore = fallbackScore;
                bestFallbackCollider = collider;
            }

            if (bestFallbackCollider == null)
                return;

            var fallbackThreshold = !float.IsNaN(bestFallbackScore) && !float.IsInfinity(bestFallbackScore)
                ? bestFallbackScore - Mathf.Max(4f, Mathf.Abs(bestFallbackScore) * 0.35f)
                : bestFallbackScore;
            for (var i = 0; i < candidateColliders.Count; i++)
            {
                var collider = candidateColliders[i];
                if (!IsArenaPlatformSpawnSurfaceCandidate(collider))
                    continue;

                if (ScoreArenaPlatformFallbackSurface(collider, arenaCandidateBounds) >= fallbackThreshold)
                    AddArenaPlatformSurface(collider);
            }

            if (m_ArenaPlatformSurfaceColliders.Count == 0)
                AddArenaPlatformSurface(bestFallbackCollider);
        }

        void AddArenaPlatformSurface(Collider collider)
        {
            if (collider != null && !m_ArenaPlatformSurfaceColliders.Contains(collider))
                m_ArenaPlatformSurfaceColliders.Add(collider);
        }

        static bool IsArenaPlatformSpawnSurfaceCandidate(Collider collider)
        {
            return collider != null &&
                   collider.enabled &&
                   !collider.isTrigger &&
                   !HasColliderOrMeshAnyNameToken(collider, s_PlatformSpawnRejectedNameTokens) &&
                   TryGetColliderTopSurfaceHit(collider, out _);
        }

        float ScoreArenaPlatformFallbackSurface(Collider collider, Bounds arenaCandidateBounds)
        {
            if (collider == null)
                return float.NegativeInfinity;

            var bounds = collider.bounds;
            var horizontalArea = Mathf.Max(0.01f, bounds.size.x * bounds.size.z);
            var referencePosition = GetArenaPlatformReferencePosition(arenaCandidateBounds, bounds);
            var distanceFromCenter = Vector2.Distance(
                new Vector2(bounds.center.x, bounds.center.z),
                new Vector2(referencePosition.x, referencePosition.z));
            var floorNameBonus =
                HasColliderOrMeshLocalNameToken(collider, "floor") ||
                HasColliderOrMeshLocalNameToken(collider, "arena")
                    ? horizontalArea * 1.5f
                    : 0f;
            var containsReferenceBonus = ContainsHorizontalPoint(bounds, referencePosition)
                ? horizontalArea * 2.5f
                : 0f;

            return horizontalArea + floorNameBonus + containsReferenceBonus - distanceFromCenter * Mathf.Max(1f, Mathf.Sqrt(horizontalArea));
        }

        Vector3 GetArenaPlatformReferencePosition(Bounds arenaCandidateBounds, Bounds fallbackBounds)
        {
            if (m_PlayerCamera != null)
                return m_PlayerCamera.transform.position;

            return arenaCandidateBounds.size.sqrMagnitude > 0.0001f
                ? arenaCandidateBounds.center
                : fallbackBounds.center;
        }

        static bool ContainsHorizontalPoint(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x &&
                   point.x <= bounds.max.x &&
                   point.z >= bounds.min.z &&
                   point.z <= bounds.max.z;
        }

        static bool TryGetColliderTopSurfaceHit(Collider collider, out RaycastHit bestHit)
        {
            bestHit = default;
            if (collider == null || !collider.enabled || collider.isTrigger)
                return false;

            var bounds = collider.bounds;
            if (!IsFiniteVector3(bounds.center) || !IsFiniteVector3(bounds.size))
                return false;

            var insetX = Mathf.Min(0.2f, bounds.extents.x * 0.35f);
            var insetZ = Mathf.Min(0.2f, bounds.extents.z * 0.35f);
            var minX = bounds.min.x + insetX;
            var maxX = bounds.max.x - insetX;
            var minZ = bounds.min.z + insetZ;
            var maxZ = bounds.max.z - insetZ;
            if (minX > maxX)
                minX = maxX = bounds.center.x;
            if (minZ > maxZ)
                minZ = maxZ = bounds.center.z;

            var rayDistance = Mathf.Max(2f, bounds.size.y + ArenaGroundSnapProbeHeight + 2f);
            var rayStartY = bounds.max.y + ArenaGroundSnapProbeHeight + 0.75f;
            var closestDistance = float.PositiveInfinity;

            const int sampleGridResolution = 7;
            for (var xIndex = 0; xIndex < sampleGridResolution; xIndex++)
            {
                var tx = xIndex / (float)(sampleGridResolution - 1);
                var sampleX = Mathf.Lerp(minX, maxX, tx);
                for (var zIndex = 0; zIndex < sampleGridResolution; zIndex++)
                {
                    var tz = zIndex / (float)(sampleGridResolution - 1);
                    var sampleZ = Mathf.Lerp(minZ, maxZ, tz);
                    var ray = new Ray(new Vector3(sampleX, rayStartY, sampleZ), Vector3.down);
                    if (!collider.Raycast(ray, out var hit, rayDistance))
                        continue;

                    if (hit.normal.y < ArenaGroundSnapNormalThreshold)
                        continue;

                    if (hit.distance >= closestDistance)
                        continue;

                    closestDistance = hit.distance;
                    bestHit = hit;
                }
            }

            return closestDistance < float.PositiveInfinity;
        }

        static bool HasColliderOrMeshAnyNameToken(Collider collider, string[] nameTokens)
        {
            if (collider == null || nameTokens == null)
                return false;

            if (HasAnyNameToken(collider.transform, nameTokens))
                return true;

            for (var i = 0; i < nameTokens.Length; i++)
            {
                if (HasColliderOrMeshLocalNameToken(collider, nameTokens[i]))
                    return true;
            }

            return false;
        }

        static bool HasColliderOrMeshNameToken(Collider collider, string nameToken)
        {
            if (collider == null || string.IsNullOrWhiteSpace(nameToken))
                return false;

            if (HasNameToken(collider.transform, nameToken))
                return true;

            if (collider is MeshCollider meshCollider &&
                meshCollider.sharedMesh != null &&
                ContainsNameToken(meshCollider.sharedMesh.name, nameToken))
            {
                return true;
            }

            var meshFilter = collider.GetComponent<MeshFilter>();
            return meshFilter != null &&
                   meshFilter.sharedMesh != null &&
                   ContainsNameToken(meshFilter.sharedMesh.name, nameToken);
        }

        static bool HasColliderOrMeshLocalNameToken(Collider collider, string nameToken)
        {
            if (collider == null || string.IsNullOrWhiteSpace(nameToken))
                return false;

            if (ContainsNameToken(collider.transform.name, nameToken))
                return true;

            if (collider is MeshCollider meshCollider &&
                meshCollider.sharedMesh != null &&
                ContainsNameToken(meshCollider.sharedMesh.name, nameToken))
            {
                return true;
            }

            var meshFilter = collider.GetComponent<MeshFilter>();
            return meshFilter != null &&
                   meshFilter.sharedMesh != null &&
                   ContainsNameToken(meshFilter.sharedMesh.name, nameToken);
        }

        static bool TryGetBoundsFromColliders(IReadOnlyList<Collider> colliders, out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            if (colliders == null)
                return false;

            for (var i = 0; i < colliders.Count; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }

            return hasBounds;
        }

        static bool HasAnyNameToken(Transform transformCandidate, params string[] tokens)
        {
            if (transformCandidate == null || tokens == null)
                return false;

            for (var i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                if (!string.IsNullOrWhiteSpace(token) && HasNameToken(transformCandidate, token))
                    return true;
            }

            return false;
        }

        static bool HasNameToken(Transform transformCandidate, string nameToken)
        {
            if (transformCandidate == null || string.IsNullOrWhiteSpace(nameToken))
                return false;

            var current = transformCandidate;
            while (current != null)
            {
                if (ContainsNameToken(current.name, nameToken))
                    return true;

                current = current.parent;
            }

            return false;
        }

        static bool ContainsNameToken(string value, string nameToken)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(nameToken) &&
                   value.IndexOf(nameToken, StringComparison.OrdinalIgnoreCase) >= 0;
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

        bool TryFindArenaPlatformRoot(Transform preferredArenaRoot, out Transform platformRoot)
        {
            platformRoot = null;
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var nameIndex = 0; nameIndex < s_ArenaPlatformObjectNames.Length; nameIndex++)
            {
                var platformName = s_ArenaPlatformObjectNames[nameIndex];
                for (var i = 0; i < allTransforms.Length; i++)
                {
                    var candidate = allTransforms[i];
                    if (!IsNamedArenaPlatformCandidate(candidate, platformName))
                        continue;

                    if (preferredArenaRoot != null && candidate != preferredArenaRoot && !candidate.IsChildOf(preferredArenaRoot))
                        continue;

                    platformRoot = candidate;
                    return true;
                }
            }

            for (var nameIndex = 0; nameIndex < s_ArenaPlatformObjectNames.Length; nameIndex++)
            {
                var platformName = s_ArenaPlatformObjectNames[nameIndex];
                for (var i = 0; i < allTransforms.Length; i++)
                {
                    var candidate = allTransforms[i];
                    if (!IsNamedArenaPlatformCandidate(candidate, platformName))
                        continue;

                    platformRoot = candidate;
                    return true;
                }
            }

            return false;
        }

        static bool IsNamedArenaPlatformCandidate(Transform candidate, string platformName)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(platformName))
                return false;

            if (!string.Equals(candidate.name, platformName, StringComparison.OrdinalIgnoreCase))
                return false;

            return candidate.GetComponentInChildren<Collider>(true) != null ||
                   candidate.GetComponentInChildren<MeshFilter>(true) != null;
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
            teleportationArea.colliders.RemoveAll(collider => collider == null);
            var runtimeTeleportCollider = ConfigureRuntimeArenaTeleportCollider(arenaRoot, teleportationArea.gameObject);
            if (runtimeTeleportCollider != null && !teleportationArea.colliders.Contains(runtimeTeleportCollider))
                teleportationArea.colliders.Insert(0, runtimeTeleportCollider);

            for (var i = 0; i < m_ArenaSurfaceColliders.Count; i++)
            {
                var collider = m_ArenaSurfaceColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (!teleportationArea.colliders.Contains(collider))
                    teleportationArea.colliders.Add(collider);
            }

            DisableTeleportHelperColliders(teleportationArea, teleportationArea.colliders);
            IgnoreTeleportSurfaceCollisionWithPlayer(runtimeTeleportCollider);

            // Restrict teleport hits to walkable faces when the arena mesh includes walls.
            TrySetMemberValue(teleportationArea, "filterSelectionByHitNormal", true);
            TrySetMemberValue(teleportationArea, "m_FilterSelectionByHitNormal", true);
            TrySetMemberValue(teleportationArea, "upNormalToleranceDegrees", 45f);
            TrySetMemberValue(teleportationArea, "m_UpNormalToleranceDegrees", 45f);
        }

        static void DisableTeleportHelperColliders(TeleportationArea teleportationArea, IReadOnlyList<Collider> selectedColliders)
        {
            if (teleportationArea == null)
                return;

            var helperColliders = teleportationArea.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < helperColliders.Length; i++)
            {
                var collider = helperColliders[i];
                if (collider == null)
                    continue;

                if (ContainsCollider(selectedColliders, collider))
                {
                    collider.enabled = true;
                    continue;
                }

                collider.enabled = false;
            }
        }

        Collider ConfigureRuntimeArenaTeleportCollider(Transform arenaRoot, GameObject teleportAreaObject)
        {
            if (!TryGetArenaWorldBounds(arenaRoot, out var arenaBounds))
                return null;

            if (!(EnsureRuntimeArenaTeleportCollider(teleportAreaObject) is BoxCollider boxCollider))
                return null;

            var surfaceTransform = boxCollider.transform;
            if (teleportAreaObject != null && surfaceTransform.parent != teleportAreaObject.transform)
                surfaceTransform.SetParent(teleportAreaObject.transform, false);

            surfaceTransform.SetPositionAndRotation(
                new Vector3(
                    arenaBounds.center.x,
                    arenaBounds.min.y + ArenaTeleportSurfaceLift + ArenaTeleportSurfaceThickness * 0.5f,
                    arenaBounds.center.z),
                Quaternion.identity);
            surfaceTransform.localScale = Vector3.one;

            boxCollider.center = Vector3.zero;
            boxCollider.size = new Vector3(
                Mathf.Max(0.5f, arenaBounds.size.x * 0.94f),
                ArenaTeleportSurfaceThickness,
                Mathf.Max(0.5f, arenaBounds.size.z * 0.94f));
            boxCollider.isTrigger = false;
            boxCollider.enabled = true;
            return boxCollider;
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

        void IgnoreTeleportSurfaceCollisionWithPlayer(Collider teleportCollider)
        {
            if (teleportCollider == null || m_PlayerRoot == null)
                return;

            var playerColliders = m_PlayerRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < playerColliders.Length; i++)
            {
                var playerCollider = playerColliders[i];
                if (playerCollider == null || playerCollider == teleportCollider)
                    continue;

                Physics.IgnoreCollision(teleportCollider, playerCollider, true);
            }
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

            for (var i = 0; i < s_LegacyArenaPickupNames.Length; i++)
            {
                if (string.Equals(root.name, s_LegacyArenaPickupNames[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

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
            ConfigureBladeGrabInteractable(swordRoot, grabInteractable, gripCollider, bladeLayout.AttachLocalPosition, WeaponKind.Sword);

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

        void HideLegacyArenaCenterObjects()
        {
            for (var i = 0; i < s_LegacyArenaPickupNames.Length; i++)
            {
                var legacyObject = FindSceneObjectByExactName(s_LegacyArenaPickupNames[i]);
                if (legacyObject == null)
                    continue;

                legacyObject.gameObject.SetActive(false);
            }
        }

        void ResolveRunStartPose()
        {
            if (!TryBuildRunStartPose(out m_CustomRunStartPosition, out m_CustomRunStartRotation))
            {
                m_HasCustomRunStartPose = false;
                m_CustomRunStartRotation = Quaternion.identity;
                return;
            }

            m_HasCustomRunStartPose = true;
        }

        bool TryBuildRunStartPose(out Vector3 position, out Quaternion rotation)
        {
            position = RunStartHeadPosition + Vector3.up * PlayerEyeHeightOffsetMeters;
            rotation = RunStartHeadRotation;
            return true;
        }

        Vector3 GetResolvedRunStartHeadPosition()
        {
            return TryGetRunStartPose(out var position, out _)
                ? position
                : RunStartHeadPosition + Vector3.up * PlayerEyeHeightOffsetMeters;
        }

        Quaternion GetResolvedRunStartHeadRotation()
        {
            return TryGetRunStartPose(out _, out var rotation)
                ? rotation
                : RunStartHeadRotation;
        }

        void InitializeCardTables()
        {
            m_RuntimeCardTables.Clear();

            var sceneTables = FindObjectsByType<CardTableRuntime>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (sceneTables != null && sceneTables.Length > 0)
            {
                for (var i = 0; i < sceneTables.Length; i++)
                {
                    var table = sceneTables[i];
                    if (table == null)
                        continue;

                    m_RuntimeCardTables.Add(table);
                }
            }

            for (var i = 0; i < m_RuntimeCardTables.Count; i++)
            {
                var table = m_RuntimeCardTables[i];
                if (table == null)
                    continue;

                table.ClearCard();
            }

            if (m_RuntimeCardTables.Count == 0)
                CreateDefaultCardTables(0, 1);

            if (m_RuntimeCardTables.Count == 0)
                return;

            var populatedTableCount = 0;
            for (var i = 0; i < m_RuntimeCardTables.Count; i++)
            {
                var table = m_RuntimeCardTables[i];
                if (table == null)
                    continue;

                if (m_RuntimeSpawnedObjects.Contains(table.gameObject))
                    RepositionTableIfInsideSpawn(table, populatedTableCount);

                var firstChoice = m_RunProgressionController != null ? m_RunProgressionController.DrawNextCard() : null;
                var secondChoice = m_RunProgressionController != null ? m_RunProgressionController.DrawNextCard() : null;
                if (firstChoice == null && secondChoice == null)
                {
                    table.ClearCard();
                    continue;
                }

                if (table.MountCardChoices(firstChoice, secondChoice, m_RunProgressionController, this, m_MaxPhysicalGrabDistance))
                    populatedTableCount++;
            }
        }

        void CreateDefaultCardTables(int startIndex, int tableCount)
        {
            var startPosition = GetResolvedRunStartHeadPosition();
            var startRotation = GetResolvedRunStartHeadRotation();
            var forward = Vector3.ProjectOnPlane(startRotation * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            var right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;

            var fallbackPositions = new[]
            {
                startPosition + forward * 2.05f,
                startPosition + forward * 2.65f - right * 1.15f,
                startPosition + forward * 2.65f + right * 1.15f
            };

            for (var i = 0; i < tableCount && i < fallbackPositions.Length; i++)
            {
                var fallbackIndex = Mathf.Clamp(startIndex + i, 0, fallbackPositions.Length - 1);
                var tableObject = new GameObject($"Runtime Card Table {startIndex + i + 1}");
                tableObject.transform.position = ResolveGroundedSpawnPosition(fallbackPositions[fallbackIndex]);
                tableObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                RegisterRuntimeObject(tableObject);

                var table = tableObject.AddComponent<CardTableRuntime>();
                m_RuntimeCardTables.Add(table);
            }
        }

        void RepositionTableIfInsideSpawn(CardTableRuntime table, int index)
        {
            if (table == null)
                return;

            var startPosition = GetResolvedRunStartHeadPosition();
            var startRotation = GetResolvedRunStartHeadRotation();
            var tablePosition = table.transform.position;
            var horizontalOffset = new Vector2(tablePosition.x - startPosition.x, tablePosition.z - startPosition.z);
            if (horizontalOffset.magnitude > 1.25f)
                return;

            var forward = Vector3.ProjectOnPlane(startRotation * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var fallbackOffsets = new[]
            {
                forward * 2.05f,
                forward * 2.65f - right * 1.15f,
                forward * 2.65f + right * 1.15f
            };
            var offset = fallbackOffsets[Mathf.Clamp(index, 0, fallbackOffsets.Length - 1)];
            table.transform.position = ResolveGroundedSpawnPosition(startPosition + offset);
            table.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }


        static Transform FindSceneObjectByExactName(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                return null;

            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Transform bestCandidate = null;
            var bestDepth = int.MaxValue;
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null || !string.Equals(candidate.name, targetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var depth = GetSceneTransformDepth(candidate);
                if (depth >= bestDepth)
                    continue;

                bestCandidate = candidate;
                bestDepth = depth;
            }

            return bestCandidate;
        }

        static int GetSceneTransformDepth(Transform transformCandidate)
        {
            var depth = 0;
            while (transformCandidate != null)
            {
                depth++;
                transformCandidate = transformCandidate.parent;
            }

            return depth;
        }

        static bool TryGetSceneObjectWorldPosition(string objectName, out Vector3 position)
        {
            position = Vector3.zero;
            var transformCandidate = FindSceneObjectByExactName(objectName);
            if (transformCandidate == null)
                return false;

            position = transformCandidate.position;
            return true;
        }

        Vector3 ResolveGroundedSpawnPosition(Vector3 worldPosition)
        {
            var rayOrigin = worldPosition + Vector3.up * 3f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * Mathf.Max(0.02f, m_TeleportSpawnHeightOffset);

            worldPosition.y += Mathf.Max(0.02f, m_TeleportSpawnHeightOffset);
            return worldPosition;
        }

        public bool TryResolveArenaSpawnPosition(
            Vector3 desiredWorldPosition,
            float capsuleRadius,
            float capsuleHeight,
            out Vector3 resolvedWorldPosition)
        {
            resolvedWorldPosition = ResolveGroundedSpawnPosition(desiredWorldPosition);
            if (m_ArenaSurfaceColliders.Count == 0)
                ConfigureArenaSurface();

            if (m_ArenaSurfaceColliders.Count == 0 ||
                !TryFindArenaRoot(out var arenaRoot) ||
                !TryGetArenaWorldBounds(arenaRoot, out var arenaBounds))
            {
                return false;
            }

            var horizontalPadding = Mathf.Max(0.45f, capsuleRadius + 0.2f);
            var minX = arenaBounds.min.x + horizontalPadding;
            var maxX = arenaBounds.max.x - horizontalPadding;
            var minZ = arenaBounds.min.z + horizontalPadding;
            var maxZ = arenaBounds.max.z - horizontalPadding;
            if (minX >= maxX || minZ >= maxZ)
                return false;

            var probeY = arenaBounds.max.y + ArenaGroundSnapProbeHeight + Mathf.Max(0.5f, capsuleHeight);
            var probeDistance = Mathf.Max(arenaBounds.size.y + ArenaGroundSnapDistance + capsuleHeight + 2f, 6f);
            var clampedDesired = new Vector3(
                Mathf.Clamp(desiredWorldPosition.x, minX, maxX),
                probeY,
                Mathf.Clamp(desiredWorldPosition.z, minZ, maxZ));
            if (TryFindClearArenaSpawnCandidate(
                    clampedDesired,
                    probeDistance,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    capsuleRadius,
                    capsuleHeight,
                    m_ArenaSurfaceColliders,
                    out resolvedWorldPosition))
            {
                return true;
            }

            var fallbackPoint = new Vector3(arenaBounds.center.x, probeY, arenaBounds.center.z);
            if (TryFindClearArenaSpawnCandidate(
                    fallbackPoint,
                    probeDistance,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    capsuleRadius,
                    capsuleHeight,
                    m_ArenaSurfaceColliders,
                    out resolvedWorldPosition))
            {
                return true;
            }

            return false;
        }

        bool TryResolveArenaPlatformSpawnPosition(
            Vector3 desiredWorldPosition,
            float capsuleRadius,
            float capsuleHeight,
            out Vector3 resolvedWorldPosition)
        {
            resolvedWorldPosition = default;
            if (m_ArenaPlatformSurfaceColliders.Count == 0)
                ConfigureArenaSurface();

            if (m_ArenaPlatformSurfaceColliders.Count == 0 ||
                !TryGetBoundsFromColliders(m_ArenaPlatformSurfaceColliders, out var platformBounds))
            {
                return false;
            }

            var horizontalPadding = Mathf.Max(0.3f, capsuleRadius + 0.15f);
            var minX = platformBounds.min.x + horizontalPadding;
            var maxX = platformBounds.max.x - horizontalPadding;
            var minZ = platformBounds.min.z + horizontalPadding;
            var maxZ = platformBounds.max.z - horizontalPadding;
            if (minX >= maxX || minZ >= maxZ)
                return false;

            var probeY = platformBounds.max.y + ArenaGroundSnapProbeHeight + Mathf.Max(0.5f, capsuleHeight);
            var probeDistance = Mathf.Max(platformBounds.size.y + ArenaGroundSnapDistance + capsuleHeight + 2f, 6f);
            var clampedDesired = new Vector3(
                Mathf.Clamp(desiredWorldPosition.x, minX, maxX),
                probeY,
                Mathf.Clamp(desiredWorldPosition.z, minZ, maxZ));

            if (TryFindClearArenaSpawnCandidate(
                    clampedDesired,
                    probeDistance,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    capsuleRadius,
                    capsuleHeight,
                    m_ArenaPlatformSurfaceColliders,
                    out resolvedWorldPosition))
            {
                return true;
            }

            var fallbackPoint = new Vector3(platformBounds.center.x, probeY, platformBounds.center.z);
            if (TryFindClearArenaSpawnCandidate(
                    fallbackPoint,
                    probeDistance,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    capsuleRadius,
                    capsuleHeight,
                    m_ArenaPlatformSurfaceColliders,
                    out resolvedWorldPosition))
            {
                return true;
            }

            return TryFindClearPlatformColliderSpawnCandidate(
                capsuleRadius,
                capsuleHeight,
                out resolvedWorldPosition);
        }

        bool TryResolveWaveEnemySpawnPosition(
            Vector3 desiredWorldPosition,
            float capsuleRadius,
            float capsuleHeight,
            out Vector3 resolvedWorldPosition)
        {
            if (TryResolveArenaPlatformSpawnPosition(desiredWorldPosition, capsuleRadius, capsuleHeight, out resolvedWorldPosition))
            {
                ReserveEncounterSpace(resolvedWorldPosition, 6f, 3f, 6f);
                return true;
            }

            LogWavePlatformSpawnFailureOnce();
            resolvedWorldPosition = default;
            return false;
        }

        void LogWavePlatformSpawnFailureOnce()
        {
            if (m_HasLoggedWavePlatformSpawnFailure)
                return;

            m_HasLoggedWavePlatformSpawnFailure = true;
            var surfaceNames = new StringBuilder();
            for (var i = 0; i < m_ArenaPlatformSurfaceColliders.Count; i++)
            {
                var surface = m_ArenaPlatformSurfaceColliders[i];
                if (surface == null)
                    continue;

                if (surfaceNames.Length > 0)
                    surfaceNames.Append(", ");

                surfaceNames.Append(surface.name);
            }

            Debug.LogWarning(
                $"Wave spawn skipped because no clear point was found on the arena platform. Platform surfaces={m_ArenaPlatformSurfaceColliders.Count}; names=[{surfaceNames}]",
                this);
        }

        public bool TryResolveLooseGroundSpawnPosition(
            Vector3 desiredWorldPosition,
            float capsuleRadius,
            float capsuleHeight,
            out Vector3 resolvedWorldPosition)
        {
            resolvedWorldPosition = ResolveGroundedSpawnPosition(desiredWorldPosition);
            Collider supportCollider = null;

            var probeOrigin = desiredWorldPosition + Vector3.up * Mathf.Max(2f, capsuleHeight + 1.5f);
            var probeDistance = Mathf.Max(6f, capsuleHeight + 6f);
            if (Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out var hit,
                    probeDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                resolvedWorldPosition = hit.point + Vector3.up * Mathf.Max(ArenaGroundSnapYOffset, m_TeleportSpawnHeightOffset);
                supportCollider = hit.collider;
            }

            return HasLooseSpawnClearance(resolvedWorldPosition, capsuleRadius, capsuleHeight, supportCollider);
        }

        public bool TryResolveCombatEnemySpawnPosition(
            Vector3 desiredWorldPosition,
            float capsuleRadius,
            float capsuleHeight,
            out Vector3 resolvedWorldPosition)
        {
            if (TryResolveArenaSpawnPosition(desiredWorldPosition, capsuleRadius, capsuleHeight, out resolvedWorldPosition) ||
                TryResolveLooseGroundSpawnPosition(desiredWorldPosition, capsuleRadius, capsuleHeight, out resolvedWorldPosition))
            {
                ReserveEncounterSpace(resolvedWorldPosition, 6f, 3f, 6f);
                return true;
            }

            resolvedWorldPosition = default;
            return false;
        }

        bool TryFindClearPlatformColliderSpawnCandidate(
            float capsuleRadius,
            float capsuleHeight,
            out Vector3 resolvedWorldPosition)
        {
            resolvedWorldPosition = default;
            if (m_ArenaPlatformSurfaceColliders.Count == 0)
                return false;

            var surfaceCount = m_ArenaPlatformSurfaceColliders.Count;
            var maxAttempts = Mathf.Max(EnemySpawnResolutionAttempts * 6, surfaceCount * 8);
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var surface = m_ArenaPlatformSurfaceColliders[attempt % surfaceCount];
                if (surface == null || !surface.enabled || surface.isTrigger)
                    continue;

                var bounds = surface.bounds;
                var horizontalPadding = Mathf.Max(0.18f, capsuleRadius + 0.08f);
                var minX = bounds.min.x + horizontalPadding;
                var maxX = bounds.max.x - horizontalPadding;
                var minZ = bounds.min.z + horizontalPadding;
                var maxZ = bounds.max.z - horizontalPadding;
                if (minX >= maxX || minZ >= maxZ)
                    continue;

                var samplePoint = GetPlatformColliderSpawnSamplePoint(bounds, attempt, minX, maxX, minZ, maxZ);
                var probeDistance = Mathf.Max(bounds.size.y + ArenaGroundSnapDistance + capsuleHeight + 2f, 6f);
                Vector3 groundedPosition;
                if (TryGetArenaGroundHit(
                        samplePoint,
                        probeDistance,
                        m_ArenaPlatformSurfaceColliders,
                        float.PositiveInfinity,
                        out var hit))
                {
                    groundedPosition = hit.point + Vector3.up * Mathf.Max(ArenaGroundSnapYOffset, m_TeleportSpawnHeightOffset);
                }
                else
                {
                    continue;
                }

                if (HasSpawnClearance(groundedPosition, capsuleRadius, capsuleHeight))
                {
                    resolvedWorldPosition = groundedPosition;
                    return true;
                }
            }

            return false;
        }

        static bool IsFiniteVector3(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        bool TryFindClearArenaSpawnCandidate(
            Vector3 anchorPoint,
            float probeDistance,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float capsuleRadius,
            float capsuleHeight,
            IReadOnlyList<Collider> allowedSurfaceColliders,
            out Vector3 resolvedWorldPosition)
        {
            resolvedWorldPosition = default;
            var maxAttempts = ReferenceEquals(allowedSurfaceColliders, m_ArenaPlatformSurfaceColliders)
                ? EnemySpawnResolutionAttempts * 3
                : EnemySpawnResolutionAttempts;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var samplePoint = GetArenaSpawnSamplePoint(anchorPoint, attempt, minX, maxX, minZ, maxZ);
                if (!TryGetArenaGroundHit(
                        samplePoint,
                        probeDistance,
                        allowedSurfaceColliders,
                        float.PositiveInfinity,
                        out var hit))
                {
                    continue;
                }

                var groundedPosition = hit.point + Vector3.up * Mathf.Max(ArenaGroundSnapYOffset, m_TeleportSpawnHeightOffset);
                if (!HasSpawnClearance(groundedPosition, capsuleRadius, capsuleHeight))
                    continue;

                resolvedWorldPosition = groundedPosition;
                return true;
            }

            return false;
        }

        static Vector3 GetPlatformColliderSpawnSamplePoint(
            Bounds bounds,
            int attempt,
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            if (attempt <= 0)
                return new Vector3(bounds.center.x, bounds.max.y + ArenaGroundSnapProbeHeight + 1.5f, bounds.center.z);

            var normalizedAttempt = Mathf.Clamp01((attempt - 1) / (float)Mathf.Max(1, EnemySpawnResolutionAttempts * 6 - 2));
            var angle = (attempt - 1) * 137.50776f * Mathf.Deg2Rad;
            var radiusX = Mathf.Lerp(0.15f, Mathf.Max(0.15f, (maxX - minX) * 0.5f), normalizedAttempt);
            var radiusZ = Mathf.Lerp(0.15f, Mathf.Max(0.15f, (maxZ - minZ) * 0.5f), normalizedAttempt);
            var x = Mathf.Clamp(bounds.center.x + Mathf.Cos(angle) * radiusX, minX, maxX);
            var z = Mathf.Clamp(bounds.center.z + Mathf.Sin(angle) * radiusZ, minZ, maxZ);
            return new Vector3(x, bounds.max.y + ArenaGroundSnapProbeHeight + 1.5f, z);
        }

        static Vector3 GetArenaSpawnSamplePoint(Vector3 clampedDesired, int attempt, float minX, float maxX, float minZ, float maxZ)
        {
            if (attempt <= 0)
                return clampedDesired;

            var normalizedAttempt = Mathf.Clamp01((attempt - 1) / (float)Mathf.Max(1, EnemySpawnResolutionAttempts - 2));
            var angle = (attempt - 1) * 137.50776f * Mathf.Deg2Rad;
            var radius = Mathf.Lerp(0.35f, EnemySpawnRetryRadius, normalizedAttempt);
            var x = Mathf.Clamp(clampedDesired.x + Mathf.Cos(angle) * radius, minX, maxX);
            var z = Mathf.Clamp(clampedDesired.z + Mathf.Sin(angle) * radius, minZ, maxZ);
            return new Vector3(x, clampedDesired.y, z);
        }

        bool HasSpawnClearance(Vector3 rootPosition, float capsuleRadius, float capsuleHeight)
        {
            return CountSpawnBlockingColliders(rootPosition, capsuleRadius, capsuleHeight) == 0;
        }

        void RegisterEnemyKillZoneGrace(CapsuleEnemy enemy)
        {
            if (enemy == null)
                return;

            m_EnemyKillZoneGraceUntil[enemy] = Time.unscaledTime + EnemySpawnKillZoneGraceSeconds;
        }

        bool IsEnemyKillZoneGraceActive(CapsuleEnemy enemy)
        {
            if (enemy == null)
                return false;

            if (!m_EnemyKillZoneGraceUntil.TryGetValue(enemy, out var graceUntil))
                return false;

            if (Time.unscaledTime < graceUntil)
                return true;

            m_EnemyKillZoneGraceUntil.Remove(enemy);
            return false;
        }

        bool HasLooseSpawnClearance(Vector3 rootPosition, float capsuleRadius, float capsuleHeight, Collider supportCollider)
        {
            var radius = Mathf.Max(0.05f, capsuleRadius + EnemySpawnClearanceSkin);
            var height = Mathf.Max(radius * 2f + 0.1f, capsuleHeight);
            var cylindricalHeight = Mathf.Max(0.01f, height - radius * 2f);
            var bottom = rootPosition + Vector3.up * (radius + 0.02f);
            var top = bottom + Vector3.up * cylindricalHeight;
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                s_SpawnClearanceBuffer,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < overlapCount; i++)
            {
                var collider = s_SpawnClearanceBuffer[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (collider == supportCollider)
                    continue;

                if (m_RuntimeArenaTeleportCollider != null && collider == m_RuntimeArenaTeleportCollider)
                    continue;

                if (collider.bounds.max.y <= rootPosition.y + 0.06f)
                    continue;

                if (collider.GetComponentInParent<TeleportationArea>() != null ||
                    collider.GetComponentInParent<ArenaOpeningEncounter>() != null ||
                    collider.GetComponentInParent<TriggerSpawner>() != null)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        int CountSpawnBlockingColliders(Vector3 rootPosition, float capsuleRadius, float capsuleHeight)
        {
            var radius = Mathf.Max(0.05f, capsuleRadius + EnemySpawnClearanceSkin);
            var height = Mathf.Max(radius * 2f + 0.1f, capsuleHeight);
            var cylindricalHeight = Mathf.Max(0.01f, height - radius * 2f);
            var bottom = rootPosition + Vector3.up * (radius + 0.02f);
            var top = bottom + Vector3.up * cylindricalHeight;
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                s_SpawnClearanceBuffer,
                ~0,
                QueryTriggerInteraction.Ignore);
            var blockingCount = 0;

            for (var i = 0; i < overlapCount; i++)
            {
                var collider = s_SpawnClearanceBuffer[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                if (m_PlayerRoot != null && collider.transform.IsChildOf(m_PlayerRoot))
                    continue;

                if (ContainsCollider(m_ArenaSurfaceColliders, collider))
                    continue;

                if (ContainsCollider(m_ArenaPlatformSurfaceColliders, collider))
                    continue;

                if (m_RuntimeArenaTeleportCollider != null && collider == m_RuntimeArenaTeleportCollider)
                    continue;

                if (collider.GetComponentInParent<CapsuleEnemy>() == null &&
                    (collider.GetComponentInParent<XRBaseInteractable>() != null ||
                     collider.GetComponentInParent<XRGrabInteractable>() != null))
                {
                    continue;
                }

                if (collider.GetComponentInParent<TeleportationArea>() != null ||
                    collider.GetComponentInParent<ArenaOpeningEncounter>() != null ||
                    collider.GetComponentInParent<TriggerSpawner>() != null)
                {
                    continue;
                }

                blockingCount++;
            }

            return blockingCount;
        }

        void MovePlayerToTeleportAnchor()
        {
            if (m_PlayerRoot == null || m_PlayerCamera == null)
                return;

            if (TryGetRunStartPose(out var runStartHeadPosition, out var runStartHeadRotation))
            {
                var currentHeadPosition = m_PlayerCamera.transform.position;
                var currentForward = Vector3.ProjectOnPlane(m_PlayerCamera.transform.forward, Vector3.up);
                var targetForward = Vector3.ProjectOnPlane(runStartHeadRotation * Vector3.forward, Vector3.up);
                if (currentForward.sqrMagnitude > 0.0001f && targetForward.sqrMagnitude > 0.0001f)
                {
                    var yawDelta = Vector3.SignedAngle(currentForward.normalized, targetForward.normalized, Vector3.up);
                    m_PlayerRoot.RotateAround(currentHeadPosition, Vector3.up, yawDelta);
                }

                var rootToHead = m_PlayerCamera.transform.position - m_PlayerRoot.position;
                m_PlayerRoot.position = runStartHeadPosition - rootToHead;

                if (!m_KillZoneDerivedFromArena)
                {
                    m_KillZoneCenter = runStartHeadPosition;
                    m_HasKillZoneCenter = true;
                }

                EnsureRuntimeKillZoneBoundary();
                return;
            }

            if (!TryGetTeleportStartPose(out var destinationPosition, out var destinationRotation))
            {
                return;
            }

            var teleportCurrentHeadPosition = m_PlayerCamera.transform.position;
            var teleportCurrentForward = Vector3.ProjectOnPlane(m_PlayerCamera.transform.forward, Vector3.up);
            var teleportTargetForward = Vector3.ProjectOnPlane(destinationRotation * Vector3.forward, Vector3.up);
            if (teleportCurrentForward.sqrMagnitude > 0.0001f && teleportTargetForward.sqrMagnitude > 0.0001f)
            {
                var yawDelta = Vector3.SignedAngle(teleportCurrentForward.normalized, teleportTargetForward.normalized, Vector3.up);
                m_PlayerRoot.RotateAround(teleportCurrentHeadPosition, Vector3.up, yawDelta);
            }

            var teleportRootToHead = m_PlayerCamera.transform.position - m_PlayerRoot.position;
            var horizontalHeadOffset = new Vector3(teleportRootToHead.x, 0f, teleportRootToHead.z);
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

        bool TryGetRunStartPose(out Vector3 position, out Quaternion rotation)
        {
            position = m_CustomRunStartPosition;
            rotation = m_CustomRunStartRotation;
            return m_HasCustomRunStartPose;
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
            ExpandKillZoneToInclude(GetResolvedRunStartHeadPosition(), 4f, 2f, 3f);
            m_HasKillZoneCenter = true;
            m_KillZoneDerivedFromArena = true;
            EnsureRuntimeKillZoneBoundary();
            return true;
        }

        void ExpandKillZoneToInclude(Vector3 worldPosition, float horizontalPadding, float belowPadding, float abovePadding)
        {
            var horizontalOffset = new Vector2(worldPosition.x - m_KillZoneCenter.x, worldPosition.z - m_KillZoneCenter.z).magnitude;
            m_KillZoneHorizontalRadius = Mathf.Max(m_KillZoneHorizontalRadius, horizontalOffset + Mathf.Max(0.5f, horizontalPadding));

            var belowDistance = m_KillZoneCenter.y - worldPosition.y;
            if (belowDistance >= 0f)
                m_KillZoneBelowCenter = Mathf.Max(m_KillZoneBelowCenter, belowDistance + Mathf.Max(0.5f, belowPadding));

            var aboveDistance = worldPosition.y - m_KillZoneCenter.y;
            if (aboveDistance >= 0f)
                m_KillZoneAboveCenter = Mathf.Max(m_KillZoneAboveCenter, aboveDistance + Mathf.Max(0.5f, abovePadding));
        }

        public void ReserveEncounterSpace(
            Vector3 worldPosition,
            float horizontalPadding = 8f,
            float belowPadding = 3f,
            float abovePadding = 8f)
        {
            if (!m_HasKillZoneCenter)
                InitializeKillZoneCenter();

            if (!m_HasKillZoneCenter)
                return;

            ExpandKillZoneToInclude(worldPosition, horizontalPadding, belowPadding, abovePadding);
            EnsureRuntimeKillZoneBoundary();
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

            if (m_IsGameOver || m_IsRestarting || !m_HasKillZoneCenter)
                return;

            if (m_PlayerDamageReceiver != null && !m_PlayerDamageReceiver.IsDead)
            {
                var shouldValidatePlayerBounds =
                    m_RunStarted &&
                    Time.unscaledTime >= m_SpawnProtectionUntilTime &&
                    (m_WristChainIntroController == null || !m_WristChainIntroController.IsActive);

                if (shouldValidatePlayerBounds)
                {
                    var playerPosition = m_PlayerCamera != null
                        ? m_PlayerCamera.transform.position
                        : m_PlayerDamageReceiver.transform.position;

                    if (IsOutsideKillZone(playerPosition))
                    {
                        if (!TryKeepGroundedPlayerInsideKillZone(playerPosition))
                            m_PlayerDamageReceiver.ForceKill();
                    }
                }
            }

            m_ActiveWaveEnemies.RemoveAll(enemy => enemy == null);
            for (var i = 0; i < m_ActiveWaveEnemies.Count; i++)
            {
                var enemy = m_ActiveWaveEnemies[i];
                if (enemy == null)
                    continue;

                if (IsEnemyKillZoneGraceActive(enemy))
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
            if (playerDamageReceiver == null ||
                playerDamageReceiver.IsDead ||
                m_IsRestarting ||
                Time.unscaledTime < m_SpawnProtectionUntilTime ||
                !m_RunStarted ||
                (m_WristChainIntroController != null && m_WristChainIntroController.IsActive))
            {
                return;
            }

            var playerPosition = m_PlayerCamera != null
                ? m_PlayerCamera.transform.position
                : playerDamageReceiver.transform.position;

            if (!IsOutsideKillZone(playerPosition))
                return;

            if (TryKeepGroundedPlayerInsideKillZone(playerPosition))
                return;

            playerDamageReceiver.ForceKill();
        }

        void OnKillZoneEnemyExited(CapsuleEnemy enemy)
        {
            if (IsEnemyKillZoneGraceActive(enemy))
                return;

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
                if (enemy == null || IsEnemyKillZoneGraceActive(enemy) || !IsOutsideKillZone(enemy.transform.position))
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
                    if (IsEnemyKillZoneGraceActive(enemy))
                        continue;

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

        bool TryKeepGroundedPlayerInsideKillZone(Vector3 playerPosition)
        {
            if (!m_HasKillZoneCenter)
                return false;

            if (playerPosition.y < m_KillZoneCenter.y - Mathf.Max(0.5f, m_KillZoneBelowCenter))
                return false;

            var probeOrigin = playerPosition + Vector3.up * PlayerKillZoneGroundProbeHeight;
            if (!Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out var groundHit,
                    PlayerKillZoneGroundProbeDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (groundHit.collider == null ||
                groundHit.collider.GetComponentInParent<RuntimeKillZoneBoundary>() != null ||
                groundHit.collider.GetComponentInParent<PlayerDamageReceiver>() != null)
            {
                return false;
            }

            if (Vector3.Angle(groundHit.normal, Vector3.up) > PlayerKillZoneMaxGroundSlopeAngle)
                return false;

            if (playerPosition.y - groundHit.point.y > PlayerKillZoneMaxGroundDrop)
                return false;

            ExpandKillZoneToInclude(
                groundHit.point,
                PlayerKillZoneExpansionHorizontalPadding,
                PlayerKillZoneExpansionBelowPadding,
                PlayerKillZoneExpansionAbovePadding);
            ExpandKillZoneToInclude(
                playerPosition,
                PlayerKillZoneExpansionHorizontalPadding,
                PlayerKillZoneExpansionBelowPadding,
                PlayerKillZoneExpansionAbovePadding);
            EnsureRuntimeKillZoneBoundary();
            return true;
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
            ConfigureBladeGrabInteractable(root, grabInteractable, gripCollider, bladeLayout.AttachLocalPosition, WeaponKind.Dagger);

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
            var chainDefinition = GetWeaponDefinitionOrDefault(WeaponKind.Chain, 0.92f);
            rigidbody.mass = chainDefinition.RigidbodyMass;
            rigidbody.linearDamping = 0.018f;
            rigidbody.angularDamping = 0.028f;
            rigidbody.solverIterations = 18;
            rigidbody.solverVelocityIterations = 8;
            rigidbody.maxAngularVelocity = 420f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: true,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;
            ConfigureChainHoldFollow(grabInteractable);

            var attachPoint = new GameObject("Attach Point");
            attachPoint.transform.SetParent(root.transform, false);
            attachPoint.transform.localPosition = new Vector3(0f, 0f, -0.055f);
            attachPoint.transform.localRotation = Quaternion.identity;
            grabInteractable.attachTransform = attachPoint.transform;

            var riggedChainWeapon = InitializeRuntimeChainWeapon(root, rigidbody, collider, chainDefinition);

            SetupWallMountedPickup(root, grabInteractable, riggedChainWeapon, keepKinematicWhileHeld: true);
        }

        void CreateWallShieldPickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            var grabInteractable = ConfigureShieldPickup(root);

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

        void SetupLoosePickup(
            GameObject pickupRoot,
            XRGrabInteractable grabInteractable,
            RiggedChainWeapon riggedChainWeapon = null,
            bool keepKinematicWhileHeld = false)
        {
            var mountedPickup = EnsureRuntimeMountedPickup(pickupRoot, riggedChainWeapon, keepKinematicWhileHeld);
            if (mountedPickup == null || grabInteractable == null)
                return;

            mountedPickup.SetState(RuntimeMountedPickupState.Dropped);
            AddPickupSelectListeners(grabInteractable, mountedPickup);
        }

        public void ShowRuntimeBanner(string message, float durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            m_CombatHud?.ShowBanner(message, durationSeconds);
        }

        public void SpawnCardRewardWeapon(WeaponKind weaponKind, Vector3 worldPosition, Quaternion worldRotation)
        {
            var flatForward = Vector3.ProjectOnPlane(worldRotation * Vector3.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;

            var spawnPosition = worldPosition + flatForward * 0.14f - Vector3.up * 0.04f;
            if (TryResolveLooseGroundSpawnPosition(spawnPosition, 0.18f, 0.45f, out var groundedPosition) &&
                Mathf.Abs(groundedPosition.y - spawnPosition.y) <= 0.2f)
            {
                spawnPosition = groundedPosition;
            }

            var groundedRotation = Quaternion.LookRotation(flatForward, Vector3.up);

            switch (weaponKind)
            {
                case WeaponKind.Chain:
                    CreateLooseChainPickup("Reward Chain", spawnPosition, groundedRotation);
                    break;
                case WeaponKind.Dagger:
                    CreateLooseBladePickup(
                        "Reward Dagger A",
                        spawnPosition + Vector3.left * 0.08f,
                        groundedRotation,
                        DaggerModelResourcePath,
                        0.6f,
                        WeaponKind.Dagger);
                    CreateLooseBladePickup(
                        "Reward Dagger B",
                        spawnPosition + Vector3.right * 0.08f,
                        groundedRotation,
                        DaggerModelResourcePath,
                        0.6f,
                        WeaponKind.Dagger);
                    break;
                case WeaponKind.Flintlock:
                    TryCreateLooseFlintlockPickup("Reward Flintlock", spawnPosition, groundedRotation);
                    break;
                case WeaponKind.Mace:
                    CreateLooseBladePickup("Reward Mace", spawnPosition, groundedRotation, MaceModelResourcePath, 0.78f, WeaponKind.Mace);
                    break;
                case WeaponKind.Shield:
                    CreateLooseShieldPickup("Reward Shield", spawnPosition, groundedRotation);
                    break;
                case WeaponKind.Spear:
                    CreateLooseBladePickup("Reward Spear", spawnPosition, groundedRotation, SpearModelResourcePath, 1.18f, WeaponKind.Spear);
                    break;
                case WeaponKind.Sword:
                    CreateLooseBladePickup("Reward Sword", spawnPosition, groundedRotation, SwordModelResourcePath, 0.95f, WeaponKind.Sword);
                    break;
            }

            m_RunProgressionController?.RegisterWeaponAcquired(weaponKind);
        }

        void CreateLooseBladePickup(
            string pickupName,
            Vector3 worldPosition,
            Quaternion worldRotation,
            string resourcePath,
            float targetLength,
            WeaponKind weaponKind)
        {
            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            var weaponDefinition = GetWeaponDefinitionOrDefault(weaponKind, targetLength);
            if (!TryCreateImportedBladeVisual(resourcePath, weaponDefinition.PickupLength, weaponKind, root.transform, out var bladeLayout))
                CreateFallbackBladeVisual(root.transform, out bladeLayout);

            CreateBladeColliders(root.transform, bladeLayout, out var gripCollider, out var bodyCollider);
            var damageEmitter = ConfigureWeaponDamageProfile(root.transform, weaponKind, weaponDefinition, bladeLayout, bodyCollider);

            var rigidbody = root.AddComponent<Rigidbody>();
            ConfigureBladePickupRigidbody(rigidbody, weaponDefinition);

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureBladeGrabInteractable(root, grabInteractable, gripCollider, bladeLayout.AttachLocalPosition, weaponKind);
            grabInteractable.throwOnDetach = weaponDefinition.ThrowOnDetach;
            ConfigureWeaponDamageEmitter(damageEmitter, weaponKind, weaponDefinition);
            ConfigureWeaponSpecialization(root, weaponKind, weaponDefinition, damageEmitter, grabInteractable);
            ConfigureRuntimeWeaponModifiers(root, weaponKind);
            SetupLoosePickup(root, grabInteractable);
        }

        bool TryCreateImportedBladeVisual(
            string resourcePath,
            float targetLength,
            WeaponKind weaponKind,
            Transform parent,
            out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();
            if (!TryInstantiateRuntimeModelVisual(resourcePath, "Weapon Visual", parent, out var visualRoot))
                return false;

            AlignLongestVisualAxisToForward(visualRoot.transform);
            UniformScaleVisualToLength(visualRoot.transform, targetLength);
            var gripEnd = ResolveRuntimeWeaponGripEnd(weaponKind);
            var builtLayout = weaponKind == WeaponKind.Mace
                ? TryBuildMacePickupLayout(visualRoot.transform, gripEnd, out bladeLayout)
                : TryBuildBladePickupLayout(visualRoot.transform, alignGripToOrigin: true, gripEnd, out bladeLayout);
            if (!builtLayout &&
                !TryBuildBladePickupLayout(visualRoot.transform, alignGripToOrigin: true, gripEnd, out bladeLayout))
            {
                bladeLayout = CreateDefaultBladePickupLayout();
            }
            return true;
        }

        void CreateLooseShieldPickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            var grabInteractable = ConfigureShieldPickup(root);
            SetupLoosePickup(root, grabInteractable);
        }

        XRGrabInteractable ConfigureShieldPickup(GameObject root)
        {
            if (root == null)
                return null;

            var weaponDefinition = GetWeaponDefinitionOrDefault(WeaponKind.Shield, 0.65f);
            CreateShieldVisual(root.transform, weaponDefinition.PickupLength, out var shieldLayout);

            var collider = root.AddComponent<BoxCollider>();
            collider.center = shieldLayout.ColliderCenter;
            collider.size = shieldLayout.ColliderSize;
            collider.contactOffset = 0.0065f;

            var rigidbody = root.AddComponent<Rigidbody>();
            ConfigureShieldRigidbody(rigidbody, weaponDefinition);

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureShieldGrabInteractable(root, grabInteractable, collider, shieldLayout);

            EnsureSwingWeapon(
                root,
                makeTriggerCollider: false,
                forceKinematic: false,
                defaultRadius: Mathf.Max(0.18f, Mathf.Max(shieldLayout.ColliderSize.x, shieldLayout.ColliderSize.y) * 0.45f));

            var damageDealer = root.GetComponent<SwingDamageDealer>();
            if (damageDealer != null)
            {
                damageDealer.Configure(
                    weaponDefinition.BaseDamage,
                    weaponDefinition.MinSwingSpeed,
                    weaponDefinition.MaxSwingSpeedForScaling,
                    weaponDefinition.HitCooldownSeconds,
                    weaponDefinition.ProximityFallbackRadius);
            }

            ConfigureRuntimeWeaponModifiers(root, WeaponKind.Shield, requiresShieldUnlock: true);
            return grabInteractable;
        }

        void CreateShieldVisual(Transform parent, float targetLength, out ShieldPickupLayout shieldLayout)
        {
            if (TryInstantiateRuntimeModelVisual(ShieldModelResourcePath, "Shield Visual", parent, out var visualRoot))
            {
                RuntimeCombatModelMaterialBinder.Apply(visualRoot, GetOrCreateShieldMaterial());
                UniformScaleVisualToLength(visualRoot.transform, targetLength);
                CenterVisualAndPlaceBackEdge(visualRoot.transform, -0.02f);
            }
            else
            {
                CreateFallbackShieldVisual(parent);
            }

            if (!TryGetVisualBoundsRelativeToReference(parent, parent, out var shieldBounds))
                shieldBounds = new Bounds(new Vector3(0f, 0f, -0.02f), new Vector3(0.42f, 0.42f, 0.08f));

            shieldLayout = BuildShieldPickupLayoutFromBounds(shieldBounds);
        }

        void CreateFallbackShieldVisual(Transform parent)
        {
            var shieldDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shieldDisk.name = "Shield Disk";
            shieldDisk.transform.SetParent(parent, false);
            shieldDisk.transform.localPosition = Vector3.zero;
            shieldDisk.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shieldDisk.transform.localScale = new Vector3(0.3f, 0.032f, 0.3f);
            ApplyMaterial(shieldDisk, GetOrCreateShieldMaterial());

            var shieldHandle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shieldHandle.name = "Shield Handle";
            shieldHandle.transform.SetParent(parent, false);
            shieldHandle.transform.localPosition = new Vector3(0f, 0f, -0.055f);
            shieldHandle.transform.localRotation = Quaternion.identity;
            shieldHandle.transform.localScale = new Vector3(0.12f, 0.042f, 0.04f);
            ApplyMaterial(shieldHandle, GetOrCreatePickupMaterial());

            StripImportedColliders(shieldDisk);
            StripImportedColliders(shieldHandle);
        }

        static ShieldPickupLayout BuildShieldPickupLayoutFromBounds(Bounds bounds)
        {
            var colliderSize = new Vector3(
                Mathf.Max(0.36f, bounds.size.x + 0.04f),
                Mathf.Max(0.36f, bounds.size.y + 0.04f),
                Mathf.Max(0.08f, bounds.size.z + 0.04f));

            return new ShieldPickupLayout
            {
                ColliderCenter = bounds.center,
                ColliderSize = colliderSize,
                AttachLocalPosition = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 0.035f),
                AttachLocalRotation = Quaternion.identity
            };
        }

        static void ConfigureShieldRigidbody(Rigidbody rigidbody, WeaponDefinition weaponDefinition)
        {
            if (rigidbody == null)
                return;

            rigidbody.mass = weaponDefinition != null ? weaponDefinition.RigidbodyMass : 2.5f;
            rigidbody.linearDamping = weaponDefinition != null ? weaponDefinition.RigidbodyLinearDamping : 0.2f;
            rigidbody.angularDamping = weaponDefinition != null ? weaponDefinition.RigidbodyAngularDamping : 0.22f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void ConfigureShieldGrabInteractable(
            GameObject pickupRoot,
            XRGrabInteractable grabInteractable,
            Collider shieldCollider,
            ShieldPickupLayout shieldLayout)
        {
            if (pickupRoot == null || grabInteractable == null)
                return;

            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: false,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;
            ConfigureWeaponHoldFollow(grabInteractable);
            grabInteractable.colliders.Clear();
            if (shieldCollider != null)
                grabInteractable.colliders.Add(shieldCollider);

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
            attachTransform.localPosition = shieldLayout.AttachLocalPosition;
            attachTransform.localRotation = shieldLayout.AttachLocalRotation;
            grabInteractable.attachTransform = attachTransform;
        }

        void ResolveChainGripPose(Transform root, out Vector3 gripCenter, out float gripRadius, out float gripHeight)
        {
            gripCenter = new Vector3(0f, 0f, 0.02f);
            gripRadius = 0.032f;
            gripHeight = 0.18f;

            if (root == null || !TryGetNamedVisualBoundsRelativeToReference(root, root, "handcuff", out var cuffBounds))
                return;

            gripCenter = cuffBounds.center;
            gripRadius = Mathf.Clamp(Mathf.Max(cuffBounds.size.x, cuffBounds.size.y) * 0.32f, 0.025f, 0.045f);
            gripHeight = Mathf.Max(gripRadius * 2.2f, cuffBounds.size.z * 0.92f);
        }

        void CreateLooseChainPickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var root = new GameObject(pickupName);
            root.transform.position = worldPosition;
            root.transform.rotation = worldRotation;
            RegisterRuntimeObject(root);

            var hasImportedChainVisual = TryCreateChainVisual(root.transform, out _, showOnlyNail: false);
            if (!hasImportedChainVisual)
            {
                var handleVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                handleVisual.name = "Chain Handle";
                handleVisual.transform.SetParent(root.transform, false);
                handleVisual.transform.localPosition = new Vector3(0f, 0f, -0.055f);
                handleVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                handleVisual.transform.localScale = new Vector3(0.023f, 0.09f, 0.023f);
                ApplyMaterial(handleVisual, GetOrCreatePickupMaterial());

                var handleCollider = handleVisual.GetComponent<Collider>();
                if (handleCollider != null)
                    Destroy(handleCollider);

                CreateFallbackChainVisual(root.transform);
            }

            ResolveChainGripPose(root.transform, out var gripCenter, out var gripRadius, out var gripHeight);
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 2;
            collider.center = gripCenter;
            collider.radius = gripRadius;
            collider.height = gripHeight;
            collider.contactOffset = 0.0065f;

            var rigidbody = root.AddComponent<Rigidbody>();
            var chainDefinition = GetWeaponDefinitionOrDefault(WeaponKind.Chain, 0.92f);
            rigidbody.mass = chainDefinition.RigidbodyMass;
            rigidbody.linearDamping = 0.018f;
            rigidbody.angularDamping = 0.028f;
            rigidbody.solverIterations = 18;
            rigidbody.solverVelocityIterations = 8;
            rigidbody.maxAngularVelocity = 420f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: true,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;
            ConfigureChainHoldFollow(grabInteractable);

            var attachPoint = new GameObject(AttachPointObjectName);
            attachPoint.transform.SetParent(root.transform, false);
            attachPoint.transform.localPosition = gripCenter;
            grabInteractable.attachTransform = attachPoint.transform;

            var riggedChainWeapon = InitializeRuntimeChainWeapon(root, rigidbody, collider, chainDefinition);

            ConfigureRuntimeWeaponModifiers(root, WeaponKind.Chain);
            SetupLoosePickup(root, grabInteractable, riggedChainWeapon, keepKinematicWhileHeld: true);
        }

        RiggedChainWeapon InitializeRuntimeChainWeapon(
            GameObject pickupRoot,
            Rigidbody rigidbody,
            Collider holdCollider,
            WeaponDefinition chainDefinition)
        {
            if (pickupRoot == null || rigidbody == null || holdCollider == null || chainDefinition == null)
                return null;

            var riggedChainWeapon = pickupRoot.AddComponent<RiggedChainWeapon>();
            var initialized = riggedChainWeapon.Initialize(
                rigidbody,
                holdCollider,
                boneStep: 1,
                segmentRadius: 0.016f,
                tipRadius: 0.05f,
                segmentMass: 0.03f,
                baseDamage: chainDefinition.BaseDamage,
                minSwingSpeed: chainDefinition.MinSwingSpeed,
                maxSwingSpeedForScaling: chainDefinition.MaxSwingSpeedForScaling,
                hitCooldownSeconds: chainDefinition.HitCooldownSeconds,
                rootSpring: ChainJointRootSpring,
                segmentSpring: ChainJointSegmentSpring,
                damper: ChainJointDamper);

            if (initialized)
                return riggedChainWeapon;

            EnsureSwingWeapon(pickupRoot, makeTriggerCollider: false, forceKinematic: false, defaultRadius: 0.1f);
            var fallbackDamageDealer = pickupRoot.GetComponent<SwingDamageDealer>();
            if (fallbackDamageDealer != null)
            {
                fallbackDamageDealer.Configure(
                    chainDefinition.BaseDamage,
                    chainDefinition.MinSwingSpeed,
                    chainDefinition.MaxSwingSpeedForScaling,
                    chainDefinition.HitCooldownSeconds,
                    chainDefinition.ProximityFallbackRadius);
            }

            return riggedChainWeapon;
        }

        bool TryCreateLooseFlintlockPickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var template = FindSceneObjectByExactName("Flintlock");
            if (template == null)
                return TryCreateRuntimeFlintlockPickup(pickupName, worldPosition, worldRotation);

            var root = Instantiate(template.gameObject);
            root.name = pickupName;
            root.transform.SetPositionAndRotation(worldPosition, worldRotation);
            root.SetActive(true);
            RegisterRuntimeObject(root);
            StripImportedSceneComponents(root, keepAnimators: false);

            var sceneResettable = root.GetComponent<SceneAuthoredResettable>();
            if (sceneResettable != null)
                sceneResettable.enabled = false;

            var scenePickup = root.GetComponent<SceneAuthoredPickup>();
            if (scenePickup != null)
                scenePickup.enabled = false;

            var rigidbody = root.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = false;
                rigidbody.useGravity = true;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            }

            var grabInteractable = root.GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
                grabInteractable = root.GetComponentInChildren<XRGrabInteractable>(true);

            if (grabInteractable == null)
                return false;

            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: false,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;

            if (!TryGetVisualBoundsRelativeToReference(root.transform, root.transform, out var flintlockBounds))
                flintlockBounds = new Bounds(new Vector3(0f, 0f, 0.12f), new Vector3(0.16f, 0.12f, 0.42f));

            EnsureConfiguredFlintlockWeapon(root, root.transform, flintlockBounds);
            SetupLoosePickup(root, grabInteractable, keepKinematicWhileHeld: true);
            return true;
        }

        bool TryCreateRuntimeFlintlockPickup(string pickupName, Vector3 worldPosition, Quaternion worldRotation)
        {
            var root = new GameObject(pickupName);
            root.transform.SetPositionAndRotation(worldPosition, worldRotation);
            RegisterRuntimeObject(root);

            if (!TryInstantiateRuntimeModelVisual(FlintlockModelResourcePath, "Flintlock Visual", root.transform, out var visualRoot))
            {
                Debug.LogWarning("[VRCombat] Could not create a runtime flintlock pickup because the flintlock model could not be loaded.");
                Destroy(root);
                return false;
            }

            AlignLongestVisualAxisToForward(visualRoot.transform);
            UniformScaleVisualToLength(visualRoot.transform, 0.65f);
            CenterVisualAndPlaceBackEdge(visualRoot.transform, -0.08f);

            if (!TryGetVisualBoundsRelativeToReference(root.transform, root.transform, out var flintlockBounds))
                flintlockBounds = new Bounds(new Vector3(0f, 0f, 0.12f), new Vector3(0.16f, 0.12f, 0.42f));

            var collider = root.AddComponent<BoxCollider>();
            collider.center = flintlockBounds.center;
            collider.size = flintlockBounds.size + new Vector3(0.04f, 0.04f, 0.04f);
            collider.contactOffset = 0.0065f;

            var rigidbody = root.AddComponent<Rigidbody>();
            var weaponDefinition = GetWeaponDefinitionOrDefault(WeaponKind.Flintlock, 0.65f);
            rigidbody.mass = weaponDefinition.RigidbodyMass;
            rigidbody.linearDamping = weaponDefinition.RigidbodyLinearDamping;
            rigidbody.angularDamping = weaponDefinition.RigidbodyAngularDamping;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var grabInteractable = root.AddComponent<XRGrabInteractable>();
            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: false,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;

            var hasTriggerBounds = TryGetNamedVisualBoundsRelativeToReference(visualRoot.transform, root.transform, "trigger", out var triggerBounds);
            var gripReferencePosition = hasTriggerBounds
                ? new Vector3(
                    triggerBounds.center.x,
                    Mathf.Lerp(triggerBounds.min.y, triggerBounds.max.y, 0.75f),
                    triggerBounds.center.z)
                : new Vector3(
                    flintlockBounds.center.x,
                    flintlockBounds.center.y - flintlockBounds.extents.y * 0.18f,
                    flintlockBounds.min.z + flintlockBounds.size.z * 0.22f);
            ConfigureWeaponAttachTransform(root, grabInteractable, gripReferencePosition, WeaponKind.Flintlock);

            EnsureConfiguredFlintlockWeapon(root, visualRoot.transform, flintlockBounds);
            SetupLoosePickup(root, grabInteractable, keepKinematicWhileHeld: true);
            return true;
        }

        FlintlockWeapon EnsureConfiguredFlintlockWeapon(GameObject root, Transform visualRoot, Bounds flintlockBounds)
        {
            if (root == null)
                return null;

            var searchRoot = visualRoot != null ? visualRoot : root.transform;
            var hasBulletBounds = TryGetNamedVisualBoundsRelativeToReference(searchRoot, root.transform, "bullet", out var bulletBounds);
            var bulletTransform = FindChildByNameToken(searchRoot, "bullet");
            var triggerTransform = FindChildByNameToken(searchRoot, "trigger");
            var muzzleTransform = FindChildByNameToken(searchRoot, "muzzle");
            var barrelTransform = FindChildByNameToken(searchRoot, "barrel");
            var flashTransform = FindChildByNameToken(searchRoot, "flash");
            ResolveFlintlockBarrelCenterline(
                root.transform,
                searchRoot,
                flintlockBounds,
                hasBulletBounds,
                bulletBounds,
                muzzleTransform,
                barrelTransform,
                flashTransform,
                out var raycastLocalPosition,
                out var barrelAxisLocal,
                out var barrelUpLocal);

            if (barrelAxisLocal.sqrMagnitude <= 0.0001f)
                barrelAxisLocal = Vector3.forward;
            if (barrelUpLocal.sqrMagnitude <= 0.0001f ||
                Mathf.Abs(Vector3.Dot(barrelAxisLocal.normalized, barrelUpLocal.normalized)) > 0.98f)
            {
                barrelUpLocal = Mathf.Abs(Vector3.Dot(barrelAxisLocal.normalized, Vector3.up)) < 0.95f
                    ? Vector3.up
                    : Vector3.right;
            }

            barrelAxisLocal.Normalize();
            barrelUpLocal = Vector3.ProjectOnPlane(barrelUpLocal, barrelAxisLocal).normalized;
            var fireAxisLocal = -barrelAxisLocal;
            var raycastLocalRotation = Quaternion.LookRotation(fireAxisLocal, barrelUpLocal);

            var raycastOriginTransform = FindChildByNameToken(root.transform, "raycast origin");
            if (raycastOriginTransform == null)
            {
                var raycastOrigin = new GameObject("Raycast Origin");
                raycastOrigin.transform.SetParent(root.transform, false);
                raycastOriginTransform = raycastOrigin.transform;
            }

            raycastOriginTransform.localPosition = raycastLocalPosition;
            raycastOriginTransform.localRotation = raycastLocalRotation;

            var bulletVisual = bulletTransform != null
                ? bulletTransform.gameObject
                : CreateRuntimeFlintlockBulletFallback(root.transform, raycastLocalPosition, fireAxisLocal);
            var visualTrigger = triggerTransform;

            var weapon = root.GetComponent<FlintlockWeapon>();
            if (weapon == null)
                weapon = root.AddComponent<FlintlockWeapon>();

            weapon.ConfigureRuntimeSetup(bulletVisual, raycastOriginTransform, visualTrigger, m_RunProgressionController);
            return weapon;
        }

        static void ResolveFlintlockBarrelCenterline(
            Transform rootTransform,
            Transform searchRoot,
            Bounds flintlockBounds,
            bool hasBulletBounds,
            Bounds bulletBounds,
            Transform muzzleTransform,
            Transform barrelTransform,
            Transform flashTransform,
            out Vector3 muzzleTipLocalPosition,
            out Vector3 barrelAxisLocal,
            out Vector3 barrelUpLocal)
        {
            muzzleTipLocalPosition = new Vector3(flintlockBounds.center.x, flintlockBounds.center.y, flintlockBounds.max.z);
            barrelAxisLocal = Vector3.forward;
            barrelUpLocal = Vector3.up;
            if (rootTransform == null)
                return;

            var hasBarrelBounds = TryGetNamedVisualBoundsRelativeToReference(searchRoot, rootTransform, "barrel", out var barrelBounds);
            var hasMuzzleBounds = TryGetNamedVisualBoundsRelativeToReference(searchRoot, rootTransform, "muzzle", out var muzzleBounds);
            var hasFlashBounds = TryGetNamedVisualBoundsRelativeToReference(searchRoot, rootTransform, "flash", out var flashBounds);

            var barrelCenterLocalPosition = hasBarrelBounds
                ? barrelBounds.center
                : hasBulletBounds
                    ? bulletBounds.center
                    : new Vector3(
                        flintlockBounds.center.x,
                        flintlockBounds.center.y,
                        Mathf.Lerp(flintlockBounds.center.z, flintlockBounds.max.z, 0.35f));

            var muzzleCenterLocalPosition = hasMuzzleBounds
                ? muzzleBounds.center
                : hasFlashBounds
                    ? flashBounds.center
                    : muzzleTransform != null
                        ? rootTransform.InverseTransformPoint(muzzleTransform.position)
                        : barrelTransform != null
                            ? rootTransform.InverseTransformPoint(barrelTransform.position)
                            : flashTransform != null
                                ? rootTransform.InverseTransformPoint(flashTransform.position)
                                : new Vector3(
                                    flintlockBounds.center.x,
                                    flintlockBounds.center.y,
                                    flintlockBounds.max.z);

            if (TryGetFlintlockLocalDirection(muzzleTransform ?? barrelTransform ?? flashTransform, rootTransform, out var sourceForwardLocal, out var sourceUpLocal))
            {
                if ((muzzleCenterLocalPosition - barrelCenterLocalPosition).sqrMagnitude > 0.0001f &&
                    Vector3.Dot(sourceForwardLocal, muzzleCenterLocalPosition - barrelCenterLocalPosition) < 0f)
                {
                    sourceForwardLocal = -sourceForwardLocal;
                }

                barrelAxisLocal = sourceForwardLocal.sqrMagnitude > 0.0001f
                    ? sourceForwardLocal.normalized
                    : barrelAxisLocal;
                barrelUpLocal = sourceUpLocal.sqrMagnitude > 0.0001f
                    ? sourceUpLocal.normalized
                    : barrelUpLocal;
            }

            var centerlineLocal = muzzleCenterLocalPosition - barrelCenterLocalPosition;
            if (centerlineLocal.sqrMagnitude > 0.0001f &&
                (barrelAxisLocal.sqrMagnitude <= 0.0001f ||
                    Mathf.Abs(Vector3.Dot(barrelAxisLocal.normalized, centerlineLocal.normalized)) < 0.65f))
            {
                barrelAxisLocal = centerlineLocal.normalized;
            }

            if (barrelAxisLocal.sqrMagnitude <= 0.0001f)
                barrelAxisLocal = Vector3.forward;

            barrelAxisLocal.Normalize();

            var muzzleReferenceBounds = hasMuzzleBounds
                ? muzzleBounds
                : hasFlashBounds
                    ? flashBounds
                    : hasBarrelBounds
                        ? barrelBounds
                        : flintlockBounds;
            muzzleTipLocalPosition = GetBoundsCenterlineTip(muzzleReferenceBounds, barrelAxisLocal);

            if ((muzzleTipLocalPosition - barrelCenterLocalPosition).sqrMagnitude > 0.0001f &&
                Vector3.Dot(barrelAxisLocal, muzzleTipLocalPosition - barrelCenterLocalPosition) < 0f)
            {
                barrelAxisLocal = -barrelAxisLocal;
                muzzleTipLocalPosition = GetBoundsCenterlineTip(muzzleReferenceBounds, barrelAxisLocal);
            }
        }

        static bool TryGetFlintlockLocalDirection(Transform sourceTransform, Transform rootTransform, out Vector3 forwardLocal, out Vector3 upLocal)
        {
            forwardLocal = Vector3.zero;
            upLocal = Vector3.zero;
            if (sourceTransform == null || rootTransform == null)
                return false;

            forwardLocal = rootTransform.InverseTransformDirection(sourceTransform.forward);
            upLocal = rootTransform.InverseTransformDirection(sourceTransform.up);
            return forwardLocal.sqrMagnitude > 0.0001f;
        }

        static Vector3 GetBoundsCenterlineTip(Bounds bounds, Vector3 direction)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return bounds.center;

            var normalizedDirection = direction.normalized;
            var projectedExtent =
                Mathf.Abs(normalizedDirection.x) * bounds.extents.x +
                Mathf.Abs(normalizedDirection.y) * bounds.extents.y +
                Mathf.Abs(normalizedDirection.z) * bounds.extents.z;
            return bounds.center + normalizedDirection * projectedExtent;
        }

        static Transform FindChildByNameToken(Transform root, string nameToken)
        {
            if (root == null || string.IsNullOrWhiteSpace(nameToken))
                return null;

            var children = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child != null && child.name.IndexOf(nameToken, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }

            return null;
        }

        void ConfigureRuntimeWeaponModifiers(
            GameObject pickupRoot,
            WeaponKind weaponKind,
            bool requiresShieldUnlock = false)
        {
            if (pickupRoot == null)
                return;

            var damageDealers = pickupRoot.GetComponentsInChildren<SwingDamageDealer>(true);
            for (var i = 0; i < damageDealers.Length; i++)
            {
                var damageDealer = damageDealers[i];
                if (damageDealer == null)
                    continue;

                damageDealer.ConfigureRuntimeModifiers(m_RunProgressionController, weaponKind, requiresShieldUnlock);
            }

            var flintlockWeapon = pickupRoot.GetComponentInChildren<FlintlockWeapon>(true);
            if (flintlockWeapon != null)
                flintlockWeapon.ConfigureRuntimeModifiers(m_RunProgressionController);
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
                var side = HandleHandReplacementEquipped(grabInteractable, args);
                SetPickupCollisionsIgnoredWithPlayer(grabInteractable, true);
                SetPickupCollisionsIgnoredWithOtherHeldPickups(grabInteractable, true);
                SetHandSideVisualSuppressed(side, true);
                SetHandSideCollidersSuppressed(side, true);
                SetPickupIgnoredForGrounding(grabInteractable, true);
                if (mountedPickup != null)
                    mountedPickup.SetState(RuntimeMountedPickupState.Held);
            });

            grabInteractable.selectExited.AddListener(args =>
            {
                var side = HandleHandReplacementReleased(grabInteractable, args);
                SetPickupCollisionsIgnoredWithPlayer(grabInteractable, false);
                SetPickupCollisionsIgnoredWithOtherHeldPickups(grabInteractable, false);
                SetHandSideVisualSuppressed(side, false);
                SetHandSideCollidersSuppressed(side, false);
                if (mountedPickup != null)
                    mountedPickup.SetState(RuntimeMountedPickupState.Dropped);
                SetPickupIgnoredForGrounding(grabInteractable, false);
            });
        }

        void SetPickupCollisionsIgnoredWithPlayer(XRGrabInteractable grabInteractable, bool ignore)
        {
            if (grabInteractable == null || m_PlayerRoot == null)
                return;

            var pickupColliders = grabInteractable.transform.GetComponentsInChildren<Collider>(true);
            var playerColliders = m_PlayerRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < pickupColliders.Length; i++)
            {
                var pickupCollider = pickupColliders[i];
                if (pickupCollider == null)
                    continue;

                for (var j = 0; j < playerColliders.Length; j++)
                {
                    var playerCollider = playerColliders[j];
                    if (playerCollider == null || ReferenceEquals(playerCollider, pickupCollider))
                        continue;

                    Physics.IgnoreCollision(pickupCollider, playerCollider, ignore);
                }
            }
        }

        void SetPickupIgnoredForGrounding(XRGrabInteractable grabInteractable, bool ignore)
        {
            if (grabInteractable == null)
                return;

            if (!ignore)
            {
                RestorePickupLayers(grabInteractable);
                return;
            }

            var ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer < 0)
                return;

            if (!m_HeldPickupOriginalLayers.TryGetValue(grabInteractable, out var originalLayers))
            {
                originalLayers = new Dictionary<Transform, int>();
                m_HeldPickupOriginalLayers[grabInteractable] = originalLayers;
            }

            var transforms = grabInteractable.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child == null)
                    continue;

                if (!originalLayers.ContainsKey(child))
                    originalLayers.Add(child, child.gameObject.layer);

                child.gameObject.layer = ignoreRaycastLayer;
            }
        }

        void RestorePickupLayers(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable == null)
                return;

            if (!m_HeldPickupOriginalLayers.TryGetValue(grabInteractable, out var originalLayers))
                return;

            foreach (var pair in originalLayers)
            {
                if (pair.Key != null)
                    pair.Key.gameObject.layer = pair.Value;
            }

            m_HeldPickupOriginalLayers.Remove(grabInteractable);
        }

        void RestoreAllHeldPickupLayers()
        {
            foreach (var pair in m_HeldPickupOriginalLayers)
            {
                var originalLayers = pair.Value;
                if (originalLayers == null)
                    continue;

                foreach (var layerPair in originalLayers)
                {
                    if (layerPair.Key != null)
                        layerPair.Key.gameObject.layer = layerPair.Value;
                }
            }

            m_HeldPickupOriginalLayers.Clear();
        }

        void SetPickupCollisionsIgnoredWithOtherHeldPickups(XRGrabInteractable pickup, bool ignore)
        {
            if (pickup == null)
                return;

            var pickupColliders = pickup.transform.GetComponentsInChildren<Collider>(true);
            foreach (var pair in m_EquippedHandByPickup)
            {
                var otherPickup = pair.Key;
                if (otherPickup == null || ReferenceEquals(otherPickup, pickup))
                    continue;

                var otherColliders = otherPickup.transform.GetComponentsInChildren<Collider>(true);
                for (var i = 0; i < pickupColliders.Length; i++)
                {
                    var pickupCollider = pickupColliders[i];
                    if (pickupCollider == null)
                        continue;

                    for (var j = 0; j < otherColliders.Length; j++)
                    {
                        var otherCollider = otherColliders[j];
                        if (otherCollider == null || ReferenceEquals(otherCollider, pickupCollider))
                            continue;

                        Physics.IgnoreCollision(pickupCollider, otherCollider, ignore);
                    }
                }
            }
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
            var interactorComponent = args?.interactorObject as Component;
            if (TryResolvePickupHandSideFromInteractor(interactorComponent, out var interactorSide))
                return interactorSide;

            var interactorTransform = interactorComponent != null ? interactorComponent.transform : null;
            if (interactorTransform != null)
            {
                var current = interactorTransform;
                while (current != null)
                {
                    var name = current.name.ToLowerInvariant();
                    if (name.Contains("left"))
                        return HandSide.Left;
                    if (name.Contains("right"))
                        return HandSide.Right;
                    current = current.parent;
                }
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

        bool TryResolvePickupHandSideFromInteractor(Component interactorComponent, out HandSide side)
        {
            side = HandSide.None;
            var interactorTransform = interactorComponent != null ? interactorComponent.transform : null;
            if (interactorTransform == null)
                return false;

            if (MatchesSideHierarchy(interactorTransform, HandSide.Left))
            {
                side = HandSide.Left;
                return true;
            }

            if (MatchesSideHierarchy(interactorTransform, HandSide.Right))
            {
                side = HandSide.Right;
                return true;
            }

            return false;
        }

        bool MatchesSideHierarchy(Transform candidate, HandSide side)
        {
            if (candidate == null || side == HandSide.None)
                return false;

            RefreshHandTransformsIfNeeded();
            var handRoot = side == HandSide.Left ? m_LeftHandTransform : m_RightHandTransform;
            var controllerRoot = side == HandSide.Left ? m_LeftControllerTransform : m_RightControllerTransform;
            if (IsSameOrChildOf(candidate, handRoot) || IsSameOrChildOf(candidate, controllerRoot))
                return true;

            if (m_InputModalityManager == null)
                return false;

            if (side == HandSide.Left)
            {
                return TryResolveModalityTransform(m_InputModalityManager.leftHand, out var leftHandRoot) && IsSameOrChildOf(candidate, leftHandRoot)
                    || TryResolveModalityTransform(m_InputModalityManager.leftController, out var leftControllerRoot) && IsSameOrChildOf(candidate, leftControllerRoot);
            }

            return TryResolveModalityTransform(m_InputModalityManager.rightHand, out var rightHandRoot) && IsSameOrChildOf(candidate, rightHandRoot)
                || TryResolveModalityTransform(m_InputModalityManager.rightController, out var rightControllerRoot) && IsSameOrChildOf(candidate, rightControllerRoot);
        }

        static bool IsSameOrChildOf(Transform candidate, Transform root)
        {
            return candidate != null &&
                   root != null &&
                   (ReferenceEquals(candidate, root) || candidate.IsChildOf(root) || root.IsChildOf(candidate));
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
                int remainingHolds;
                if (side == HandSide.Left)
                {
                    m_LeftSuppressedRendererHoldCount = Mathf.Max(0, m_LeftSuppressedRendererHoldCount - 1);
                    remainingHolds = m_LeftSuppressedRendererHoldCount;
                }
                else
                {
                    m_RightSuppressedRendererHoldCount = Mathf.Max(0, m_RightSuppressedRendererHoldCount - 1);
                    remainingHolds = m_RightSuppressedRendererHoldCount;
                }

                if (remainingHolds > 0)
                    return;

                RestoreRendererList(suppressedList);
                return;
            }

            if (side == HandSide.Left)
                m_LeftSuppressedRendererHoldCount++;
            else
                m_RightSuppressedRendererHoldCount++;

            var suppressionRoots = new List<Transform>(4);
            CollectSideSuppressionRoots(side, suppressionRoots);
            for (var i = 0; i < suppressionRoots.Count; i++)
                CollectSuppressedHandRenderers(suppressionRoots[i], suppressedList);
        }

        void RefreshHandTransformsIfNeeded()
        {
            if (m_PlayerRoot == null)
                return;

            if (m_LeftHandTransform == null || m_RightHandTransform == null || m_LeftControllerTransform == null || m_RightControllerTransform == null)
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
                int remainingHolds;
                if (side == HandSide.Left)
                {
                    m_LeftSuppressedColliderHoldCount = Mathf.Max(0, m_LeftSuppressedColliderHoldCount - 1);
                    remainingHolds = m_LeftSuppressedColliderHoldCount;
                }
                else
                {
                    m_RightSuppressedColliderHoldCount = Mathf.Max(0, m_RightSuppressedColliderHoldCount - 1);
                    remainingHolds = m_RightSuppressedColliderHoldCount;
                }

                if (remainingHolds > 0)
                    return;

                RestoreColliderList(suppressedList);
                return;
            }

            if (side == HandSide.Left)
                m_LeftSuppressedColliderHoldCount++;
            else
                m_RightSuppressedColliderHoldCount++;

            var suppressionRoots = new List<Transform>(4);
            CollectSideSuppressionRoots(side, suppressionRoots);
            for (var i = 0; i < suppressionRoots.Count; i++)
                CollectSuppressedHandColliders(suppressionRoots[i], suppressedList);
        }

        void CollectSideSuppressionRoots(HandSide side, List<Transform> suppressionRoots)
        {
            if (suppressionRoots == null)
                return;

            suppressionRoots.Clear();
            RefreshHandTransformsIfNeeded();

            if (side == HandSide.Left)
            {
                AddSuppressionRoot(suppressionRoots, m_LeftHandTransform);
                AddSuppressionRoot(suppressionRoots, m_LeftControllerTransform);
                if (m_InputModalityManager != null)
                {
                    if (TryResolveModalityTransform(m_InputModalityManager.leftHand, out var leftHandRoot))
                        AddSuppressionRoot(suppressionRoots, leftHandRoot);
                    if (TryResolveModalityTransform(m_InputModalityManager.leftController, out var leftControllerRoot))
                        AddSuppressionRoot(suppressionRoots, leftControllerRoot);
                }

                return;
            }

            AddSuppressionRoot(suppressionRoots, m_RightHandTransform);
            AddSuppressionRoot(suppressionRoots, m_RightControllerTransform);
            if (m_InputModalityManager == null)
                return;

            if (TryResolveModalityTransform(m_InputModalityManager.rightHand, out var rightHandRoot))
                AddSuppressionRoot(suppressionRoots, rightHandRoot);
            if (TryResolveModalityTransform(m_InputModalityManager.rightController, out var rightControllerRoot))
                AddSuppressionRoot(suppressionRoots, rightControllerRoot);
        }

        static void AddSuppressionRoot(List<Transform> suppressionRoots, Transform candidate)
        {
            if (suppressionRoots == null || candidate == null)
                return;

            for (var i = 0; i < suppressionRoots.Count; i++)
            {
                var existing = suppressionRoots[i];
                if (existing == null)
                    continue;

                if (ReferenceEquals(existing, candidate) || candidate.IsChildOf(existing))
                    return;

                if (existing.IsChildOf(candidate))
                {
                    suppressionRoots[i] = candidate;
                    return;
                }
            }

            suppressionRoots.Add(candidate);
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
            RestoreAllHeldPickupLayers();
            RestoreRendererList(m_LeftSuppressedHandRenderers);
            RestoreRendererList(m_RightSuppressedHandRenderers);
            RestoreSuppressedHandColliders();
            m_LeftSuppressedRendererHoldCount = 0;
            m_RightSuppressedRendererHoldCount = 0;
            m_EquippedHandByPickup.Clear();
        }

        void RestoreSuppressedHandColliders()
        {
            RestoreColliderList(m_LeftSuppressedHandColliders);
            RestoreColliderList(m_RightSuppressedHandColliders);
            m_LeftSuppressedColliderHoldCount = 0;
            m_RightSuppressedColliderHoldCount = 0;
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

            m_CurrentWave = Mathf.Max(1, m_CurrentWave);

            while (!m_IsGameOver)
            {
                m_HitsTakenThisWave = 0;
                var enemiesThisWave = Mathf.Max(1, m_StartingEnemiesPerWave + (m_CurrentWave - 1) * m_EnemiesAddedPerWave);
                var shouldSpawnBossThisWave = ShouldSpawnBossForWave(m_CurrentWave);
                var totalEnemiesThisWave = enemiesThisWave + (shouldSpawnBossThisWave ? 1 : 0);
                m_CurrentFlowRate = Mathf.Max(
                    0.08f,
                    m_StartingFlowRatePerSecond + (m_CurrentWave - 1) * m_FlowRateIncreasePerWave);

                var spawnInterval = 1f / m_CurrentFlowRate;
                m_CombatHud?.SetWaveInfo(m_CurrentWave, totalEnemiesThisWave, m_CurrentFlowRate);
                m_CombatHud?.ShowBanner($"Wave {m_CurrentWave} starting", 2f);

                m_ActiveWaveEnemies.Clear();
                if (shouldSpawnBossThisWave)
                {
                    var boss = SpawnWaveBoss();
                    if (boss != null)
                        m_ActiveWaveEnemies.Add(boss);
                    else
                        Debug.LogWarning("[VRCombat] Wave 15 boss spawn failed. Continuing the wave to avoid stalling progression.", this);
                }

                var spawnedThisWave = 0;
                var spawnAttempts = 0;
                var maxSpawnAttempts = Mathf.Max(24, enemiesThisWave * 8);
                while (spawnedThisWave < enemiesThisWave)
                {
                    if (m_IsGameOver)
                        yield break;

                    spawnAttempts++;
                    var enemy = SpawnSingleEnemy(logFailure: false);
                    if (enemy != null)
                    {
                        m_ActiveWaveEnemies.Add(enemy);
                        spawnedThisWave++;
                    }

                    var remainingToSpawn = enemiesThisWave - spawnedThisWave;
                    var aliveNow = AliveEnemyCount();
                    m_CombatHud?.SetWaveInfo(m_CurrentWave, aliveNow + remainingToSpawn, m_CurrentFlowRate);
                    if (enemy != null)
                    {
                        spawnAttempts = 0;
                        yield return new WaitForSeconds(spawnInterval);
                    }
                    else
                    {
                        if (spawnAttempts >= maxSpawnAttempts)
                        {
                            ConfigureArenaSurface();
                            spawnAttempts = 0;
                            yield return new WaitForSeconds(0.25f);
                            continue;
                        }

                        yield return null;
                    }
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
                if (ShouldShowVictoryAfterWave(m_CurrentWave))
                {
                    yield return HandleVictoryTransition(nextWave);
                    if (m_IsGameOver || m_IsRestarting)
                        yield break;
                    if (!m_ShouldContinueAfterVictory)
                        yield break;

                    m_CurrentWave = nextWave;
                    continue;
                }

                if (TryGetWaveUnlockEncounter(m_CurrentWave, out var waveUnlockEncounter))
                {
                    yield return HandleWaveMilestoneTransition(m_CurrentWave, nextWave, waveUnlockEncounter);
                    if (m_IsGameOver)
                        yield break;

                    m_CurrentWave = nextWave;
                    continue;
                }

                m_CombatHud?.ShowBanner($"Wave cleared. Wave {nextWave} incoming", 2.2f);
                yield return new WaitForSeconds(m_TimeBetweenWavesSeconds);
                m_CurrentWave = nextWave;
            }
        }

        bool ShouldSpawnBossForWave(int wave)
        {
            return wave == BossWaveNumber &&
                   !m_EndlessModeActive &&
                   !m_HasShownVictoryChoice;
        }

        bool ShouldShowVictoryAfterWave(int wave)
        {
            return wave == BossWaveNumber &&
                   !m_EndlessModeActive &&
                   !m_HasShownVictoryChoice;
        }

        IEnumerator HandleVictoryTransition(int nextWave)
        {
            m_HasShownVictoryChoice = true;
            m_IsVictoryMenuOpen = true;
            m_ShouldContinueAfterVictory = false;
            SetPauseMenuOpen(false);
            Time.timeScale = 0f;
            m_CombatHud?.ShowVictoryPanel();
            RefreshRunInteractionState();

            while (!m_IsGameOver && !m_IsRestarting && m_IsVictoryMenuOpen)
                yield return null;

            if (m_IsGameOver || m_IsRestarting || !m_ShouldContinueAfterVictory)
                yield break;

            m_CombatHud?.ShowBanner($"Endless mode. Wave {nextWave} incoming", 2.2f);
            yield return new WaitForSecondsRealtime(Mathf.Min(1.25f, m_TimeBetweenWavesSeconds));
        }

        CapsuleEnemy SpawnWaveBoss()
        {
            var seedPosition = GetEnemySpawnPosition();
            if (!TryResolveWaveEnemySpawnPosition(
                    seedPosition,
                    BossGoblinCapsuleRadius,
                    BossGoblinCapsuleHeight,
                    out var position))
            {
                Debug.LogWarning("[VRCombat] Skipping boss spawn because no safe spawn position was available.", this);
                return null;
            }

            var boss = SpawnEnemyAt(
                position,
                EnemyRarity.Boss,
                null,
                BossGoblinModelResourcePath,
                BossGoblinTargetHeight,
                BossGoblinCapsuleRadius,
                BossGoblinCapsuleHeight);
            if (boss != null)
                m_CombatHud?.ShowBanner("Boss goblin has entered the arena", 2.4f);

            return boss;
        }

        bool TryGetWaveUnlockEncounter(int clearedWave, out ArenaOpeningEncounter encounter)
        {
            encounter = null;
            if (Array.IndexOf(s_WaveEncounterMilestones, clearedWave) < 0)
                return false;

            if (m_WaveUnlockEncounters.TryGetValue(clearedWave, out encounter) && encounter != null)
                return true;

            Debug.LogWarning(
                $"[VRCombat] Wave {clearedWave} is configured as a milestone, but no ArenaOpeningEncounter is assigned with UnlockAfterWave = {clearedWave}.",
                this);
            return false;
        }

        IEnumerator HandleWaveMilestoneTransition(int clearedWave, int nextWave, ArenaOpeningEncounter encounter)
        {
            if (encounter == null)
                yield break;

            m_PendingWaveResumeEncounter = encounter;
            m_DeferredNextWave = nextWave;
            encounter.PrepareLockedGate(this);

            m_CombatHud?.ShowBanner($"Wave {clearedWave} cleared. Defeat the key carrier.", 2.4f);
            var keyCarrier = SpawnMilestoneKeyCarrier();
            if (keyCarrier == null)
            {
                Debug.LogWarning(
                    $"[VRCombat] Could not spawn the milestone key carrier for wave {clearedWave}. Continuing to the next wave immediately.",
                    encounter);
                m_PendingWaveResumeEncounter = null;
                m_DeferredNextWave = -1;
                yield return new WaitForSeconds(m_TimeBetweenWavesSeconds);
                yield break;
            }

            m_MilestoneKeyCarrier = keyCarrier;
            m_MilestoneKeyCarrier.Died -= HandleMilestoneKeyCarrierDied;
            m_MilestoneKeyCarrier.Died += HandleMilestoneKeyCarrierDied;
            if (!m_ActiveWaveEnemies.Contains(m_MilestoneKeyCarrier))
                m_ActiveWaveEnemies.Add(m_MilestoneKeyCarrier);

            while (!m_IsGameOver && m_MilestoneKeyCarrier != null)
            {
                m_CombatHud?.SetWaveInfo(m_CurrentWave, 1, m_CurrentFlowRate);
                yield return null;
            }

            if (m_IsGameOver)
                yield break;

            m_IsWaitingForEncounterResume = true;
            m_CombatHud?.ShowBanner($"Unlock the gate and clear the encounter to reach wave {nextWave}.", 2.6f);
            while (!m_IsGameOver && m_IsWaitingForEncounterResume)
                yield return null;

            if (m_IsGameOver)
                yield break;

            var resumedWave = m_DeferredNextWave > 0 ? m_DeferredNextWave : nextWave;
            m_DeferredNextWave = -1;
            m_CombatHud?.ShowBanner($"Wave {resumedWave} incoming", 2.2f);
            yield return new WaitForSeconds(m_TimeBetweenWavesSeconds);
        }

        CapsuleEnemy SpawnMilestoneKeyCarrier()
        {
            var seedPosition = GetEnemySpawnPosition();
            if (!TryResolveWaveEnemySpawnPosition(seedPosition, 0.28f, 1.7f, out var position))
            {
                Debug.LogWarning("[VRCombat] Skipping milestone key carrier spawn because no safe spawn position was available.", this);
                return null;
            }

            var rarity = GetRandomRarityForWave();
            if (rarity == EnemyRarity.Common)
                rarity = EnemyRarity.Uncommon;

            return SpawnEnemyAt(position, rarity, null);
        }

        void HandleMilestoneKeyCarrierDied(CapsuleEnemy enemy)
        {
            if (enemy != null)
                enemy.Died -= HandleMilestoneKeyCarrierDied;

            if (!ReferenceEquals(enemy, m_MilestoneKeyCarrier))
                return;

            m_ActiveWaveEnemies.Remove(enemy);
            m_MilestoneKeyCarrier = null;

            if (m_PendingWaveResumeEncounter == null)
                return;

            m_PendingWaveResumeEncounter.SpawnConfiguredKeyAt(
                this,
                enemy != null ? enemy.transform.position : m_PendingWaveResumeEncounter.transform.position);
        }

        EnemyRarity GetRandomRarityForWave()
        {
            float redWeight;
            float greenWeight;
            float blueWeight;
            float goldWeight;

            if (m_CurrentWave <= 2)
            {
                redWeight = 85f;
                greenWeight = 15f;
                blueWeight = 0f;
                goldWeight = 0f;
            }
            else if (m_CurrentWave <= 4)
            {
                redWeight = 70f;
                greenWeight = 22f;
                blueWeight = 8f;
                goldWeight = 0f;
            }
            else if (m_CurrentWave <= 7)
            {
                redWeight = 58f;
                greenWeight = 25f;
                blueWeight = 15f;
                goldWeight = 2f;
            }
            else
            {
                redWeight = 45f;
                greenWeight = 28f;
                blueWeight = 20f;
                goldWeight = 7f;
            }

            float total = redWeight + greenWeight + blueWeight + goldWeight;
            float roll = UnityEngine.Random.Range(0, total);

            if (roll < goldWeight) return EnemyRarity.Epic;
            roll -= goldWeight;
            if (roll < blueWeight) return EnemyRarity.Rare;
            roll -= blueWeight;
            if (roll < greenWeight) return EnemyRarity.Uncommon;
            return EnemyRarity.Common;
        }

        public CapsuleEnemy SpawnEnemyAt(
            Vector3 position,
            EnemyRarity rarity,
            GameObject keyPrefab = null,
            string modelResourcePath = GoblinModelResourcePath,
            float targetVisualHeight = 1.65f,
            float capsuleRadius = 0.28f,
            float capsuleHeight = 1.7f)
        {
            var isBoss = rarity == EnemyRarity.Boss;
            var resolvedRadius = Mathf.Max(0.05f, capsuleRadius);
            var resolvedHeight = Mathf.Max(resolvedRadius * 2f, capsuleHeight);
            var enemyObject = new GameObject(isBoss ? "Boss Goblin Enemy" : "Goblin Enemy");
            enemyObject.transform.position = position;
            RegisterRuntimeObject(enemyObject);

            var collider = enemyObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, resolvedHeight * 0.5f + 0.1f, 0f);
            collider.radius = resolvedRadius;
            collider.height = resolvedHeight;

            var rigidbody = enemyObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = true;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var enemy = enemyObject.AddComponent<CapsuleEnemy>();
            enemy.SetPlayerTarget(m_PlayerCamera.transform, m_PlayerDamageReceiver);
            enemy.ConfigureGrabDistance(isBoss ? Mathf.Max(m_MaxPhysicalGrabDistance, 1.6f) : m_MaxPhysicalGrabDistance);
            enemy.SetRarity(rarity, keyPrefab);
            enemy.Died += HandleEnemyDied;

            var enemyRenderer = CreateGoblinVisual(
                enemyObject.transform,
                out var animationDriver,
                modelResourcePath,
                targetVisualHeight,
                isBoss);
            if (enemyRenderer != null)
                enemy.AssignRenderer(enemyRenderer);
            if (animationDriver != null)
                enemy.AttachAnimationDriver(animationDriver);

            RegisterEnemyKillZoneGrace(enemy);

            return enemy;
        }

        CapsuleEnemy SpawnSingleEnemy(bool logFailure = true)
        {
            var seedPosition = GetEnemySpawnPosition();
            if (!TryResolveWaveEnemySpawnPosition(seedPosition, 0.28f, 1.7f, out var position))
            {
                if (logFailure)
                    Debug.LogWarning("[VRCombat] Skipping enemy spawn because no safe spawn position was available.", this);
                return null;
            }

            return SpawnEnemyAt(position, GetRandomRarityForWave(), null);
        }

        void HandleEnemyDied(CapsuleEnemy enemy)
        {
            if (enemy != null)
            {
                enemy.Died -= HandleEnemyDied;
                m_EnemyKillZoneGraceUntil.Remove(enemy);
            }

            m_RunProgressionController?.AwardXp(EnemyXpReward);
        }

        Renderer CreateGoblinVisual(
            Transform parent,
            out GoblinAnimationDriver animationDriver,
            string modelResourcePath = GoblinModelResourcePath,
            float targetHeight = 1.65f,
            bool useBossAnimationSet = false)
        {
            animationDriver = null;
            if (parent == null)
                return null;

            var goblinPrefab = Resources.Load<GameObject>(string.IsNullOrWhiteSpace(modelResourcePath) ? GoblinModelResourcePath : modelResourcePath);
            if (goblinPrefab == null)
            {
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = useBossAnimationSet ? "Boss Goblin Visual" : "Goblin Visual";
                fallback.transform.SetParent(parent, false);
                fallback.transform.localPosition = new Vector3(0f, Mathf.Max(0.1f, targetHeight) * 0.5f + 0.1f, 0f);
                fallback.transform.localRotation = Quaternion.identity;
                var fallbackHeight = Mathf.Max(0.1f, targetHeight);
                var fallbackScale = fallbackHeight / 1.65f;
                fallback.transform.localScale = new Vector3(0.5f * fallbackScale, fallbackHeight * 0.5f, 0.5f * fallbackScale);
                ApplyMaterial(fallback, GetOrCreateEnemyMaterial());
                return fallback.GetComponent<Renderer>();
            }

            var visualRoot = Instantiate(goblinPrefab, parent, false);
            visualRoot.name = useBossAnimationSet ? "Boss Goblin Visual" : "Goblin Visual";
            StripImportedSceneComponents(visualRoot, keepAnimators: true);
            StripImportedColliders(visualRoot);
            RuntimeCombatModelMaterialBinder.Apply(visualRoot, GetOrCreateEnemyMaterial());
            ScaleImportedCharacterToHeight(visualRoot.transform, Mathf.Max(0.5f, targetHeight));
            AlignCharacterFeetToGround(visualRoot.transform);

            var animator = visualRoot.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;

                animationDriver = animator.gameObject.GetComponent<GoblinAnimationDriver>();
                if (animationDriver == null)
                    animationDriver = animator.gameObject.AddComponent<GoblinAnimationDriver>();
                animationDriver.Configure(
                    useBossAnimationSet ? ResolveBossLocomotionClip() : ResolveGoblinLocomotionClip(),
                    useBossAnimationSet ? ResolveBossAttackClip() : ResolveGoblinAttackClip());
            }

            var skinnedRenderer = visualRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinnedRenderer != null)
                return skinnedRenderer;

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0 ? renderers[0] : null;
        }

        static AnimationClip ResolveGoblinLocomotionClip()
        {
            return ResolvePreferredGoblinClip(
                preferredResourcePaths: new[] { "CombatModels/HobGoblin", "CombatModels/Goblin", "CombatModels/Walking" },
                preferAttackClip: false);
        }

        static AnimationClip ResolveGoblinAttackClip()
        {
            return ResolvePreferredGoblinClip(
                preferredResourcePaths: new[] { "CombatModels/Mutant Punch", "CombatModels/Zombie Punching", "CombatModels/HobGoblin", "CombatModels/Goblin" },
                preferAttackClip: true);
        }

        static AnimationClip ResolveBossLocomotionClip()
        {
            return ResolvePreferredGoblinClip(
                preferredResourcePaths: new[] { "CombatModels/WalkingHumanoid", "CombatModels/Walking", "CombatModels/HobGoblin", "CombatModels/Goblin" },
                preferAttackClip: false);
        }

        static AnimationClip ResolveBossAttackClip()
        {
            return ResolvePreferredGoblinClip(
                preferredResourcePaths: new[] { "CombatModels/Mutant Punch", "CombatModels/HobGoblin", "CombatModels/Zombie Punching", "CombatModels/Goblin" },
                preferAttackClip: true);
        }

        static AnimationClip ResolvePreferredGoblinClip(IReadOnlyList<string> preferredResourcePaths, bool preferAttackClip)
        {
            AnimationClip bestClip = null;
            var bestScore = int.MinValue;
            if (preferredResourcePaths == null)
                return null;

            for (var resourceIndex = 0; resourceIndex < preferredResourcePaths.Count; resourceIndex++)
            {
                var resourcePath = preferredResourcePaths[resourceIndex];
                if (string.IsNullOrWhiteSpace(resourcePath))
                    continue;

                var clips = Resources.LoadAll<AnimationClip>(resourcePath);
                for (var i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip == null || clip.length <= 0.05f)
                        continue;

                    var score = ScoreGoblinClip(clip, resourceIndex, preferAttackClip);
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    bestClip = clip;
                }
            }

            return bestScore > int.MinValue ? bestClip : null;
        }

        static int ScoreGoblinClip(AnimationClip clip, int resourcePriority, bool preferAttackClip)
        {
            if (clip == null)
                return int.MinValue;

            var lowerName = clip.name.ToLowerInvariant();
            var score = (100 - resourcePriority * 20) + Mathf.RoundToInt(clip.length * 10f);

            if (preferAttackClip)
            {
                if (lowerName.Contains("attack"))
                    score += 12;
                if (lowerName.Contains("punch"))
                    score += 10;
                if (lowerName.Contains("slash"))
                    score += 8;
                if (lowerName.Contains("hit"))
                    score += 4;
                if (lowerName.Contains("walk") || lowerName.Contains("run") || lowerName.Contains("idle"))
                    score -= 20;
            }
            else
            {
                if (lowerName.Contains("walk"))
                    score += 12;
                if (lowerName.Contains("sneak"))
                    score += 10;
                if (lowerName.Contains("run"))
                    score += 6;
                if (lowerName.Contains("attack") || lowerName.Contains("punch") || lowerName.Contains("slash"))
                    score -= 20;
            }

            if (lowerName.Contains("__preview__"))
                score -= 40;

            return score;
        }

        void ScaleImportedCharacterToHeight(Transform visualRoot, float targetHeight)
        {
            if (visualRoot == null || targetHeight <= 0f || !TryGetVisualLocalBounds(visualRoot, out var bounds))
                return;

            var currentHeight = Mathf.Max(0.01f, bounds.size.y);
            var scaleFactor = targetHeight / currentHeight;
            visualRoot.localScale *= scaleFactor;
        }

        void AlignCharacterFeetToGround(Transform visualRoot)
        {
            if (visualRoot == null || !TryGetVisualBoundsRelativeToReference(visualRoot, visualRoot.parent != null ? visualRoot.parent : visualRoot, out var bounds))
                return;

            visualRoot.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        int AliveEnemyCount()
        {
            m_ActiveWaveEnemies.RemoveAll(enemy => enemy == null);
            return m_ActiveWaveEnemies.Count;
        }

        Vector3 GetEnemySpawnPosition()
        {
            if (m_ArenaPlatformSurfaceColliders.Count == 0)
                ConfigureArenaSurface();

            if (TryGetBoundsFromColliders(m_ArenaPlatformSurfaceColliders, out var platformBounds))
            {
                var horizontalPadding = Mathf.Max(0.35f, BossGoblinCapsuleRadius + 0.2f);
                var minX = platformBounds.min.x + horizontalPadding;
                var maxX = platformBounds.max.x - horizontalPadding;
                var minZ = platformBounds.min.z + horizontalPadding;
                var maxZ = platformBounds.max.z - horizontalPadding;
                if (minX < maxX && minZ < maxZ)
                {
                    return new Vector3(
                        UnityEngine.Random.Range(minX, maxX),
                        platformBounds.max.y + ArenaGroundSnapProbeHeight + 1.5f,
                        UnityEngine.Random.Range(minZ, maxZ));
                }
            }

            var center = m_HasKillZoneCenter
                ? m_KillZoneCenter
                : m_PlayerCamera != null ? m_PlayerCamera.transform.position : transform.position;
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

        Material GetOrCreateFlintlockBulletMaterial()
        {
            if (m_FlintlockBulletMaterial != null)
                return m_FlintlockBulletMaterial;

            m_FlintlockBulletMaterial = CreateRuntimeLitMaterial("Runtime Flintlock Bullet Material", new Color(0.16f, 0.16f, 0.18f, 1f));
            if (m_FlintlockBulletMaterial == null)
                return null;

            if (m_FlintlockBulletMaterial.HasProperty("_Metallic"))
                m_FlintlockBulletMaterial.SetFloat("_Metallic", 0.9f);
            if (m_FlintlockBulletMaterial.HasProperty("_Smoothness"))
                m_FlintlockBulletMaterial.SetFloat("_Smoothness", 0.8f);
            return m_FlintlockBulletMaterial;
        }

        public void RegisterSpawnedRuntimeObject(GameObject runtimeObject)
        {
            RegisterRuntimeObject(runtimeObject);
        }

        void RegisterRuntimeObject(GameObject runtimeObject)
        {
            if (runtimeObject == null || m_RuntimeSpawnedObjects.Contains(runtimeObject))
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

        void ConfigureBladePickupRigidbody(Rigidbody rigidbody, WeaponDefinition weaponDefinition = null)
        {
            if (rigidbody == null)
                return;

            rigidbody.mass = weaponDefinition != null ? weaponDefinition.RigidbodyMass : 0.95f;
            rigidbody.linearDamping = weaponDefinition != null ? weaponDefinition.RigidbodyLinearDamping : 0.06f;
            rigidbody.angularDamping = weaponDefinition != null ? weaponDefinition.RigidbodyAngularDamping : 0.08f;
            rigidbody.solverIterations = 12;
            rigidbody.solverVelocityIterations = 4;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void ConfigureBladeGrabInteractable(
            GameObject pickupRoot,
            XRGrabInteractable grabInteractable,
            Collider gripCollider,
            Vector3 attachLocalPosition,
            WeaponKind weaponKind)
        {
            if (pickupRoot == null || grabInteractable == null)
                return;

            ConfigureGrabInteractable(
                grabInteractable,
                allowDynamicAttach: false,
                movementType: XRBaseInteractable.MovementType.Instantaneous);
            grabInteractable.throwOnDetach = false;
            ConfigureWeaponHoldFollow(grabInteractable);
            grabInteractable.colliders.Clear();
            if (gripCollider != null)
                grabInteractable.colliders.Add(gripCollider);

            ConfigureWeaponAttachTransform(pickupRoot, grabInteractable, attachLocalPosition, weaponKind);
        }

        void ConfigureWeaponAttachTransform(
            GameObject pickupRoot,
            XRGrabInteractable grabInteractable,
            Vector3 attachLocalPosition,
            WeaponKind weaponKind)
        {
            if (pickupRoot == null || grabInteractable == null)
                return;

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
            attachTransform.localPosition = attachLocalPosition + GetWeaponAttachLocalOffset(weaponKind);
            attachTransform.localRotation = GetWeaponAttachLocalRotation(weaponKind);
            grabInteractable.attachTransform = attachTransform;
        }

        static Vector3 GetWeaponAttachLocalOffset(WeaponKind weaponKind)
        {
            return weaponKind switch
            {
                WeaponKind.Dagger => new Vector3(0f, 0f, -0.06f),
                WeaponKind.Flintlock => new Vector3(0f, 0.01f, -0.035f),
                WeaponKind.Mace => Vector3.zero,
                WeaponKind.Spear => new Vector3(0f, 0f, 0f),
                WeaponKind.Sword => new Vector3(0f, -0.012f, -0.12f),
                _ => Vector3.zero
            };
        }

        static Quaternion GetWeaponAttachLocalRotation(WeaponKind weaponKind)
        {
            return weaponKind switch
            {
                WeaponKind.Dagger => Quaternion.Euler(75f, 0f, 0f),
                WeaponKind.Flintlock => Quaternion.Euler(0f, 180f, 0f),
                WeaponKind.Mace => Quaternion.Euler(75f, 180f, 0f),
                WeaponKind.Spear => Quaternion.Euler(75f, 180f, 0f),
                WeaponKind.Sword => Quaternion.Euler(70f, 0f, 0f),
                _ => Quaternion.identity
            };
        }

        static void ConfigureWeaponHoldFollow(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable == null)
                return;

            grabInteractable.attachEaseInTime = 0f;
            grabInteractable.smoothPosition = false;
            grabInteractable.smoothRotation = false;
            grabInteractable.tightenPosition = 1f;
            grabInteractable.tightenRotation = 1f;
        }

        static void ConfigureChainHoldFollow(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable == null)
                return;

            ConfigureWeaponHoldFollow(grabInteractable);
            grabInteractable.velocityDamping = 0.04f;
            grabInteractable.velocityScale = 1.85f;
            grabInteractable.angularVelocityDamping = 0.04f;
            grabInteractable.angularVelocityScale = 1.8f;
        }

        static WeaponDefinition GetWeaponDefinitionOrDefault(WeaponKind weaponKind, float fallbackLength)
        {
            if (RunCatalog.TryGetWeapon(weaponKind, out var weaponDefinition) && weaponDefinition != null)
                return weaponDefinition;

            return new WeaponDefinition(
                weaponKind,
                weaponKind.ToString(),
                pickupLength: fallbackLength,
                rigidbodyMass: 0.95f,
                rigidbodyLinearDamping: 0.06f,
                rigidbodyAngularDamping: 0.08f,
                baseDamage: 12f,
                minSwingSpeed: 0.45f,
                maxSwingSpeedForScaling: 6f,
                hitCooldownSeconds: 0.08f,
                proximityFallbackRadius: 0.1f);
        }

        Transform ConfigureWeaponDamageProfile(
            Transform root,
            WeaponKind weaponKind,
            WeaponDefinition weaponDefinition,
            BladePickupLayout bladeLayout,
            CapsuleCollider bodyCollider)
        {
            if (root == null || bodyCollider == null)
                return root;

            var colliderTransform = bodyCollider.transform;
            var damageDirectionSign = Mathf.Approximately(bladeLayout.BodyColliderCenter.z, 0f)
                ? 1f
                : Mathf.Sign(bladeLayout.BodyColliderCenter.z);
            colliderTransform.localPosition = bladeLayout.BodyColliderCenter + Vector3.forward * weaponDefinition.DamageForwardBias * damageDirectionSign;
            bodyCollider.radius = Mathf.Max(0.009f, bladeLayout.BodyColliderRadius * Mathf.Max(0.45f, weaponDefinition.DamageRadiusMultiplier));
            bodyCollider.height = Mathf.Max(bodyCollider.radius * 2.05f, bladeLayout.BodyColliderHeight * Mathf.Max(0.18f, weaponDefinition.DamageLengthMultiplier));

            switch (weaponKind)
            {
                case WeaponKind.Mace:
                    colliderTransform.localPosition = bladeLayout.BodyColliderCenter + Vector3.forward * Mathf.Max(0.14f, weaponDefinition.DamageForwardBias) * damageDirectionSign;
                    bodyCollider.height = Mathf.Max(bodyCollider.radius * 2.05f, bladeLayout.BodyColliderHeight * 0.34f);
                    break;
                case WeaponKind.Sword:
                    bodyCollider.radius = Mathf.Max(bodyCollider.radius, bladeLayout.BodyColliderRadius * 1.25f);
                    bodyCollider.height = Mathf.Max(bodyCollider.height, bladeLayout.BodyColliderHeight * 1.15f);
                    break;
                case WeaponKind.Spear:
                    bodyCollider.radius = Mathf.Max(0.008f, bladeLayout.BodyColliderRadius * 0.52f);
                    bodyCollider.height = Mathf.Max(bodyCollider.radius * 2.05f, bladeLayout.BodyColliderHeight);
                    var tipCollider = CreateCapsuleCollider(
                        root,
                        "Tip Damage Collider",
                        new Vector3(
                            bladeLayout.BodyColliderCenter.x,
                            bladeLayout.BodyColliderCenter.y,
                            bladeLayout.BodyColliderCenter.z + bladeLayout.BodyColliderHeight * 0.48f * damageDirectionSign),
                        Mathf.Max(0.01f, bladeLayout.BodyColliderRadius * 0.8f),
                        Mathf.Max(weaponDefinition.TipOnlyReach, 0.08f));
                    tipCollider.isTrigger = true;
                    tipCollider.contactOffset = 0.004f;
                    return tipCollider.transform;
            }

            return colliderTransform;
        }

        void ConfigureWeaponDamageEmitter(Transform damageEmitter, WeaponKind weaponKind, WeaponDefinition weaponDefinition)
        {
            if (damageEmitter == null || weaponDefinition == null)
                return;

            var damageDealer = damageEmitter.GetComponent<SwingDamageDealer>();
            if (damageDealer == null)
                damageDealer = damageEmitter.gameObject.AddComponent<SwingDamageDealer>();

            damageDealer.SetDamageGate(null);
            damageDealer.Configure(
                weaponDefinition.BaseDamage,
                weaponDefinition.MinSwingSpeed,
                weaponDefinition.MaxSwingSpeedForScaling,
                weaponDefinition.HitCooldownSeconds,
                weaponDefinition.ProximityFallbackRadius);
        }

        void ConfigureWeaponSpecialization(
            GameObject pickupRoot,
            WeaponKind weaponKind,
            WeaponDefinition weaponDefinition,
            Transform damageEmitter,
            XRGrabInteractable grabInteractable)
        {
            if (pickupRoot == null || weaponDefinition == null)
                return;

            switch (weaponKind)
            {
                case WeaponKind.Dagger:
                    var thrownDaggerWeapon = pickupRoot.GetComponent<ThrownDaggerWeapon>() ?? pickupRoot.AddComponent<ThrownDaggerWeapon>();
                    thrownDaggerWeapon.Configure(weaponDefinition.ThrowArmSpeed, weaponDefinition.ThrowImpactDamage);
                    if (grabInteractable != null)
                    {
                        grabInteractable.throwOnDetach = weaponDefinition.ThrowOnDetach;
                        grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
                        grabInteractable.velocityScale = 1.25f;
                        grabInteractable.angularVelocityScale = 1.2f;
                    }
                    break;
                case WeaponKind.Mace:
                    var heavyImpactWeapon = pickupRoot.GetComponent<HeavyImpactWeapon>() ?? pickupRoot.AddComponent<HeavyImpactWeapon>();
                    heavyImpactWeapon.Configure(weaponDefinition.HeavySwingSpeed, weaponDefinition.StunDuration, weaponDefinition.ImpactImpulse);
                    break;
                case WeaponKind.Sword:
                    var sweepSlashWeapon = pickupRoot.GetComponent<SweepSlashWeapon>() ?? pickupRoot.AddComponent<SweepSlashWeapon>();
                    sweepSlashWeapon.Configure(weaponDefinition.SweepRadius, weaponDefinition.SweepDamageMultiplier);
                    break;
                case WeaponKind.Spear:
                    var tipThrustWeapon = pickupRoot.GetComponent<TipThrustWeapon>() ?? pickupRoot.AddComponent<TipThrustWeapon>();
                    tipThrustWeapon.Configure(damageEmitter, weaponDefinition.TipOnlyReach);
                    break;
            }
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
            if (!TryBuildBladePickupLayout(visualRoot.transform, alignGripToOrigin: true, BladeGripEnd.Rear, out bladeLayout))
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

        GameObject CreateIntroChainVisual(Transform parent, bool showOnlyNail)
        {
            if (parent == null)
                return null;

            if (TryCreateChainVisual(parent, out var visualRoot, showOnlyNail))
                return visualRoot;

            if (showOnlyNail)
                CreateFallbackChainNailVisual(parent);
            else
                CreateFallbackChainVisual(parent);

            return parent.gameObject;
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

            StripImportedSceneComponents(visualRoot, keepAnimators: false);
            StripImportedColliders(visualRoot);
            DisableImportedAnimation(visualRoot);
            RuntimeCombatModelMaterialBinder.Apply(visualRoot, GetOrCreatePickupMaterial());
            return true;
        }

        GameObject CreateRuntimeFlintlockBulletFallback(Transform parent, Vector3 raycastLocalPosition, Vector3 barrelAxisLocal)
        {
            if (parent == null)
                return null;

            var bulletVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletVisual.name = "Attached Bullet";
            bulletVisual.transform.SetParent(parent, false);
            var seatDirection = barrelAxisLocal.sqrMagnitude > 0.0001f ? barrelAxisLocal.normalized : Vector3.forward;
            bulletVisual.transform.localPosition = raycastLocalPosition - seatDirection * 0.006f;
            bulletVisual.transform.localRotation = Quaternion.identity;
            bulletVisual.transform.localScale = Vector3.one * 0.014f;
            var renderer = bulletVisual.GetComponent<Renderer>();
            if (renderer != null)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ApplyMaterial(bulletVisual, GetOrCreateFlintlockBulletMaterial());

            var bulletCollider = bulletVisual.GetComponent<Collider>();
            if (bulletCollider != null)
                Destroy(bulletCollider);

            return bulletVisual;
        }

        bool TryCreateSceneBladePickupLayout(Transform pickupRoot, out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();
            if (pickupRoot == null || !TryGetVisualBoundsRelativeToReference(pickupRoot, pickupRoot, out var bounds))
                return false;

            bladeLayout = BuildBladePickupLayoutFromBounds(bounds, alignGripToOrigin: false, BladeGripEnd.Rear);
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

        void StripImportedSceneComponents(GameObject visualRoot, bool keepAnimators)
        {
            if (visualRoot == null)
                return;

            var cameras = visualRoot.GetComponentsInChildren<Camera>(true);
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    Destroy(cameras[i]);
            }

            var audioListeners = visualRoot.GetComponentsInChildren<AudioListener>(true);
            for (var i = 0; i < audioListeners.Length; i++)
            {
                if (audioListeners[i] != null)
                    Destroy(audioListeners[i]);
            }

            var lights = visualRoot.GetComponentsInChildren<Light>(true);
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                    Destroy(lights[i]);
            }

            if (keepAnimators)
                return;

            var animators = visualRoot.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null)
                    animators[i].enabled = false;
            }
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

        static BladeGripEnd ResolveRuntimeWeaponGripEnd(WeaponKind weaponKind)
        {
            return weaponKind switch
            {
                WeaponKind.Mace => BladeGripEnd.Front,
                WeaponKind.Spear => BladeGripEnd.Front,
                _ => BladeGripEnd.Rear
            };
        }

        bool TryBuildBladePickupLayout(
            Transform visualRoot,
            bool alignGripToOrigin,
            BladeGripEnd gripEnd,
            out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();
            var referenceTransform = visualRoot != null && visualRoot.parent != null ? visualRoot.parent : visualRoot;
            if (visualRoot == null || !TryGetVisualBoundsRelativeToReference(visualRoot, referenceTransform, out var bounds))
                return false;

            if (alignGripToOrigin)
                visualRoot.localPosition += -GetBladeGripCenter(bounds, gripEnd);

            bladeLayout = BuildBladePickupLayoutFromBounds(bounds, alignGripToOrigin, gripEnd);
            return true;
        }

        bool TryBuildMacePickupLayout(Transform visualRoot, BladeGripEnd gripEnd, out BladePickupLayout bladeLayout)
        {
            bladeLayout = CreateDefaultBladePickupLayout();
            var referenceTransform = visualRoot != null && visualRoot.parent != null ? visualRoot.parent : visualRoot;
            if (visualRoot == null || referenceTransform == null)
                return false;

            if (!TryGetVisualBoundsRelativeToReference(visualRoot, referenceTransform, out var bounds))
                return false;

            var handleSliceStart = gripEnd == BladeGripEnd.Front ? 0.8f : 0f;
            var handleSliceEnd = gripEnd == BladeGripEnd.Front ? 1f : 0.2f;
            var headSliceStart = gripEnd == BladeGripEnd.Front ? 0f : 0.55f;
            var headSliceEnd = gripEnd == BladeGripEnd.Front ? 0.4f : 1f;

            if (!TryGetMeshVertexSliceBounds(visualRoot, referenceTransform, handleSliceStart, handleSliceEnd, out var handleBounds))
                handleBounds = CreateSliceBoundsFromBounds(bounds, handleSliceStart, handleSliceEnd);

            if (!TryGetMeshVertexSliceBounds(visualRoot, referenceTransform, headSliceStart, headSliceEnd, out var headBounds))
                headBounds = CreateSliceBoundsFromBounds(bounds, headSliceStart, headSliceEnd);

            var length = Mathf.Max(0.22f, bounds.size.z);
            var gripLength = Mathf.Clamp(length * 0.16f, 0.08f, 0.12f);
            var gripCenterZ = gripEnd == BladeGripEnd.Front
                ? bounds.max.z - gripLength * 0.5f
                : bounds.min.z + gripLength * 0.5f;
            var gripCenter = new Vector3(handleBounds.center.x, handleBounds.center.y, gripCenterZ);
            visualRoot.localPosition += -gripCenter;

            var shiftedHeadBounds = ShiftBounds(headBounds, -gripCenter);
            var handleThickness = Mathf.Max(handleBounds.size.x, handleBounds.size.y);
            if (handleThickness <= 0.0001f)
                handleThickness = Mathf.Max(bounds.size.x, bounds.size.y) * 0.4f;

            var headThickness = Mathf.Max(headBounds.size.x, headBounds.size.y);
            if (headThickness <= 0.0001f)
                headThickness = Mathf.Max(bounds.size.x, bounds.size.y);

            var gripRadius = Mathf.Clamp(handleThickness * 0.18f, 0.014f, 0.023f);
            var bodyRadius = Mathf.Clamp(headThickness * 0.17f, gripRadius * 1.25f, 0.05f);
            var bodyHeight = Mathf.Max(0.1f, shiftedHeadBounds.size.z * 1.12f);

            bladeLayout = new BladePickupLayout
            {
                GripColliderCenter = Vector3.zero,
                GripColliderHeight = Mathf.Max(gripLength, gripRadius * 2.05f),
                GripColliderRadius = gripRadius,
                BodyColliderCenter = shiftedHeadBounds.center,
                BodyColliderHeight = bodyHeight,
                BodyColliderRadius = bodyRadius,
                AttachLocalPosition = Vector3.zero
            };
            return true;
        }

        static bool TryGetMeshVertexSliceBounds(
            Transform visualRoot,
            Transform referenceTransform,
            float normalizedStart,
            float normalizedEnd,
            out Bounds bounds)
        {
            bounds = default;
            var vertices = new List<Vector3>();
            if (!TryCollectMeshVerticesRelativeToReference(visualRoot, referenceTransform, vertices))
                return false;

            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;
            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                if (vertex.z < minZ)
                    minZ = vertex.z;
                if (vertex.z > maxZ)
                    maxZ = vertex.z;
            }

            if (float.IsNaN(minZ) || float.IsInfinity(minZ) ||
                float.IsNaN(maxZ) || float.IsInfinity(maxZ) ||
                maxZ - minZ <= 0.0001f)
                return false;

            var clampedStart = Mathf.Clamp01(normalizedStart);
            var clampedEnd = Mathf.Clamp01(normalizedEnd);
            if (clampedEnd < clampedStart)
            {
                var swap = clampedStart;
                clampedStart = clampedEnd;
                clampedEnd = swap;
            }

            var sliceStartZ = Mathf.Lerp(minZ, maxZ, clampedStart);
            var sliceEndZ = Mathf.Lerp(minZ, maxZ, clampedEnd);
            var hasBounds = false;
            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                if (vertex.z < sliceStartZ || vertex.z > sliceEndZ)
                    continue;

                if (!hasBounds)
                {
                    bounds = new Bounds(vertex, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(vertex);
                }
            }

            return hasBounds;
        }

        static bool TryCollectMeshVerticesRelativeToReference(
            Transform visualRoot,
            Transform referenceTransform,
            List<Vector3> vertices)
        {
            if (visualRoot == null || referenceTransform == null || vertices == null)
                return false;

            vertices.Clear();
            var meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
            for (var i = 0; i < meshFilters.Length; i++)
            {
                var meshFilter = meshFilters[i];
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                var meshVertices = meshFilter.sharedMesh.vertices;
                var localToWorld = meshFilter.transform.localToWorldMatrix;
                for (var vertexIndex = 0; vertexIndex < meshVertices.Length; vertexIndex++)
                {
                    var worldVertex = localToWorld.MultiplyPoint3x4(meshVertices[vertexIndex]);
                    vertices.Add(referenceTransform.InverseTransformPoint(worldVertex));
                }
            }

            return vertices.Count > 0;
        }

        static Bounds CreateSliceBoundsFromBounds(Bounds bounds, float normalizedStart, float normalizedEnd)
        {
            var clampedStart = Mathf.Clamp01(normalizedStart);
            var clampedEnd = Mathf.Clamp01(normalizedEnd);
            if (clampedEnd < clampedStart)
            {
                var swap = clampedStart;
                clampedStart = clampedEnd;
                clampedEnd = swap;
            }

            var sliceMinZ = Mathf.Lerp(bounds.min.z, bounds.max.z, clampedStart);
            var sliceMaxZ = Mathf.Lerp(bounds.min.z, bounds.max.z, clampedEnd);
            var sliceCenterZ = (sliceMinZ + sliceMaxZ) * 0.5f;
            return new Bounds(
                new Vector3(bounds.center.x, bounds.center.y, sliceCenterZ),
                new Vector3(bounds.size.x, bounds.size.y, Mathf.Max(0.001f, sliceMaxZ - sliceMinZ)));
        }

        static Bounds ShiftBounds(Bounds bounds, Vector3 offset)
        {
            return new Bounds(bounds.center + offset, bounds.size);
        }

        static Vector3 GetBladeGripCenter(Bounds bounds, BladeGripEnd gripEnd)
        {
            var length = Mathf.Max(0.18f, bounds.size.z);
            var gripLength = Mathf.Clamp(length * 0.22f, 0.08f, 0.14f);
            var gripCenterZ = gripEnd == BladeGripEnd.Front
                ? bounds.max.z - gripLength * 0.5f
                : bounds.min.z + gripLength * 0.5f;
            return new Vector3(bounds.center.x, bounds.center.y, gripCenterZ);
        }

        static BladePickupLayout BuildBladePickupLayoutFromBounds(Bounds bounds, bool alignGripToOrigin, BladeGripEnd gripEnd)
        {
            const float importedBladeBodyColliderBackshift = 0.15f;

            var length = Mathf.Max(0.18f, bounds.size.z);
            var gripLength = Mathf.Clamp(length * 0.22f, 0.08f, 0.14f);
            var gripCenter = GetBladeGripCenter(bounds, gripEnd);
            var gripBaseZ = alignGripToOrigin ? 0f : gripCenter.z;
            var gripCenterX = alignGripToOrigin ? 0f : gripCenter.x;
            var gripCenterY = alignGripToOrigin ? 0f : gripCenter.y;
            var crossSection = Mathf.Max(bounds.size.x, bounds.size.y);
            var gripRadius = Mathf.Clamp(crossSection * 0.28f, 0.016f, 0.028f);
            var bladeRadius = Mathf.Clamp(crossSection * 0.18f, 0.009f, gripRadius * 0.8f);
            float bodyStartZ;
            float bodyEndZ;
            if (gripEnd == BladeGripEnd.Rear)
            {
                var frontEdgeZ = alignGripToOrigin ? bounds.max.z - gripCenter.z : bounds.max.z;
                bodyStartZ = gripBaseZ + gripLength * 0.25f;
                bodyEndZ = Mathf.Max(bodyStartZ + 0.12f, frontEdgeZ - gripLength * 0.06f);
            }
            else
            {
                var strikingEdgeZ = alignGripToOrigin ? bounds.min.z - gripCenter.z : bounds.min.z;
                bodyEndZ = gripBaseZ - gripLength * 0.25f;
                bodyStartZ = Mathf.Min(bodyEndZ - 0.12f, strikingEdgeZ + gripLength * 0.06f);
            }

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
                visualRoot.localRotation *= Quaternion.Euler(90f, 0f, 0f);
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

            if (TryGetNamedVisualBoundsRelativeToReference(visualRoot, visualRoot, "handcuff", out var cuffBounds))
            {
                visualRoot.localPosition += new Vector3(
                    -cuffBounds.center.x,
                    -cuffBounds.center.y,
                    gripForwardOffset - cuffBounds.min.z);
                return;
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

        static bool TryGetNamedVisualBoundsRelativeToReference(Transform visualRoot, Transform referenceTransform, string nameToken, out Bounds bounds)
        {
            bounds = default;
            if (visualRoot == null || referenceTransform == null || string.IsNullOrWhiteSpace(nameToken))
                return false;

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (renderer.name.IndexOf(nameToken, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var corners = GetBoundsCorners(renderer.bounds);
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
            EnsureSingleOwnerWhileHeldSelectFilter(interactable);
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

                EnsureSingleOwnerWhileHeldSelectFilter(grabInteractable);
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

        void EnsureSingleOwnerWhileHeldSelectFilter(XRGrabInteractable interactable)
        {
            if (interactable == null)
                return;

            interactable.selectMode = InteractableSelectMode.Single;

            var filter = interactable.GetComponent<SingleOwnerWhileHeldSelectFilter>();
            if (filter == null)
                filter = interactable.gameObject.AddComponent<SingleOwnerWhileHeldSelectFilter>();

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
