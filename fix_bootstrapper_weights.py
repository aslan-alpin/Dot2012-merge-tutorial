with open("Assets/Scripts/Core/VRCombatBootstrapper.cs", "r") as f:
    text = f.read()

old_weight = """        EnemyRarity GetRandomRarityForWave()
        {
            float redWeight = 100f;
            float greenWeight = m_CurrentWave * 2f;
            float blueWeight = Mathf.Max(0, m_CurrentWave - 5) * 2f;
            float goldWeight = Mathf.Max(0, m_CurrentWave - 10) * 1f;"""

new_weight = """        EnemyRarity GetRandomRarityForWave()
        {
            // Accelerated scaling to make rarity and abilities immediately apparent
            float redWeight = 100f;
            float greenWeight = m_CurrentWave * 15f;
            float blueWeight = m_CurrentWave * 8f;
            float goldWeight = m_CurrentWave * 4f;"""

text = text.replace(old_weight, new_weight)

with open("Assets/Scripts/Core/VRCombatBootstrapper.cs", "w") as f:
    f.write(text)
print("Accelerated wave weights applied")
