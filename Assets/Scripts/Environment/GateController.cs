using UnityEngine;

namespace VRCombat.Environment
{
    public class GateController : MonoBehaviour
    {
        [SerializeField] float m_OpenSpeed = 2f;
        [SerializeField] float m_DropDistance = 4f;

        bool m_IsOpen = false;
        Vector3 m_ClosedPosition;
        Vector3 m_TargetPosition;

        void Awake()
        {
            m_ClosedPosition = transform.position;
            m_TargetPosition = m_ClosedPosition - new Vector3(0f, m_DropDistance, 0f);
        }

        void Update()
        {
            if (m_IsOpen)
            {
                transform.position = Vector3.MoveTowards(transform.position, m_TargetPosition, m_OpenSpeed * Time.deltaTime);
            }
        }

        public void OpenGate()
        {
            m_IsOpen = true;
        }
    }
}