using UnityEngine;

namespace VRCombat.Environment
{
    public sealed class GateController : MonoBehaviour
    {
        [SerializeField] float m_OpenSpeed = 2f;
        [SerializeField] float m_DropDistance = 4f;
        [SerializeField] Vector3 m_BlockingColliderCenter = new Vector3(0f, 1.8f, 0f);
        [SerializeField] Vector3 m_BlockingColliderSize = new Vector3(6f, 3.6f, 0.4f);

        BoxCollider m_RuntimeBlockingCollider;
        Vector3 m_ClosedLocalPosition;
        Vector3 m_OpenLocalPosition;
        bool m_IsOpening;
        bool m_HasCapturedClosedPose;

        void Awake()
        {
            CaptureClosedPose();
            EnsureRuntimeSetup();
            ResetToClosedImmediate();
        }

        void Update()
        {
            if (!m_IsOpening)
                return;

            transform.localPosition = Vector3.MoveTowards(
                transform.localPosition,
                m_OpenLocalPosition,
                Mathf.Max(0.01f, m_OpenSpeed) * Time.deltaTime);

            if ((transform.localPosition - m_OpenLocalPosition).sqrMagnitude <= 0.000001f)
                m_IsOpening = false;
        }

        void CaptureClosedPose()
        {
            if (m_HasCapturedClosedPose)
                return;

            m_ClosedLocalPosition = transform.localPosition;
            m_OpenLocalPosition = m_ClosedLocalPosition - new Vector3(0f, Mathf.Max(0.1f, m_DropDistance), 0f);
            m_HasCapturedClosedPose = true;
        }

        void EnsureRuntimeSetup()
        {
            if (GetComponent<Collider>() != null || GetComponentInChildren<Collider>(true) != null)
                return;

            if (m_RuntimeBlockingCollider == null)
                m_RuntimeBlockingCollider = gameObject.AddComponent<BoxCollider>();

            m_RuntimeBlockingCollider.center = m_BlockingColliderCenter;
            m_RuntimeBlockingCollider.size = m_BlockingColliderSize;
            m_RuntimeBlockingCollider.isTrigger = false;
        }

        public void OpenGate()
        {
            CaptureClosedPose();
            EnsureRuntimeSetup();
            m_IsOpening = true;
        }

        public void ResetToClosedImmediate()
        {
            CaptureClosedPose();
            EnsureRuntimeSetup();
            m_IsOpening = false;
            transform.localPosition = m_ClosedLocalPosition;
        }
    }
}
