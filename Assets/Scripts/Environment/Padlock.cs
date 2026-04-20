using System;
using UnityEngine;

namespace VRCombat.Environment
{
    public sealed class Padlock : MonoBehaviour
    {
        struct AuthoredPartState
        {
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public bool IsCaptured;
        }

        [SerializeField] GateController m_LinkedGate;
        [SerializeField] int m_RequiredKeyId = 1;
        [SerializeField] float m_MinUnlockImpactSpeed = 1.2f;
        [SerializeField] Transform m_LockDownPart;
        [SerializeField] Transform m_LockUpPart;
        [SerializeField] Vector3 m_ColliderCenter = Vector3.zero;
        [SerializeField] Vector3 m_ColliderSize = new Vector3(0.32f, 0.38f, 0.18f);
        [SerializeField] float m_SplitImpulse = 1.45f;
        [SerializeField] float m_SplitLift = 0.35f;
        [SerializeField] float m_SplitTorque = 2.25f;

        Rigidbody m_Rigidbody;
        BoxCollider m_BoxCollider;
        Transform m_AuthoredParent;
        Vector3 m_AuthoredLocalPosition;
        Quaternion m_AuthoredLocalRotation = Quaternion.identity;
        Vector3 m_AuthoredLocalScale = Vector3.one;
        AuthoredPartState m_LockDownState;
        AuthoredPartState m_LockUpState;
        readonly Collider[] m_KeyOverlapBuffer = new Collider[12];
        bool m_HasCapturedAuthoredState;
        bool m_IsUnlocked;

        public event Action<Padlock, KeyItem> Unlocked;

        public void AssignGate(GateController gate)
        {
            m_LinkedGate = gate;
        }

        public void ConfigureKeyRequirement(int requiredKeyId)
        {
            m_RequiredKeyId = Mathf.Max(0, requiredKeyId);
        }

        public void AssignAuthoredParts(Transform lockDownPart, Transform lockUpPart)
        {
            m_LockDownPart = lockDownPart;
            m_LockUpPart = lockUpPart;
        }

        void Awake()
        {
            CaptureAuthoredState();
            EnsureRuntimeSetup();
            ResetPadlock();
        }

        void CaptureAuthoredState()
        {
            if (m_HasCapturedAuthoredState)
                return;

            m_AuthoredParent = transform.parent;
            m_AuthoredLocalPosition = transform.localPosition;
            m_AuthoredLocalRotation = transform.localRotation;
            m_AuthoredLocalScale = transform.localScale;
            m_HasCapturedAuthoredState = true;
        }

        void Update()
        {
            if (m_IsUnlocked)
                return;

            TryUnlockFromNearbyKey();
        }

        void EnsureRuntimeSetup()
        {
            EnsureAuthoredParts();
            CaptureAuthoredPartState(ref m_LockDownState, m_LockDownPart);
            CaptureAuthoredPartState(ref m_LockUpState, m_LockUpPart);

            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

            if (m_BoxCollider == null)
                m_BoxCollider = GetComponent<BoxCollider>() ?? gameObject.AddComponent<BoxCollider>();

            if (TryGetRenderableLocalBounds(out var localBounds))
            {
                m_BoxCollider.center = localBounds.center;
                m_BoxCollider.size = localBounds.size;
            }
            else
            {
                m_BoxCollider.center = m_ColliderCenter;
                m_BoxCollider.size = m_ColliderSize;
            }

            m_BoxCollider.isTrigger = false;
        }

        void EnsureAuthoredParts()
        {
            if (m_LockDownPart == null)
                m_LockDownPart = FindChildByName(transform, "Lock_down");
            if (m_LockUpPart == null)
                m_LockUpPart = FindChildByName(transform, "Lock_up");
        }

        void CaptureAuthoredPartState(ref AuthoredPartState state, Transform part)
        {
            if (state.IsCaptured || part == null)
                return;

            state.Parent = part.parent;
            state.LocalPosition = part.localPosition;
            state.LocalRotation = part.localRotation;
            state.LocalScale = part.localScale;
            state.IsCaptured = true;
        }

        public void ResetPadlock()
        {
            CaptureAuthoredState();
            EnsureRuntimeSetup();

            m_IsUnlocked = false;

            if (transform.parent != m_AuthoredParent)
                transform.SetParent(m_AuthoredParent, false);

            transform.localPosition = m_AuthoredLocalPosition;
            transform.localRotation = m_AuthoredLocalRotation;
            transform.localScale = m_AuthoredLocalScale;

            RestoreAuthoredPart(m_LockDownPart, m_LockDownState);
            RestoreAuthoredPart(m_LockUpPart, m_LockUpState);
            SetAuthoredPartPhysicsEnabled(m_LockDownPart, enabled: false);
            SetAuthoredPartPhysicsEnabled(m_LockUpPart, enabled: false);

            if (m_BoxCollider != null)
            {
                m_BoxCollider.enabled = true;
                m_BoxCollider.isTrigger = false;
            }

            if (m_Rigidbody == null)
                return;

            m_Rigidbody.detectCollisions = true;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.useGravity = false;
            m_Rigidbody.isKinematic = true;
            m_Rigidbody.constraints = RigidbodyConstraints.FreezeAll;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.Sleep();
        }

        void RestoreAuthoredPart(Transform part, AuthoredPartState state)
        {
            if (part == null || !state.IsCaptured)
                return;

            part.SetParent(state.Parent, false);
            part.localPosition = state.LocalPosition;
            part.localRotation = state.LocalRotation;
            part.localScale = state.LocalScale;
            part.gameObject.SetActive(true);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (m_IsUnlocked || collision == null)
                return;

            if (TryResolveCollisionUnlock(collision, out var key, out var impactVelocity))
                Unlock(key, impactVelocity);
        }

        void OnCollisionStay(Collision collision)
        {
            if (m_IsUnlocked || collision == null)
                return;

            if (TryResolveCollisionUnlock(collision, out var key, out var impactVelocity))
                Unlock(key, impactVelocity);
        }

        void Unlock(KeyItem key, Vector3 impactVelocity)
        {
            if (m_IsUnlocked)
                return;

            m_IsUnlocked = true;
            m_LinkedGate?.OpenGate();
            Unlocked?.Invoke(this, key);

            if (key != null)
                Destroy(key.gameObject);

            if (TrySplitAuthoredParts(impactVelocity))
                return;

            DropWholePadlock(impactVelocity);
        }

        bool TryResolveCollisionUnlock(Collision collision, out KeyItem key, out Vector3 impactVelocity)
        {
            key = collision.collider != null
                ? collision.collider.GetComponentInParent<KeyItem>()
                : null;
            impactVelocity = Vector3.zero;
            if (key == null || key.KeyId != m_RequiredKeyId)
                return false;

            var collisionVelocity = collision.relativeVelocity;
            var sampledVelocity = key.CurrentWorldVelocity;
            impactVelocity = sampledVelocity.sqrMagnitude > collisionVelocity.sqrMagnitude
                ? sampledVelocity
                : collisionVelocity;

            var requiredImpactSpeed = Mathf.Max(0.01f, m_MinUnlockImpactSpeed);
            return impactVelocity.magnitude >= requiredImpactSpeed ||
                key.GetApproachSpeed(transform.position) >= requiredImpactSpeed;
        }

        void TryUnlockFromNearbyKey()
        {
            if (m_BoxCollider == null || !m_BoxCollider.enabled)
                return;

            var overlapCount = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(m_BoxCollider.center),
                GetWorldHalfExtents(m_BoxCollider) + Vector3.one * 0.01f,
                m_KeyOverlapBuffer,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < overlapCount; i++)
            {
                var overlapCollider = m_KeyOverlapBuffer[i];
                if (overlapCollider == null || overlapCollider.transform.IsChildOf(transform))
                    continue;

                var key = overlapCollider.GetComponentInParent<KeyItem>();
                if (!TryResolveUnlockVelocity(key, out var impactVelocity))
                    continue;

                Unlock(key, impactVelocity);
                return;
            }
        }

        bool TryResolveUnlockVelocity(KeyItem key, out Vector3 impactVelocity)
        {
            impactVelocity = Vector3.zero;
            if (key == null || key.KeyId != m_RequiredKeyId)
                return false;

            var requiredImpactSpeed = Mathf.Max(0.01f, m_MinUnlockImpactSpeed);
            var approachSpeed = key.GetApproachSpeed(transform.position);
            if (approachSpeed < requiredImpactSpeed)
                return false;

            impactVelocity = key.CurrentWorldVelocity;
            if (impactVelocity.sqrMagnitude <= 0.0001f)
            {
                var toPadlock = transform.position - key.transform.position;
                impactVelocity = toPadlock.sqrMagnitude > 0.0001f
                    ? toPadlock.normalized * approachSpeed
                    : transform.forward * approachSpeed;
            }

            return true;
        }

        static Vector3 GetWorldHalfExtents(BoxCollider collider)
        {
            var lossyScale = collider.transform.lossyScale;
            return new Vector3(
                Mathf.Abs(lossyScale.x) * collider.size.x * 0.5f,
                Mathf.Abs(lossyScale.y) * collider.size.y * 0.5f,
                Mathf.Abs(lossyScale.z) * collider.size.z * 0.5f);
        }

        bool TrySplitAuthoredParts(Vector3 impactVelocity)
        {
            if (m_LockDownPart == null || m_LockUpPart == null)
                return false;

            EnsureRuntimeSetup();
            if (!m_LockDownState.IsCaptured || !m_LockUpState.IsCaptured)
                return false;

            if (m_BoxCollider != null)
                m_BoxCollider.enabled = false;

            if (m_Rigidbody != null)
            {
                m_Rigidbody.detectCollisions = false;
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
                m_Rigidbody.useGravity = false;
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.constraints = RigidbodyConstraints.FreezeAll;
            }

            var impactDirection = impactVelocity.sqrMagnitude > 0.0001f
                ? impactVelocity.normalized
                : (transform.forward + Vector3.up * 0.25f).normalized;
            var splitAxis = transform.right.sqrMagnitude > 0.0001f ? transform.right.normalized : Vector3.right;

            ReleaseAuthoredPart(m_LockDownPart, impactDirection, -splitAxis, 0.95f);
            ReleaseAuthoredPart(m_LockUpPart, impactDirection, splitAxis, 1.08f);
            return true;
        }

        void ReleaseAuthoredPart(Transform part, Vector3 impactDirection, Vector3 splitAxis, float impulseMultiplier)
        {
            var rigidbody = SetAuthoredPartPhysicsEnabled(part, enabled: true);
            if (rigidbody == null)
                return;

            var impulseDirection = (impactDirection + splitAxis * 0.48f + Vector3.up * m_SplitLift).normalized;
            rigidbody.AddForce(impulseDirection * m_SplitImpulse * impulseMultiplier, ForceMode.Impulse);
            rigidbody.AddTorque(UnityEngine.Random.onUnitSphere * m_SplitTorque, ForceMode.Impulse);
        }

        void DropWholePadlock(Vector3 impactVelocity)
        {
            if (m_Rigidbody == null)
                return;

            transform.SetParent(null, true);
            m_Rigidbody.constraints = RigidbodyConstraints.None;
            m_Rigidbody.isKinematic = false;
            m_Rigidbody.useGravity = true;
            m_Rigidbody.detectCollisions = true;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;

            var fallImpulse = (impactVelocity.normalized + Vector3.up * 0.2f).normalized;
            m_Rigidbody.AddForce(fallImpulse * 1.6f, ForceMode.Impulse);
            m_Rigidbody.AddTorque(UnityEngine.Random.onUnitSphere * 2.5f, ForceMode.Impulse);
        }

        Rigidbody SetAuthoredPartPhysicsEnabled(Transform part, bool enabled)
        {
            if (part == null)
                return null;

            var partCollider = EnsureAuthoredPartCollider(part);
            var partRigidbody = part.GetComponent<Rigidbody>() ?? part.gameObject.AddComponent<Rigidbody>();
            if (partCollider != null)
                partCollider.enabled = enabled;

            var nestedColliders = part.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < nestedColliders.Length; i++)
            {
                var nestedCollider = nestedColliders[i];
                if (nestedCollider == null || nestedCollider == partCollider)
                    continue;

                nestedCollider.enabled = false;
            }

            partRigidbody.linearVelocity = Vector3.zero;
            partRigidbody.angularVelocity = Vector3.zero;
            partRigidbody.useGravity = enabled;
            partRigidbody.isKinematic = !enabled;
            partRigidbody.detectCollisions = enabled;
            partRigidbody.collisionDetectionMode = enabled
                ? CollisionDetectionMode.ContinuousDynamic
                : CollisionDetectionMode.ContinuousSpeculative;
            partRigidbody.interpolation = enabled
                ? RigidbodyInterpolation.Interpolate
                : RigidbodyInterpolation.None;
            partRigidbody.constraints = enabled ? RigidbodyConstraints.None : RigidbodyConstraints.FreezeAll;
            partRigidbody.mass = 0.2f;

            if (enabled)
                partRigidbody.WakeUp();
            else
                partRigidbody.Sleep();

            return partRigidbody;
        }

        BoxCollider EnsureAuthoredPartCollider(Transform part)
        {
            if (part == null)
                return null;

            var boxCollider = part.GetComponent<BoxCollider>() ?? part.gameObject.AddComponent<BoxCollider>();
            if (TryGetPartRenderableLocalBounds(part, out var localBounds))
            {
                boxCollider.center = localBounds.center;
                boxCollider.size = localBounds.size;
            }
            else
            {
                boxCollider.center = Vector3.zero;
                boxCollider.size = Vector3.one * 0.12f;
            }

            boxCollider.isTrigger = false;
            return boxCollider;
        }

        static bool TryGetPartRenderableLocalBounds(Transform part, out Bounds bounds)
        {
            var renderers = part != null ? part.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            var hasBounds = false;
            bounds = default;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var localBounds = ConvertWorldBoundsToLocal(part, renderer.bounds);
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
                bounds.Expand(0.02f);

            return hasBounds;
        }

        bool TryGetRenderableLocalBounds(out Bounds bounds)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            bounds = default;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var localBounds = ConvertWorldBoundsToLocal(transform, renderer.bounds);
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
                bounds.Expand(0.02f);

            return hasBounds;
        }

        static Bounds ConvertWorldBoundsToLocal(Transform reference, Bounds worldBounds)
        {
            var center = reference.InverseTransformPoint(worldBounds.center);
            var extents = worldBounds.extents;
            var localBounds = new Bounds(center, Vector3.zero);

            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = worldBounds.center + Vector3.Scale(extents, new Vector3(x, y, z));
                        localBounds.Encapsulate(reference.InverseTransformPoint(corner));
                    }
                }
            }

            return localBounds;
        }

        static Transform FindChildByName(Transform root, string expectedName)
        {
            if (root == null || string.IsNullOrWhiteSpace(expectedName))
                return null;

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                    return child;

                var nested = FindChildByName(child, expectedName);
                if (nested != null)
                    return nested;
            }

            return null;
        }
    }
}
