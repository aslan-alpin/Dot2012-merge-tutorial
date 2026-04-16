using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using VRCombat.Enemies;
using VRCombat.Player;
using VRCombat.Core;

namespace VRCombat.Environment
{
    [RequireComponent(typeof(BoxCollider))]
    public class TriggerSpawner : MonoBehaviour
    {
        [SerializeField] int m_SpawnCount = 3;
        [SerializeField] EnemyRarity m_MinRarity = EnemyRarity.Common;
        [SerializeField] EnemyRarity m_MaxRarity = EnemyRarity.Epic;
        [SerializeField] GameObject m_KeyPrefab;
        
        bool m_Triggered = false;
        
        void Awake()
        {
            var col = GetComponent<BoxCollider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (m_Triggered) return;
            
            if (other.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null || other.name.Contains("XR Origin"))
            {
                m_Triggered = true;
                SpawnEnemies();
            }
        }

        void SpawnEnemies()
        {
            var bootstrapper = FindFirstObjectByType<VRCombatBootstrapper>();
            if (bootstrapper == null) return;
            
            for (int i = 0; i < m_SpawnCount; i++)
            {
                var randRarity = (EnemyRarity)UnityEngine.Random.Range((int)m_MinRarity, (int)m_MaxRarity + 1);
                
                var pos = transform.position + new Vector3(
                    UnityEngine.Random.Range(-3f, 3f),
                    1f,
                    UnityEngine.Random.Range(-3f, 3f)
                );
                
                // Set key prefix on the first enemy only
                bootstrapper.SpawnEnemyAt(pos, randRarity, i == 0 ? m_KeyPrefab : null);
            }
        }
    }
}
