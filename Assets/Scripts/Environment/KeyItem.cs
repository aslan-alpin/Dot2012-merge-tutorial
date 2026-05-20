using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRCombat.Combat;

namespace VRCombat.Environment
{
    public sealed class KeyItem : MonoBehaviour
    {
        [SerializeField] int m_KeyId = 1;
        [SerializeField] Vector3 m_FallbackColliderSize = new Vector3(0.18f, 0.18f, 0.48f);
        [SerializeField] Vector3 m_FallbackColliderCenter = Vector3.zero;
        [SerializeField] Vector3 m_GrabRotationOffset = Vector3.zero;
        [SerializeField] bool m_HideTorchGeometryForKey = true;

        Rigidbody m_Rigidbody;
        Collider m_PrimaryCollider;
        XRGrabInteractable m_GrabInteractable;
        RuntimeMountedPickup m_MountedPickup;
        bool m_AreGrabListenersRegistered;
        Vector3 m_LastSampledWorldPosition;
        Vector3 m_CurrentWorldVelocity;
        float m_LastSampledTime = -1f;

        public int KeyId => m_KeyId;
        public Vector3 CurrentWorldVelocity => m_CurrentWorldVelocity;
        public float CurrentSpeed => m_CurrentWorldVelocity.magnitude;

        public void SetKeyId(int keyId)
        {
            m_KeyId = Mathf.Max(0, keyId);
        }

        public void ConfigureRuntimeInstance(int keyId)
        {
            m_KeyId = Mathf.Max(0, keyId);
            ConfigureKeyOnlyVisuals();
            EnsurePhysicsComponents();
            ConfigureRigidbody();
            ConfigureGrabInteractable();
            ConfigureMountedPickup();
            RegisterGrabListeners();
            m_MountedPickup?.SetState(RuntimeMountedPickupState.Dropped);
            ResetVelocityTracking();
        }

        void OnEnable()
        {
            ResetVelocityTracking();
        }

        void OnDestroy()
        {
            UnregisterGrabListeners();
        }

        void LateUpdate()
        {
            SampleWorldVelocity();
        }

        void EnsurePhysicsComponents()
        {
            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

            if (m_PrimaryCollider == null)
                m_PrimaryCollider = GetComponent<Collider>();
            if (m_PrimaryCollider == null)
                m_PrimaryCollider = GetComponentInChildren<Collider>(true);

            if (m_PrimaryCollider != null)
            {
                ConfigureKeyCollider(m_PrimaryCollider);
                return;
            }

            var boxCollider = GetComponent<BoxCollider>() ?? gameObject.AddComponent<BoxCollider>();
            if (TryGetRendererBounds(out var bounds))
            {
                boxCollider.center = bounds.center;
                boxCollider.size = bounds.size;
            }
            else
            {
                boxCollider.center = m_FallbackColliderCenter;
                boxCollider.size = m_FallbackColliderSize;
            }

            m_PrimaryCollider = boxCollider;
            ConfigureKeyCollider(m_PrimaryCollider);
        }

        void ConfigureRigidbody()
        {
            if (m_Rigidbody == null)
                return;

            m_Rigidbody.mass = 0.45f;
            m_Rigidbody.useGravity = true;
            m_Rigidbody.isKinematic = false;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.linearDamping = 0.08f;
            m_Rigidbody.angularDamping = 0.06f;
            m_Rigidbody.WakeUp();
        }

        void ConfigureGrabInteractable()
        {
            if (m_GrabInteractable == null)
                m_GrabInteractable = GetComponent<XRGrabInteractable>() ?? gameObject.AddComponent<XRGrabInteractable>();

            m_GrabInteractable.enabled = true;

            var attachTransform = m_GrabInteractable.attachTransform;
            var createdAttachTransform = false;
            if (attachTransform == null)
            {
                var attachPivot = new GameObject("Grab Attach Point");
                attachPivot.transform.SetParent(transform, false);
                attachTransform = attachPivot.transform;
                createdAttachTransform = true;
            }

            attachTransform.SetParent(transform, false);
            if (createdAttachTransform || attachTransform.name == "Grab Attach Point")
            {
                attachTransform.localPosition = GetFallbackAttachLocalPosition();
                attachTransform.localRotation = GetFallbackAttachLocalRotation(attachTransform.localPosition);
            }
            m_GrabInteractable.attachTransform = attachTransform;
            m_GrabInteractable.trackPosition = true;
            m_GrabInteractable.trackRotation = true;
            m_GrabInteractable.throwOnDetach = false;
            m_GrabInteractable.retainTransformParent = false;
            m_GrabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            m_GrabInteractable.useDynamicAttach = false;
            m_GrabInteractable.matchAttachPosition = false;
            m_GrabInteractable.matchAttachRotation = false;
            m_GrabInteractable.snapToColliderVolume = false;
            m_GrabInteractable.reinitializeDynamicAttachEverySingleGrab = false;
            m_GrabInteractable.attachEaseInTime = 0f;
            m_GrabInteractable.smoothPosition = false;
            m_GrabInteractable.smoothRotation = false;
            m_GrabInteractable.tightenPosition = 1f;
            m_GrabInteractable.tightenRotation = 1f;
            m_GrabInteractable.selectMode = InteractableSelectMode.Single;

            m_GrabInteractable.colliders.Clear();
            var grabColliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < grabColliders.Length; i++)
            {
                var grabCollider = grabColliders[i];
                if (grabCollider == null)
                    continue;

                if (m_HideTorchGeometryForKey && IsTorchGeometryTransform(grabCollider.transform))
                {
                    grabCollider.enabled = false;
                    continue;
                }

                ConfigureKeyCollider(grabCollider);
                m_GrabInteractable.colliders.Add(grabCollider);
            }

            if (m_GrabInteractable.colliders.Count == 0 && m_PrimaryCollider != null)
                m_GrabInteractable.colliders.Add(m_PrimaryCollider);

            EnsureSingleOwnerWhileHeldSelectFilter();
        }

        static void ConfigureKeyCollider(Collider keyCollider)
        {
            if (keyCollider == null)
                return;

            keyCollider.enabled = true;
            keyCollider.isTrigger = false;
        }

        void EnsureSingleOwnerWhileHeldSelectFilter()
        {
            if (m_GrabInteractable == null)
                return;

            var filter = GetComponent<SingleOwnerWhileHeldSelectFilter>();
            if (filter == null)
                filter = gameObject.AddComponent<SingleOwnerWhileHeldSelectFilter>();

            AddSelectFilterIfMissing(m_GrabInteractable, filter);
        }

        static void AddSelectFilterIfMissing(XRBaseInteractable interactable, IXRSelectFilter filter)
        {
            if (interactable == null || filter == null)
                return;

            var filters = new System.Collections.Generic.List<IXRSelectFilter>();
            interactable.selectFilters.GetAll(filters);
            for (var i = 0; i < filters.Count; i++)
            {
                if (object.ReferenceEquals(filters[i], filter))
                    return;
            }

            interactable.selectFilters.Add(filter);
        }

        void ConfigureMountedPickup()
        {
            if (m_Rigidbody == null)
                return;

            if (m_MountedPickup == null)
                m_MountedPickup = GetComponent<RuntimeMountedPickup>() ?? gameObject.AddComponent<RuntimeMountedPickup>();

            m_MountedPickup.Configure(m_Rigidbody, riggedChainWeapon: null, keepKinematicWhileHeld: true);
        }

        void ConfigureKeyOnlyVisuals()
        {
            DisableTorchLighting();

            if (!m_HideTorchGeometryForKey)
                return;

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var keyRenderer = renderers[i];
                if (keyRenderer != null && IsTorchGeometryRenderer(keyRenderer))
                    keyRenderer.enabled = false;
            }

            var colliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var keyCollider = colliders[i];
                if (keyCollider != null && IsTorchGeometryTransform(keyCollider.transform))
                    keyCollider.enabled = false;
            }
        }

        void DisableTorchLighting()
        {
            var torchLightSources = GetComponentsInChildren<TorchLightSource>(true);
            for (var i = 0; i < torchLightSources.Length; i++)
            {
                if (torchLightSources[i] != null)
                    torchLightSources[i].enabled = false;
            }

            var lights = GetComponentsInChildren<Light>(true);
            for (var i = 0; i < lights.Length; i++)
            {
                var keyLight = lights[i];
                if (keyLight != null && IsTorchGeometryTransform(keyLight.transform))
                    keyLight.enabled = false;
            }

            var particles = GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < particles.Length; i++)
            {
                var keyParticles = particles[i];
                if (keyParticles != null && IsTorchGeometryTransform(keyParticles.transform))
                    keyParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var audioSources = GetComponentsInChildren<AudioSource>(true);
            for (var i = 0; i < audioSources.Length; i++)
            {
                var keyAudio = audioSources[i];
                if (keyAudio != null && IsTorchGeometryTransform(keyAudio.transform))
                    keyAudio.Stop();
            }
        }

        void RegisterGrabListeners()
        {
            if (m_AreGrabListenersRegistered || m_GrabInteractable == null)
                return;

            m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
            m_GrabInteractable.selectExited.AddListener(OnSelectExited);
            m_AreGrabListenersRegistered = true;
        }

        void UnregisterGrabListeners()
        {
            if (!m_AreGrabListenersRegistered || m_GrabInteractable == null)
                return;

            m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
            m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
            m_AreGrabListenersRegistered = false;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_MountedPickup?.SetState(RuntimeMountedPickupState.Held);
            ResetVelocityTracking();
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            m_MountedPickup?.SetState(RuntimeMountedPickupState.Dropped);
            ResetVelocityTracking();
        }

        public float GetApproachSpeed(Vector3 worldPoint)
        {
            if (m_CurrentWorldVelocity.sqrMagnitude <= 0.0001f)
                return 0f;

            var toTarget = worldPoint - transform.position;
            if (toTarget.sqrMagnitude <= 0.0001f)
                return m_CurrentWorldVelocity.magnitude;

            return Mathf.Max(0f, Vector3.Dot(m_CurrentWorldVelocity, toTarget.normalized));
        }

        void ResetVelocityTracking()
        {
            m_LastSampledWorldPosition = transform.position;
            m_CurrentWorldVelocity = Vector3.zero;
            m_LastSampledTime = Time.unscaledTime;
        }

        void SampleWorldVelocity()
        {
            var currentTime = Time.unscaledTime;
            if (m_LastSampledTime < 0f)
            {
                ResetVelocityTracking();
                return;
            }

            var deltaTime = currentTime - m_LastSampledTime;
            if (deltaTime <= 0.0001f)
                return;

            var transformVelocity = (transform.position - m_LastSampledWorldPosition) / deltaTime;
            var rigidbodyVelocity = m_Rigidbody != null ? m_Rigidbody.linearVelocity : Vector3.zero;
            m_CurrentWorldVelocity = rigidbodyVelocity.sqrMagnitude > transformVelocity.sqrMagnitude
                ? rigidbodyVelocity
                : transformVelocity;
            m_LastSampledWorldPosition = transform.position;
            m_LastSampledTime = currentTime;
        }

        Vector3 GetFallbackAttachLocalPosition()
        {
            if (!TryGetRendererBounds(out var bounds))
                return Vector3.zero;

            return new Vector3(
                bounds.center.x,
                Mathf.Lerp(bounds.min.y, bounds.max.y, 0.72f),
                Mathf.Lerp(bounds.min.z, bounds.max.z, 0.2f));
        }

        Quaternion GetFallbackAttachLocalRotation(Vector3 attachLocalPosition)
        {
            if (!TryGetRendererBounds(out var bounds))
                return Quaternion.Euler(m_GrabRotationOffset);

            var keyTipDirection = GetKeyTipLocalDirection(bounds, attachLocalPosition);
            if (keyTipDirection.sqrMagnitude <= 0.0001f)
                return Quaternion.Euler(m_GrabRotationOffset);

            var baseRotation = Quaternion.FromToRotation(keyTipDirection.normalized, Vector3.forward);
            return baseRotation * Quaternion.Euler(m_GrabRotationOffset);
        }

        static Vector3 GetKeyTipLocalDirection(Bounds bounds, Vector3 attachLocalPosition)
        {
            var size = bounds.size;
            var dominantAxis = 0;
            if (size.y > size.x && size.y >= size.z)
                dominantAxis = 1;
            else if (size.z > size.x && size.z > size.y)
                dominantAxis = 2;

            var positiveDistance = GetAxisValue(bounds.max, dominantAxis) - GetAxisValue(attachLocalPosition, dominantAxis);
            var negativeDistance = GetAxisValue(attachLocalPosition, dominantAxis) - GetAxisValue(bounds.min, dominantAxis);
            var axisSign = positiveDistance >= negativeDistance ? 1f : -1f;
            return dominantAxis switch
            {
                1 => Vector3.up * axisSign,
                2 => Vector3.forward * axisSign,
                _ => Vector3.right * axisSign
            };
        }

        static float GetAxisValue(Vector3 vector, int axis)
        {
            return axis switch
            {
                1 => vector.y,
                2 => vector.z,
                _ => vector.x
            };
        }

        bool TryGetRendererBounds(out Bounds bounds)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            bounds = default;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (m_HideTorchGeometryForKey && IsTorchGeometryRenderer(renderer))
                    continue;

                var localBounds = ConvertWorldBoundsToLocal(renderer.bounds);
                if (!hasBounds)
                {
                    bounds = localBounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(localBounds.min);
                bounds.Encapsulate(localBounds.max);
            }

            if (hasBounds)
                bounds.Expand(0.03f);

            return hasBounds;
        }

        static bool IsTorchGeometryRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            if (IsTorchGeometryTransform(renderer.transform))
                return true;

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && IsTorchVisualName(skinnedMeshRenderer.sharedMesh?.name))
                return true;

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && IsTorchVisualName(meshFilter.sharedMesh?.name))
                return true;

            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material != null && IsTorchVisualName(material.name))
                    return true;
            }

            return false;
        }

        static bool IsTorchGeometryTransform(Transform candidate)
        {
            var current = candidate;
            while (current != null)
            {
                if (IsTorchVisualName(current.name))
                    return true;

                current = current.parent;
            }

            return false;
        }

        static bool IsTorchVisualName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            var namesTorchVisual = value.IndexOf("Torch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   value.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0;
            var namesKey = value.IndexOf("Key", StringComparison.OrdinalIgnoreCase) >= 0;
            return namesTorchVisual && !namesKey;
        }

        Bounds ConvertWorldBoundsToLocal(Bounds worldBounds)
        {
            var center = transform.InverseTransformPoint(worldBounds.center);
            var extents = worldBounds.extents;
            var localBounds = new Bounds(center, Vector3.zero);

            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = worldBounds.center + Vector3.Scale(extents, new Vector3(x, y, z));
                        localBounds.Encapsulate(transform.InverseTransformPoint(corner));
                    }
                }
            }

            return localBounds;
        }
    }
}
