using UnityEngine;

namespace VRCombat.Enemies
{
    public interface IStatusEffectTarget
    {
        void ApplySlow(float speedMultiplier, float durationSeconds, GameObject source);
        void ApplyBurn(float damagePerSecond, float durationSeconds, GameObject source);
        void ApplyStun(float durationSeconds);
    }
}
