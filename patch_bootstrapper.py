with open('Assets/Scripts/Core/VRCombatBootstrapper.cs', 'r') as f:
    content = f.read()

# I need to find SpawnSingleEnemy() and add EnemyRarity logic
spawn_func_old = """
        CapsuleEnemy SpawnSingleEnemy()
        {
            var position = GetEnemySpawnPosition();

            var enemyObject = new GameObject("Goblin Enemy");"""

spawn_func_new = """
        EnemyRarity GetRandomRarityForWave()
        {
            float redWeight = 100f;
            float greenWeight = m_CurrentWave * 2f;
            float blueWeight = Mathf.Max(0, m_CurrentWave - 5) * 2f;
            float goldWeight = Mathf.Max(0, m_CurrentWave - 10) * 1f;

            float total = redWeight + greenWeight + blueWeight + goldWeight;
            float roll = Random.Range(0, total);

            if (roll < goldWeight) return EnemyRarity.Epic;
            roll -= goldWeight;
            if (roll < blueWeight) return EnemyRarity.Rare;
            roll -= blueWeight;
            if (roll < greenWeight) return EnemyRarity.Uncommon;
            return EnemyRarity.Common;
        }

        CapsuleEnemy SpawnSingleEnemy()
        {
            var position = GetEnemySpawnPosition();

            var enemyObject = new GameObject("Goblin Enemy");"""
content = content.replace(spawn_func_old, spawn_func_new)

init_enemy_old = """
            var enemy = enemyObject.AddComponent<CapsuleEnemy>();
            enemy.SetPlayerTarget(m_PlayerCamera.transform, m_PlayerDamageReceiver);
            enemy.ConfigureGrabDistance(m_MaxPhysicalGrabDistance);
            enemy.Died += HandleEnemyDied;

            var enemyRenderer = CreateGoblinVisual(enemyObject.transform, out var animationDriver);"""

init_enemy_new = """
            var enemy = enemyObject.AddComponent<CapsuleEnemy>();
            enemy.SetPlayerTarget(m_PlayerCamera.transform, m_PlayerDamageReceiver);
            enemy.ConfigureGrabDistance(m_MaxPhysicalGrabDistance);
            enemy.SetRarity(GetRandomRarityForWave(), null); // no key for random waves
            enemy.Died += HandleEnemyDied;

            var enemyRenderer = CreateGoblinVisual(enemyObject.transform, out var animationDriver);"""
content = content.replace(init_enemy_old, init_enemy_new)

with open('Assets/Scripts/Core/VRCombatBootstrapper.cs', 'w') as f:
    f.write(content)
