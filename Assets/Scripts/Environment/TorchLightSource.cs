using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCombat.Environment
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TorchLightSource : MonoBehaviour
    {
        const string DefaultTorchNameHint = "Torch";
        const string AuthoredFlameName = "Flame";
        const string FlameAnchorName = "Runtime Torch Flame";
        const string MetaTorchFlameParticlesName = "torchParticleFlames";
        const string MetaTorchBaseParticlesName = "torchParticleBase";
        const string MetaTorchSparksParticlesName = "torchParticleSparks";
        const string MetaTorchPrefabResourcePath = "MetaTorchVFX/Torch";
        const string MetaTorchFlameMaterialResourcePath = "MetaTorchVFX/Flame";
        const string MetaTorchFlamePoolMaterialResourcePath = "MetaTorchVFX/FlamePool";
        const int CrackleFrequency = 22050;
        const float MinimumLightIntensity = 0.25f;
        const float MaxDynamicTorchLightRange = 7.25f;
        const float MaxDynamicTorchLightIntensity = 4.8f;
        const float DefaultSightLightRange = 7.25f;
        const float DefaultSightLightIntensity = 4.8f;
        const float DefaultSightFlickerIntensity = 0.8f;
        static readonly Vector3 s_DefaultFlameTipOffset = Vector3.zero;
        static readonly Vector3 s_LegacyFlameTipOffset = new Vector3(0f, 0.55f, 0f);

        [SerializeField] Transform m_TorchRoot;
        [SerializeField] bool m_AutoPlaceFlame = true;
        [SerializeField] Vector3 m_FlameLocalOffset = s_DefaultFlameTipOffset;
        [SerializeField] Color m_FireColor = new Color(1f, 0.42f, 0.08f, 1f);
        [SerializeField] float m_LightRange = DefaultSightLightRange;
        [SerializeField] float m_BaseIntensity = DefaultSightLightIntensity;
        [SerializeField] float m_FlickerIntensity = DefaultSightFlickerIntensity;
        [SerializeField] float m_FlickerSpeed = 9f;
        [SerializeField] bool m_CastTorchShadows;
        [SerializeField] bool m_HideKeyGeometryWhenDecorative = true;
        [SerializeField] bool m_EnableFireParticles = true;
        [SerializeField] bool m_EnableFireAudio = true;

        Transform m_FlameAnchor;
        Light m_PointLight;
        ParticleSystem m_FireParticles;
        ParticleSystem m_FireBaseParticles;
        ParticleSystem m_FireSparkParticles;
        AudioSource m_AudioSource;
        float m_FlickerSeed;

        static Material s_FireParticleMaterial;
        static Material s_MetaTorchFlameMaterial;
        static Material s_MetaTorchFlamePoolMaterial;
        static AudioClip s_FireCrackleClip;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureSceneTorchLightSources()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            var rootObjects = scene.GetRootGameObjects();
            for (var i = 0; i < rootObjects.Length; i++)
            {
                var root = rootObjects[i];
                if (root != null)
                    EnsureTorchLightSourcesInHierarchy(root.transform);
            }
        }

        public void ConfigureRuntimeInstance()
        {
            EnsureInitialized();
        }

        public static bool TryFindTorchTransform(Transform root, out Transform torchTransform)
        {
            torchTransform = null;
            if (root == null)
                return false;

            if (IsExactTorchName(root.name))
            {
                torchTransform = root;
                return true;
            }

            var children = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child != null && child != root && IsExactTorchName(child.name))
                {
                    torchTransform = child;
                    return true;
                }
            }

            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child != null && child != root && NameContainsTorch(child.name))
                {
                    torchTransform = child;
                    return true;
                }
            }

            if (NameContainsTorch(root.name))
            {
                torchTransform = root;
                return true;
            }

            return false;
        }

        void Awake()
        {
            EnsureInitialized();
        }

        void OnEnable()
        {
            EnsureInitialized();
            if (Application.isPlaying && m_AudioSource != null && m_EnableFireAudio && !m_AudioSource.isPlaying)
                m_AudioSource.Play();
        }

        void OnDisable()
        {
            if (m_PointLight != null)
                m_PointLight.enabled = false;
            if (m_FireParticles != null)
                m_FireParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (m_FireBaseParticles != null)
                m_FireBaseParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (m_FireSparkParticles != null)
                m_FireSparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (m_AudioSource != null)
                m_AudioSource.Stop();
        }

        void OnValidate()
        {
            if (!Application.isPlaying && isActiveAndEnabled)
                EnsureInitialized();
        }

        void Update()
        {
            if (m_PointLight == null)
                return;

            var time = Time.time * Mathf.Max(0.1f, m_FlickerSpeed);
            var broadFlicker = Mathf.PerlinNoise(m_FlickerSeed, time);
            var sharpFlicker = Mathf.PerlinNoise(m_FlickerSeed + 37.2f, time * 2.15f);
            var flicker = ((broadFlicker - 0.5f) * 0.75f + (sharpFlicker - 0.5f) * 0.25f) * m_FlickerIntensity;
            m_PointLight.intensity = Mathf.Max(MinimumLightIntensity, GetEffectiveBaseIntensity() + flicker);
        }

        void EnsureInitialized()
        {
            if (!isActiveAndEnabled && Application.isPlaying)
                return;

            if (m_FlickerSeed <= 0f)
                m_FlickerSeed = UnityEngine.Random.Range(1f, 1000f);

            UpgradeLegacySightDefaults();

            if (HasEnabledKeyItem())
            {
                enabled = false;
                return;
            }

            if (m_TorchRoot == null || !m_TorchRoot.IsChildOf(transform))
            {
                if (!TryFindTorchTransform(transform, out m_TorchRoot))
                    return;
            }

            EnsureFlameAnchor();
            DisableAuthoredFlameEffects();
            EnsurePointLight();
            ConfigureDecorativeKeyGeometryVisibility();

            if (Application.isPlaying && m_EnableFireParticles)
                EnsureFireParticles();
            else
                StopFireParticles();

            if (Application.isPlaying && m_EnableFireAudio)
                EnsureFireAudio();
        }

        void EnsureFlameAnchor()
        {
            if (m_TorchRoot == null)
                return;

            if (m_FlameAnchor == null)
            {
                var existing = m_TorchRoot.Find(FlameAnchorName);
                if (existing != null)
                    m_FlameAnchor = existing;
            }

            if (m_FlameAnchor == null)
            {
                var flameObject = new GameObject(FlameAnchorName);
                flameObject.transform.SetParent(m_TorchRoot, false);
                m_FlameAnchor = flameObject.transform;
            }

            m_FlameAnchor.localPosition = ResolveFlameLocalPosition();
            m_FlameAnchor.localRotation = Quaternion.identity;
            m_FlameAnchor.localScale = Vector3.one;
        }

        Vector3 ResolveFlameLocalPosition()
        {
            if (!m_AutoPlaceFlame || m_TorchRoot == null || !TryGetLocalRendererBounds(m_TorchRoot, m_FlameAnchor, out var bounds))
            {
                if (TryFindAuthoredFlameTransform(m_TorchRoot, out var authoredFlame))
                    return m_TorchRoot.InverseTransformPoint(authoredFlame.position);

                return m_FlameLocalOffset;
            }

            var dominantAxis = GetDominantAxis(bounds.size);
            var flamePosition = bounds.center;
            SetAxisValue(ref flamePosition, dominantAxis, GetAxisValue(bounds.max, dominantAxis) + 0.035f);
            return flamePosition + m_FlameLocalOffset;
        }

        void DisableAuthoredFlameEffects()
        {
            if (m_TorchRoot == null)
                return;

            var children = m_TorchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null ||
                    child == m_FlameAnchor ||
                    (m_FlameAnchor != null && child.IsChildOf(m_FlameAnchor)) ||
                    !string.Equals(child.name, AuthoredFlameName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DisableAuthoredFlameHierarchy(child);
            }
        }

        static void DisableAuthoredFlameHierarchy(Transform flameRoot)
        {
            if (flameRoot == null)
                return;

            var renderers = flameRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }

            var particles = flameRoot.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                    particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var lights = flameRoot.GetComponentsInChildren<Light>(true);
            for (var i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                    lights[i].enabled = false;
            }

            var audioSources = flameRoot.GetComponentsInChildren<AudioSource>(true);
            for (var i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] != null)
                    audioSources[i].Stop();
            }
        }

        void EnsurePointLight()
        {
            if (m_FlameAnchor == null)
                return;

            if (m_PointLight == null || m_PointLight.transform != m_FlameAnchor)
                m_PointLight = GetOrAddComponent<Light>(m_FlameAnchor.gameObject);
            if (m_PointLight == null)
                return;

            m_PointLight.enabled = true;
            m_PointLight.type = LightType.Point;
            m_PointLight.color = new Color(1f, 0.52f, 0.18f, 1f);
            m_PointLight.range = Mathf.Clamp(m_LightRange, 0.25f, MaxDynamicTorchLightRange);
            m_PointLight.intensity = Mathf.Max(MinimumLightIntensity, GetEffectiveBaseIntensity());
            m_PointLight.shadows = m_CastTorchShadows ? LightShadows.Soft : LightShadows.None;
            m_PointLight.shadowStrength = m_CastTorchShadows ? 0.45f : 0f;
            m_PointLight.bounceIntensity = 0.95f;
            m_PointLight.renderMode = LightRenderMode.Auto;
        }

        void EnsureFireParticles()
        {
            if (m_FlameAnchor == null)
                return;

            StopLegacyAnchorParticles();
            if (TryEnsureMetaTorchPrefabParticles())
            {
                PlayFireParticles();
                return;
            }

            m_FireParticles = GetOrCreateParticleSystemChild(MetaTorchFlameParticlesName, m_FlameAnchor, m_FireParticles);
            m_FireBaseParticles = GetOrCreateParticleSystemChild(MetaTorchBaseParticlesName, m_FlameAnchor, m_FireBaseParticles);
            m_FireSparkParticles = GetOrCreateParticleSystemChild(MetaTorchSparksParticlesName, m_FlameAnchor, m_FireSparkParticles);
            if (m_FireParticles == null || m_FireBaseParticles == null || m_FireSparkParticles == null)
                return;

            ConfigureMetaFlameParticles(m_FireParticles);
            ConfigureMetaBaseParticles(m_FireBaseParticles);
            ConfigureMetaSparkParticles(m_FireSparkParticles);
            PlayFireParticles();
        }

        bool TryEnsureMetaTorchPrefabParticles()
        {
            var torchPrefab = Resources.Load<GameObject>(MetaTorchPrefabResourcePath);
            if (torchPrefab == null || m_FlameAnchor == null)
                return false;

            m_FireParticles = InstantiateParticleTemplateChild(torchPrefab, MetaTorchFlameParticlesName, m_FlameAnchor, m_FireParticles);
            if (m_FireParticles != null)
            {
                m_FireBaseParticles = GetDescendantParticleSystem(m_FireParticles.transform, MetaTorchBaseParticlesName);
                m_FireSparkParticles = GetDescendantParticleSystem(m_FireParticles.transform, MetaTorchSparksParticlesName);
            }

            ConfigureTorchParticleHierarchy(m_FireParticles);
            return m_FireParticles != null && m_FireBaseParticles != null && m_FireSparkParticles != null;
        }

        static ParticleSystem InstantiateParticleTemplateChild(
            GameObject templateRoot,
            string childName,
            Transform parent,
            ParticleSystem cachedSystem)
        {
            if (templateRoot == null || parent == null)
                return null;

            if (cachedSystem != null && cachedSystem.transform.parent == parent && string.Equals(cachedSystem.name, childName, StringComparison.Ordinal))
            {
                NormalizeParticleTemplatePlacement(cachedSystem.transform);
                StripNonParticleComponents(cachedSystem.gameObject);
                return cachedSystem;
            }

            var template = FindDescendantByName(templateRoot.transform, childName);
            if (template == null)
                return null;

            var existing = parent.Find(childName);
            if (existing != null)
                Destroy(existing.gameObject);

            var clone = Instantiate(template.gameObject, parent, false);
            clone.name = childName;
            NormalizeParticleTemplatePlacement(clone.transform);
            StripNonParticleComponents(clone);
            return clone.GetComponent<ParticleSystem>();
        }

        static void NormalizeParticleTemplatePlacement(Transform root)
        {
            if (root == null)
                return;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child == null)
                    continue;

                if (child == root || child.GetComponent<ParticleSystem>() != null || child.GetComponentInChildren<ParticleSystem>(true) != null)
                    child.localPosition = Vector3.zero;
            }
        }

        static ParticleSystem GetDescendantParticleSystem(Transform root, string childName)
        {
            var child = FindDescendantByName(root, childName);
            return child != null ? child.GetComponent<ParticleSystem>() : null;
        }

        static Transform FindDescendantByName(Transform root, string expectedName)
        {
            if (root == null)
                return null;

            if (string.Equals(root.name, expectedName, StringComparison.Ordinal))
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var match = FindDescendantByName(root.GetChild(i), expectedName);
                if (match != null)
                    return match;
            }

            return null;
        }

        static void StripNonParticleComponents(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    Destroy(colliders[i]);
            }

            var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < rigidbodies.Length; i++)
            {
                if (rigidbodies[i] != null)
                    Destroy(rigidbodies[i]);
            }

            var audioSources = root.GetComponentsInChildren<AudioSource>(true);
            for (var i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] != null)
                    Destroy(audioSources[i]);
            }
        }

        void StopLegacyAnchorParticles()
        {
            var legacyParticles = m_FlameAnchor.GetComponent<ParticleSystem>();
            if (legacyParticles != null)
                legacyParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void PlayFireParticles()
        {
            PlayParticleSystem(m_FireParticles);
            PlayParticleSystem(m_FireBaseParticles);
            PlayParticleSystem(m_FireSparkParticles);
        }

        void StopFireParticles()
        {
            StopParticleSystem(m_FireParticles);
            StopParticleSystem(m_FireBaseParticles);
            StopParticleSystem(m_FireSparkParticles);
        }

        static void PlayParticleSystem(ParticleSystem particles)
        {
            if (particles != null && !particles.isPlaying)
                particles.Play();
        }

        static void StopParticleSystem(ParticleSystem particles)
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static ParticleSystem GetOrCreateParticleSystemChild(string childName, Transform parent, ParticleSystem cachedSystem)
        {
            if (parent == null)
                return null;

            if (cachedSystem != null && cachedSystem.transform.parent == parent)
                return cachedSystem;

            var child = parent.Find(childName);
            if (child == null)
            {
                var childObject = new GameObject(childName);
                child = childObject.transform;
                child.SetParent(parent, false);
            }

            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return GetOrAddComponent<ParticleSystem>(child.gameObject);
        }

        void ConfigureMetaFlameParticles(ParticleSystem particles)
        {
            var main = particles.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.16f, 0.62f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startColor = new ParticleSystem.MinMaxGradient(CreateFireGradient());
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            main.maxParticles = 120;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 62f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 13f;
            shape.radius = 0.035f;
            shape.radiusThickness = 0.7f;

            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = new ParticleSystem.MinMaxCurve(0.16f, 0.55f);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.3f);
            sizeCurve.AddKey(0.18f, 1f);
            sizeCurve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ConfigureAlphaFade(particles, 0.86f, 0.5f);
            ConfigureTextureSheet(particles);
            ConfigureParticleRenderer(particles, GetMetaTorchFlameMaterial(), ParticleSystemRenderMode.Billboard, 3f);
        }

        void ConfigureMetaBaseParticles(ParticleSystem particles)
        {
            var main = particles.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.48f, 0.08f, 0.72f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            main.maxParticles = 55;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 32f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.045f;
            shape.radiusThickness = 0.25f;

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.8f);
            sizeCurve.AddKey(0.4f, 1f);
            sizeCurve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ConfigureAlphaFade(particles, 0.6f, 0.2f);
            ConfigureParticleRenderer(particles, GetMetaTorchFlamePoolMaterial(), ParticleSystemRenderMode.Billboard, 2f);
        }

        void ConfigureMetaSparkParticles(ParticleSystem particles)
        {
            var main = particles.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 1.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.66f, 0.18f, 0.95f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            main.maxParticles = 35;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 8f;

            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.025f;

            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.y = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);

            ConfigureAlphaFade(particles, 1f, 0f);
            ConfigureParticleRenderer(particles, GetMetaTorchFlameMaterial(), ParticleSystemRenderMode.Stretch, 4f);
        }

        static void ConfigureAlphaFade(ParticleSystem particles, float peakAlpha, float endAlpha)
        {
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peakAlpha, 0.16f),
                    new GradientAlphaKey(endAlpha, 0.64f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        static void ConfigureTextureSheet(ParticleSystem particles)
        {
            var textureSheet = particles.textureSheetAnimation;
            textureSheet.enabled = true;
            textureSheet.mode = ParticleSystemAnimationMode.Grid;
            textureSheet.numTilesX = 4;
            textureSheet.numTilesY = 4;
            textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
            textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 15f);
            textureSheet.cycleCount = 1;
        }

        static void ConfigureParticleRenderer(
            ParticleSystem particles,
            Material material,
            ParticleSystemRenderMode renderMode,
            float sortingFudge)
        {
            var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.material = material != null ? material : GetFireParticleMaterial();
            particleRenderer.renderMode = renderMode;
            particleRenderer.sortingFudge = sortingFudge;
            particleRenderer.minParticleSize = 0f;
            particleRenderer.maxParticleSize = 0.55f;
        }

        static void ConfigureTorchParticleHierarchy(ParticleSystem rootParticles)
        {
            if (rootParticles == null)
                return;

            var particleSystems = rootParticles.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < particleSystems.Length; i++)
                ConfigureTorchParticleCulling(particleSystems[i]);
        }

        static void ConfigureTorchParticleCulling(ParticleSystem particles)
        {
            if (particles == null)
                return;

            var main = particles.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var lights = particles.lights;
            lights.enabled = false;
        }

        void UpgradeLegacySightDefaults()
        {
            if (m_LightRange < 4f || m_LightRange > 14f)
                m_LightRange = DefaultSightLightRange;
            if (m_BaseIntensity < 1f || m_BaseIntensity > 12f)
                m_BaseIntensity = DefaultSightLightIntensity;
            if (m_FlickerIntensity < 0.1f || m_FlickerIntensity > 4f)
                m_FlickerIntensity = DefaultSightFlickerIntensity;
            if (IsApproximatelyVector(m_FlameLocalOffset, s_LegacyFlameTipOffset))
                m_FlameLocalOffset = s_DefaultFlameTipOffset;
        }

        static bool IsApproximatelyVector(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.0001f;
        }

        float GetEffectiveBaseIntensity()
        {
            return Mathf.Clamp(m_BaseIntensity, MinimumLightIntensity, MaxDynamicTorchLightIntensity);
        }

        void ConfigureDecorativeKeyGeometryVisibility()
        {
            if (!m_HideKeyGeometryWhenDecorative)
                return;

            var showKeyGeometry = HasEnabledKeyItem();
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !IsKeyGeometryRenderer(renderer))
                    continue;

                renderer.enabled = showKeyGeometry;
            }
        }

        bool HasEnabledKeyItem()
        {
            var keyItems = GetComponentsInParent<KeyItem>(true);
            for (var i = 0; i < keyItems.Length; i++)
            {
                var keyItem = keyItems[i];
                if (keyItem != null && keyItem.enabled)
                    return true;
            }

            return false;
        }

        static bool IsKeyGeometryRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            var current = renderer.transform;
            while (current != null)
            {
                if (IsKeyOnlyName(current.name))
                    return true;

                current = current.parent;
            }

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer && IsKeyOnlyName(skinnedMeshRenderer.sharedMesh?.name))
                return true;

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && IsKeyOnlyName(meshFilter.sharedMesh?.name))
                return true;

            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material != null && IsKeyOnlyName(material.name))
                    return true;
            }

            return false;
        }

        void EnsureFireAudio()
        {
            if (m_FlameAnchor == null)
                return;

            if (m_AudioSource == null)
                m_AudioSource = GetOrAddComponent<AudioSource>(m_FlameAnchor.gameObject);
            if (m_AudioSource == null)
                return;

            m_AudioSource.clip = GetFireCrackleClip();
            m_AudioSource.playOnAwake = true;
            m_AudioSource.loop = true;
            m_AudioSource.spatialBlend = 1f;
            m_AudioSource.rolloffMode = AudioRolloffMode.Linear;
            m_AudioSource.minDistance = 0.25f;
            m_AudioSource.maxDistance = Mathf.Max(2f, m_LightRange);
            m_AudioSource.volume = 0.12f;

            if (isActiveAndEnabled && !m_AudioSource.isPlaying)
                m_AudioSource.Play();
        }

        Gradient CreateFireGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(m_FireColor, 0f),
                    new GradientColorKey(new Color(1f, 0.78f, 0.2f, 1f), 0.35f),
                    new GradientColorKey(new Color(0.72f, 0.12f, 0.02f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.65f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        static Material GetFireParticleMaterial()
        {
            if (s_FireParticleMaterial != null)
                return s_FireParticleMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            s_FireParticleMaterial = new Material(shader)
            {
                name = "Runtime Torch Fire Material"
            };

            var color = new Color(1f, 0.48f, 0.1f, 1f);
            if (s_FireParticleMaterial.HasProperty("_BaseColor"))
                s_FireParticleMaterial.SetColor("_BaseColor", color);
            if (s_FireParticleMaterial.HasProperty("_Color"))
                s_FireParticleMaterial.SetColor("_Color", color);
            if (s_FireParticleMaterial.HasProperty("_EmissionColor"))
                s_FireParticleMaterial.SetColor("_EmissionColor", color * 2f);

            return s_FireParticleMaterial;
        }

        static Material GetMetaTorchFlameMaterial()
        {
            if (s_MetaTorchFlameMaterial != null)
                return s_MetaTorchFlameMaterial;

            s_MetaTorchFlameMaterial = Resources.Load<Material>(MetaTorchFlameMaterialResourcePath);
            return s_MetaTorchFlameMaterial != null ? s_MetaTorchFlameMaterial : GetFireParticleMaterial();
        }

        static Material GetMetaTorchFlamePoolMaterial()
        {
            if (s_MetaTorchFlamePoolMaterial != null)
                return s_MetaTorchFlamePoolMaterial;

            s_MetaTorchFlamePoolMaterial = Resources.Load<Material>(MetaTorchFlamePoolMaterialResourcePath);
            return s_MetaTorchFlamePoolMaterial != null ? s_MetaTorchFlamePoolMaterial : GetMetaTorchFlameMaterial();
        }

        static AudioClip GetFireCrackleClip()
        {
            if (s_FireCrackleClip != null)
                return s_FireCrackleClip;

            const int lengthSeconds = 2;
            var sampleCount = CrackleFrequency * lengthSeconds;
            var samples = new float[sampleCount];
            var random = new System.Random(1379);
            var lowNoise = 0f;
            var crackleEnvelope = 0f;

            for (var i = 0; i < sampleCount; i++)
            {
                var whiteNoise = (float)(random.NextDouble() * 2.0 - 1.0);
                lowNoise = Mathf.Lerp(lowNoise, whiteNoise, 0.018f);

                if (random.NextDouble() < 0.0017)
                    crackleEnvelope = Mathf.Max(crackleEnvelope, 0.16f + (float)random.NextDouble() * 0.22f);

                crackleEnvelope *= 0.992f;
                var fadeIn = Mathf.Clamp01(i / (CrackleFrequency * 0.05f));
                var fadeOut = Mathf.Clamp01((sampleCount - i) / (CrackleFrequency * 0.05f));
                samples[i] = (lowNoise * 0.035f + whiteNoise * crackleEnvelope) * 0.42f * Mathf.Min(fadeIn, fadeOut);
            }

            s_FireCrackleClip = AudioClip.Create("Runtime Torch Fire Crackle", sampleCount, 1, CrackleFrequency, false);
            s_FireCrackleClip.SetData(samples, 0);
            return s_FireCrackleClip;
        }

        static bool TryGetLocalRendererBounds(Transform root, Transform excludedRoot, out Bounds localBounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            localBounds = default;
            var hasBounds = false;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!renderer.enabled)
                    continue;

                if (excludedRoot != null && renderer.transform.IsChildOf(excludedRoot))
                    continue;

                if (IsAuthoredFlameTransform(renderer.transform))
                    continue;

                if (IsKeyGeometryRenderer(renderer))
                    continue;

                var worldBounds = renderer.bounds;
                var extents = worldBounds.extents;
                for (var x = -1; x <= 1; x += 2)
                {
                    for (var y = -1; y <= 1; y += 2)
                    {
                        for (var z = -1; z <= 1; z += 2)
                        {
                            var corner = worldBounds.center + Vector3.Scale(extents, new Vector3(x, y, z));
                            var localCorner = root.InverseTransformPoint(corner);
                            if (!hasBounds)
                            {
                                localBounds = new Bounds(localCorner, Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                localBounds.Encapsulate(localCorner);
                            }
                        }
                    }
                }
            }

            return hasBounds;
        }

        static bool IsAuthoredFlameTransform(Transform candidate)
        {
            var current = candidate;
            while (current != null)
            {
                if (string.Equals(current.name, AuthoredFlameName, StringComparison.OrdinalIgnoreCase))
                    return true;

                current = current.parent;
            }

            return false;
        }

        static int GetDominantAxis(Vector3 size)
        {
            if (size.y >= size.x && size.y >= size.z)
                return 1;

            return size.z >= size.x ? 2 : 0;
        }

        static float GetAxisValue(Vector3 vector, int axis)
        {
            return axis switch
            {
                1 => vector.y,
                2 => vector.z,
                _ => vector.x
            };
        }

        static void SetAxisValue(ref Vector3 vector, int axis, float value)
        {
            switch (axis)
            {
                case 1:
                    vector.y = value;
                    break;
                case 2:
                    vector.z = value;
                    break;
                default:
                    vector.x = value;
                    break;
            }
        }

        static void EnsureTorchLightSourcesInHierarchy(Transform root)
        {
            var existingSources = root.GetComponentsInChildren<TorchLightSource>(true);
            for (var i = 0; i < existingSources.Length; i++)
            {
                if (existingSources[i] != null)
                    existingSources[i].ConfigureRuntimeInstance();
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (!IsSceneTorchCandidate(candidate) || HasNearbyTorchLightSource(candidate))
                    continue;

                var source = candidate.gameObject.AddComponent<TorchLightSource>();
                if (TryFindTorchTransform(candidate, out var torchTransform))
                    source.m_TorchRoot = torchTransform;
                else
                    source.m_TorchRoot = candidate;

                source.ConfigureRuntimeInstance();
            }
        }

        static bool HasNearbyTorchLightSource(Transform candidate)
        {
            var parent = candidate;
            while (parent != null)
            {
                if (parent.GetComponent<TorchLightSource>() != null)
                    return true;

                parent = parent.parent;
            }

            return candidate.GetComponentInChildren<TorchLightSource>(true) != null;
        }

        static bool IsSceneTorchCandidate(Transform candidate)
        {
            if (candidate == null || string.Equals(candidate.name, FlameAnchorName, StringComparison.OrdinalIgnoreCase))
                return false;

            return !HasEnabledKeyItemInParents(candidate) &&
                   (IsExactTorchName(candidate.name) || NameContainsTorch(candidate.name));
        }

        static bool HasEnabledKeyItemInParents(Transform candidate)
        {
            var current = candidate;
            while (current != null)
            {
                var keyItem = current.GetComponent<KeyItem>();
                if (keyItem != null && keyItem.enabled)
                    return true;

                current = current.parent;
            }

            return false;
        }

        static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            if (target == null)
                return null;

            var component = target.GetComponent<T>();
            if (component == null)
                component = target.AddComponent<T>();

            return component;
        }

        static bool TryFindAuthoredFlameTransform(Transform root, out Transform flameTransform)
        {
            flameTransform = null;
            if (root == null)
                return false;

            var children = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child == null ||
                    child == root ||
                    string.Equals(child.name, FlameAnchorName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(child.name, AuthoredFlameName, StringComparison.OrdinalIgnoreCase))
                {
                    flameTransform = child;
                    return true;
                }
            }

            return false;
        }

        static bool IsExactTorchName(string value)
        {
            return string.Equals(value, DefaultTorchNameHint, StringComparison.OrdinalIgnoreCase);
        }

        static bool NameContainsTorch(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(DefaultTorchNameHint, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool NameContainsKey(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("Key", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsKeyOnlyName(string value)
        {
            return NameContainsKey(value) && !NameContainsTorch(value);
        }
    }
}
