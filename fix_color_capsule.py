import re

with open("Assets/Scripts/Enemies/CapsuleEnemy.cs", "r") as f:
    code = f.read()

# 1. Change field
code = code.replace("Material m_RuntimeMaterial;", "Material[] m_RuntimeMaterials;")

# 2. Change Start() assignment
code = code.replace("m_RuntimeMaterial = m_Renderer.material;", "m_RuntimeMaterials = m_Renderer.materials;")

# 3. UpdateColor()
old_color_start = """        void UpdateColor()
        {
            if (m_RuntimeMaterial == null)
                return;"""

new_color_start = """        void UpdateColor()
        {
            if (m_RuntimeMaterials == null || m_RuntimeMaterials.Length == 0)
                return;"""
code = code.replace(old_color_start, new_color_start)

old_color_end = "            SetMaterialColor(m_RuntimeMaterial, finalColor);"
new_color_end = """            foreach(var mat in m_RuntimeMaterials)
                SetMaterialColor(mat, finalColor);"""
code = code.replace(old_color_end, new_color_end)

# 4. Update SetMaterialColor to use Emission
old_set_color = """        static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);

            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, color);
        }"""
new_set_color = """        static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);

            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, color);
                
            // Use emission to overpower the base green texture, otherwise they look "darker"
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.5f);
            }
        }"""
code = code.replace(old_set_color, new_set_color)

with open("Assets/Scripts/Enemies/CapsuleEnemy.cs", "w") as f:
    f.write(code)

print("Color logic fixed!")
