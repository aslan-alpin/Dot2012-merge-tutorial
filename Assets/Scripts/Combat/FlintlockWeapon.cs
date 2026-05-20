using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using VRCombat.Core;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;

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
        const float DefaultReloadDuration = 0.8f;
        const float DefaultProjectileSpeed = 30f;
        const float DefaultProjectileLifetime = 6f;
        const float DefaultProjectileRadius = 0.018f;
        const float AttachedBulletFallbackSeatDistance = 0.008f;
        const float AttachedBulletFallbackDistanceThreshold = 0.08f;
        const float FireSfxMinPitch = 0.96f;
        const float FireSfxMaxPitch = 1.04f;
        const float HitscanRadius = 0.028f;
        const float MinimumVisibleProjectileDistance = 0.6f;
        const string FireSfxResourcePath = "SFX/flintlockfire";
        const string ReloadSfxResourcePath = "SFX/flintlockreload";
        const float MaxSfxPlayDuration = 1f;

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

        [SerializeField]
        float m_ReloadDuration = DefaultReloadDuration;

        [SerializeField]
        AudioClip m_FireSfxClip;

        [SerializeField]
        AudioClip m_ReloadSfxClip;

        XRGrabInteractable m_Interactable;
        XRInputDevice m_HeldDevice;
        XRNode m_HeldHandNode = XRNode.LeftHand;
        IXRSelectInteractor m_HoldingInteractor;
        AudioSource m_FireAudioSource;
        Transform m_TriggerAnimationTransform;
        Transform m_AttachedBulletParent;
        Vector3 m_AttachedBulletLocalPosition;
        Quaternion m_AttachedBulletLocalRotation;
        Vector3 m_AttachedBulletLocalScale = Vector3.one;
        GameObject m_AttachedBulletTemplate;
        bool m_HasHeldHandNode;
        bool m_IsLoaded = true;
        bool m_IsReloading;
        bool m_WasHeldTriggerPressed;
        bool m_WasHeldReloadPressed;
        float m_ReloadCompleteTime = -1f;
        static readonly RaycastHit[] s_ShotHitBuffer = new RaycastHit[16];

        RunProgressionController m_ProgressionController;

        public void ConfigureRuntimeModifiers(RunProgressionController progressionController)
        {
            m_ProgressionController = progressionController;
        }

        public void ConfigureRuntimeSetup(
            GameObject bulletVisual,
            Transform fireOrigin,
            Transform triggerVisual,
            RunProgressionController progressionController = null)
        {
            if (progressionController != null)
                m_ProgressionController = progressionController;

            if (m_AttachedBulletTemplate != null && m_AttachedBulletTemplate != attachedBullet)
                Destroy(m_AttachedBulletTemplate);

            attachedBullet = bulletVisual;
            raycastOrigin = fireOrigin;
            visualTrigger = triggerVisual;
            m_AttachedBulletTemplate = null;
            m_AttachedBulletParent = null;

            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();

            ConfigureGrabInteractable();
            EnsureGripAttachTransform();
            EnsureFireAudioSource();
            CacheAttachedBulletPose();
            EnsureAttachedBulletTemplate();
            ConfigureTriggerAnimation();
            ResetWeaponState();
        }

        void Awake()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();
            ConfigureGrabInteractable();
            EnsureGripAttachTransform();
            EnsureFireAudioSource();
            CacheAttachedBulletPose();
            EnsureAttachedBulletTemplate();
            ConfigureTriggerAnimation();
            ResetWeaponState();
        }

        void EnsureFireAudioSource()
        {
            if (m_FireAudioSource == null)
                m_FireAudioSource = GetComponent<AudioSource>();

            if (m_FireAudioSource == null)
                m_FireAudioSource = gameObject.AddComponent<AudioSource>();

            m_FireAudioSource.playOnAwake = false;
            m_FireAudioSource.loop = false;
            m_FireAudioSource.spatialBlend = 1f;
            m_FireAudioSource.rolloffMode = AudioRolloffMode.Linear;
            m_FireAudioSource.minDistance = 1f;
            m_FireAudioSource.maxDistance = 16f;

            if (m_FireSfxClip == null)
                m_FireSfxClip = Resources.Load<AudioClip>(FireSfxResourcePath);

            if (m_ReloadSfxClip == null)
                m_ReloadSfxClip = Resources.Load<AudioClip>(ReloadSfxResourcePath);
        }

        void OnEnable()
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();

            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
            m_Interactable.activated.AddListener(OnActivated);
            m_Interactable.deactivated.AddListener(OnDeactivated);
        }

        void OnDisable()
        {
            if (m_Interactable == null)
                return;

            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
            m_Interactable.activated.RemoveListener(OnActivated);
            m_Interactable.deactivated.RemoveListener(OnDeactivated);
        }

        void Update()
        {
            UpdateReloadState();

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

        void OnActivated(ActivateEventArgs args)
        {
            if (args.interactorObject is IXRSelectInteractor selectInteractor)
                m_HoldingInteractor = selectInteractor;

            if (!m_HasHeldHandNode)
                m_HasHeldHandNode = TryResolveHeldHandNode(args.interactorObject, out m_HeldHandNode);

            m_WasHeldTriggerPressed = true;
            Fire();
        }

        void OnDeactivated(DeactivateEventArgs args)
        {
            if (m_HoldingInteractor == null || args.interactorObject == m_HoldingInteractor)
                m_WasHeldTriggerPressed = false;
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

        public void CacheAttachedBulletPose()
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

            if (string.Equals(visualTrigger.parent.name, TriggerPivotName, StringComparison.Ordinal))
            {
                m_TriggerAnimationTransform = visualTrigger.parent;
                return;
            }

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

            // Keep trying to resolve hand node each frame until successful
            if (!m_HasHeldHandNode)
            {
                if (!TryResolveHeldHandNode(m_HoldingInteractor, out m_HeldHandNode))
                    return;
                m_HasHeldHandNode = true;
                m_HeldDevice = default; // Reset device when hand node changes
            }

            if (!m_HeldDevice.isValid)
                m_HeldDevice = InputDevices.GetDeviceAtXRNode(m_HeldHandNode);
        }

        float ReadHeldTriggerValue()
        {
            if (m_HoldingInteractor is XRBaseInputInteractor inputInteractor)
            {
                var activateValue = inputInteractor.activateInput.ReadValue();
                if (activateValue > 0f)
                    return activateValue;

                if (inputInteractor.activateInput.ReadIsPerformed())
                    return 1f;
            }

            var inputSystemTriggerValue = ReadHeldInputSystemTriggerValue();
            if (inputSystemTriggerValue > 0f)
                return inputSystemTriggerValue;

            if (TryGetHeldDevice(out var heldDevice))
            {
                if (heldDevice.TryGetFeatureValue(XRCommonUsages.trigger, out var triggerValue))
                    return triggerValue;

                if (heldDevice.TryGetFeatureValue(XRCommonUsages.triggerButton, out var triggerButtonState))
                    return triggerButtonState ? 1f : 0f;
            }

            return 0f;
        }

        bool ReadHeldReloadPressed()
        {
            if (IsHeldInputSystemButtonPressed("primaryButton", "secondaryButton"))
                return true;

            return TryGetHeldDevice(out var heldDevice)
                && ((heldDevice.TryGetFeatureValue(XRCommonUsages.primaryButton, out var primaryReloadPressed) && primaryReloadPressed)
                    || (heldDevice.TryGetFeatureValue(XRCommonUsages.secondaryButton, out var secondaryReloadPressed) && secondaryReloadPressed));
        }

        bool TryGetHeldDevice(out XRInputDevice heldDevice)
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

        float ReadHeldInputSystemTriggerValue()
        {
            if (!m_HasHeldHandNode)
                return 0f;

            var bestTriggerValue = 0f;
            var handController = GetHeldInputSystemController();
            if (TryGetInputSystemAxisValue(handController, "trigger", out var triggerValue))
                bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

            if (TryGetInputSystemAxisValue(handController, "triggerPressed", out triggerValue))
                bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

            if (TryGetInputSystemAxisValue(handController, "triggerButton", out triggerValue))
                bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

            if (TryGetInputSystemAxisValue(handController, "indexButton", out triggerValue))
                bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

            if (bestTriggerValue > 0f)
                return bestTriggerValue;

            var inputSystemDevices = UnityEngine.InputSystem.InputSystem.devices;
            for (var i = 0; i < inputSystemDevices.Count; i++)
            {
                var device = inputSystemDevices[i];
                if (!MatchesHandNode(device, m_HeldHandNode))
                    continue;

                if (TryGetInputSystemAxisValue(device, "trigger", out triggerValue))
                    bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

                if (TryGetInputSystemAxisValue(device, "triggerPressed", out triggerValue))
                    bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

                if (TryGetInputSystemAxisValue(device, "triggerButton", out triggerValue))
                    bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

                if (TryGetInputSystemAxisValue(device, "indexButton", out triggerValue))
                    bestTriggerValue = Mathf.Max(bestTriggerValue, triggerValue);

                if (bestTriggerValue > 0f)
                    return bestTriggerValue;
            }

            return bestTriggerValue;
        }

        bool IsHeldInputSystemButtonPressed(params string[] controlPaths)
        {
            if (!m_HasHeldHandNode || controlPaths == null || controlPaths.Length == 0)
                return false;

            var handController = GetHeldInputSystemController();
            for (var controlIndex = 0; controlIndex < controlPaths.Length; controlIndex++)
            {
                if (IsInputSystemButtonPressed(handController, controlPaths[controlIndex]))
                    return true;
            }

            var inputSystemDevices = UnityEngine.InputSystem.InputSystem.devices;
            for (var i = 0; i < inputSystemDevices.Count; i++)
            {
                var device = inputSystemDevices[i];
                if (!MatchesHandNode(device, m_HeldHandNode))
                    continue;

                for (var controlIndex = 0; controlIndex < controlPaths.Length; controlIndex++)
                {
                    if (IsInputSystemButtonPressed(device, controlPaths[controlIndex]))
                        return true;
                }
            }

            return false;
        }

        UnityEngine.InputSystem.XR.XRController GetHeldInputSystemController()
        {
            return m_HeldHandNode == XRNode.RightHand
                ? UnityEngine.InputSystem.XR.XRController.rightHand
                : UnityEngine.InputSystem.XR.XRController.leftHand;
        }

        static bool TryGetInputSystemAxisValue(UnityEngine.InputSystem.InputDevice device, string controlPath, out float value)
        {
            value = 0f;
            if (device == null || !device.added || string.IsNullOrWhiteSpace(controlPath))
                return false;

            var axisControl = device.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>(controlPath);
            if (axisControl != null)
            {
                value = axisControl.ReadValue();
                return true;
            }

            var buttonControl = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(controlPath);
            if (buttonControl != null)
            {
                value = buttonControl.ReadValue();
                return true;
            }

            return false;
        }

        static bool IsInputSystemButtonPressed(UnityEngine.InputSystem.InputDevice device, string controlPath)
        {
            if (device == null || !device.added || string.IsNullOrWhiteSpace(controlPath))
                return false;

            var buttonControl = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(controlPath);
            return buttonControl != null && buttonControl.isPressed;
        }

        static bool MatchesHandNode(UnityEngine.InputSystem.InputDevice device, XRNode handNode)
        {
            if (device == null)
                return false;

            var desiredUsage = handNode == XRNode.RightHand ? "RightHand" : "LeftHand";
            if (HasInputSystemUsage(device, desiredUsage))
                return true;

            var descriptor = $"{device.displayName} {device.name} {device.layout}";
            var handednessToken = handNode == XRNode.RightHand ? "right" : "left";
            return descriptor.IndexOf(handednessToken, StringComparison.OrdinalIgnoreCase) >= 0;
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

                // Try distance-based detection with both hands
                var hasLeftDistance = TryGetNodeDistanceSquared(XRNode.LeftHand, interactorTransform.position, out var leftDistanceSqr);
                var hasRightDistance = TryGetNodeDistanceSquared(XRNode.RightHand, interactorTransform.position, out var rightDistanceSqr);

                if (hasLeftDistance && hasRightDistance)
                {
                    handNode = leftDistanceSqr <= rightDistanceSqr ? XRNode.LeftHand : XRNode.RightHand;
                    return true;
                }

                // Only one hand device is available - use it
                if (hasLeftDistance)
                {
                    handNode = XRNode.LeftHand;
                    return true;
                }

                if (hasRightDistance)
                {
                    handNode = XRNode.RightHand;
                    return true;
                }
            }

            // Last resort: check if interactor has handedness info via its hierarchy name
            if (interactor is Component interactorComponent)
            {
                var root = interactorComponent.transform.root;
                if (root != null)
                {
                    var rootName = root.name.ToLowerInvariant();
                    if (rootName.Contains("left"))
                    {
                        handNode = XRNode.LeftHand;
                        return true;
                    }
                    if (rootName.Contains("right"))
                    {
                        handNode = XRNode.RightHand;
                        return true;
                    }
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

            if (!device.TryGetFeatureValue(XRCommonUsages.devicePosition, out var devicePosition))
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
            if (!m_IsLoaded || m_IsReloading || !TryCreateProjectileInstance(out var projectileObject))
                return;

            var origin = GetFireOrigin();
            var direction = GetFireDirection();
            var speed = Mathf.Max(1f, m_ProjectileSpeed);
            var maxDistance = Mathf.Max(0.25f, range);
            var visualTravelDistance = maxDistance;
            var ignoredColliders = CollectIgnoredProjectileColliders();
            if (TryResolveShotImpact(origin, direction, maxDistance, ignoredColliders, out var impact) ||
                TryResolveFallbackShotImpact(origin, direction, maxDistance, ignoredColliders, out impact))
            {
                ApplyResolvedShotImpact(impact, direction);
                visualTravelDistance = Mathf.Max(MinimumVisibleProjectileDistance, impact.distance);
            }

            projectileObject.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
            projectileObject.SetActive(true);

            var projectile = projectileObject.GetComponent<FlintlockProjectile>();
            if (projectile == null)
                projectile = projectileObject.AddComponent<FlintlockProjectile>();

            projectile.Initialize(
                speed,
                visualTravelDistance,
                Mathf.Max(0.5f, m_ProjectileLifetime),
                Mathf.Max(0.005f, DefaultProjectileRadius));

            m_IsLoaded = false;
            SetLoadedVisualState();

            if (muzzleFlash != null)
                muzzleFlash.Play();

            PlayFireSfx();
        }

        Vector3 GetFireOrigin()
        {
            var fireDirection = GetFireDirection();
            var fireAnchor = GetFireAnchor();
            return fireAnchor != null
                ? fireAnchor.position + fireDirection * 0.002f
                : transform.position + fireDirection * 0.12f;
        }

        Vector3 GetFireDirection()
        {
            return TryGetPrimaryFireDirection(out var fireDirection)
                ? fireDirection
                : Vector3.forward;
        }

        Transform GetFireAnchor()
        {
            if (raycastOrigin != null)
                return raycastOrigin;

            if (attachedBullet != null)
                return attachedBullet.transform;

            return transform;
        }

        bool TryGetBarrelDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            return TryGetMuzzleToBulletDirection(out direction);
        }

        bool TryGetPrimaryFireDirection(out Vector3 direction)
        {
            direction = Vector3.zero;

            if (TryGetRaycastOriginDirection(out direction))
                return true;

            if (TryGetBarrelDirection(out direction))
                return true;

            var fireAnchor = GetFireAnchor();
            if (fireAnchor != null && fireAnchor.forward.sqrMagnitude > MinimumDirectionMagnitude)
            {
                direction = fireAnchor.forward.normalized;
                return true;
            }

            if (transform.forward.sqrMagnitude > MinimumDirectionMagnitude)
            {
                direction = transform.forward.normalized;
                return true;
            }

            return false;
        }

        bool TryGetRaycastOriginDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            if (raycastOrigin == null || raycastOrigin.forward.sqrMagnitude <= MinimumDirectionMagnitude)
                return false;

            direction = raycastOrigin.forward.normalized;
            return true;
        }

        bool TryGetMuzzleToBulletDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            if (raycastOrigin == null || attachedBullet == null)
                return false;

            var muzzleVector = raycastOrigin.position - attachedBullet.transform.position;
            if (muzzleVector.sqrMagnitude <= MinimumDirectionMagnitude)
                return false;
            direction = muzzleVector.normalized;
            return true;
        }

        bool TryResolveFallbackShotImpact(
            Vector3 origin,
            Vector3 primaryDirection,
            float maxDistance,
            Collider[] ignoredColliders,
            out RaycastHit resolvedHit)
        {
            resolvedHit = default;

            var hasOriginDirection = TryGetRaycastOriginDirection(out var originDirection);
            if (hasOriginDirection &&
                Vector3.Dot(primaryDirection, originDirection) > 0f &&
                Vector3.Dot(primaryDirection, originDirection) < 0.995f &&
                TryResolveShotImpact(origin, originDirection, maxDistance, ignoredColliders, out resolvedHit))
            {
                return true;
            }

            if (!hasOriginDirection &&
                TryGetBarrelDirection(out var barrelDirection) &&
                Vector3.Dot(primaryDirection, barrelDirection) > 0f &&
                Vector3.Dot(primaryDirection, barrelDirection) < 0.995f &&
                TryResolveShotImpact(origin, barrelDirection, maxDistance, ignoredColliders, out resolvedHit))
            {
                return true;
            }

            if (transform.forward.sqrMagnitude > MinimumDirectionMagnitude)
            {
                var fallbackDirection = transform.forward.normalized;
                if (Vector3.Dot(primaryDirection, fallbackDirection) > 0f &&
                    Vector3.Dot(primaryDirection, fallbackDirection) < 0.995f &&
                    TryResolveShotImpact(origin, fallbackDirection, maxDistance, ignoredColliders, out resolvedHit))
                {
                    return true;
                }
            }

            return false;
        }

        bool TryResolveShotImpact(
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            Collider[] ignoredColliders,
            out RaycastHit resolvedHit)
        {
            resolvedHit = default;
            var hitCount = Physics.SphereCastNonAlloc(
                origin,
                HitscanRadius,
                direction,
                s_ShotHitBuffer,
                maxDistance,
                ~0,
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0)
                return false;

            var foundHit = false;
            var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var candidate = s_ShotHitBuffer[i];
                if (candidate.collider == null || ShouldIgnoreShotCollider(candidate.collider, ignoredColliders))
                    continue;

                if (candidate.distance >= bestDistance)
                    continue;

                bestDistance = candidate.distance;
                resolvedHit = candidate;
                foundHit = true;
            }

            return foundHit;
        }

        static bool ShouldIgnoreShotCollider(Collider candidate, Collider[] ignoredColliders)
        {
            if (candidate == null)
                return true;

            if (ignoredColliders == null)
                return false;

            for (var i = 0; i < ignoredColliders.Length; i++)
            {
                var ignoredCollider = ignoredColliders[i];
                if (ignoredCollider == null)
                    continue;

                if (candidate == ignoredCollider)
                    return true;
            }

            return false;
        }

        void ApplyResolvedShotImpact(RaycastHit impact, Vector3 direction)
        {
            if (impact.collider == null)
                return;

            if (TryResolveDamageable(impact.collider, out var damageable))
            {
                var adjustedDamage = damageAmount;
                if (m_ProgressionController != null)
                    adjustedDamage *= m_ProgressionController.GetWeaponDamageMultiplier(WeaponKind.Flintlock);

                damageable.ApplyDamage(adjustedDamage, impact.point, gameObject);
            }

            var impactRigidbody = impact.rigidbody != null ? impact.rigidbody : impact.collider.attachedRigidbody;
            if (impactRigidbody != null && knockbackAmount > 0f)
                impactRigidbody.AddForceAtPosition(direction * knockbackAmount, impact.point, ForceMode.Impulse);
        }

        void PlayFireSfx()
        {
            if (m_FireAudioSource == null || m_FireSfxClip == null)
                return;

            m_FireAudioSource.pitch = UnityEngine.Random.Range(FireSfxMinPitch, FireSfxMaxPitch);
            PlayClipLimited(m_FireSfxClip);
        }

        void PlayReloadSfx()
        {
            if (m_FireAudioSource == null || m_ReloadSfxClip == null)
                return;

            m_FireAudioSource.pitch = UnityEngine.Random.Range(FireSfxMinPitch, FireSfxMaxPitch);
            PlayClipLimited(m_ReloadSfxClip);
        }

        void PlayClipLimited(AudioClip clip)
        {
            if (m_FireAudioSource == null || clip == null)
                return;

            // Use PlayOneShot to allow overlapping sounds
            // Create a temporary AudioSource for time-limited playback
            var tempSource = gameObject.AddComponent<AudioSource>();
            tempSource.spatialBlend = m_FireAudioSource.spatialBlend;
            tempSource.rolloffMode = m_FireAudioSource.rolloffMode;
            tempSource.minDistance = m_FireAudioSource.minDistance;
            tempSource.maxDistance = m_FireAudioSource.maxDistance;
            tempSource.pitch = m_FireAudioSource.pitch;
            tempSource.playOnAwake = false;
            tempSource.clip = clip;
            tempSource.Play();

            var duration = Mathf.Min(clip.length, MaxSfxPlayDuration);
            StartCoroutine(StopAndDestroyAudioAfterDelay(tempSource, duration));
        }

        System.Collections.IEnumerator StopAndDestroyAudioAfterDelay(AudioSource source, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (source != null)
            {
                source.Stop();
                Destroy(source);
            }
        }

        static bool TryResolveDamageable(Collider hitCollider, out IDamageable damageable)
        {
            damageable = null;
            if (hitCollider == null)
                return false;

            if (TryResolveDamageable(hitCollider.transform, out damageable))
                return true;

            return hitCollider.attachedRigidbody != null
                && TryResolveDamageable(hitCollider.attachedRigidbody.transform, out damageable);
        }

        static bool TryResolveDamageable(Transform current, out IDamageable damageable)
        {
            damageable = null;
            while (current != null)
            {
                var behaviours = current.GetComponents<MonoBehaviour>();
                for (var i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IDamageable resolvedDamageable)
                    {
                        damageable = resolvedDamageable;
                        return true;
                    }
                }

                current = current.parent;
            }

            return false;
        }

        bool TryCreateProjectileInstance(out GameObject projectileObject)
        {
            projectileObject = null;

            var projectileTemplate = GetProjectileTemplate();
            if (projectileTemplate != null)
            {
                projectileObject = Instantiate(projectileTemplate);
                projectileObject.name = projectileTemplate.name.Replace(" Template", string.Empty);
                projectileObject.hideFlags = HideFlags.None;
                projectileObject.SetActive(false);
                return true;
            }

            projectileObject = new GameObject("Flintlock Projectile");
            projectileObject.SetActive(false);
            return true;
        }

        GameObject GetProjectileTemplate()
        {
            return m_ProjectileTemplate;
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
            bulletTransform.localPosition = TryGetFallbackAttachedBulletLocalPosition(parent, out var fallbackLocalPosition)
                ? fallbackLocalPosition
                : m_AttachedBulletLocalPosition;
            bulletTransform.localRotation = m_AttachedBulletLocalRotation;
            bulletTransform.localScale = m_AttachedBulletLocalScale;
        }

        bool TryGetFallbackAttachedBulletLocalPosition(Transform parent, out Vector3 localPosition)
        {
            localPosition = Vector3.zero;
            if (parent == null || raycastOrigin == null)
                return false;

            var fireDirection = GetFireDirection();
            if (fireDirection.sqrMagnitude <= MinimumDirectionMagnitude)
                return false;

            var seatWorldPosition = raycastOrigin.position - fireDirection * GetAttachedBulletSeatDistance(fireDirection);
            localPosition = parent.InverseTransformPoint(seatWorldPosition);
            return true;
        }

        float GetAttachedBulletSeatDistance(Vector3 fireDirection)
        {
            var seatDistance = AttachedBulletFallbackSeatDistance;
            if (attachedBullet == null)
                return seatDistance;

            var renderers = attachedBullet.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                var bounds = renderer.bounds;
                var projectedExtent =
                    Mathf.Abs(fireDirection.x) * bounds.extents.x +
                    Mathf.Abs(fireDirection.y) * bounds.extents.y +
                    Mathf.Abs(fireDirection.z) * bounds.extents.z;
                seatDistance = Mathf.Max(seatDistance, projectedExtent + 0.001f);
            }

            return seatDistance;
        }

        Collider[] CollectIgnoredProjectileColliders()
        {
            var weaponColliders = GetComponentsInChildren<Collider>(true);
            if (m_HoldingInteractor == null)
                return weaponColliders;

            var playerReceiver = m_HoldingInteractor.transform.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            var interactorColliders = playerReceiver != null
                ? playerReceiver.GetComponentsInChildren<Collider>(true)
                : m_HoldingInteractor.transform.GetComponentsInChildren<Collider>(true);

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
                attachedBullet.SetActive(m_IsLoaded && !m_IsReloading);
        }

        public void Reload()
        {
            if (m_IsLoaded || m_IsReloading)
                return;

            SetLoadedVisualState();
            m_IsReloading = true;
            m_ReloadCompleteTime = Time.time + Mathf.Max(0.05f, m_ReloadDuration);
        }

        void UpdateReloadState()
        {
            if (!m_IsReloading || Time.time < m_ReloadCompleteTime)
                return;

            m_IsReloading = false;
            m_ReloadCompleteTime = -1f;
            m_IsLoaded = true;
            RestoreAttachedBulletVisual();
            SetLoadedVisualState();
            PlayReloadSfx();
        }

        public void ResetWeaponState()
        {
            ClearHeldState();
            m_IsReloading = false;
            m_ReloadCompleteTime = -1f;
            m_IsLoaded = true;
            RestoreAttachedBulletVisual();
            SetLoadedVisualState();
        }
    }

    [DisallowMultipleComponent]
    public class FlintlockProjectile : MonoBehaviour
    {
        const float MinimumVisualRadius = 0.0125f;
        const float TrailDuration = 0.08f;
        static Material s_TrailMaterial;
        static Material s_GlowMaterial;

        Rigidbody m_Rigidbody;
        float m_MaxTravelDistance;
        float m_TravelDistance;
        float m_LifetimeRemaining;
        float m_Speed;
        Vector3 m_PreviousPosition;

        public void Initialize(
            float speed,
            float maxTravelDistance,
            float lifetimeSeconds,
            float fallbackRadius)
        {
            m_Speed = Mathf.Max(0.01f, speed);
            m_MaxTravelDistance = Mathf.Max(0.25f, maxTravelDistance);
            m_TravelDistance = 0f;
            m_LifetimeRemaining = lifetimeSeconds;
            m_PreviousPosition = transform.position;

            EnsureProjectileCollider(Mathf.Max(0.005f, fallbackRadius));
            m_Rigidbody = GetComponent<Rigidbody>();
            if (m_Rigidbody != null)
            {
                m_Rigidbody.useGravity = false;
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.detectCollisions = false;
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }

            EnsureVisiblePresentation(Mathf.Max(MinimumVisualRadius, fallbackRadius));
        }

        void Update()
        {
            transform.position += transform.forward * (m_Speed * Time.deltaTime);

            m_TravelDistance += Vector3.Distance(transform.position, m_PreviousPosition);
            m_PreviousPosition = transform.position;
            if (m_TravelDistance >= m_MaxTravelDistance)
            {
                Destroy(gameObject);
                return;
            }

            m_LifetimeRemaining -= Time.deltaTime;
            if (m_LifetimeRemaining <= 0f)
                Destroy(gameObject);
        }

        Collider EnsureProjectileCollider(float fallbackRadius)
        {
            var existingColliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < existingColliders.Length; i++)
            {
                var existingCollider = existingColliders[i];
                if (existingCollider != null)
                    existingCollider.enabled = false;
            }

            var existingSphereCollider = GetComponent<SphereCollider>();
            if (existingSphereCollider != null)
            {
                existingSphereCollider.enabled = false;
                return existingSphereCollider;
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
            sphereCollider.isTrigger = true;
            sphereCollider.center = transform.InverseTransformPoint(bounds.center);
            sphereCollider.radius = Mathf.Max(
                fallbackRadius,
                Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)));
            sphereCollider.enabled = false;
            return sphereCollider;
        }

        void EnsureVisiblePresentation(float minimumRadius)
        {
            DisableImportedProjectilePresentation();
            var glowRenderer = EnsureGlowVisual(minimumRadius);
            if (glowRenderer != null)
                glowRenderer.enabled = true;

            var trail = GetComponent<TrailRenderer>();
            if (trail == null)
                trail = gameObject.AddComponent<TrailRenderer>();

            trail.time = TrailDuration;
            trail.minVertexDistance = 0.01f;
            trail.startWidth = minimumRadius * 1.45f;
            trail.endWidth = minimumRadius * 0.55f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.alignment = LineAlignment.View;
            trail.material = GetTrailMaterial();
            trail.startColor = new Color(1f, 0.92f, 0.72f, 0.95f);
            trail.endColor = new Color(1f, 0.72f, 0.26f, 0f);
            trail.enabled = true;
            trail.Clear();
        }

        void DisableImportedProjectilePresentation()
        {
            var cameras = GetComponentsInChildren<Camera>(true);
            for (var i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    cameras[i].enabled = false;
            }

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer is TrailRenderer)
                    continue;

                renderer.enabled = false;
            }
        }

        Renderer EnsureGlowVisual(float minimumRadius)
        {
            var glowTransform = transform.Find("Projectile Glow");
            GameObject glowObject;
            if (glowTransform == null)
            {
                glowObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                glowObject.name = "Projectile Glow";
                glowObject.transform.SetParent(transform, false);

                var glowCollider = glowObject.GetComponent<Collider>();
                if (glowCollider != null)
                    Destroy(glowCollider);
            }
            else
            {
                glowObject = glowTransform.gameObject;
            }

            glowObject.transform.localPosition = Vector3.zero;
            glowObject.transform.localRotation = Quaternion.identity;
            glowObject.transform.localScale = Vector3.one * (minimumRadius * 2.15f);

            var glowRenderer = glowObject.GetComponent<Renderer>();
            if (glowRenderer == null)
                return null;

            glowRenderer.enabled = true;
            glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowRenderer.receiveShadows = false;
            glowRenderer.sharedMaterial = GetGlowMaterial();
            return glowRenderer;
        }

        static Bounds Encapsulate(Bounds a, Bounds b)
        {
            a.Encapsulate(b.min);
            a.Encapsulate(b.max);
            return a;
        }

        static Material GetTrailMaterial()
        {
            if (s_TrailMaterial != null)
                return s_TrailMaterial;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            s_TrailMaterial = new Material(shader)
            {
                name = "Runtime Flintlock Trail Material"
            };
            if (s_TrailMaterial.HasProperty("_Color"))
                s_TrailMaterial.color = new Color(1f, 0.85f, 0.45f, 1f);
            return s_TrailMaterial;
        }

        static Material GetGlowMaterial()
        {
            if (s_GlowMaterial != null)
                return s_GlowMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            s_GlowMaterial = new Material(shader)
            {
                name = "Runtime Flintlock Glow Material"
            };

            if (s_GlowMaterial.HasProperty("_BaseColor"))
                s_GlowMaterial.SetColor("_BaseColor", new Color(1f, 0.82f, 0.42f, 0.95f));
            if (s_GlowMaterial.HasProperty("_Color"))
                s_GlowMaterial.color = new Color(1f, 0.82f, 0.42f, 0.95f);

            return s_GlowMaterial;
        }
    }
}
