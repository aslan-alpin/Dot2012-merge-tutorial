using UnityEngine;

namespace VRCombat.Combat
{
    public enum SceneAuthoredPickupStartMode
    {
        Mounted,
        Loose
    }

    [DisallowMultipleComponent]
    public sealed class SceneAuthoredPickup : MonoBehaviour
    {
        Rigidbody m_Rigidbody;
        RuntimeMountedPickup m_MountedPickup;
        SceneAuthoredPickupStartMode m_StartMode;
        Vector3 m_AuthoredPosition;
        Quaternion m_AuthoredRotation = Quaternion.identity;
        Vector3 m_AuthoredScale = Vector3.one;
        bool m_HasAuthoredPose;

        public void Configure(
            Rigidbody rigidbody,
            RuntimeMountedPickup mountedPickup,
            SceneAuthoredPickupStartMode startMode)
        {
            m_Rigidbody = rigidbody != null ? rigidbody : GetComponent<Rigidbody>();
            m_MountedPickup = mountedPickup != null ? mountedPickup : GetComponent<RuntimeMountedPickup>();
            m_StartMode = startMode;
            CaptureAuthoredPose();
        }

        public void CaptureAuthoredPose()
        {
            m_AuthoredPosition = transform.position;
            m_AuthoredRotation = transform.rotation;
            m_AuthoredScale = transform.localScale;
            m_HasAuthoredPose = true;

            if (m_MountedPickup != null)
                m_MountedPickup.CaptureMountedPose();
        }

        public void ResetToInitialState()
        {
            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>();
            if (m_MountedPickup == null)
                m_MountedPickup = GetComponent<RuntimeMountedPickup>();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }

            if (m_HasAuthoredPose)
            {
                transform.SetPositionAndRotation(m_AuthoredPosition, m_AuthoredRotation);
                transform.localScale = m_AuthoredScale;
            }

            if (m_MountedPickup != null)
            {
                m_MountedPickup.CaptureMountedPose();
                m_MountedPickup.SetState(
                    m_StartMode == SceneAuthoredPickupStartMode.Mounted
                        ? RuntimeMountedPickupState.Mounted
                        : RuntimeMountedPickupState.Dropped);
                return;
            }

            if (m_Rigidbody == null)
                return;

            if (m_StartMode == SceneAuthoredPickupStartMode.Mounted)
            {
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.useGravity = false;
                m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                return;
            }

            m_Rigidbody.isKinematic = false;
            m_Rigidbody.useGravity = true;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.WakeUp();
        }
    }

    [DisallowMultipleComponent]
    public sealed class SceneAuthoredResettable : MonoBehaviour
    {
        Rigidbody m_Rigidbody;
        Transform m_AuthoredParent;
        Vector3 m_AuthoredLocalPosition;
        Quaternion m_AuthoredLocalRotation = Quaternion.identity;
        Vector3 m_AuthoredLocalScale = Vector3.one;
        bool m_AuthoredActive = true;
        bool m_HasAuthoredState;
        bool m_UseGravity;
        bool m_IsKinematic;
        bool m_DetectCollisions = true;
        RigidbodyInterpolation m_Interpolation;
        CollisionDetectionMode m_CollisionDetectionMode;
        RigidbodyConstraints m_Constraints;

        public void Configure(Rigidbody rigidbody = null)
        {
            m_Rigidbody = rigidbody != null ? rigidbody : GetComponent<Rigidbody>();
            CaptureAuthoredState();
        }

        public void CaptureAuthoredState()
        {
            m_AuthoredParent = transform.parent;
            m_AuthoredLocalPosition = transform.localPosition;
            m_AuthoredLocalRotation = transform.localRotation;
            m_AuthoredLocalScale = transform.localScale;
            m_AuthoredActive = gameObject.activeSelf;
            m_HasAuthoredState = true;

            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Rigidbody == null)
                return;

            m_UseGravity = m_Rigidbody.useGravity;
            m_IsKinematic = m_Rigidbody.isKinematic;
            m_DetectCollisions = m_Rigidbody.detectCollisions;
            m_Interpolation = m_Rigidbody.interpolation;
            m_CollisionDetectionMode = m_Rigidbody.collisionDetectionMode;
            m_Constraints = m_Rigidbody.constraints;
        }

        public void ResetToInitialState()
        {
            if (!m_HasAuthoredState)
                CaptureAuthoredState();

            transform.SetParent(m_AuthoredParent, false);
            transform.localPosition = m_AuthoredLocalPosition;
            transform.localRotation = m_AuthoredLocalRotation;
            transform.localScale = m_AuthoredLocalScale;

            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
                m_Rigidbody.useGravity = m_UseGravity;
                m_Rigidbody.isKinematic = m_IsKinematic;
                m_Rigidbody.detectCollisions = m_DetectCollisions;
                m_Rigidbody.interpolation = m_Interpolation;
                m_Rigidbody.collisionDetectionMode = m_CollisionDetectionMode;
                m_Rigidbody.constraints = m_Constraints;

                if (m_IsKinematic)
                    m_Rigidbody.Sleep();
                else
                    m_Rigidbody.WakeUp();
            }

            gameObject.SetActive(m_AuthoredActive);
        }
    }
}
