using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRCombat.Environment;

namespace VRCombat.Editor
{
    public static class KeyTorchLightPrefabBuilder
    {
        const string PackedKeySourceModelPath = "Assets/Resources/CombatModels/Key_Torch.fbx";
        const string TorchSourceModelPath = "Assets/Resources/CombatModels/Torch.fbx";
        const string LegacyLitKeyTorchPrefabPath = "Assets/Resources/CombatModels/Key_Torch_Light.prefab";
        const string ArenaKeyPrefabPath = "Assets/Resources/CombatModels/Arena_Key.prefab";
        const string TorchLightPrefabPath = "Assets/Resources/CombatModels/Torch_Light.prefab";
        const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("VR Combat/Rebuild Key Torch Light Prefab")]
        public static void BuildKeyTorchLightPrefab()
        {
            BuildSeparatedKeyAndTorchPrefabs();
        }

        [MenuItem("VR Combat/Rebuild Separated Key And Torch Prefabs")]
        public static void BuildSeparatedKeyAndTorchPrefabs()
        {
            BuildArenaKeyPrefab();
            BuildTorchLightPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static void BuildSeparatedPrefabsAndReplaceSampleScene()
        {
            BuildSeparatedKeyAndTorchPrefabs();
            ReplaceDecorativeTorchesInSampleScene();
        }

        static void BuildArenaKeyPrefab()
        {
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(PackedKeySourceModelPath);
            if (sourceModel == null)
            {
                Debug.LogError($"Could not load packed key/torch model at {PackedKeySourceModelPath}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel);
            if (instance == null)
            {
                Debug.LogError($"Could not instantiate packed key/torch model at {PackedKeySourceModelPath}.");
                return;
            }

            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Arena_Key";
            RemoveDescendantsMatching(instance.transform, IsTorchOnlyName);
            if (instance.GetComponent<KeyItem>() == null)
                instance.AddComponent<KeyItem>();

            RemoveComponentsInChildren<TorchLightSource>(instance);

            PrefabUtility.SaveAsPrefabAsset(instance, ArenaKeyPrefabPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"Rebuilt arena key prefab at {ArenaKeyPrefabPath}.");
        }

        static void BuildTorchLightPrefab()
        {
            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(TorchSourceModelPath);
            if (sourceModel == null)
            {
                Debug.LogError($"Could not load torch model at {TorchSourceModelPath}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel);
            if (instance == null)
            {
                Debug.LogError($"Could not instantiate torch model at {TorchSourceModelPath}.");
                return;
            }

            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "Torch_Light";
            RemoveDescendantsMatching(instance.transform, IsKeyOnlyName);
            RemoveComponentsInChildren<KeyItem>(instance);
            if (instance.GetComponent<TorchLightSource>() == null)
                instance.AddComponent<TorchLightSource>();

            PrefabUtility.SaveAsPrefabAsset(instance, TorchLightPrefabPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"Rebuilt lit torch prefab at {TorchLightPrefabPath}.");
        }

        [MenuItem("VR Combat/Replace Decorative Torches In Sample Scene")]
        public static void ReplaceDecorativeTorchesInSampleScene()
        {
            var oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyLitKeyTorchPrefabPath);
            var torchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TorchLightPrefabPath);
            if (oldPrefab == null || torchPrefab == null)
            {
                Debug.LogError("Cannot replace decorative torches because one of the torch prefabs is missing.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(SampleScenePath);
            var roots = scene.GetRootGameObjects();
            var replacements = new System.Collections.Generic.List<GameObject>();
            for (var i = 0; i < roots.Length; i++)
                CollectDecorativePackedTorchInstances(roots[i].transform, oldPrefab, replacements);

            for (var i = 0; i < replacements.Count; i++)
                ReplaceWithTorchPrefab(replacements[i], torchPrefab);

            if (replacements.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log($"Replaced {replacements.Count} decorative packed key/torch instance(s) with {TorchLightPrefabPath}.");
        }

        static void CollectDecorativePackedTorchInstances(
            Transform candidate,
            GameObject oldPrefab,
            System.Collections.Generic.List<GameObject> replacements)
        {
            if (candidate == null)
                return;

            if (IsDecorativePackedTorchInstance(candidate.gameObject, oldPrefab))
                replacements.Add(candidate.gameObject);

            for (var i = 0; i < candidate.childCount; i++)
                CollectDecorativePackedTorchInstances(candidate.GetChild(i), oldPrefab, replacements);
        }

        static bool IsDecorativePackedTorchInstance(GameObject candidate, GameObject oldPrefab)
        {
            if (candidate == null)
                return false;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
            var sourceRoot = source != null ? PrefabUtility.GetOutermostPrefabInstanceRoot(candidate) : null;
            var sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
            var isOldPrefabInstance = sourceRoot == candidate &&
                                      (source == oldPrefab || sourcePath == LegacyLitKeyTorchPrefabPath);
            if (!isOldPrefabInstance)
                return false;

            var keyItem = candidate.GetComponent<KeyItem>();
            var torchLightSource = candidate.GetComponent<TorchLightSource>();
            return keyItem != null && !keyItem.enabled && torchLightSource != null && torchLightSource.enabled;
        }

        static void ReplaceWithTorchPrefab(GameObject oldInstance, GameObject torchPrefab)
        {
            var oldTransform = oldInstance.transform;
            var parent = oldTransform.parent;
            var siblingIndex = oldTransform.GetSiblingIndex();
            var localPosition = oldTransform.localPosition;
            var localRotation = oldTransform.localRotation;
            var localScale = oldTransform.localScale;
            var name = oldInstance.name;

            var newInstance = (GameObject)PrefabUtility.InstantiatePrefab(torchPrefab, parent);
            newInstance.name = name;
            newInstance.transform.localPosition = localPosition;
            newInstance.transform.localRotation = localRotation;
            newInstance.transform.localScale = localScale;
            newInstance.transform.SetSiblingIndex(siblingIndex);

            Object.DestroyImmediate(oldInstance);
        }

        static void RemoveComponentsInChildren<T>(GameObject root) where T : Component
        {
            var components = root.GetComponentsInChildren<T>(true);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    Object.DestroyImmediate(components[i]);
            }
        }

        static void RemoveDescendantsMatching(Transform root, System.Func<string, bool> predicate)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (predicate(child.name))
                {
                    Object.DestroyImmediate(child.gameObject);
                    continue;
                }

                RemoveDescendantsMatching(child, predicate);
            }
        }

        static bool IsTorchOnlyName(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.IndexOf("Torch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Flame", System.StringComparison.OrdinalIgnoreCase) >= 0) &&
                   value.IndexOf("Key", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        static bool IsKeyOnlyName(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("Key", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                   value.IndexOf("Torch", System.StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
