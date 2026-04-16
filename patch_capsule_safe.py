import re

with open("Assets/Scripts/Enemies/CapsuleEnemy.cs", "r") as f:
    code = f.read()

# Jump/Charge in FixedUpdate
# Replace the ApplyKnockbackMovement chunk in FixedUpdate
fixed_update_target = """            if (m_Target == null || m_IsGrabbed || Time.time < m_ResumeChaseAtTime)
                return;

            MoveTowardsPlayer(Time.fixedDeltaTime);"""

fixed_update_payload = """            if (m_Target == null || m_IsGrabbed || Time.time < m_ResumeChaseAtTime)
                return;

            if (m_Rarity == EnemyRarity.Rare && Time.time > m_NextAbilityTime)
            {
                // Jump attack towards player
                var dir = (m_Target.position - transform.position).normalized;
                m_Rigidbody.AddForce((dir + Vector3.up * 1.5f) * 5f, ForceMode.Impulse);
                m_NextAbilityTime = Time.time + Random.Range(3f, 5f);
            }
            else if (m_Rarity == EnemyRarity.Epic && Time.time > m_NextAbilityTime)
            {
                if (!m_IsCharging)
                {
                    m_IsCharging = true;
                    m_ChargeDirection = (m_Target.position - transform.position).normalized;
                    m_ChargeDirection.y = 0;
                    m_NextAbilityTime = Time.time + 1.5f; // charge duration
                }
            }

            if (m_IsCharging)
            {
                m_Rigidbody.MovePosition(m_Rigidbody.position + m_ChargeDirection * (m_MoveSpeed * 3f * Time.fixedDeltaTime));
                if (Time.time > m_NextAbilityTime)
                {
                    m_IsCharging = false;
                    m_NextAbilityTime = Time.time + Random.Range(4f, 7f);
                }
            }
            else
            {
                MoveTowardsPlayer(Time.fixedDeltaTime);
            }"""
code = code.replace(fixed_update_target, fixed_update_payload)

# Override colors in UpdateColor
update_color_target = "var baseColor = Color.Lerp(m_DamagedColor, m_HealthyColor, normalizedHealth);"
update_color_payload = """
            Color rarityHealthy = m_HealthyColor;
            switch(m_Rarity)
            {
                case EnemyRarity.Common: rarityHealthy = Color.red; break;
                case EnemyRarity.Uncommon: rarityHealthy = Color.green; break;
                case EnemyRarity.Rare: rarityHealthy = Color.blue; break;
                case EnemyRarity.Epic: rarityHealthy = new Color(1f, 0.84f, 0f); break; // Gold
            }
            var baseColor = Color.Lerp(m_DamagedColor, rarityHealthy, normalizedHealth);"""
code = code.replace(update_color_target, update_color_payload)

# Key drop on Death
death_target = "StartCoroutine(AnimateDeathAndDestroy());"
death_payload = """            if (m_DropKeyFlag && m_KeyPrefab != null)
            {
                Instantiate(m_KeyPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
                m_DropKeyFlag = false;
            }
            StartCoroutine(AnimateDeathAndDestroy());"""
code = code.replace(death_target, death_payload)

# Interface implementation to fix CS1061 errors
# Append just before closing brace
interface_payload = """
        public event System.Action<CapsuleEnemy> Died;
        
        public void ApplyStun(float durationSeconds)
        {
            // Simple stun implementation
            m_NextAbilityTime = Mathf.Max(m_NextAbilityTime, Time.time + durationSeconds);
            m_AbilityCooldownEndsAt = Time.time + durationSeconds;
            m_ResumeChaseAtTime = Time.time + durationSeconds;
        }

        public void ApplyImpactImpulse(Vector3 direction, float impulse)
        {
            m_KnockbackVelocity += direction.normalized * impulse;
            m_ResumeChaseAtTime = Time.time + 0.5f;
            m_Rigidbody.AddForce(direction.normalized * impulse, ForceMode.Impulse);
        }

        public void SetPlayerTarget(Transform cameraTransform, PlayerDamageReceiver damageReceiver)
        {
            m_Target = cameraTransform;
            // Optionally store the damage receiver
        }

        public void AssignRenderer(Renderer renderer)
        {
            m_Renderer = renderer;
        }

        public void AttachAnimationDriver(GoblinAnimationDriver driver)
        {
            // m_AnimationDriver = driver;
        }
        
        public void ApplySlow(float speedMultiplier, float durationSeconds, GameObject source) {}
        public void ApplyBurn(float damagePerSecond, float durationSeconds, GameObject source) {}
"""
if "public event System.Action<CapsuleEnemy> Died;" not in code:
    code = code.replace("\n    }\n}\n", interface_payload + "\n    }\n}\n")

with open("Assets/Scripts/Enemies/CapsuleEnemy.cs", "w") as f:
    f.write(code)
print("Updated successfully")
