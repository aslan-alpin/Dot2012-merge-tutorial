using UnityEngine;

namespace VRCombat.Environment
{
    public sealed class TriggerSpawner : MonoBehaviour
    {
        bool m_HasLoggedDeprecation;

        void OnTriggerEnter(Collider other)
        {
            if (m_HasLoggedDeprecation)
                return;

            m_HasLoggedDeprecation = true;
            Debug.Log("[VRCombat] TriggerSpawner is deprecated. ArenaOpeningEncounter now owns the opening goblin/key flow.", this);
        }
    }
}
