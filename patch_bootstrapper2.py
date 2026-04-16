with open('Assets/Scripts/Core/VRCombatBootstrapper.cs', 'r') as f:
    content = f.read()

# I want to make SpawnSingleEnemy public 
# Wait, SpawnSingleEnemy creates a Goblin at a random spawn spot. We can pass a position.
spawn_func_old = """
        CapsuleEnemy SpawnSingleEnemy()
        {
            var position = GetEnemySpawnPosition();"""

spawn_func_new = """
        public CapsuleEnemy SpawnEnemyAt(Vector3 position, EnemyRarity rarity, GameObject keyPrefab = null)
        {
            var enemyObject = new GameObject("Goblin Enemy");
            enemyObject.transform.position = position;
            RegisterRuntimeObject(enemyObject);

            var collider = enemyObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.95f, 0f);
            collider.radius = 0.28f;
            collider.height = 1.7f;

            var rigidbody = enemyObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = true;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var enemy = enemyObject.AddComponent<CapsuleEnemy>();
            enemy.SetPlayerTarget(m_PlayerCamera.transform, m_PlayerDamageReceiver);
            enemy.ConfigureGrabDistance(m_MaxPhysicalGrabDistance);
            enemy.SetRarity(rarity, keyPrefab);
            enemy.Died += HandleEnemyDied;

            var enemyRenderer = CreateGoblinVisual(enemyObject.transform, out var animationDriver);
            if (enemyRenderer != null)
                enemy.AssignRenderer(enemyRenderer);
            if (animationDriver != null)
                enemy.AttachAnimationDriver(animationDriver);

            return enemy;
        }

        CapsuleEnemy SpawnSingleEnemy()
        {
            var position = GetEnemySpawnPosition();"""
content = content.replace(spawn_func_old, spawn_func_new)

init_enemy_old_to_remove = """
            var enemyObject = new GameObject("Goblin Enemy");
            enemyObject.transform.position = position;
            RegisterRuntimeObject(enemyObject);

            var collider = enemyObject.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.95f, 0f);
            collider.radius = 0.28f;
            collider.height = 1.7f;

            var rigidbody = enemyObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = true;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var enemy = enemyObject.AddComponent<CapsuleEnemy>();
            enemy.SetPlayerTarget(m_PlayerCamera.transform, m_PlayerDamageReceiver);
            enemy.ConfigureGrabDistance(m_MaxPhysicalGrabDistance);
            enemy.SetRarity(GetRandomRarityForWave(), null); // no key for random waves
            enemy.Died += HandleEnemyDied;

            var enemyRenderer = CreateGoblinVisual(enemyObject.transform, out var animationDriver);
            if (enemyRenderer != null)
                enemy.AssignRenderer(enemyRenderer);
            if (animationDriver != null)
                enemy.AttachAnimationDriver(animationDriver);

            return enemy;
        }"""
content = content.replace(init_enemy_old_to_remove, "            return SpawnEnemyAt(position, GetRandomRarityForWave(), null);\n        }")

with open('Assets/Scripts/Core/VRCombatBootstrapper.cs', 'w') as f:
    f.write(content)
