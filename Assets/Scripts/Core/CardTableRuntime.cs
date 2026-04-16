using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRCombat.Combat;

namespace VRCombat.Core
{
    [DisallowMultipleComponent]
    public sealed class CardTableRuntime : MonoBehaviour
    {
        const string TableVisualName = "Runtime Table Visual";
        const string CardSocketName = "Card Socket";
        const string TableResourcePath = "CombatModels/table";
        const string CardModelResourcePath = "CombatModels/Cards";
        const float TargetTableHeightMeters = 0.78f;
        const int ChoiceSlotCount = 2;
        const float ImportedCardScale = 0.48f;
        const float CardSurfaceOffsetMeters = 0.0015f;
        const float MinimumCardSpacingMeters = 0.28f;
        const float MaximumCardSpacingMeters = 0.48f;

        static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapShaderId = Shader.PropertyToID("_BaseMap");
        static readonly int ColorShaderId = Shader.PropertyToID("_Color");
        static readonly int MainTexShaderId = Shader.PropertyToID("_MainTex");
        static readonly int CullShaderId = Shader.PropertyToID("_Cull");

        static readonly Dictionary<string, Material> s_CardMaterialCache = new Dictionary<string, Material>();

        Transform m_TableVisualRoot;
        Transform m_CardSocket;
        readonly List<Transform> m_CardSlots = new List<Transform>(ChoiceSlotCount);
        readonly List<GameObject> m_CurrentCardObjects = new List<GameObject>(ChoiceSlotCount);
        Material m_TableFallbackMaterial;
        Material m_CardFallbackMaterial;

        void Awake()
        {
            EnsureRuntimeVisuals();
        }

        public void ClearCard()
        {
            ClearCards();
        }

        public void ClearCards()
        {
            if (m_CurrentCardObjects.Count == 0)
                return;

            var cardsToDestroy = new List<GameObject>(m_CurrentCardObjects);
            m_CurrentCardObjects.Clear();
            for (var i = 0; i < cardsToDestroy.Count; i++)
            {
                if (cardsToDestroy[i] != null)
                    Destroy(cardsToDestroy[i]);
            }
        }

        public Vector3 GetSocketWorldPosition()
        {
            EnsureRuntimeVisuals();
            UpdateCardSlotLayout(1);
            return m_CardSlots.Count > 0 && m_CardSlots[0] != null
                ? m_CardSlots[0].position
                : m_CardSocket != null
                    ? m_CardSocket.position
                    : transform.position + Vector3.up;
        }

        public bool MountCardChoices(
            CardDefinition firstChoice,
            CardDefinition secondChoice,
            RunProgressionController progressionController,
            VRCombatBootstrapper bootstrapper,
            float maxGrabDistance)
        {
            ClearCards();
            EnsureRuntimeVisuals();
            if (m_CardSocket == null)
                return false;

            var cardChoices = new List<CardDefinition>(ChoiceSlotCount);
            if (firstChoice != null)
                cardChoices.Add(firstChoice);
            if (secondChoice != null)
                cardChoices.Add(secondChoice);

            if (cardChoices.Count == 0)
                return false;

            UpdateCardSlotLayout(cardChoices.Count);
            var mountedCardCount = 0;
            for (var i = 0; i < cardChoices.Count && i < m_CardSlots.Count; i++)
            {
                if (MountCardToSlot(cardChoices[i], i, progressionController, bootstrapper, maxGrabDistance))
                    mountedCardCount++;
            }

            return mountedCardCount > 0;
        }

        bool MountCardToSlot(
            CardDefinition cardDefinition,
            int slotIndex,
            RunProgressionController progressionController,
            VRCombatBootstrapper bootstrapper,
            float maxGrabDistance)
        {
            if (cardDefinition == null || slotIndex < 0 || slotIndex >= m_CardSlots.Count || m_CardSlots[slotIndex] == null)
                return false;

            var cardRoot = new GameObject($"{cardDefinition.DisplayName} Card");
            cardRoot.transform.SetParent(m_CardSlots[slotIndex], false);
            cardRoot.transform.localPosition = Vector3.zero;
            cardRoot.transform.localRotation = Quaternion.identity;
            cardRoot.transform.localScale = Vector3.one;

            var hasVisibleImportedVisual = false;
            if (TryInstantiateCardVisual(cardDefinition, cardRoot.transform, out var cardVisualRoot))
            {
                cardVisualRoot.transform.localScale = Vector3.one * ImportedCardScale;
                OrientCardVisualFlat(cardVisualRoot.transform);
                hasVisibleImportedVisual = HasVisibleRenderers(cardVisualRoot) &&
                                          TryGetVisualBounds(cardVisualRoot.transform, cardRoot.transform, out _);
                if (hasVisibleImportedVisual)
                {
                    CenterVisualOnFloor(cardVisualRoot.transform, cardRoot.transform);
                    cardVisualRoot.transform.localPosition += Vector3.up * CardSurfaceOffsetMeters;
                }
                else
                {
                    Destroy(cardVisualRoot);
                }
            }

            if (!hasVisibleImportedVisual)
            {
                var fallbackVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fallbackVisual.name = "Card Visual";
                fallbackVisual.transform.SetParent(cardRoot.transform, false);
                fallbackVisual.transform.localPosition = new Vector3(0f, 0.006f, 0f);
                fallbackVisual.transform.localScale = new Vector3(0.32f, 0.012f, 0.48f);
                ApplyMaterial(fallbackVisual.GetComponent<Renderer>(), GetOrCreateCardFallbackMaterial());
            }

            if (!TryGetVisualBounds(cardRoot.transform, cardRoot.transform, out var localBounds))
                localBounds = new Bounds(new Vector3(0f, 0.006f, 0f), new Vector3(0.32f, 0.012f, 0.48f));

            var collider = cardRoot.AddComponent<BoxCollider>();
            collider.center = localBounds.center;
            collider.size = localBounds.size + new Vector3(0.02f, 0.01f, 0.02f);

            var rigidbody = cardRoot.AddComponent<Rigidbody>();
            rigidbody.mass = 0.08f;
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var interactable = cardRoot.AddComponent<XRGrabInteractable>();
            interactable.trackPosition = true;
            interactable.trackRotation = true;
            interactable.throwOnDetach = false;
            interactable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            interactable.useDynamicAttach = true;
            interactable.matchAttachPosition = true;
            interactable.matchAttachRotation = true;
            interactable.reinitializeDynamicAttachEverySingleGrab = true;
            interactable.attachEaseInTime = 0.04f;
            interactable.smoothPosition = true;
            interactable.tightenPosition = 0.85f;
            interactable.smoothRotation = true;
            interactable.tightenRotation = 0.85f;
            interactable.colliders.Clear();
            interactable.colliders.Add(collider);

            var distanceFilter = cardRoot.AddComponent<MaxGrabDistanceSelectFilter>();
            distanceFilter.Configure(maxGrabDistance);
            interactable.selectFilters.Add(distanceFilter);

            var pickup = cardRoot.AddComponent<CardPickup>();
            pickup.Initialize(cardDefinition, progressionController, bootstrapper, rigidbody, this);

            m_CurrentCardObjects.Add(cardRoot);
            return true;
        }

        void EnsureRuntimeVisuals()
        {
            if (m_TableVisualRoot == null)
            {
                var existing = transform.Find(TableVisualName);
                if (existing != null)
                    m_TableVisualRoot = existing;
            }

            if (m_TableVisualRoot == null)
            {
                var tableRoot = new GameObject(TableVisualName);
                tableRoot.transform.SetParent(transform, false);
                tableRoot.transform.localPosition = Vector3.zero;
                tableRoot.transform.localRotation = Quaternion.identity;
                tableRoot.transform.localScale = Vector3.one;
                m_TableVisualRoot = tableRoot.transform;

                var tablePrefab = Resources.Load<GameObject>(TableResourcePath);
                if (tablePrefab != null)
                {
                    var tableInstance = Instantiate(tablePrefab, m_TableVisualRoot, false);
                    tableInstance.name = "Table Model";
                    StripImportedSceneComponents(tableInstance);
                    RuntimeCombatModelMaterialBinder.Apply(tableInstance, GetOrCreateTableFallbackMaterial());
                    ApplyFallbackMaterialToRenderers(tableInstance, GetOrCreateTableFallbackMaterial());
                }
                else
                {
                    var fallback = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fallback.name = "Table Model";
                    fallback.transform.SetParent(m_TableVisualRoot, false);
                    fallback.transform.localScale = new Vector3(0.8f, 0.78f, 0.52f);
                    ApplyMaterial(fallback.GetComponent<Renderer>(), GetOrCreateTableFallbackMaterial());
                }
            }

            NormalizeVisualHeight(m_TableVisualRoot, TargetTableHeightMeters);
            CenterVisualOnFloor(m_TableVisualRoot, transform);
            EnsureTableCollider();

            EnsureCardSocket();
            UpdateCardSlotLayout(Mathf.Max(1, m_CurrentCardObjects.Count));
        }

        void EnsureCardSocket()
        {
            if (m_CardSocket == null)
            {
                var existingSocket = transform.Find(CardSocketName);
                if (existingSocket != null)
                    m_CardSocket = existingSocket;
            }

            if (m_CardSocket == null)
            {
                var socketObject = new GameObject(CardSocketName);
                socketObject.transform.SetParent(transform, false);
                m_CardSocket = socketObject.transform;
            }

            if (m_CardSlots.Count == ChoiceSlotCount)
                return;

            m_CardSlots.Clear();
            for (var i = 0; i < ChoiceSlotCount; i++)
            {
                var slotName = $"Card Slot {i + 1}";
                var slot = m_CardSocket.Find(slotName);
                if (slot == null)
                {
                    var slotObject = new GameObject(slotName);
                    slotObject.transform.SetParent(m_CardSocket, false);
                    slot = slotObject.transform;
                }

                m_CardSlots.Add(slot);
            }
        }

        void UpdateCardSlotLayout(int activeCardCount)
        {
            if (m_CardSocket == null || m_CardSlots.Count == 0)
                return;

            var hasTableBounds = TryGetVisualBounds(m_TableVisualRoot, transform, out var tableBounds);
            var tabletopCenter = hasTableBounds
                ? new Vector3(tableBounds.center.x, tableBounds.max.y + CardSurfaceOffsetMeters, tableBounds.center.z)
                : new Vector3(0f, TargetTableHeightMeters, 0f);
            var lateralAxis = Vector3.right;
            var lateralSpacing = 0.18f;
            if (hasTableBounds)
            {
                var useXAxis = tableBounds.size.x >= tableBounds.size.z;
                lateralAxis = useXAxis ? Vector3.right : Vector3.forward;
                var wideDimension = useXAxis ? tableBounds.size.x : tableBounds.size.z;
                lateralSpacing = Mathf.Clamp(wideDimension * 0.34f, MinimumCardSpacingMeters, MaximumCardSpacingMeters);
            }

            m_CardSocket.localPosition = tabletopCenter;
            m_CardSocket.localRotation = Quaternion.identity;

            if (activeCardCount <= 1)
            {
                if (m_CardSlots[0] != null)
                {
                    m_CardSlots[0].localPosition = Vector3.zero;
                    m_CardSlots[0].localRotation = Quaternion.identity;
                }

                if (m_CardSlots.Count > 1 && m_CardSlots[1] != null)
                {
                    m_CardSlots[1].localPosition = lateralAxis * (lateralSpacing * 0.5f);
                    m_CardSlots[1].localRotation = Quaternion.identity;
                }

                return;
            }

            var halfSpacing = lateralSpacing * 0.5f;
            for (var i = 0; i < m_CardSlots.Count; i++)
            {
                var slot = m_CardSlots[i];
                if (slot == null)
                    continue;

                var direction = i == 0 ? -1f : 1f;
                slot.localPosition = lateralAxis * (direction * halfSpacing);
                slot.localRotation = Quaternion.identity;
            }
        }

        static void OrientCardVisualFlat(Transform visualRoot)
        {
            if (visualRoot == null || !TryGetVisualBounds(visualRoot, visualRoot, out var bounds))
                return;

            var size = bounds.size;
            Vector3 thicknessAxis;
            if (size.x <= size.y && size.x <= size.z)
                thicknessAxis = Vector3.right;
            else if (size.y <= size.x && size.y <= size.z)
                thicknessAxis = Vector3.up;
            else
                thicknessAxis = Vector3.forward;

            visualRoot.localRotation = Quaternion.FromToRotation(thicknessAxis, Vector3.up);
            if (!TryGetVisualBounds(visualRoot, visualRoot, out bounds))
                return;

            if (bounds.size.z > bounds.size.x)
                visualRoot.localRotation *= Quaternion.Euler(0f, 90f, 0f);
        }

        bool TryInstantiateCardVisual(CardDefinition cardDefinition, Transform parent, out GameObject visualRoot)
        {
            visualRoot = null;
            var modelPrefab = Resources.Load<GameObject>(CardModelResourcePath);
            if (modelPrefab == null || cardDefinition == null || parent == null)
                return false;

            visualRoot = Instantiate(modelPrefab, parent, false);
            visualRoot.name = "Card Visual";
            StripImportedSceneComponents(visualRoot);
            RuntimeCombatModelMaterialBinder.Apply(visualRoot, GetOrCreateCardFallbackMaterial());
            SetCardMeshVisibility(visualRoot, cardDefinition.CardMeshNames);
            ApplyCardArtMaterial(visualRoot, cardDefinition);
            ApplyFallbackMaterialToRenderers(visualRoot, GetOrCreateCardFallbackMaterial());
            if (!HasVisibleRenderers(visualRoot))
            {
                Destroy(visualRoot);
                visualRoot = null;
                return false;
            }

            return true;
        }

        static bool HasVisibleRenderers(GameObject root)
        {
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled)
                    return true;
            }

            return false;
        }

        void EnsureTableCollider()
        {
            if (!TryGetVisualBounds(m_TableVisualRoot, transform, out var tableBounds))
                return;

            var collider = GetComponent<BoxCollider>();
            if (collider == null)
                collider = gameObject.AddComponent<BoxCollider>();

            collider.center = tableBounds.center;
            collider.size = tableBounds.size + new Vector3(0.02f, 0.01f, 0.02f);
        }

        static void NormalizeVisualHeight(Transform visualRoot, float targetHeight)
        {
            if (visualRoot == null || targetHeight <= 0f || !TryGetVisualBounds(visualRoot, visualRoot, out var bounds))
                return;

            var currentHeight = Mathf.Max(0.001f, bounds.size.y);
            var scaleFactor = targetHeight / currentHeight;
            visualRoot.localScale *= scaleFactor;
        }

        static void CenterVisualOnFloor(Transform visualRoot, Transform referenceTransform)
        {
            if (visualRoot == null || referenceTransform == null || !TryGetVisualBounds(visualRoot, referenceTransform, out var bounds))
                return;

            visualRoot.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        static void SetCardMeshVisibility(GameObject cardModelRoot, IReadOnlyList<string> cardMeshNames)
        {
            var renderers = cardModelRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var shouldRender = false;
                if (cardMeshNames != null)
                {
                    for (var j = 0; j < cardMeshNames.Count; j++)
                    {
                        var meshName = cardMeshNames[j];
                        if (string.IsNullOrWhiteSpace(meshName))
                            continue;

                        if (renderer.name.IndexOf(meshName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            shouldRender = true;
                            break;
                        }
                    }
                }

                renderer.enabled = shouldRender;
            }
        }

        void ApplyCardArtMaterial(GameObject visualRoot, CardDefinition cardDefinition)
        {
            if (visualRoot == null || cardDefinition == null || string.IsNullOrWhiteSpace(cardDefinition.CardArtResourcePath))
                return;

            var texture = Resources.Load<Texture2D>(cardDefinition.CardArtResourcePath);
            if (texture == null)
                return;

            if (!s_CardMaterialCache.TryGetValue(cardDefinition.CardArtResourcePath, out var material) || material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
                if (shader == null)
                    return;

                material = new Material(shader)
                {
                    name = $"{cardDefinition.DisplayName} Card Runtime"
                };
                if (material.HasProperty(BaseColorShaderId))
                    material.SetColor(BaseColorShaderId, Color.white);
                if (material.HasProperty(ColorShaderId))
                    material.SetColor(ColorShaderId, Color.white);
                if (material.HasProperty(BaseMapShaderId))
                    material.SetTexture(BaseMapShaderId, texture);
                if (material.HasProperty(MainTexShaderId))
                    material.SetTexture(MainTexShaderId, texture);
                if (material.HasProperty(CullShaderId))
                    material.SetFloat(CullShaderId, 0f);
                material.doubleSidedGI = true;
                s_CardMaterialCache[cardDefinition.CardArtResourcePath] = material;
            }

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                var materials = renderer.sharedMaterials;
                for (var j = 0; j < materials.Length; j++)
                    materials[j] = material;
                renderer.sharedMaterials = materials;
            }
        }

        static bool TryGetVisualBounds(Transform visualRoot, Transform referenceTransform, out Bounds bounds)
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

        static void StripImportedSceneComponents(GameObject root)
        {
            if (root == null)
                return;

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    Destroy(colliders[i]);
            }

            var animators = root.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null)
                    animators[i].enabled = false;
            }

            var cameras = root.GetComponentsInChildren<Camera>(true);
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    Destroy(cameras[i]);
            }

            var audioListeners = root.GetComponentsInChildren<AudioListener>(true);
            for (var i = 0; i < audioListeners.Length; i++)
            {
                if (audioListeners[i] != null)
                    Destroy(audioListeners[i]);
            }

            var lights = root.GetComponentsInChildren<Light>(true);
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                    Destroy(lights[i]);
            }
        }

        void ApplyFallbackMaterialToRenderers(GameObject root, Material fallbackMaterial)
        {
            if (root == null || fallbackMaterial == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
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

        void ApplyMaterial(Renderer renderer, Material material)
        {
            if (renderer == null || material == null)
                return;

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = material;
                return;
            }

            for (var i = 0; i < materials.Length; i++)
                materials[i] = material;
            renderer.sharedMaterials = materials;
        }

        Material GetOrCreateTableFallbackMaterial()
        {
            if (m_TableFallbackMaterial != null)
                return m_TableFallbackMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
            if (shader == null)
                return null;

            m_TableFallbackMaterial = new Material(shader)
            {
                name = "Runtime Table Fallback"
            };
            if (m_TableFallbackMaterial.HasProperty(BaseColorShaderId))
                m_TableFallbackMaterial.SetColor(BaseColorShaderId, new Color(0.43f, 0.26f, 0.16f, 1f));
            if (m_TableFallbackMaterial.HasProperty(ColorShaderId))
                m_TableFallbackMaterial.SetColor(ColorShaderId, new Color(0.43f, 0.26f, 0.16f, 1f));
            return m_TableFallbackMaterial;
        }

        Material GetOrCreateCardFallbackMaterial()
        {
            if (m_CardFallbackMaterial != null)
                return m_CardFallbackMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
            if (shader == null)
                return null;

            m_CardFallbackMaterial = new Material(shader)
            {
                name = "Runtime Card Fallback"
            };
            if (m_CardFallbackMaterial.HasProperty(BaseColorShaderId))
                m_CardFallbackMaterial.SetColor(BaseColorShaderId, new Color(0.93f, 0.9f, 0.78f, 1f));
            if (m_CardFallbackMaterial.HasProperty(ColorShaderId))
                m_CardFallbackMaterial.SetColor(ColorShaderId, new Color(0.93f, 0.9f, 0.78f, 1f));
            if (m_CardFallbackMaterial.HasProperty(CullShaderId))
                m_CardFallbackMaterial.SetFloat(CullShaderId, 0f);
            m_CardFallbackMaterial.doubleSidedGI = true;
            return m_CardFallbackMaterial;
        }

        public void HandleCardChosen(GameObject selectedCardObject)
        {
            for (var i = m_CurrentCardObjects.Count - 1; i >= 0; i--)
            {
                var cardObject = m_CurrentCardObjects[i];
                if (cardObject == null)
                {
                    m_CurrentCardObjects.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(cardObject, selectedCardObject))
                    continue;

                m_CurrentCardObjects.RemoveAt(i);
                Destroy(cardObject);
            }
        }

        public void UnregisterCard(GameObject cardObject)
        {
            if (cardObject == null)
                return;

            m_CurrentCardObjects.Remove(cardObject);
        }
    }

    [DisallowMultipleComponent]
    public sealed class CardPickup : MonoBehaviour
    {
        CardDefinition m_CardDefinition;
        RunProgressionController m_ProgressionController;
        VRCombatBootstrapper m_Bootstrapper;
        Rigidbody m_Rigidbody;
        CardTableRuntime m_TableRuntime;
        bool m_Consumed;

        public void Initialize(
            CardDefinition cardDefinition,
            RunProgressionController progressionController,
            VRCombatBootstrapper bootstrapper,
            Rigidbody rigidbody,
            CardTableRuntime tableRuntime)
        {
            m_CardDefinition = cardDefinition;
            m_ProgressionController = progressionController;
            m_Bootstrapper = bootstrapper;
            m_Rigidbody = rigidbody != null ? rigidbody : GetComponent<Rigidbody>();
            m_TableRuntime = tableRuntime;

            var interactable = GetComponent<XRGrabInteractable>();
            if (interactable != null)
                interactable.selectEntered.AddListener(OnSelectEntered);

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
            }
        }

        void OnDestroy()
        {
            var interactable = GetComponent<XRGrabInteractable>();
            if (interactable != null)
                interactable.selectEntered.RemoveListener(OnSelectEntered);

            m_TableRuntime?.UnregisterCard(gameObject);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (m_Consumed || m_CardDefinition == null || m_Bootstrapper == null)
                return;

            m_Consumed = true;
            var rewardPosition = transform.position;
            var rewardRotation = transform.rotation;
            m_TableRuntime?.HandleCardChosen(gameObject);

            switch (m_CardDefinition.RewardType)
            {
                case CardRewardType.Weapon:
                    m_ProgressionController?.RegisterWeaponAcquired(m_CardDefinition.WeaponKind);
                    m_Bootstrapper.SpawnCardRewardWeapon(m_CardDefinition.WeaponKind, rewardPosition, rewardRotation);
                    m_Bootstrapper.ShowRuntimeBanner($"{m_CardDefinition.DisplayName} acquired", 1.25f);
                    break;
                case CardRewardType.Spell:
                    if (m_ProgressionController != null && m_ProgressionController.UnlockSpell(m_CardDefinition.SpellKind))
                        m_Bootstrapper.ShowRuntimeBanner($"{m_CardDefinition.DisplayName} unlocked", 1.25f);
                    break;
            }

            Destroy(gameObject);
        }
    }
}
