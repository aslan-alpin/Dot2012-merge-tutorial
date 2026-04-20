using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VRCombat.Core;
using VRCombat.Enemies;
using VRCombat.Player;

namespace VRCombat.Environment
{
    [Serializable]
    public struct GoblinSpawnEntry
    {
        public Vector3 localPosition;
        public EnemyRarity rarity;
        public bool dropsKey;
    }

    [Serializable]
    public struct EncounterBridgeMove
    {
        public Transform bridge;
        public Vector3 localPositionOffset;
    }

    public enum EncounterStartMode
    {
        AutoOnRunStart = 0,
        OnTriggerEnter = 1,
    }

    public enum EncounterCompletionMode
    {
        RequireGateUnlock = 0,
        ClearEnemies = 1,
    }

    [DisallowMultipleComponent]
    public sealed class EncounterStartTriggerRelay : MonoBehaviour
    {
        ArenaOpeningEncounter m_Owner;

        public void Configure(ArenaOpeningEncounter owner)
        {
            m_Owner = owner;
        }

        void OnTriggerEnter(Collider other)
        {
            m_Owner?.HandleStartTriggerEntered(other);
        }
    }

    public sealed class ArenaOpeningEncounter : MonoBehaviour
    {
        struct AuthoredTransformState
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public bool isCaptured;
        }

        const string DefaultArenaResourcePath = "CombatModels/Arena_01";
        const string DefaultKeyResourcePath = "CombatModels/Key_Torch";
        const float GoblinSpawnCapsuleRadius = 0.28f;
        const float GoblinSpawnCapsuleHeight = 1.7f;
        const float KeyDropCapsuleRadius = 0.1f;
        const float KeyDropCapsuleHeight = 0.3f;
        const float EncounterSpawnFallbackRadius = 2.4f;
        const int EncounterSpawnFallbackSamples = 6;
        static readonly GoblinSpawnEntry[] s_DefaultGoblinGroup =
        {
            new GoblinSpawnEntry { localPosition = new Vector3(-2f, 0f, 2.2f), rarity = EnemyRarity.Common, dropsKey = false },
            new GoblinSpawnEntry { localPosition = new Vector3(0f, 0f, 3.1f), rarity = EnemyRarity.Uncommon, dropsKey = true },
            new GoblinSpawnEntry { localPosition = new Vector3(2f, 0f, 2.2f), rarity = EnemyRarity.Common, dropsKey = false },
        };

        [Header("Encounter Flow")]
        [SerializeField] EncounterStartMode m_StartMode = EncounterStartMode.AutoOnRunStart;
        [SerializeField] EncounterCompletionMode m_CompletionMode = EncounterCompletionMode.RequireGateUnlock;
        [SerializeField, FormerlySerializedAs("m_ContinueWavesOnCompletion")]
        [Tooltip("If enabled, completing this encounter starts the initial wave loop or resumes waves after a milestone pause.")]
        bool m_StartsOrResumesWavesOnCompletion;
        [SerializeField, Min(0)]
        [Tooltip("When greater than 0, clearing that wave spawns a milestone key carrier that drops this encounter's key.")]
        int m_UnlockAfterWave;
        [SerializeField] Collider m_StartTrigger;

        [Header("Locked Gate")]
        [SerializeField, FormerlySerializedAs("m_GateToOpen")]
        [Tooltip("The barred gate unlocked by this encounter's key and padlock.")]
        GateController m_GateToUnlock;
        [SerializeField] Transform m_PadlockMountPoint;
        [SerializeField] GameObject m_PadlockPrefab;
        [SerializeField] GameObject m_KeyDropPrefab;
        [SerializeField] int m_KeyId = 1;
        [SerializeField] float m_KeyDropHeight = 0.2f;

        [Header("Arena Motion")]
        [SerializeField] Transform m_PlatformToRaise;
        [SerializeField] float m_PlatformRaiseAmount;
        [SerializeField] EncounterBridgeMove[] m_BridgeMoves = Array.Empty<EncounterBridgeMove>();
        [SerializeField] float m_MotionSpeed = 1.75f;

        [Header("Spawn Setup")]
        [SerializeField] float m_GroundProbeHeight = 2f;
        [SerializeField] float m_GroundProbeDistance = 6f;
        [SerializeField] GoblinSpawnEntry[] m_GoblinGroup =
        {
            new GoblinSpawnEntry { localPosition = new Vector3(-2f, 0f, 2.2f), rarity = EnemyRarity.Common, dropsKey = false },
            new GoblinSpawnEntry { localPosition = new Vector3(0f, 0f, 3.1f), rarity = EnemyRarity.Uncommon, dropsKey = true },
            new GoblinSpawnEntry { localPosition = new Vector3(2f, 0f, 2.2f), rarity = EnemyRarity.Common, dropsKey = false },
        };

        readonly List<CapsuleEnemy> m_EncounterEnemies = new List<CapsuleEnemy>();

        VRCombatBootstrapper m_Bootstrapper;
        CapsuleEnemy m_KeyCarrier;
        Padlock m_RuntimePadlockInstance;
        GameObject m_ActiveKeyObject;
        EncounterStartTriggerRelay m_StartTriggerRelay;
        Coroutine m_MotionRoutine;
        AuthoredTransformState m_RootState;
        AuthoredTransformState m_PlatformState;
        AuthoredTransformState[] m_BridgeStates = Array.Empty<AuthoredTransformState>();
        bool m_HasBegun;
        bool m_HasCompleted;
        bool m_HasDroppedEncounterKey;
        bool m_IsCompleting;
        bool m_HasPreparedGateLock;

        public event Action<ArenaOpeningEncounter> EncounterCompleted;

        public EncounterStartMode StartMode => m_StartMode;
        public EncounterCompletionMode CompletionMode => m_CompletionMode;
        public bool StartsOrResumesWavesOnCompletion => m_StartsOrResumesWavesOnCompletion;
        public bool ContinueWavesOnCompletion => m_StartsOrResumesWavesOnCompletion;
        public int UnlockAfterWave => Mathf.Max(0, m_UnlockAfterWave);
        public int KeyId => Mathf.Max(0, m_KeyId);
        public bool HasBegun => m_HasBegun;
        public bool HasCompleted => m_HasCompleted;
        public GateController GateToUnlock => m_GateToUnlock;

        void Awake()
        {
            if (m_PadlockMountPoint == null)
                m_PadlockMountPoint = transform;
            if (m_BridgeMoves == null)
                m_BridgeMoves = Array.Empty<EncounterBridgeMove>();

            ResolveDefaultStartTrigger();
            CaptureAuthoredStates();
            ConfigureStartTrigger();
            DisableEncounterHelperColliders();
            m_GateToUnlock?.ResetToClosedImmediate();
        }

        void OnDestroy()
        {
            StopArenaMotion();
            UnsubscribePadlock();
            DetachEncounterEnemyListeners();
        }

        public void BeginEncounter(VRCombatBootstrapper bootstrapper)
        {
            if (m_HasBegun || bootstrapper == null)
                return;

            m_Bootstrapper = bootstrapper;
            ReserveEncounterSpace();
            m_HasBegun = true;
            m_HasCompleted = false;
            m_IsCompleting = false;

            if (m_CompletionMode == EncounterCompletionMode.RequireGateUnlock)
                PrepareLockedGateInternal(resetExisting: true);

            SpawnEncounterGroup();

            var startMessage = m_CompletionMode == EncounterCompletionMode.RequireGateUnlock
                ? "Defeat the goblins and strike the lock with the key."
                : "Defeat the goblins to secure the arena.";
            m_Bootstrapper.ShowRuntimeBanner(startMessage, 2f);
        }

        public void ResetEncounter()
        {
            StopArenaMotion();
            DetachEncounterEnemyListeners();
            m_EncounterEnemies.Clear();
            m_KeyCarrier = null;
            m_HasBegun = false;
            m_HasCompleted = false;
            m_HasDroppedEncounterKey = false;
            m_IsCompleting = false;
            m_HasPreparedGateLock = false;
            m_Bootstrapper = null;

            RestoreAuthoredStates();
            DestroyActiveKey();
            DestroyRuntimePadlock();
            m_GateToUnlock?.ResetToClosedImmediate();
            ConfigureStartTrigger();
        }

        public void PrepareLockedGate(VRCombatBootstrapper bootstrapper)
        {
            if (bootstrapper == null)
                return;

            m_Bootstrapper = bootstrapper;
            ReserveEncounterSpace();
            PrepareLockedGateInternal(resetExisting: false);
        }

        public bool SpawnConfiguredKeyAt(VRCombatBootstrapper bootstrapper, Vector3 enemyPosition)
        {
            if (bootstrapper != null)
                m_Bootstrapper = bootstrapper;

            if (m_ActiveKeyObject != null)
                return true;

            SpawnKeyAt(enemyPosition);
            if (m_ActiveKeyObject != null)
            {
                m_HasDroppedEncounterKey = true;
                return true;
            }

            return false;
        }

        public void HandleStartTriggerEntered(Collider other)
        {
            if (m_StartMode != EncounterStartMode.OnTriggerEnter ||
                m_HasBegun ||
                m_HasCompleted ||
                m_IsCompleting ||
                !isActiveAndEnabled)
            {
                return;
            }

            if (!IsPlayerTrigger(other))
                return;

            var bootstrapper = m_Bootstrapper != null
                ? m_Bootstrapper
                : FindAnyObjectByType<VRCombatBootstrapper>();
            if (bootstrapper == null)
                return;

            BeginEncounter(bootstrapper);
        }

        void CaptureAuthoredStates()
        {
            CaptureTransformState(ref m_RootState, transform);
            CaptureTransformState(ref m_PlatformState, m_PlatformToRaise);

            var bridgeCount = m_BridgeMoves != null ? m_BridgeMoves.Length : 0;
            m_BridgeStates = new AuthoredTransformState[bridgeCount];
            for (var i = 0; i < bridgeCount; i++)
                CaptureTransformState(ref m_BridgeStates[i], m_BridgeMoves[i].bridge);
        }

        void ReserveEncounterSpace()
        {
            if (m_Bootstrapper == null)
                return;

            m_Bootstrapper.ReserveEncounterSpace(transform.position);

            if (m_StartTrigger != null)
                m_Bootstrapper.ReserveEncounterSpace(m_StartTrigger.bounds.center, 6f, 3f, 6f);

            if (m_PadlockMountPoint != null)
                m_Bootstrapper.ReserveEncounterSpace(m_PadlockMountPoint.position, 6f, 3f, 6f);

            if (m_PlatformToRaise != null)
                m_Bootstrapper.ReserveEncounterSpace(m_PlatformToRaise.position, 10f, 4f, 10f);

            for (var i = 0; i < m_BridgeMoves.Length; i++)
            {
                if (m_BridgeMoves[i].bridge != null)
                    m_Bootstrapper.ReserveEncounterSpace(m_BridgeMoves[i].bridge.position, 10f, 4f, 10f);
            }
        }

        static void CaptureTransformState(ref AuthoredTransformState state, Transform target)
        {
            if (target == null)
                return;

            state.transform = target;
            state.localPosition = target.localPosition;
            state.localRotation = target.localRotation;
            state.localScale = target.localScale;
            state.isCaptured = true;
        }

        void RestoreAuthoredStates()
        {
            RestoreTransformState(m_RootState);
            RestoreTransformState(m_PlatformState);

            for (var i = 0; i < m_BridgeStates.Length; i++)
                RestoreTransformState(m_BridgeStates[i]);
        }

        static void RestoreTransformState(AuthoredTransformState state)
        {
            if (!state.isCaptured || state.transform == null)
                return;

            state.transform.localPosition = state.localPosition;
            state.transform.localRotation = state.localRotation;
            state.transform.localScale = state.localScale;
        }

        void ResolveDefaultStartTrigger()
        {
            if (m_StartMode != EncounterStartMode.OnTriggerEnter || m_StartTrigger != null)
                return;

            m_StartTrigger = GetComponent<Collider>();
            if (m_StartTrigger == null)
                m_StartTrigger = GetComponentInChildren<Collider>(true);
        }

        void ConfigureStartTrigger()
        {
            if (m_StartMode != EncounterStartMode.OnTriggerEnter || m_StartTrigger == null)
                return;

            m_StartTrigger.enabled = true;
            m_StartTrigger.isTrigger = true;
            m_StartTriggerRelay = m_StartTrigger.GetComponent<EncounterStartTriggerRelay>();
            if (m_StartTriggerRelay == null)
                m_StartTriggerRelay = m_StartTrigger.gameObject.AddComponent<EncounterStartTriggerRelay>();

            m_StartTriggerRelay.Configure(this);
        }

        bool IsPlayerTrigger(Collider other)
        {
            if (other == null)
                return false;

            if (other.GetComponentInParent<PlayerDamageReceiver>() != null)
                return true;

            return other.GetComponentInParent<CharacterController>() != null;
        }

        void PrepareLockedGateInternal(bool resetExisting)
        {
            if (resetExisting)
            {
                DestroyActiveKey();
                DestroyRuntimePadlock();
                m_HasDroppedEncounterKey = false;
                m_HasPreparedGateLock = false;
            }

            if (m_HasPreparedGateLock || m_GateToUnlock == null)
            {
                m_HasPreparedGateLock = m_GateToUnlock == null || m_RuntimePadlockInstance != null;
                return;
            }

            m_GateToUnlock.ResetToClosedImmediate();
            SpawnRuntimePadlock();
            m_HasPreparedGateLock = m_RuntimePadlockInstance != null;
        }

        void SpawnRuntimePadlock()
        {
            if (m_GateToUnlock == null)
            {
                Debug.LogWarning("[VRCombat] ArenaOpeningEncounter is missing a gate reference.", this);
                return;
            }

            var mountPoint = m_PadlockMountPoint != null ? m_PadlockMountPoint : transform;
            Padlock runtimePadlock = null;

            if (m_PadlockPrefab != null)
            {
                var padlockObject = Instantiate(m_PadlockPrefab, mountPoint.position, mountPoint.rotation, mountPoint);
                padlockObject.transform.localScale = m_PadlockPrefab.transform.localScale;

                runtimePadlock = padlockObject.GetComponent<Padlock>();
                if (runtimePadlock == null)
                {
                    runtimePadlock = padlockObject.AddComponent<Padlock>();
                    var lockDown = FindDescendantByName(padlockObject.transform, "Lock_down");
                    var lockUp = FindDescendantByName(padlockObject.transform, "Lock_up");
                    runtimePadlock.AssignAuthoredParts(lockDown, lockUp);
                }
            }
            else
            {
                runtimePadlock = CreatePadlockFromArenaAssets(mountPoint);
            }

            if (runtimePadlock == null)
            {
                Debug.LogWarning("[VRCombat] ArenaOpeningEncounter could not create a padlock instance.", this);
                return;
            }

            runtimePadlock.name = "Arena Padlock";
            runtimePadlock.transform.localPosition = Vector3.zero;
            runtimePadlock.transform.localRotation = Quaternion.identity;
            runtimePadlock.AssignGate(m_GateToUnlock);
            runtimePadlock.ConfigureKeyRequirement(m_KeyId);
            runtimePadlock.Unlocked -= HandlePadlockUnlocked;
            runtimePadlock.Unlocked += HandlePadlockUnlocked;
            runtimePadlock.ResetPadlock();

            m_RuntimePadlockInstance = runtimePadlock;
            m_Bootstrapper?.RegisterSpawnedRuntimeObject(runtimePadlock.gameObject);
        }

        Padlock CreatePadlockFromArenaAssets(Transform mountPoint)
        {
            var arenaPrefab = Resources.Load<GameObject>(DefaultArenaResourcePath);
            if (arenaPrefab == null)
                return null;

            var lockDownSource = FindDescendantByName(arenaPrefab.transform, "Lock_down");
            var lockUpSource = FindDescendantByName(arenaPrefab.transform, "Lock_up");
            if (lockDownSource == null || lockUpSource == null)
                return null;

            var runtimeRoot = new GameObject("Arena Padlock");
            runtimeRoot.transform.SetParent(mountPoint, false);
            runtimeRoot.transform.localPosition = Vector3.zero;
            runtimeRoot.transform.localRotation = Quaternion.identity;
            runtimeRoot.transform.localScale = Vector3.one;

            var anchorPosition = (lockDownSource.position + lockUpSource.position) * 0.5f;
            var lowerClone = ClonePadlockPart(lockDownSource, runtimeRoot.transform, anchorPosition);
            var upperClone = ClonePadlockPart(lockUpSource, runtimeRoot.transform, anchorPosition);

            var padlock = runtimeRoot.AddComponent<Padlock>();
            padlock.AssignAuthoredParts(lowerClone, upperClone);
            return padlock;
        }

        static Transform ClonePadlockPart(Transform source, Transform parent, Vector3 anchorPosition)
        {
            var clone = Instantiate(source.gameObject, parent, false);
            clone.name = source.name;
            clone.transform.localPosition = source.position - anchorPosition;
            clone.transform.localRotation = source.rotation;
            clone.transform.localScale = source.lossyScale;

            var nestedColliders = clone.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < nestedColliders.Length; i++)
            {
                if (nestedColliders[i] != null)
                    Destroy(nestedColliders[i]);
            }

            var nestedRigidbodies = clone.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < nestedRigidbodies.Length; i++)
            {
                if (nestedRigidbodies[i] != null)
                    Destroy(nestedRigidbodies[i]);
            }

            return clone.transform;
        }

        void SpawnEncounterGroup()
        {
            DetachEncounterEnemyListeners();
            m_EncounterEnemies.Clear();
            m_KeyCarrier = null;

            var goblinGroup = GetEffectiveGoblinGroup();
            if (m_Bootstrapper == null || goblinGroup == null || goblinGroup.Count == 0)
            {
                Debug.LogWarning("[VRCombat] Opening encounter has no goblin entries. Falling back to immediate completion.", this);
                BeginCompletionFlow();
                return;
            }

            var assignedKeyCarrier = false;
            for (var i = 0; i < goblinGroup.Count; i++)
            {
                var entry = goblinGroup[i];
                if (!TryResolveEncounterSpawnPosition(goblinGroup, i, entry, out var spawnPosition))
                    continue;

                var enemy = m_Bootstrapper.SpawnEnemyAt(spawnPosition, entry.rarity, null);
                if (enemy == null)
                    continue;

                enemy.Died += HandleEncounterEnemyDied;
                m_EncounterEnemies.Add(enemy);

                if (!assignedKeyCarrier && entry.dropsKey)
                {
                    m_KeyCarrier = enemy;
                    assignedKeyCarrier = true;
                }
            }

            if (!assignedKeyCarrier && m_EncounterEnemies.Count > 0 && m_CompletionMode == EncounterCompletionMode.RequireGateUnlock)
                m_KeyCarrier = m_EncounterEnemies[0];

            if (m_EncounterEnemies.Count == 0)
            {
                Debug.LogWarning("[VRCombat] Opening encounter could not find any valid spawn positions.", this);
                BeginCompletionFlow();
            }
        }

        IReadOnlyList<GoblinSpawnEntry> GetEffectiveGoblinGroup()
        {
            if (IsValidGoblinGroup(m_GoblinGroup))
                return m_GoblinGroup;

            return s_DefaultGoblinGroup;
        }

        bool IsValidGoblinGroup(GoblinSpawnEntry[] goblinGroup)
        {
            if (goblinGroup == null || goblinGroup.Length == 0)
                return false;

            if (m_CompletionMode != EncounterCompletionMode.RequireGateUnlock)
                return true;

            for (var i = 0; i < goblinGroup.Length; i++)
            {
                if (goblinGroup[i].dropsKey)
                    return true;
            }

            return false;
        }

        void HandleEncounterEnemyDied(CapsuleEnemy enemy)
        {
            if (enemy != null)
                enemy.Died -= HandleEncounterEnemyDied;

            m_EncounterEnemies.Remove(enemy);

            if (m_CompletionMode == EncounterCompletionMode.ClearEnemies)
            {
                if (m_EncounterEnemies.Count == 0)
                    BeginCompletionFlow();

                return;
            }

            var carrierDied = enemy != null && enemy == m_KeyCarrier;
            if (carrierDied)
                m_KeyCarrier = null;

            var shouldDropKey = !m_HasDroppedEncounterKey &&
                m_ActiveKeyObject == null &&
                (carrierDied || m_EncounterEnemies.Count == 0);

            if (shouldDropKey)
            {
                SpawnKeyAt(enemy != null ? enemy.transform.position : transform.position);
                m_HasDroppedEncounterKey = true;
            }
        }

        void SpawnKeyAt(Vector3 enemyPosition)
        {
            if (m_ActiveKeyObject != null)
                return;

            var keyPrefab = ResolveKeyDropPrefab();
            if (keyPrefab == null)
            {
                Debug.LogWarning("[VRCombat] ArenaOpeningEncounter is missing a key drop prefab.", this);
                return;
            }

            var spawnBasePosition = ResolveKeyDropSpawnPosition(enemyPosition);
            var keyClone = Instantiate(
                keyPrefab,
                spawnBasePosition + Vector3.up * Mathf.Max(0.05f, m_KeyDropHeight),
                Quaternion.identity);
            keyClone.name = $"Arena Key {m_KeyId}";
            keyClone.SetActive(true);

            var keyItem = keyClone.GetComponent<KeyItem>() ?? keyClone.AddComponent<KeyItem>();
            keyItem.ConfigureRuntimeInstance(m_KeyId);

            m_ActiveKeyObject = keyClone;
            m_Bootstrapper?.RegisterSpawnedRuntimeObject(keyClone);
            m_Bootstrapper?.ShowRuntimeBanner("The goblin dropped the arena key.", 1.4f);
        }

        Vector3 ResolveKeyDropSpawnPosition(Vector3 desiredWorldPosition)
        {
            if (m_Bootstrapper != null &&
                m_Bootstrapper.TryResolveArenaSpawnPosition(
                    desiredWorldPosition,
                    KeyDropCapsuleRadius,
                    KeyDropCapsuleHeight,
                    out var resolvedSpawnPosition))
            {
                return resolvedSpawnPosition;
            }

            return ResolveGroundedSpawnPosition(desiredWorldPosition);
        }

        GameObject ResolveKeyDropPrefab()
        {
            if (m_KeyDropPrefab != null)
                return m_KeyDropPrefab;

            return Resources.Load<GameObject>(DefaultKeyResourcePath);
        }

        void HandlePadlockUnlocked(Padlock padlock, KeyItem key)
        {
            if (m_ActiveKeyObject != null && key != null && m_ActiveKeyObject == key.gameObject)
                m_ActiveKeyObject = null;

            if (m_CompletionMode == EncounterCompletionMode.RequireGateUnlock)
            {
                BeginCompletionFlow();
                return;
            }

            m_Bootstrapper?.ShowRuntimeBanner("The arena gate unlocks.", 1.25f);
        }

        void BeginCompletionFlow()
        {
            if (m_HasCompleted || m_IsCompleting)
                return;

            m_IsCompleting = true;
            StopArenaMotion();
            m_MotionRoutine = StartCoroutine(CompleteEncounterRoutine());
        }

        IEnumerator CompleteEncounterRoutine()
        {
            yield return AnimateArenaTransforms();

            m_HasCompleted = true;
            m_IsCompleting = false;
            m_MotionRoutine = null;

            var completionMessage = m_CompletionMode == EncounterCompletionMode.RequireGateUnlock
                ? "The arena gate opens."
                : "Encounter cleared.";
            m_Bootstrapper?.ShowRuntimeBanner(completionMessage, 1.35f);
            EncounterCompleted?.Invoke(this);
        }

        IEnumerator AnimateArenaTransforms()
        {
            var hasPlatformMotion = m_PlatformState.isCaptured &&
                m_PlatformState.transform != null &&
                Mathf.Abs(m_PlatformRaiseAmount) > 0.001f;

            var hasBridgeMotion = false;
            for (var i = 0; i < m_BridgeMoves.Length; i++)
            {
                if (m_BridgeStates.Length <= i ||
                    !m_BridgeStates[i].isCaptured ||
                    m_BridgeStates[i].transform == null ||
                    m_BridgeMoves[i].localPositionOffset.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                hasBridgeMotion = true;
                break;
            }

            if (!hasPlatformMotion && !hasBridgeMotion)
                yield break;

            var speed = Mathf.Max(0.1f, m_MotionSpeed);
            var platformTarget = hasPlatformMotion
                ? m_PlatformState.localPosition + Vector3.up * m_PlatformRaiseAmount
                : Vector3.zero;

            var moving = true;
            while (moving)
            {
                moving = false;

                if (hasPlatformMotion && m_PlatformToRaise != null)
                {
                    m_PlatformToRaise.localPosition = Vector3.MoveTowards(
                        m_PlatformToRaise.localPosition,
                        platformTarget,
                        speed * Time.deltaTime);
                    if ((m_PlatformToRaise.localPosition - platformTarget).sqrMagnitude > 0.000001f)
                        moving = true;
                }

                for (var i = 0; i < m_BridgeMoves.Length; i++)
                {
                    if (m_BridgeStates.Length <= i ||
                        !m_BridgeStates[i].isCaptured ||
                        m_BridgeStates[i].transform == null)
                    {
                        continue;
                    }

                    var target = m_BridgeStates[i].localPosition + m_BridgeMoves[i].localPositionOffset;
                    m_BridgeStates[i].transform.localPosition = Vector3.MoveTowards(
                        m_BridgeStates[i].transform.localPosition,
                        target,
                        speed * Time.deltaTime);
                    if ((m_BridgeStates[i].transform.localPosition - target).sqrMagnitude > 0.000001f)
                        moving = true;
                }

                if (moving)
                    yield return null;
            }
        }

        void StopArenaMotion()
        {
            if (m_MotionRoutine == null)
                return;

            StopCoroutine(m_MotionRoutine);
            m_MotionRoutine = null;
        }

        void DestroyActiveKey()
        {
            if (m_ActiveKeyObject == null)
                return;

            Destroy(m_ActiveKeyObject);
            m_ActiveKeyObject = null;
        }

        void DestroyRuntimePadlock()
        {
            UnsubscribePadlock();

            if (m_RuntimePadlockInstance == null)
                return;

            Destroy(m_RuntimePadlockInstance.gameObject);
            m_RuntimePadlockInstance = null;
        }

        void UnsubscribePadlock()
        {
            if (m_RuntimePadlockInstance != null)
                m_RuntimePadlockInstance.Unlocked -= HandlePadlockUnlocked;
        }

        void DetachEncounterEnemyListeners()
        {
            for (var i = 0; i < m_EncounterEnemies.Count; i++)
            {
                var enemy = m_EncounterEnemies[i];
                if (enemy != null)
                    enemy.Died -= HandleEncounterEnemyDied;
            }
        }

        void DisableEncounterHelperColliders()
        {
            var colliders = GetComponents<Collider>();
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null)
                    continue;

                if (collider == m_StartTrigger)
                    continue;

                collider.enabled = false;
            }
        }

        bool TryResolveEncounterSpawnPosition(
            IReadOnlyList<GoblinSpawnEntry> goblinGroup,
            int entryIndex,
            GoblinSpawnEntry entry,
            out Vector3 spawnPosition)
        {
            if (TryResolveEncounterSpawnLocalPosition(entry.localPosition, out spawnPosition))
                return true;

            if (TryResolveDefaultEncounterSpawnPosition(entryIndex, goblinGroup, out spawnPosition))
                return true;

            if (m_Bootstrapper == null)
            {
                spawnPosition = ResolveGroundedSpawnPosition(transform.position);
                return true;
            }

            for (var sampleIndex = 0; sampleIndex < EncounterSpawnFallbackSamples; sampleIndex++)
            {
                var angle = sampleIndex * 360f / EncounterSpawnFallbackSamples;
                var localOffset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * EncounterSpawnFallbackRadius);
                if (TryResolveEncounterSpawnLocalPosition(localOffset, out spawnPosition))
                    return true;
            }

            spawnPosition = default;
            return false;
        }

        bool TryResolveDefaultEncounterSpawnPosition(
            int preferredIndex,
            IReadOnlyList<GoblinSpawnEntry> currentGroup,
            out Vector3 spawnPosition)
        {
            if (s_DefaultGoblinGroup.Length > 0)
            {
                var clampedIndex = Mathf.Clamp(preferredIndex, 0, s_DefaultGoblinGroup.Length - 1);
                if (TryResolveEncounterSpawnLocalPosition(s_DefaultGoblinGroup[clampedIndex].localPosition, out spawnPosition))
                    return true;
            }

            for (var i = 0; i < s_DefaultGoblinGroup.Length; i++)
            {
                if (i == preferredIndex)
                    continue;

                if (TryResolveEncounterSpawnLocalPosition(s_DefaultGoblinGroup[i].localPosition, out spawnPosition))
                    return true;
            }

            if (currentGroup != null)
            {
                for (var i = 0; i < currentGroup.Count; i++)
                {
                    if (i == preferredIndex)
                        continue;

                    if (TryResolveEncounterSpawnLocalPosition(currentGroup[i].localPosition, out spawnPosition))
                        return true;
                }
            }

            spawnPosition = default;
            return false;
        }

        bool TryResolveEncounterSpawnLocalPosition(Vector3 localPosition, out Vector3 spawnPosition)
        {
            var desiredPosition = transform.TransformPoint(localPosition);
            if (TryResolveAuthoredEncounterWorldPosition(desiredPosition, out spawnPosition))
                return true;

            if (m_Bootstrapper == null)
            {
                spawnPosition = ResolveGroundedSpawnPosition(desiredPosition);
                return true;
            }

            spawnPosition = default;
            return false;
        }

        bool TryResolveAuthoredEncounterWorldPosition(Vector3 desiredWorldPosition, out Vector3 spawnPosition)
        {
            if (m_Bootstrapper != null)
            {
                if (m_Bootstrapper.TryResolveLooseGroundSpawnPosition(
                        desiredWorldPosition,
                        GoblinSpawnCapsuleRadius,
                        GoblinSpawnCapsuleHeight,
                        out spawnPosition))
                {
                    m_Bootstrapper.ReserveEncounterSpace(spawnPosition, 6f, 3f, 6f);
                    return true;
                }
            }

            if (m_Bootstrapper == null)
            {
                spawnPosition = ResolveGroundedSpawnPosition(desiredWorldPosition);
                return true;
            }

            spawnPosition = default;
            return false;
        }

        Vector3 ResolveGroundedSpawnPosition(Vector3 desiredWorldPosition)
        {
            var probeOrigin = desiredWorldPosition + Vector3.up * Mathf.Max(0.2f, m_GroundProbeHeight);
            if (Physics.Raycast(
                    probeOrigin,
                    Vector3.down,
                    out var hit,
                    Mathf.Max(0.5f, m_GroundProbeDistance),
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            return desiredWorldPosition;
        }

        static Transform FindDescendantByName(Transform root, string expectedName)
        {
            if (root == null || string.IsNullOrWhiteSpace(expectedName))
                return null;

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                    return child;

                var nested = FindDescendantByName(child, expectedName);
                if (nested != null)
                    return nested;
            }

            return null;
        }
    }
}
