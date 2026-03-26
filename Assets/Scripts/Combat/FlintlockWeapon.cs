using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRCombat.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class FlintlockWeapon : MonoBehaviour
    {
        const string AttachPointName = "Attach Point";
        const string TriggerPivotName = "Trigger Pivot";
        const float MinimumDirectionMagnitude = 0.0001f;
        const float MaximumReliableFireThreshold = 0.55f;
        const float TriggerReleaseThreshold = 0.2f;
        const float ReloadReleaseThreshold = 0.25f;
        const float DefaultProjectileSpeed = 30f;
        const float DefaultProjectileLifetime = 6f;
        const float DefaultProjectileRadius = 0.018f;

        [Header("Weapon Settings")]
        public float damageAmount = 50f;
        public float knockbackAmount = 50f;
        public float range = 50f;

        [Header("Visuals")]
        public GameObject attachedBullet;
        public Transform visualTrigger;
        public Transform raycastOrigin;

        [Header("Grip Pose")]
        [SerializeField]
        Transform m_GripAttachTransform;

        [Header("Trigger Animation")]
        public Vector3 triggerStartRotation = Vector3.zero;
        public Vector3 triggerEndRotation = new Vector3(-30f, 0f, 0f);

        [SerializeField]
        [Range(0.15f, 0.98f)]
        float m_FireThreshold = 0.72f;

        [Header("Effects")]
        public ParticleSystem muzzleFlash;

        [Header("Projectile")]
        [SerializeField]
        GameObject m_ProjectileTemplate;

        [SerializeField]
        float m_ProjectileSpeed = DefaultProjectileSpeed;

        [SerializeField]
        float m_ProjectileLifetime = DefaultProjectileLifetime;

        XRGrabInteractable m_Interactable;
        InputDevice m_HeldDevice;
        XRNode m_HeldHandNode = XRNode.LeftHand;
        IXRSelectInteractor m_HoldingInteractor;
        Transform m_TriggerAnimationTransform;
        Transform m_AttachedBulletParent;
        Vector3 m_AttachedBulletLocalPosition;
        Quaternion m_AttachedBulletLocalRotation;
        Vector3 m_AttachedBulletLocalScale = Vector3.one;
        GameObject m_AttachedBulletTemplate;
        bool m_HasHeldHandNode;
        bool m_IsLoaded = true;
        bool m_WasHeldTriggerPressed;
        bool m_WasHeldReloadPressed;

        void Awake()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();
            ConfigureGrabInteractable();
            EnsureGripAttachTransform();
            CacheAttachedBulletPose();
            EnsureAttachedBulletTemplate();
            ConfigureTriggerAnimation();
            ResetWeaponState();
        }

        void OnEnable()
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();

            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
        }

        void Update()
        {
            if (m_Interactable == null || !m_Interactable.isSelected)
            {
                ClearHeldState();
                return;
            }

            m_HoldingInteractor ??= m_Interactable.firstInteractorSelecting;
            RefreshHeldDevice();

            var heldTriggerValue = Mathf.Clamp01(ReadHeldTriggerValue());
            UpdateTriggerVisual(heldTriggerValue);

            var isTriggerPressed = heldTriggerValue >= GetEffectiveFireThreshold();
            if (isTriggerPressed && !m_WasHeldTriggerPressed)
                Fire();

            if (isTriggerPressed)
                m_WasHeldTriggerPressed = true;
            else if (heldTriggerValue <= TriggerReleaseThreshold)
                m_WasHeldTriggerPressed = false;

            var isReloadPressed = ReadHeldReloadPressed();
            if (isReloadPressed && !m_WasHeldReloadPressed)
                Reload();

            if (isReloadPressed)
                m_WasHeldReloadPressed = true;
            else if (!isReloadPressed || heldTriggerValue <= ReloadReleaseThreshold)
                m_WasHeldReloadPressed = false;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_HoldingInteractor = args.interactorObject;
            m_HasHeldHandNode = TryResolveHeldHandNode(args.interactorObject, out m_HeldHandNode);
            m_HeldDevice = default;
            RefreshHeldDevice();
            UpdateTriggerVisual(Mathf.Clamp01(ReadHeldTriggerValue()));
            m_WasHeldTriggerPressed = false;
            m_WasHeldReloadPressed = false;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            ClearHeldState();
        }

        void ConfigureGrabInteractable()
        {
            if (m_Interactable == null)
                return;

            m_Interactable.trackPosition = true;
            m_Interactable.trackRotation = true;
            m_Interactable.throwOnDetach = false;
            m_Interactable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            m_Interactable.velocityDamping = 0.9f;
            m_Interactable.velocityScale = 1.05f;
            m_Interactable.angularVelocityDamping = 0.9f;
            m_Interactable.angularVelocityScale = 1.05f;
            m_Interactable.useDynamicAttach = false;
            m_Interactable.matchAttachPosition = false;
            m_Interactable.matchAttachRotation = false;
            m_Interactable.snapToColliderVolume = false;
            m_Interactable.reinitializeDynamicAttachEverySingleGrab = false;
            m_Interactable.attachEaseInTime = 0.04f;
            m_Interactable.smoothPosition = true;
            m_Interactable.smoothPositionAmount = 18f;
            m_Interactable.tightenPosition = 0.92f;
            m_Interactable.smoothRotation = true;
            m_Interactable.smoothRotationAmount = 18f;
            m_Interactable.tightenRotation = 0.92f;
            m_Interactable.smoothScale = false;
            m_Interactable.attachTransform = EnsureGripAttachTransform();

            m_Interactable.colliders.Clear();
            var colliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                m_Interactable.colliders.Add(collider);
            }
        }

        Transform EnsureGripAttachTransform()
        {
            if (m_GripAttachTransform == null && m_Interactable != null && m_Interactable.attachTransform != null)
                m_GripAttachTransform = m_Interactable.attachTransform;

            if (m_GripAttachTransform == null)
            {
                var existingAttachPoint = transform.Find(AttachPointName);
                if (existingAttachPoint != null)
                {
                    m_GripAttachTransform = existingAttachPoint;
                }
                else
                {
                    var attachPointObject = new GameObject(AttachPointName);
                    m_GripAttachTransform = attachPointObject.transform;
                    m_GripAttachTransform.SetParent(transform, false);
                    m_GripAttachTransform.localPosition = Vector3.zero;
                    m_GripAttachTransform.localRotation = Quaternion.identity;
                }
            }

            if (m_GripAttachTransform != null && m_GripAttachTransform.parent != transform)
                m_GripAttachTransform.SetParent(transform, true);

            if (m_Interactable != null)
                m_Interactable.attachTransform = m_GripAttachTransform;

            return m_GripAttachTransform;
        }

        void CacheAttachedBulletPose()
        {
            if (attachedBullet == null)
                return;

            var bulletTransform = attachedBullet.transform;
            m_AttachedBulletParent = bulletTransform.parent;
            m_AttachedBulletLocalPosition = bulletTransform.localPosition;
            m_AttachedBulletLocalRotation = bulletTransform.localRotation;
            m_AttachedBulletLocalScale = bulletTransform.localScale;
        }

        void ConfigureTriggerAnimation()
        {
            m_TriggerAnimationTransform = visualTrigger;
            if (visualTrigger == null || visualTrigger.parent == null)
                return;

            if (Mathf.Abs(triggerEndRotation.x) > 0.01f &&
                Mathf.Abs(triggerEndRotation.y) < 0.01f &&
                Mathf.Abs(triggerEndRotation.z) < 0.01f)
            {
                triggerEndRotation = new Vector3(0f, 0f, triggerEndRotation.x);
            }

            var existingPivot = visualTrigger.parent.Find(TriggerPivotName);
            if (existingPivot != null)
            {
                m_TriggerAnimationTransform = existingPivot;
                return;
            }

            var meshFilter = visualTrigger.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

            var triggerBounds = meshFilter.sharedMesh.bounds;
            if (triggerBounds.size.sqrMagnitude <= MinimumDirectionMagnitude)
                return;

            var hingeLocalPosition = new Vector3(
                triggerBounds.max.x - triggerBounds.size.x * 0.18f,
                triggerBounds.max.y - triggerBounds.size.y * 0.1f,
                triggerBounds.center.z);

            var pivotObject = new GameObject(TriggerPivotName);
            var pivotTransform = pivotObject.transform;
            var triggerParent = visualTrigger.parent;
            pivotTransform.SetParent(triggerParent, false);
            pivotTransform.position = visualTrigger.TransformPoint(hingeLocalPosition);
            pivotTransform.rotation = triggerParent.rotation;
            pivotTransform.localScale = Vector3.one;

            visualTrigger.SetParent(pivotTransform, true);
            m_TriggerAnimationTransform = pivotTransform;
        }

        void RefreshHeldDevice()
        {
            if (m_HoldingInteractor == null)
            {
                m_HeldDevice = default;
                m_HasHeldHandNode = false;
                return;
            }

            if (!m_HasHeldHandNode && !TryResolveHeldHandNode(m_HoldingInteractor, out m_HeldHandNode))
                return;

            m_HasHeldHandNode = true;
            if (!m_HeldDevice.isValid)
                m_HeldDevice = InputDevices.GetDeviceAtXRNode(m_HeldHandNode);
        }

        float ReadHeldTriggerValue()
        {
            if (m_HoldingInteractor is XRBaseInputInteractor inputInteractor)
            {
                if (inputInteractor.activateInput.TryReadValue(out var activateValue))
                    return activateValue;

                if (inputInteractor.activateInput.ReadIsPerformed())
                    return 1f;
            }

            if (TryGetHeldDevice(out var heldDevice))
            {
                if (heldDevice.TryGetFeatureValue(CommonUsages.trigger, out var triggerValue))
                    return triggerValue;

                if (heldDevice.TryGetFeatureValue(CommonUsages.triggerButton, out var triggerButtonState))
                    return triggerButtonState ? 1f : 0f;
            }

            return 0f;
        }

        bool ReadHeldReloadPressed()
        {
            return TryGetHeldDevice(out var heldDevice)
                && heldDevice.TryGetFeatureValue(CommonUsages.primaryButton, out var reloadPressed)
                && reloadPressed;
        }

        bool TryGetHeldDevice(out InputDevice heldDevice)
        {
            heldDevice = m_HeldDevice;
            if (heldDevice.isValid)
                return true;

            if (!m_HasHeldHandNode)
                return false;

            heldDevice = InputDevices.GetDeviceAtXRNode(m_HeldHandNode);
            if (!heldDevice.isValid)
                return false;

            m_HeldDevice = heldDevice;
            return true;
        }

        bool TryResolveHeldHandNode(IXRInteractor interactor, out XRNode handNode)
        {
            handNode = XRNode.LeftHand;
            if (interactor == null)
                return false;

            var interactorTransform = interactor.transform;
            if (interactorTransform != null)
            {
                var interactorName = interactorTransform.name;
                if (interactorName.IndexOf("left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    handNode = XRNode.LeftHand;
                    return true;
                }

                if (interactorName.IndexOf("right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    handNode = XRNode.RightHand;
                    return true;
                }

                if (TryGetNodeDistanceSquared(XRNode.LeftHand, interactorTransform.position, out var leftDistanceSqr) &&
                    TryGetNodeDistanceSquared(XRNode.RightHand, interactorTransform.position, out var rightDistanceSqr))
                {
                    handNode = leftDistanceSqr <= rightDistanceSqr ? XRNode.LeftHand : XRNode.RightHand;
                    return true;
                }

                if (TryGetNodeDistanceSquared(XRNode.LeftHand, interactorTransform.position, out _))
                {
                    handNode = XRNode.LeftHand;
                    return true;
                }

                if (TryGetNodeDistanceSquared(XRNode.RightHand, interactorTransform.position, out _))
                {
                    handNode = XRNode.RightHand;
                    return true;
                }
            }

            return false;
        }

        void ClearHeldState()
        {
            m_HoldingInteractor = null;
            m_HeldDevice = default;
            m_HasHeldHandNode = false;
            m_WasHeldTriggerPressed = false;
            m_WasHeldReloadPressed = false;
            ResetTriggerVisual();
        }

        float GetEffectiveFireThreshold()
        {
            return Mathf.Clamp(Mathf.Min(m_FireThreshold, MaximumReliableFireThreshold), 0.15f, 0.95f);
        }

        static bool TryGetNodeDistanceSquared(XRNode node, Vector3 targetPosition, out float distanceSqr)
        {
            distanceSqr = float.PositiveInfinity;
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            if (!device.TryGetFeatureValue(CommonUsages.devicePosition, out var devicePosition))
                return false;

            distanceSqr = (devicePosition - targetPosition).sqrMagnitude;
            return true;
        }

        void UpdateTriggerVisual(float triggerValue)
        {
            var triggerTransform = m_TriggerAnimationTransform != null ? m_TriggerAnimationTransform : visualTrigger;
            if (triggerTransform != null)
                triggerTransform.localEulerAngles = Vector3.Lerp(triggerStartRotation, triggerEndRotation, triggerValue);
        }

        void ResetTriggerVisual()
        {
            var triggerTransform = m_TriggerAnimationTransform != null ? m_TriggerAnimationTransform : visualTrigger;
            if (triggerTransform != null)
                triggerTransform.localEulerAngles = triggerStartRotation;
        }

        void Fire()
        {
            if (!m_IsLoaded || !TryCreateProjectileInstance(out var projectileObject))
                return;

            var origin = GetFireOrigin();
            var direction = GetFireDirection();
            projectileObject.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
            projectileObject.SetActive(true);

            var projectile = projectileObject.GetComponent<FlintlockProjectile>();
            if (projectile == null)
                projectile = projectileObject.AddComponent<FlintlockProjectile>();

            projectile.Initialize(
                damageAmount,
                knockbackAmount,
                Mathf.Max(1f, m_ProjectileSpeed),
                Mathf.Max(0.5f, m_ProjectileLifetime),
                Mathf.Max(0.005f, DefaultProjectileRadius),
                gameObject,
                CollectIgnoredProjectileColliders());

            m_IsLoaded = false;
            SetLoadedVisualState();

            if (muzzleFlash != null)
                muzzleFlash.Play();
        }

        Vector3 GetFireOrigin()
        {
            if (raycastOrigin != null)
                return raycastOrigin.position;

            if (attachedBullet != null)
                return attachedBullet.transform.position;

            return transform.position + GetFireDirection() * 0.12f;
        }

        Vector3 GetFireDirection()
        {
            if (raycastOrigin != null && raycastOrigin.forward.sqrMagnitude > MinimumDirectionMagnitude)
                return raycastOrigin.forward.normalized;

            if (transform.forward.sqrMagnitude > MinimumDirectionMagnitude)
                return transform.forward.normalized;

            return Vector3.forward;
        }

        bool TryCreateProjectileInstance(out GameObject projectileObject)
        {
            projectileObject = null;

            var projectileTemplate = GetProjectileTemplate();
            if (projectileTemplate == null)
            {
                Debug.LogWarning($"[VRCombat] Flintlock '{name}' is missing a projectile template.", this);
                return false;
            }

            projectileObject = Instantiate(projectileTemplate);
            projectileObject.name = projectileTemplate.name.Replace(" Template", string.Empty);
            projectileObject.hideFlags = HideFlags.None;
            projectileObject.SetActive(false);
            return true;
        }

        GameObject GetProjectileTemplate()
        {
            if (m_ProjectileTemplate != null)
                return m_ProjectileTemplate;

            EnsureAttachedBulletTemplate();
            return m_AttachedBulletTemplate;
        }

        void EnsureAttachedBulletTemplate()
        {
            if (m_AttachedBulletTemplate != null || attachedBullet == null)
                return;

            CacheAttachedBulletPose();
            var templateParent = m_AttachedBulletParent != null ? m_AttachedBulletParent : transform;
            m_AttachedBulletTemplate = Instantiate(attachedBullet, templateParent, false);
            m_AttachedBulletTemplate.name = $"{attachedBullet.name} Template";
            m_AttachedBulletTemplate.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            m_AttachedBulletTemplate.SetActive(false);
        }

        void EnsureAttachedBulletInstance()
        {
            if (attachedBullet != null)
                return;

            EnsureAttachedBulletTemplate();
            if (m_AttachedBulletTemplate == null)
                return;

            var parent = m_AttachedBulletParent != null ? m_AttachedBulletParent : transform;
            attachedBullet = Instantiate(m_AttachedBulletTemplate, parent, false);
            attachedBullet.name = m_AttachedBulletTemplate.name.Replace(" Template", string.Empty);
            attachedBullet.hideFlags = HideFlags.None;
        }

        void RestoreAttachedBulletVisual()
        {
            EnsureAttachedBulletInstance();
            if (attachedBullet == null)
                return;

            var parent = m_AttachedBulletParent != null ? m_AttachedBulletParent : transform;
            var bulletTransform = attachedBullet.transform;
            bulletTransform.SetParent(parent, false);
            bulletTransform.localPosition = m_AttachedBulletLocalPosition;
            bulletTransform.localRotation = m_AttachedBulletLocalRotation;
            bulletTransform.localScale = m_AttachedBulletLocalScale;
        }

        Collider[] CollectIgnoredProjectileColliders()
        {
            var weaponColliders = GetComponentsInChildren<Collider>(true);
            if (m_HoldingInteractor == null)
                return weaponColliders;

            var interactorColliders = m_HoldingInteractor.transform != null
                ? m_HoldingInteractor.transform.GetComponentsInChildren<Collider>(true)
                : System.Array.Empty<Collider>();

            if (interactorColliders.Length == 0)
                return weaponColliders;

            var ignoredColliders = new Collider[weaponColliders.Length + interactorColliders.Length];
            weaponColliders.CopyTo(ignoredColliders, 0);
            interactorColliders.CopyTo(ignoredColliders, weaponColliders.Length);
            return ignoredColliders;
        }

        void SetLoadedVisualState()
        {
            if (attachedBullet != null)
                attachedBullet.SetActive(m_IsLoaded);
        }

        public void Reload()
        {
            if (m_IsLoaded)
                return;

            m_IsLoaded = true;
            RestoreAttachedBulletVisual();
            SetLoadedVisualState();
        }

        public void ResetWeaponState()
        {
            ClearHeldState();
            m_IsLoaded = true;
            RestoreAttachedBulletVisual();
            SetLoadedVisualState();
        }
    }

    [DisallowMultipleComponent]
    public class FlintlockProjectile : MonoBehaviour
    {
        Rigidbody m_Rigidbody;
        GameObject m_Source;
        float m_DamageAmount;
        float m_KnockbackAmount;
        float m_LifetimeRemaining;
        bool m_HasImpacted;

        public void Initialize(
            float damageAmount,
            float knockbackAmount,
            float speed,
            float lifetimeSeconds,
            float fallbackRadius,
            GameObject source,
            Collider[] ignoredColliders)
        {
            m_DamageAmount = damageAmount;
            m_KnockbackAmount = knockbackAmount;
            m_LifetimeRemaining = lifetimeSeconds;
            m_Source = source;
            m_HasImpacted = false;

            var projectileCollider = EnsureProjectileCollider(Mathf.Max(0.005f, fallbackRadius));
            m_Rigidbody = GetComponent<Rigidbody>();
            if (m_Rigidbody == null)
                m_Rigidbody = gameObject.AddComponent<Rigidbody>();

            m_Rigidbody.useGravity = false;
            m_Rigidbody.isKinematic = false;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.linearDamping = 0f;
            m_Rigidbody.angularDamping = 0f;
            m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.linearVelocity = transform.forward * speed;

            if (projectileCollider != null && ignoredColliders != null)
            {
                for (var i = 0; i < ignoredColliders.Length; i++)
                {
                    var ignoredCollider = ignoredColliders[i];
                    if (ignoredCollider == null || ignoredCollider == projectileCollider)
                        continue;

                    Physics.IgnoreCollision(projectileCollider, ignoredCollider, true);
                }
            }
        }

        void Update()
        {
            if (m_HasImpacted)
                return;

            m_LifetimeRemaining -= Time.deltaTime;
            if (m_LifetimeRemaining <= 0f)
                Destroy(gameObject);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null)
                return;

            var hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.ClosestPoint(transform.position);
            HandleImpact(collision.collider, hitPoint);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other == null)
                return;

            HandleImpact(other, other.ClosestPoint(transform.position));
        }

        void HandleImpact(Collider hitCollider, Vector3 hitPoint)
        {
            if (m_HasImpacted || hitCollider == null)
                return;

            if (m_Source != null && (hitCollider.transform == m_Source.transform || hitCollider.transform.IsChildOf(m_Source.transform)))
                return;

            m_HasImpacted = true;
            var damageable = hitCollider.GetComponentInParent<IDamageable>();
            if (damageable != null)
                damageable.ApplyDamage(m_DamageAmount, hitPoint, m_Source != null ? m_Source : gameObject);

            if (hitCollider.attachedRigidbody != null && m_KnockbackAmount > 0f)
                hitCollider.attachedRigidbody.AddForceAtPosition(transform.forward * m_KnockbackAmount, hitPoint, ForceMode.Impulse);

            Destroy(gameObject);
        }

        Collider EnsureProjectileCollider(float fallbackRadius)
        {
            var existingCollider = GetComponent<Collider>();
            if (existingCollider != null)
            {
                existingCollider.enabled = true;
                existingCollider.isTrigger = false;
                return existingCollider;
            }

            var bounds = new Bounds(transform.position, Vector3.one * fallbackRadius * 2f);
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                bounds.Encapsulate(renderer.bounds);
            }

            var sphereCollider = gameObject.AddComponent<SphereCollider>();
            sphereCollider.isTrigger = false;
            sphereCollider.center = transform.InverseTransformPoint(bounds.center);
            sphereCollider.radius = Mathf.Max(
                fallbackRadius,
                Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)));
            return sphereCollider;
        }
    }
}
