with open('Assets/Resources/CombatModels/Arena_01.fbx.meta', 'r') as f:
    text = f.read()

import re
names = re.findall(r'internalIDToNameTable:\n((?:  - first:\n.*?\n    second: .*\n)*)', text, re.DOTALL)
if names:
    print(names[0])
