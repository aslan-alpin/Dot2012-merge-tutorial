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

        void LateUpdate()
        {
            if (m_State != RuntimeMountedPickupState.Mounted || !m_HasMountedPose)
                return;

            transform.SetPositionAndRotation(m_MountedPosition, m_MountedRotation);
        }

        void ApplyMountedState()
        {
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

            m_Rigidbody.isKinematic = m_KeepKinematicWhileHeld;
            m_Rigidbody.useGravity = false;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.collisionDetectionMode = m_KeepKinematicWhileHeld
                ? CollisionDetectionMode.ContinuousSpeculative
                : CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.None;
            m_Rigidbody.WakeUp();

            if (m_RiggedChainWeapon != null)
            {
                m_RiggedChainWeapon.SetMountedState(false);
                m_RiggedChainWeapon.SetHeldState(true);
            }
        }

        void ApplyDroppedState()
        {
            if (m_Rigidbody == null)
                return;

            m_Rigidbody.isKinematic = false;
            m_Rigidbody.useGravity = true;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.WakeUp();

            if (m_RiggedChainWeapon != null)
            {
                m_RiggedChainWeapon.SetMountedState(false);
                m_RiggedChainWeapon.SetHeldState(false);
            }
        }
    }
}
