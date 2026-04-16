with open('Assets/Scripts/Enemies/CapsuleEnemy.cs', 'r') as f:
    content = f.read()

content = content.replace("    [DisallowMultipleComponent]", """    public enum EnemyRarity { Common, Uncommon, Rare, Epic }

    [DisallowMultipleComponent]""")

with open('Assets/Scripts/Enemies/CapsuleEnemy.cs', 'w') as f:
    f.write(content)
