using UnityEngine;

namespace VRCombat.Environment
{
    [RequireComponent(typeof(Collider))]
    public class Padlock : MonoBehaviour
    {
        [SerializeField] GateController m_Gate;

        public void AssignGate(GateController gate)
        {
            m_Gate = gate;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.GetComponent<KeyItem>() != null)
            {
                Unlock(collision.gameObject);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<KeyItem>() != null)
            {
                Unlock(other.GetComponentInParent<KeyItem>().gameObject);
            }
        }

        void Unlock(GameObject keyObject)
        {
            if (m_Gate != null)
            {
                m_Gate.OpenGate();
            }

            Destroy(keyObject);

            // Break animation/feel
            var rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.AddExplosionForce(500f, transform.position + Vector3.up * 0.1f, 2f);

            // Disable our lock collider so it falls freely
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            Destroy(gameObject, 3f);
        }
    }
}