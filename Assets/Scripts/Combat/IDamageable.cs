using UnityEngine;

namespace VRCombat.Combat
{
    public interface IDamageable
    {
        void ApplyDamage(float amount, Vector3 hitPoint, GameObject source);
    }
}
