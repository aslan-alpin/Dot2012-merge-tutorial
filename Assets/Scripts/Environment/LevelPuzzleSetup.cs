using UnityEngine;
using VRCombat.Environment;

namespace VRCombat.Core
{
    public class LevelPuzzleSetup : MonoBehaviour
    {
        void Start()
        {
            var allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            
            Transform padlock = null;
            Transform gate = null;

            // Simple heuristic to find objects
            foreach (var t in allTransforms)
            {
                var nameLower = t.name.ToLower();
                if (nameLower.Contains("padlock")) padlock = t;
                if (nameLower.Contains("gate") || nameLower.Contains("door_arena")) gate = t;
            }

            if (padlock != null && gate != null)
            {
                // Place the padlock on the gate
                padlock.SetParent(gate);
                padlock.localPosition = new Vector3(0, 1.5f, 0); // Rough center height
                
                var gateController = gate.gameObject.AddComponent<GateController>();
                
                var padlockComponent = padlock.gameObject.AddComponent<Padlock>();
                padlockComponent.AssignGate(gateController);

                // Add collider if missing
                if (padlock.GetComponent<Collider>() == null)
                {
                    var col = padlock.gameObject.AddComponent<BoxCollider>();
                    col.size = new Vector3(0.5f, 0.5f, 0.5f);
                    col.isTrigger = false;
                }
            }
        }
    }
}