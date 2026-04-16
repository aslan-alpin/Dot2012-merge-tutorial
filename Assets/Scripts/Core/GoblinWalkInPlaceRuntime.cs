using UnityEngine;

namespace VRCombat.Core
{
    [DisallowMultipleComponent]
    public sealed class GoblinWalkInPlaceRuntime : MonoBehaviour
    {
        // The previous test-only runtime controller assignment is intentionally disabled.
        // Goblins are now animated by GoblinAnimationDriver when they are spawned.
    }
}
