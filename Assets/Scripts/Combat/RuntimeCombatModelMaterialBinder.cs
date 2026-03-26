using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VRCombat.Combat
{
    static class RuntimeCombatModelMaterialBinder
    {
        const string CombatTextureResourcePath = "CombatModels";
        const string KabzaAliasKey = "kabza";
        const string TutamacAliasKey = "tutamac";
        const string TutamaAliasKey = "tutama";

        static readonly int BaseColorShaderId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapShaderId = Shader.PropertyToID("_BaseMap");
        static readonly int ColorShaderId = Shader.PropertyToID("_Color");
        static readonly int MainTexShaderId = Shader.PropertyToID("_MainTex");
        static readonly int MetallicShaderId = Shader.PropertyToID("_Metallic");
        static readonly int MetallicGlossMapShaderId = Shader.PropertyToID("_MetallicGlossMap");
        static readonly int SmoothnessShaderId = Shader.PropertyToID("_Smoothness");
        static readonly int EmissionColorShaderId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMapShaderId = Shader.PropertyToID("_EmissionMap");

        static readonly Dictionary<string, TextureSet> s_TextureSets = new Dictionary<string, TextureSet>(StringComparer.Ordinal);
        static readonly Dictionary<string, Material> s_RuntimeMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);

        sealed class TextureSet
        {
            public string DisplayName;
            public Texture2D BaseColor;
            public Texture2D Metallic;
            public Texture2D Emission;
        }

        public static void Apply(GameObject visualRoot, Material fallbackMaterial)
        {
            if (visualRoot == null)
                return;

            EnsureTextureSetsLoaded();

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var j = 0; j < materials.Length; j++)
                {
                    var sourceMaterial = materials[j];
                    if (TryCreateBoundMaterial(sourceMaterial, out var resolvedMaterial))
                    {
                        if (!ReferenceEquals(sourceMaterial, resolvedMaterial))
                        {
                            materials[j] = resolvedMaterial;
                            changed = true;
                        }

                        continue;
                    }

                    if (ShouldForceFallbackMaterial(sourceMaterial) && fallbackMaterial != null)
                    {
                        if (!ReferenceEquals(materials[j], fallbackMaterial))
                        {
                            materials[j] = fallbackMaterial;
                            changed = true;
                        }

                        continue;
                    }

                    if (materials[j] != null || fallbackMaterial == null)
                        continue;

                    materials[j] = fallbackMaterial;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        static bool TryCreateBoundMaterial(Material sourceMaterial, out Material resolvedMaterial)
        {
            resolvedMaterial = null;
            if (sourceMaterial == null)
                return false;

            if (!TryResolveTextureSet(sourceMaterial.name, out var textureSet))
                return false;

            var cacheKey = $"{NormalizeKey(sourceMaterial.name)}::{textureSet.DisplayName}";
            if (s_RuntimeMaterials.TryGetValue(cacheKey, out resolvedMaterial) && resolvedMaterial != null)
                return true;

            var shader = ResolveCompatibleLitShader(sourceMaterial.shader);
            if (shader == null)
                return false;

            resolvedMaterial = new Material(shader)
            {
                name = $"{sourceMaterial.name} Runtime"
            };

            if (resolvedMaterial.HasProperty(BaseColorShaderId))
                resolvedMaterial.SetColor(BaseColorShaderId, Color.white);
            if (resolvedMaterial.HasProperty(ColorShaderId))
                resolvedMaterial.SetColor(ColorShaderId, Color.white);

            if (textureSet.BaseColor != null)
            {
                if (resolvedMaterial.HasProperty(BaseMapShaderId))
                    resolvedMaterial.SetTexture(BaseMapShaderId, textureSet.BaseColor);
                if (resolvedMaterial.HasProperty(MainTexShaderId))
                    resolvedMaterial.SetTexture(MainTexShaderId, textureSet.BaseColor);
            }

            if (textureSet.Metallic != null)
            {
                if (resolvedMaterial.HasProperty(MetallicGlossMapShaderId))
                {
                    resolvedMaterial.SetTexture(MetallicGlossMapShaderId, textureSet.Metallic);
                    resolvedMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
                }

                if (resolvedMaterial.HasProperty(MetallicShaderId))
                    resolvedMaterial.SetFloat(MetallicShaderId, 1f);
                if (resolvedMaterial.HasProperty(SmoothnessShaderId))
                    resolvedMaterial.SetFloat(SmoothnessShaderId, 0.72f);
            }

            if (textureSet.Emission != null && resolvedMaterial.HasProperty(EmissionMapShaderId))
            {
                resolvedMaterial.SetTexture(EmissionMapShaderId, textureSet.Emission);
                if (resolvedMaterial.HasProperty(EmissionColorShaderId))
                    resolvedMaterial.SetColor(EmissionColorShaderId, Color.white);
                resolvedMaterial.EnableKeyword("_EMISSION");
                resolvedMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            s_RuntimeMaterials[cacheKey] = resolvedMaterial;
            return true;
        }

        static Shader ResolveCompatibleLitShader(Shader sourceShader)
        {
            if (sourceShader != null &&
                (sourceShader.name == "Universal Render Pipeline/Lit" ||
                 sourceShader.name == "Universal Render Pipeline/Simple Lit" ||
                 sourceShader.name == "Standard"))
            {
                return sourceShader;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            return shader;
        }

        static bool TryResolveTextureSet(string materialName, out TextureSet textureSet)
        {
            textureSet = null;
            if (string.IsNullOrWhiteSpace(materialName))
                return false;

            var normalizedMaterialName = NormalizeKey(materialName);
            if (string.IsNullOrEmpty(normalizedMaterialName))
                return false;

            if (s_TextureSets.TryGetValue(normalizedMaterialName, out textureSet) && textureSet != null)
                return true;

            if (TryResolveAliasTextureSet(normalizedMaterialName, out textureSet))
                return true;

            foreach (var pair in s_TextureSets)
            {
                if (normalizedMaterialName.StartsWith(pair.Key, StringComparison.Ordinal) ||
                    pair.Key.StartsWith(normalizedMaterialName, StringComparison.Ordinal) ||
                    normalizedMaterialName.IndexOf(pair.Key, StringComparison.Ordinal) >= 0)
                {
                    textureSet = pair.Value;
                    return textureSet != null;
                }
            }

            return false;
        }

        static bool TryResolveAliasTextureSet(string normalizedMaterialName, out TextureSet textureSet)
        {
            textureSet = null;
            if (string.IsNullOrEmpty(normalizedMaterialName))
                return false;

            if (normalizedMaterialName.StartsWith(KabzaAliasKey, StringComparison.Ordinal))
            {
                if (s_TextureSets.TryGetValue(TutamacAliasKey, out textureSet) && textureSet != null)
                    return true;

                if (s_TextureSets.TryGetValue(TutamaAliasKey, out textureSet) && textureSet != null)
                    return true;
            }

            return false;
        }

        static void EnsureTextureSetsLoaded()
        {
            if (s_TextureSets.Count > 0)
                return;

            var textures = Resources.LoadAll<Texture2D>(CombatTextureResourcePath);
            for (var i = 0; i < textures.Length; i++)
            {
                var texture = textures[i];
                if (texture == null)
                    continue;

                var textureName = texture.name;
                var suffixIndex = textureName.LastIndexOf('_');
                if (suffixIndex <= 0 || suffixIndex >= textureName.Length - 1)
                    continue;

                var setKey = NormalizeKey(textureName.Substring(0, suffixIndex));
                if (string.IsNullOrEmpty(setKey))
                    continue;

                if (!s_TextureSets.TryGetValue(setKey, out var textureSet) || textureSet == null)
                {
                    textureSet = new TextureSet
                    {
                        DisplayName = textureName.Substring(0, suffixIndex)
                    };
                    s_TextureSets[setKey] = textureSet;
                }

                var suffix = NormalizeKey(textureName.Substring(suffixIndex + 1));
                if (suffix == "basecolor")
                {
                    textureSet.BaseColor = texture;
                    continue;
                }

                if (suffix == "metallic")
                {
                    textureSet.Metallic = texture;
                    continue;
                }

                if (suffix == "emission")
                    textureSet.Emission = texture;
            }
        }

        static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            for (var i = 0; i < normalized.Length; i++)
            {
                var current = normalized[i];
                if (CharUnicodeInfo.GetUnicodeCategory(current) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(current))
                    builder.Append(char.ToLowerInvariant(current));
            }

            return builder.ToString();
        }

        static bool ShouldForceFallbackMaterial(Material sourceMaterial)
        {
            if (sourceMaterial == null)
                return false;

            var normalizedMaterialName = NormalizeKey(sourceMaterial.name);
            return normalizedMaterialName.StartsWith(KabzaAliasKey, StringComparison.Ordinal);
        }
    }
}
