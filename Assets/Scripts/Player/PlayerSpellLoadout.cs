using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using VRCombat.Combat;
using VRCombat.Core;
using VRCombat.Enemies;

namespace VRCombat.Player
{
    public enum PlayerHandSide
    {
        Left,
        Right
    }

    [DisallowMultipleComponent]
    public sealed class PlayerSpellLoadout : MonoBehaviour
    {
        const float MinimumDirectionMagnitude = 0.0001f;
        const float CastOriginOffsetMeters = 0.05f;
        const float MaxAttachAnchorDeviationMeters = 0.35f;

        readonly Dictionary<PlayerHandSide, float> m_NextCastTimeByHand = new Dictionary<PlayerHandSide, float>();

        RunProgressionController m_ProgressionController;
        Transform m_PlayerRoot;
        Transform m_TrackingSpace;
        Transform m_AimReference;
        Transform m_LeftHand;
        Transform m_RightHand;
        Transform m_LeftController;
        Transform m_RightController;
        Func<PlayerHandSide, bool> m_IsHandOccupiedResolver;
        bool m_SpellsEnabled;
        bool m_WasLeftTriggerPressed;
        bool m_WasRightTriggerPressed;
        bool m_WasLeftPrimaryPressed;
        bool m_WasRightPrimaryPressed;

        public void Configure(
            RunProgressionController progressionController,
            Transform playerRoot,
            Transform trackingSpace,
            Transform aimReference,
            Transform leftHand,
            Transform rightHand,
            Transform leftController,
            Transform rightController,
            Func<PlayerHandSide, bool> isHandOccupiedResolver)
        {
            m_ProgressionController = progressionController;
            m_PlayerRoot = playerRoot;
            m_TrackingSpace = trackingSpace != null ? trackingSpace : playerRoot;
            m_AimReference = aimReference != null ? aimReference : playerRoot;
            m_LeftHand = leftHand;
            m_RightHand = rightHand;
            m_LeftController = leftController != null ? leftController : leftHand;
            m_RightController = rightController != null ? rightController : rightHand;
            m_IsHandOccupiedResolver = isHandOccupiedResolver;
            m_NextCastTimeByHand[PlayerHandSide.Left] = 0f;
            m_NextCastTimeByHand[PlayerHandSide.Right] = 0f;
        }

        public void SetSpellsEnabled(bool enabled)
        {
            m_SpellsEnabled = enabled;
        }

        void Update()
        {
            if (!m_SpellsEnabled || m_ProgressionController == null || m_ProgressionController.IsUpgradeSelectionActive)
                return;

            UpdateHand(PlayerHandSide.Left, XRNode.LeftHand, m_LeftHand, m_LeftController, ref m_WasLeftTriggerPressed, ref m_WasLeftPrimaryPressed);
            UpdateHand(PlayerHandSide.Right, XRNode.RightHand, m_RightHand, m_RightController, ref m_WasRightTriggerPressed, ref m_WasRightPrimaryPressed);
        }

        void UpdateHand(
            PlayerHandSide handSide,
            XRNode xrNode,
            Transform handTransform,
            Transform controllerTransform,
            ref bool wasTriggerPressed,
            ref bool wasPrimaryPressed)
        {
            if (handTransform == null || controllerTransform == null)
                return;

            var triggerPressed = ReadTriggerPressed(xrNode);
            var primaryPressed = ReadPrimaryPressed(xrNode);
            var canUseHand = m_IsHandOccupiedResolver == null || !m_IsHandOccupiedResolver(handSide);

            if (primaryPressed && !wasPrimaryPressed && canUseHand)
                m_ProgressionController.CycleSelectedSpell(1);

            if (triggerPressed && !wasTriggerPressed && canUseHand)
                TryCastSelectedSpell(handSide, xrNode, handTransform, controllerTransform);

            wasTriggerPressed = triggerPressed;
            wasPrimaryPressed = primaryPressed;
        }

        void TryCastSelectedSpell(PlayerHandSide handSide, XRNode xrNode, Transform handTransform, Transform controllerTransform)
        {
            var selectedSpell = m_ProgressionController.SelectedSpellKind;
            if (selectedSpell == SpellKind.None || !RunCatalog.TryGetSpell(selectedSpell, out var spellDefinition))
                return;

            if (m_NextCastTimeByHand.TryGetValue(handSide, out var nextCastTime) && Time.time < nextCastTime)
                return;

            ResolveCastPose(xrNode, handTransform, controllerTransform, out var castOrigin, out var castDirection);
            if (castDirection.sqrMagnitude < MinimumDirectionMagnitude)
                castDirection = Vector3.forward;

            switch (selectedSpell)
            {
                case SpellKind.Fireball:
                    CastFireball(castOrigin, castDirection);
                    break;
                case SpellKind.Frost:
                    CastFrostVolley(castOrigin, castDirection);
                    break;
                case SpellKind.Lightning:
                    CastLightning(castOrigin, castDirection);
                    break;
            }

            m_NextCastTimeByHand[handSide] = Time.time + spellDefinition.CooldownSeconds;
        }

        void ResolveCastPose(XRNode xrNode, Transform handTransform, Transform controllerTransform, out Vector3 castOrigin, out Vector3 castDirection)
        {
            XrPoseWorldUtility.TryResolvePoseWorldDetailed(
                xrNode,
                m_TrackingSpace,
                controllerTransform,
                handTransform,
                out var resolvedPosition,
                out var hasTrackedPosition,
                out var resolvedRotation,
                out var hasTrackedRotation,
                out _);

            castOrigin = hasTrackedPosition ? resolvedPosition : (controllerTransform != null ? controllerTransform.position : (handTransform != null ? handTransform.position : transform.position));
            castDirection = hasTrackedRotation ? resolvedRotation * Vector3.forward : (controllerTransform != null ? controllerTransform.forward : (handTransform != null ? handTransform.forward : transform.forward));
            
            // Capture the hand/controller Right vector to act as the pitch axis
            var rightDirection = hasTrackedRotation ? resolvedRotation * Vector3.right : (controllerTransform != null ? controllerTransform.right : (handTransform != null ? handTransform.right : transform.right));
            
            // Pitch down around the local right axis by 60 degrees to match the hand correctly
            castDirection = Quaternion.AngleAxis(60f, rightDirection) * castDirection;

            // Offset slightly to avoid clipping with the hand
            castOrigin += castDirection * 0.05f;
        }

        Vector3 ResolveCastOriginPosition(Transform handTransform, Transform controllerTransform, Vector3? trackedPosition)
        {
            if (trackedPosition.HasValue && IsTrackedPositionUsable(trackedPosition.Value, handTransform, controllerTransform))
            {
                var trackedAnchor = FindValidatedAttachTransform(handTransform, trackedPosition.Value)
                    ?? FindValidatedAttachTransform(controllerTransform, trackedPosition.Value);
                if (trackedAnchor != null)
                    return trackedAnchor.position;

                return trackedPosition.Value;
            }

            if (handTransform != null)
            {
                var handAnchor = FindValidatedAttachTransform(handTransform, handTransform.position);
                if (handAnchor != null)
                    return handAnchor.position;

                return handTransform.position;
            }

            if (controllerTransform != null)
            {
                var controllerAnchor = FindValidatedAttachTransform(controllerTransform, controllerTransform.position);
                if (controllerAnchor != null)
                    return controllerAnchor.position;

                return controllerTransform.position;
            }

            var referenceTransform = m_AimReference != null ? m_AimReference : m_PlayerRoot;
            return referenceTransform != null
                ? referenceTransform.position
                : transform.position;
        }

        Vector3 ResolveCastDirection(Transform handTransform, Transform controllerTransform, Vector3 trackedForward)
        {
            if (TryNormalizeDirection(trackedForward, out var trackedDirection) &&
                IsTrackedDirectionUsable(trackedDirection, handTransform, controllerTransform))
                return trackedDirection;

            if (TryNormalizeDirection(handTransform != null ? handTransform.forward : Vector3.zero, out var handDirection))
                return handDirection;

            if (TryNormalizeDirection(controllerTransform != null ? controllerTransform.forward : Vector3.zero, out var controllerDirection))
                return controllerDirection;

            return ResolveViewForward();
        }

        static bool IsTrackedPositionUsable(Vector3 trackedPosition, Transform handTransform, Transform controllerTransform)
        {
            if (handTransform != null)
                return Vector3.Distance(trackedPosition, handTransform.position) <= MaxAttachAnchorDeviationMeters;

            if (controllerTransform != null)
                return Vector3.Distance(trackedPosition, controllerTransform.position) <= MaxAttachAnchorDeviationMeters;

            return true;
        }

        static bool IsTrackedDirectionUsable(Vector3 trackedDirection, Transform handTransform, Transform controllerTransform)
        {
            if (handTransform != null &&
                TryNormalizeDirection(handTransform.forward, out var handDirection) &&
                Vector3.Dot(trackedDirection, handDirection) < 0f)
            {
                return false;
            }

            if (controllerTransform != null &&
                TryNormalizeDirection(controllerTransform.forward, out var controllerDirection) &&
                Vector3.Dot(trackedDirection, controllerDirection) < -0.25f)
            {
                return false;
            }

            return true;
        }

        static Transform FindValidatedAttachTransform(Transform root, Vector3 referencePosition)
        {
            var attachTransform = FindAttachTransform(root);
            if (attachTransform == null)
                return null;

            if (root != null && Vector3.Distance(attachTransform.position, root.position) <= MaxAttachAnchorDeviationMeters)
                return attachTransform;

            return Vector3.Distance(attachTransform.position, referencePosition) <= MaxAttachAnchorDeviationMeters
                ? attachTransform
                : null;
        }

        static Transform FindAttachTransform(Transform root)
        {
            if (root == null)
                return null;

            var directAttach = root.Find("Attach") ?? root.Find("Attach Point");
            if (directAttach != null)
                return directAttach;

            var children = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                if (child != null && child.name.IndexOf("attach", StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }

            return null;
        }

        Vector3 ResolveCastOrigin(Vector3 anchorPosition, Vector3 castDirection)
        {
            return anchorPosition + castDirection * CastOriginOffsetMeters;
        }

        Vector3 ResolveViewForward()
        {
            var referenceTransform = m_AimReference != null ? m_AimReference : m_PlayerRoot;
            if (referenceTransform == null)
                return Vector3.forward;

            var forward = referenceTransform.forward;
            if (forward.sqrMagnitude < MinimumDirectionMagnitude)
                forward = referenceTransform.up;

            return forward.sqrMagnitude > MinimumDirectionMagnitude
                ? forward.normalized
                : Vector3.forward;
        }

        static bool TryNormalizeDirection(Vector3 directionCandidate, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (directionCandidate.sqrMagnitude < MinimumDirectionMagnitude)
                return false;

            direction = directionCandidate.normalized;
            return true;
        }

        void CastFireball(Vector3 origin, Vector3 direction)
        {
            var damageMultiplier = m_ProgressionController.GetSpellDamageMultiplier(SpellKind.Fireball);
            var projectileObject = CreateProjectileRoot("Fireball", origin, Quaternion.LookRotation(direction, Vector3.up));
            var projectile = projectileObject.AddComponent<SpellProjectile>();
            projectile.Initialize(
                playerRoot: m_PlayerRoot,
                speed: 12f,
                lifetimeSeconds: 2.2f,
                damage: 12f * damageMultiplier,
                hitRadius: 0.16f,
                color: new Color(1f, 0.35f, 0.08f, 1f),
                onHit: damageable =>
                {
                    if (damageable is IStatusEffectTarget statusTarget)
                        statusTarget.ApplyBurn(5f * damageMultiplier, 4f, gameObject);
                },
                visualKind: SpellProjectileVisualKind.Fireball);
        }

        void CastFrostVolley(Vector3 origin, Vector3 direction)
        {
            var damageMultiplier = m_ProgressionController.GetSpellDamageMultiplier(SpellKind.Frost);
            for (var projectileIndex = 0; projectileIndex < 3; projectileIndex++)
            {
                var spread = Quaternion.AngleAxis((projectileIndex - 1) * 7f, Vector3.up);
                var projectileDirection = spread * direction;
                var projectileObject = CreateProjectileRoot($"Icicle {projectileIndex + 1}", origin, Quaternion.LookRotation(projectileDirection, Vector3.up));
                var projectile = projectileObject.AddComponent<SpellProjectile>();
                projectile.Initialize(
                    playerRoot: m_PlayerRoot,
                    speed: 14f,
                    lifetimeSeconds: 1.6f,
                    damage: 7f * damageMultiplier,
                    hitRadius: 0.1f,
                    color: new Color(0.45f, 0.86f, 1f, 1f),
                    onHit: damageable =>
                    {
                        if (damageable is IStatusEffectTarget statusTarget)
                            statusTarget.ApplySlow(0.45f, 2.5f, gameObject);
                    },
                    visualKind: SpellProjectileVisualKind.Icicle);
            }
        }

        void CastLightning(Vector3 origin, Vector3 direction)
        {
            var damageMultiplier = m_ProgressionController.GetSpellDamageMultiplier(SpellKind.Lightning);
            if (!TryResolveLightningTarget(origin, direction, out var primaryEnemy, out var primaryHitPoint))
                return;

            var chainedEnemies = new List<CapsuleEnemy> { primaryEnemy };
            primaryEnemy.ApplyDamage(18f * damageMultiplier, primaryHitPoint, gameObject);
            SpawnLightningArc(origin, primaryHitPoint);

            var chainOrigin = primaryEnemy.transform.position;
            for (var chainIndex = 0; chainIndex < 2; chainIndex++)
            {
                if (!TryFindClosestChainTarget(chainOrigin, chainedEnemies, out var chainedEnemy))
                    break;

                chainedEnemies.Add(chainedEnemy);
                var chainHitPoint = chainedEnemy.transform.position + Vector3.up * 0.9f;
                chainedEnemy.ApplyDamage(18f * 0.65f * damageMultiplier, chainHitPoint, gameObject);
                SpawnLightningArc(chainOrigin + Vector3.up * 0.9f, chainHitPoint);
                chainOrigin = chainedEnemy.transform.position;
            }
        }

        bool TryResolveLightningTarget(Vector3 origin, Vector3 direction, out CapsuleEnemy enemy, out Vector3 hitPoint)
        {
            enemy = null;
            hitPoint = default;

            var ray = new Ray(origin, direction);
            if (Physics.SphereCast(ray, 0.25f, out var hit, 14f, ~0, QueryTriggerInteraction.Ignore))
            {
                enemy = hit.collider != null ? hit.collider.GetComponentInParent<CapsuleEnemy>() : null;
                if (enemy != null)
                {
                    hitPoint = hit.point;
                    return true;
                }
            }

            var allEnemies = FindObjectsByType<CapsuleEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var bestScore = float.PositiveInfinity;
            for (var i = 0; i < allEnemies.Length; i++)
            {
                var candidate = allEnemies[i];
                if (candidate == null)
                    continue;

                var toEnemy = candidate.transform.position - origin;
                var distance = toEnemy.magnitude;
                if (distance > 12f || distance < 0.05f)
                    continue;

                var angle = Vector3.Angle(direction, toEnemy.normalized);
                if (angle > 22f)
                    continue;

                var score = distance + angle * 0.2f;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                enemy = candidate;
                hitPoint = candidate.transform.position + Vector3.up * 0.9f;
            }

            return enemy != null;
        }

        bool TryFindClosestChainTarget(Vector3 origin, List<CapsuleEnemy> excludedEnemies, out CapsuleEnemy chainedEnemy)
        {
            chainedEnemy = null;
            var allEnemies = FindObjectsByType<CapsuleEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var bestDistanceSqr = float.PositiveInfinity;
            for (var i = 0; i < allEnemies.Length; i++)
            {
                var candidate = allEnemies[i];
                if (candidate == null || excludedEnemies.Contains(candidate))
                    continue;

                var distanceSqr = (candidate.transform.position - origin).sqrMagnitude;
                if (distanceSqr > 36f || distanceSqr >= bestDistanceSqr)
                    continue;

                bestDistanceSqr = distanceSqr;
                chainedEnemy = candidate;
            }

            return chainedEnemy != null;
        }

        void SpawnLightningArc(Vector3 origin, Vector3 target)
        {
            var arcObject = new GameObject("Lightning Arc", typeof(LineRenderer));
            arcObject.transform.SetParent(transform, false);
            var lineRenderer = arcObject.GetComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.widthMultiplier = 0.03f;
            lineRenderer.numCapVertices = 3;
            lineRenderer.SetPosition(0, origin);
            lineRenderer.SetPosition(1, target);
            lineRenderer.sharedMaterial = SpellProjectile.CreateSpellMaterial(new Color(0.76f, 0.9f, 1f, 1f));
            lineRenderer.startColor = new Color(0.95f, 0.98f, 1f, 1f);
            lineRenderer.endColor = new Color(0.45f, 0.7f, 1f, 0.35f);
            StartCoroutine(DestroyAfterSeconds(arcObject, 0.12f));
        }

        GameObject CreateProjectileRoot(string projectileName, Vector3 origin, Quaternion rotation)
        {
            var projectileObject = new GameObject(projectileName);
            projectileObject.transform.SetParent(null, true);
            projectileObject.transform.SetPositionAndRotation(origin, rotation);
            return projectileObject;
        }

        IEnumerator DestroyAfterSeconds(GameObject target, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (target != null)
                Destroy(target);
        }

        static bool ReadTriggerPressed(XRNode handNode)
        {
            var device = InputDevices.GetDeviceAtXRNode(handNode);
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out var triggerButton) && triggerButton)
                return true;

            if (device.TryGetFeatureValue(CommonUsages.trigger, out var triggerValue))
                return triggerValue >= 0.72f;

            return false;
        }

        static bool ReadPrimaryPressed(XRNode handNode)
        {
            var device = InputDevices.GetDeviceAtXRNode(handNode);
            return device.isValid &&
                   device.TryGetFeatureValue(CommonUsages.primaryButton, out var primaryButton) &&
                   primaryButton;
        }
    }

    enum SpellProjectileVisualKind
    {
        Fireball,
        Icicle
    }

    [DisallowMultipleComponent]
    sealed class SpellProjectile : MonoBehaviour
    {
        Transform m_PlayerRoot;
        float m_Speed;
        float m_LifetimeRemaining;
        float m_Damage;
        float m_HitRadius;
        Action<IDamageable> m_OnHit;
        Vector3 m_LastPosition;
        Renderer m_Renderer;
        SpellProjectileVisualKind m_VisualKind;
        Color m_Color;

        public void Initialize(
            Transform playerRoot,
            float speed,
            float lifetimeSeconds,
            float damage,
            float hitRadius,
            Color color,
            Action<IDamageable> onHit,
            SpellProjectileVisualKind visualKind)
        {
            m_PlayerRoot = playerRoot;
            m_Speed = speed;
            m_LifetimeRemaining = lifetimeSeconds;
            m_Damage = damage;
            m_HitRadius = hitRadius;
            m_OnHit = onHit;
            m_LastPosition = transform.position;
            m_VisualKind = visualKind;
            m_Color = color;

            BuildVisual(visualKind, color);
        }

        void Update()
        {
            var displacement = transform.forward * (m_Speed * Time.deltaTime);
            var nextPosition = transform.position + displacement;

            if (Physics.SphereCast(
                    transform.position,
                    m_HitRadius,
                    transform.forward,
                    out var hit,
                    displacement.magnitude,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                if (ShouldIgnoreCollider(hit.collider))
                {
                    transform.position = nextPosition;
                }
                else
                {
                    HandleHit(hit.collider, hit.point);
                    return;
                }
            }
            else
            {
                transform.position = nextPosition;
            }

            m_LastPosition = transform.position;
            m_LifetimeRemaining -= Time.deltaTime;
            if (m_LifetimeRemaining <= 0f)
                Destroy(gameObject);
        }

        void BuildVisual(SpellProjectileVisualKind visualKind, Color color)
        {
            GameObject visualObject;
            switch (visualKind)
            {
                case SpellProjectileVisualKind.Icicle:
                    visualObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    visualObject.transform.SetParent(transform, false);
                    visualObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    visualObject.transform.localScale = new Vector3(0.05f, 0.18f, 0.05f);
                    
                    var iceTrailObject = new GameObject("Ice Trail");
                    iceTrailObject.transform.SetParent(visualObject.transform, false);
                    var icePs = iceTrailObject.AddComponent<ParticleSystem>();
                    var icePsRenderer = iceTrailObject.GetComponent<ParticleSystemRenderer>();
                    icePsRenderer.material = CreateSpellMaterial(new Color(0.6f, 0.9f, 1f));

                    var iceMain = icePs.main;
                    iceMain.duration = 1f;
                    iceMain.loop = true;
                    iceMain.startLifetime = 0.5f;
                    iceMain.startSpeed = 0.5f;
                    iceMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                    iceMain.simulationSpace = ParticleSystemSimulationSpace.World;

                    var iceEmission = icePs.emission;
                    iceEmission.rateOverTime = 60;

                    var iceShape = icePs.shape;
                    iceShape.shapeType = ParticleSystemShapeType.Cone;
                    iceShape.angle = 10f;
                    iceShape.radius = 0.05f;

                    var iceSize = icePs.sizeOverLifetime;
                    iceSize.enabled = true;
                    var iceCurve = new AnimationCurve();
                    iceCurve.AddKey(0f, 1f);
                    iceCurve.AddKey(1f, 0f);
                    iceSize.size = new ParticleSystem.MinMaxCurve(1f, iceCurve);
                    break;
                case SpellProjectileVisualKind.Fireball:
                    visualObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    visualObject.transform.SetParent(transform, false);
                    visualObject.transform.localScale = Vector3.one * 0.18f;

                    // Add trailing Particle System for Fireball
                    var psObject = new GameObject("Fire Trail");
                    psObject.transform.SetParent(visualObject.transform, false);
                    var ps = psObject.AddComponent<ParticleSystem>();
                    var psRenderer = psObject.GetComponent<ParticleSystemRenderer>();
                    psRenderer.material = CreateSpellMaterial(new Color(1f, 0.6f, 0.1f));

                    var main = ps.main;
                    main.duration = 1f;
                    main.loop = true;
                    main.startLifetime = 0.3f;
                    main.startSpeed = 0f;
                    main.startSize = 0.15f;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;

                    var emission = ps.emission;
                    emission.rateOverTime = 80;

                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.08f;

                    var sizeOverLifetime = ps.sizeOverLifetime;
                    sizeOverLifetime.enabled = true;
                    var curve = new AnimationCurve();
                    curve.AddKey(0f, 1f);
                    curve.AddKey(1f, 0f);
                    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);
                    break;
                default:
                    visualObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    visualObject.transform.SetParent(transform, false);
                    visualObject.transform.localScale = Vector3.one * 0.18f;
                    break;
            }

            var collider = visualObject.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            m_Renderer = visualObject.GetComponent<Renderer>();
            if (m_Renderer != null)
                m_Renderer.sharedMaterial = CreateSpellMaterial(color);
        }

        void HandleHit(Collider hitCollider, Vector3 hitPoint)
        {
            var damageable = hitCollider != null ? hitCollider.GetComponentInParent<IDamageable>() : null;
            if (damageable != null)
            {
                damageable.ApplyDamage(m_Damage, hitPoint, gameObject);
                m_OnHit?.Invoke(damageable);
            }

            CreateHitVisual(hitPoint);
            Destroy(gameObject);
        }

        void CreateHitVisual(Vector3 hitPoint)
        {
            if (m_VisualKind != SpellProjectileVisualKind.Fireball && m_VisualKind != SpellProjectileVisualKind.Icicle) return;

            var explosionObject = new GameObject(m_VisualKind == SpellProjectileVisualKind.Fireball ? "Fireball Explosion" : "Ice Shatter");
            explosionObject.transform.position = hitPoint;
            
            var ps = explosionObject.AddComponent<ParticleSystem>();
            var psRenderer = explosionObject.GetComponent<ParticleSystemRenderer>();
            psRenderer.material = CreateSpellMaterial(m_Color);

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, m_VisualKind == SpellProjectileVisualKind.Fireball ? 3f : 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, m_VisualKind == SpellProjectileVisualKind.Fireball ? 0.3f : 0.15f);
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, m_VisualKind == SpellProjectileVisualKind.Fireball ? 30 : 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);

            if (m_VisualKind == SpellProjectileVisualKind.Icicle)
            {
                var velocityOverLifetime = ps.velocityOverLifetime;
                velocityOverLifetime.enabled = true;
                velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(1f, 2f);
            }

            ps.Play();
            Destroy(explosionObject, 1.0f);
        }

        bool ShouldIgnoreCollider(Collider candidate)
        {
            if (candidate == null)
                return true;

            var candidateTransform = candidate.transform;
            return m_PlayerRoot != null && (candidateTransform == m_PlayerRoot || candidateTransform.IsChildOf(m_PlayerRoot));
        }

        public static Material CreateSpellMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var material = new Material(shader)
            {
                name = "Runtime Spell Material"
            };

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            return material;
        }
    }
}
