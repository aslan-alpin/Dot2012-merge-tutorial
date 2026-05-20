using System.Collections.Generic;
using UnityEngine;

namespace VRCombat.Combat
{
    public enum RuntimeMountedPickupState
    {
        Mounted,
        Held,
        Dropped
    }

    [DisallowMultipleComponent]
    public sealed class RuntimeMountedPickup : MonoBehaviour
    {
        Rigidbody m_Rigidbody;
        RiggedChainWeapon m_RiggedChainWeapon;
        readonly Dictionary<Transform, int> m_OriginalLayerByTransform = new Dictionary<Transform, int>();
        bool m_KeepKinematicWhileHeld;
        Vector3 m_MountedPosition;
        Quaternion m_MountedRotation = Quaternion.identity;
        bool m_HasMountedPose;
        RuntimeMountedPickupState m_State;

        public void Configure(Rigidbody rigidbody, RiggedChainWeapon riggedChainWeapon, bool keepKinematicWhileHeld)
        {
            m_Rigidbody = rigidbody != null ? rigidbody : GetComponent<Rigidbody>();
            m_RiggedChainWeapon = riggedChainWeapon;
            m_KeepKinematicWhileHeld = keepKinematicWhileHeld;
            CaptureMountedPose();
        }

        public void CaptureMountedPose()
        {
            m_MountedPosition = transform.position;
            m_MountedRotation = transform.rotation;
            m_HasMountedPose = true;
        }

        public void SetState(RuntimeMountedPickupState state)
        {
            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>();

            m_State = state;
            switch (m_State)
            {
                case RuntimeMountedPickupState.Mounted:
                    ApplyMountedState();
                    break;
                case RuntimeMountedPickupState.Held:
                    ApplyHeldState();
                    break;
                case RuntimeMountedPickupState.Dropped:
                    ApplyDroppedState();
                    break;
            }
        }

        void OnDisable()
        {
            RestoreOriginalLayers();
        }

        void LateUpdate()
        {
            if (m_State != RuntimeMountedPickupState.Mounted || !m_HasMountedPose)
                return;

            transform.SetPositionAndRotation(m_MountedPosition, m_MountedRotation);
        }

        void ApplyMountedState()
        {
            RestoreOriginalLayers();

            if (m_Rigidbody == null)
                return;

            m_Rigidbody.isKinematic = true;
            m_Rigidbody.useGravity = false;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            if (m_HasMountedPose)
                transform.SetPositionAndRotation(m_MountedPosition, m_MountedRotation);

            if (m_RiggedChainWeapon != null)
            {
                m_RiggedChainWeapon.SetHeldState(false);
                m_RiggedChainWeapon.SetMountedState(true);
            }
        }

        void ApplyHeldState()
        {
            if (m_Rigidbody == null)
                return;

            ApplyHeldQueryLayer();

            m_Rigidbody.isKinematic = m_KeepKinematicWhileHeld;
            m_Rigidbody.useGravity = false;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.collisionDetectionMode = m_KeepKinematicWhileHeld
                ? CollisionDetectionMode.ContinuousSpeculative
                : CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = m_KeepKinematicWhileHeld
                ? RigidbodyInterpolation.None
                : RigidbodyInterpolation.Interpolate;
            m_Rigidbody.WakeUp();

            if (m_RiggedChainWeapon != null)
            {
                m_RiggedChainWeapon.SetHeldState(true);
                m_RiggedChainWeapon.SetMountedState(false);
            }
        }

        void ApplyDroppedState()
        {
            RestoreOriginalLayers();

            if (m_Rigidbody == null)
                return;

            m_Rigidbody.isKinematic = false;
            m_Rigidbody.useGravity = true;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.WakeUp();

            if (m_RiggedChainWeapon != null)
            {
                m_RiggedChainWeapon.SetHeldState(false);
                m_RiggedChainWeapon.SetMountedState(false);
            }
        }

        void ApplyHeldQueryLayer()
        {
            var ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer < 0)
                return;

            var transforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child == null)
                    continue;

                if (!m_OriginalLayerByTransform.ContainsKey(child))
                    m_OriginalLayerByTransform.Add(child, child.gameObject.layer);

                child.gameObject.layer = ignoreRaycastLayer;
            }
        }

        void RestoreOriginalLayers()
        {
            if (m_OriginalLayerByTransform.Count == 0)
                return;

            foreach (var pair in m_OriginalLayerByTransform)
            {
                if (pair.Key != null)
                    pair.Key.gameObject.layer = pair.Value;
            }

            m_OriginalLayerByTransform.Clear();
        }
    }
}
