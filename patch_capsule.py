with open('Assets/Scripts/Enemies/CapsuleEnemy.cs', 'r') as f:
    content = f.read()

# 1. Fields
fields_code = """        float m_LastAttackTime = -100f;
        float m_StunEndsAt = -100f;

        EnemyRarity m_Rarity = EnemyRarity.Common;
        public bool m_DropKeyFlag = false;
        public GameObject m_KeyPrefab;
"""
content = content.replace("        float m_StunEndsAt = -100f;\n", fields_code)

# 2. Init Method
init_method = """        public event Action<CapsuleEnemy> Died;

        public void SetRarity(EnemyRarity rarity, GameObject keyPrefab = null)
        {
            m_Rarity = rarity;
            m_DropKeyFlag = (keyPrefab != null);
            m_KeyPrefab = keyPrefab;

            switch (rarity)
            {
                case EnemyRarity.Uncommon:
                    m_MaxHealth *= 1.5f;
                    m_ContactDamage *= 1.5f;
                    m_PlayerKnockbackAtFullHealth *= 1.5f;
                    m_PlayerKnockbackAtZeroHealth *= 1.5f;
                    break;
                case EnemyRarity.Rare:
                    m_MaxHealth *= 2f;
                    m_ContactDamage *= 2f;
                    m_MoveSpeed *= 1.15f;
                    break;
                case EnemyRarity.Epic:
                    m_MaxHealth *= 3f;
                    m_ContactDamage *= 3f;
                    m_MoveSpeed *= 1.30f;
                    m_PlayerKnockbackAtFullHealth *= 1.5f;
                    m_PlayerKnockbackAtZeroHealth *= 1.5f;
                    break;
            }
            m_CurrentHealth = m_MaxHealth;
            UpdateColor();
        }"""
content = content.replace("        public event Action<CapsuleEnemy> Died;", init_method)

# 3. UpdateColor
old_color = """        void UpdateColor()
        {
            if (m_RuntimeMaterial == null)
                return;

            var normalizedHealth = Mathf.Clamp01(m_CurrentHealth / Mathf.Max(m_MaxHealth, 0.0001f));
            var baseColor = Color.Lerp(m_DamagedColor, m_HealthyColor, normalizedHealth);

            var flashElapsed = Time.time - m_LastHitTime;
            var flashStrength = flashElapsed < m_HitFlashDuration
                ? 1f - flashElapsed / Mathf.Max(m_HitFlashDuration, 0.0001f)
                : 0f;

            var finalColor = Color.Lerp(baseColor, m_HitFlashColor, flashStrength);
            SetMaterialColor(m_RuntimeMaterial, finalColor);
        }"""

new_color = """        void UpdateColor()
        {
            if (m_RuntimeMaterial == null)
                return;

            Color rarityColor = m_DamagedColor;
            if (m_Rarity == EnemyRarity.Uncommon) rarityColor = m_HealthyColor; // Green
            else if (m_Rarity == EnemyRarity.Rare) rarityColor = new Color(0.1f, 0.4f, 0.9f, 1f); // Blue
            else if (m_Rarity == EnemyRarity.Epic) rarityColor = new Color(0.9f, 0.8f, 0.1f, 1f); // Gold

            var flashElapsed = Time.time - m_LastHitTime;
            var flashStrength = flashElapsed < m_HitFlashDuration
                ? 1f - flashElapsed / Mathf.Max(m_HitFlashDuration, 0.0001f)
                : 0f;

            var finalColor = Color.Lerp(rarityColor, m_HitFlashColor, flashStrength);
            SetMaterialColor(m_RuntimeMaterial, finalColor);
        }"""
content = content.replace(old_color, new_color)

# 4. Abilities implementation in FixedUpdate
# Search for m_IsAttacking block and before moving
abilities = """
            if (m_Target != null && toTarget.magnitude < 3.5f && Time.time > m_AbilityCooldownEndsAt)
            {
                if (m_Rarity == EnemyRarity.Epic)
                {
                    // Charge
                    m_KnockbackVelocity += toTarget.normalized * 3f;
                    m_AbilityCooldownEndsAt = Time.time + 3f;
                    m_AnimationDriver?.PlayAttack(0.5f);
                    return;
                }
                else if (m_Rarity == EnemyRarity.Rare)
                {
                    // Jump
                    m_KnockbackVelocity += (toTarget.normalized * 2f) + (Vector3.up * 3f);
                    m_AbilityCooldownEndsAt = Time.time + 5f;
                    m_AnimationDriver?.PlayAttack(0.5f);
                    return;
                }
            }

            var desiredDirection = toTarget.normalized;"""
content = content.replace("            var desiredDirection = toTarget.normalized;", abilities)

# 5. Key Drop on Death
# Find perform death cleanup
key_drop = """
            if (m_DropKeyFlag && m_KeyPrefab != null)
            {
                Instantiate(m_KeyPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
                m_DropKeyFlag = false; // drop only once
            }

            ClearGrabListeners();"""
content = content.replace("            ClearGrabListeners();", key_drop)


with open('Assets/Scripts/Enemies/CapsuleEnemy.cs', 'w') as f:
    f.write(content)
