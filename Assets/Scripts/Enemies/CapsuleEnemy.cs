using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRCombat.Combat;
using VRCombat.Player;

namespace VRCombat.Enemies
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class CapsuleEnemy : MonoBehaviour, IDamageable
    {
        [SerializeField]
        float m_MaxHealth = 30f;

        [SerializeField]
        float m_MoveSpeed = 1.35f;

        [SerializeField]
        float m_ContactDamage = 12f;

        [SerializeField]
        float m_PlayerContactCooldown = 0.8f;

        [SerializeField]
        float m_PlayerKnockbackAtFullHealth = 0.62f;

        [SerializeField]
        float m_PlayerKnockbackAtZeroHealth = 2.2f;

        [SerializeField]
        float m_DamageKnockbackImpulse = 0.22f;

        [SerializeField]
        float m_MaxDamageKnockbackImpulse = 1.05f;

        [SerializeField]
        float m_KnockbackDamping = 4.2f;

        [SerializeField]
        [Range(0.1f, 0.98f)]
        float m_KnockbackBounceRetainedSpeed = 0.72f;

        [SerializeField]
        float m_KnockbackCollisionSkin = 0.02f;

        [SerializeField]
        float m_ChaseResumeKnockbackSpeed = 0.22f;

        [SerializeField]
        float m_ResumeChaseDelayAfterRelease = 0.7f;

        [SerializeField]
        float m_MaxGrabDistance = 0.9f;

        [SerializeField]
        float m_ObstacleCheckDistance = 0.95f;

        [SerializeField]
        float m_ObstacleProbeRadius = 0.22f;

        [SerializeField]
        float m_ObstacleAvoidanceStrength = 1.25f;

        [SerializeField]
        float m_SeparationRadius = 0.65f;

        [SerializeField]
        float m_SeparationStrength = 0.8f;

        [SerializeField]
        Renderer m_Renderer;

        [SerializeField]
        Color m_HealthyColor = new Color(0.2f, 0.85f, 0.35f, 1f);

        [SerializeField]
        Color m_DamagedColor = new Color(1f, 0.25f, 0.25f, 1f);

        [SerializeField]
        Color m_HitFlashColor = new Color(1f, 0.88f, 0.88f, 1f);

        [SerializeField]
        float m_HitFlashDuration = 0.08f;

        [SerializeField]
        Color m_BleedColor = new Color(0.5f, 0.03f, 0.05f, 0.72f);

        [SerializeField]
        int m_MaxBloodDecals = 90;

        [SerializeField]
        int m_MinDecalsPerHit = 1;

        [SerializeField]
        int m_MaxDecalsPerHit = 2;

        [SerializeField]
        float m_MinDecalSize = 0.08f;

        [SerializeField]
        float m_MaxDecalSize = 0.2f;

        [SerializeField]
        int m_MinSplatterParticlesPerHit = 4;

        [SerializeField]
        int m_MaxSplatterParticlesPerHit = 12;

        [SerializeField]
        int m_MaxConcurrentSplatterParticles = 120;

        [SerializeField]
        float m_DeathToppleImpulse = 1.35f;

        [SerializeField]
        float m_DeathToppleTorque = 14f;

        [SerializeField]
        float m_DeathSpinRandomTorque = 3f;

        [SerializeField]
        float m_DeathDespawnDelay = 1.2f;

        Rigidbody m_Rigidbody;
        CapsuleCollider m_CapsuleCollider;
        XRGrabInteractable m_GrabInteractable;
        MaxGrabDistanceSelectFilter m_GrabDistanceFilter;
        Transform m_Target;
        Material m_RuntimeMaterial;
        ParticleSystem m_SplatterParticles;
        float m_CurrentHealth;
        bool m_IsGrabbed;
        float m_ResumeChaseAtTime;
        float m_LastPlayerContactTime = -100f;
        float m_LastHitTime = -100f;
        bool m_DidDestroyCleanup;
        bool m_IsDying;
        Vector3 m_KnockbackVelocity;

        readonly Collider[] m_SeparationBuffer = new Collider[12];
        readonly RaycastHit[] m_KnockbackHitBuffer = new RaycastHit[10];

        static Material s_BleedDecalMaterial;
        static Material s_BleedSplatterMaterial;
        static readonly Queue<GameObject> s_BloodDecalQueue = new Queue<GameObject>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static int s_GlobalMaxBloodDecals = 140;

        public static void ClearRuntimeDecals()
        {
            while (s_BloodDecalQueue.Count > 0)
            {
                var decal = s_BloodDecalQueue.Dequeue();
                if (decal != null)
                    Destroy(decal);
            }
        }

        public void SetTarget(Transform target)
        {
            m_Target = target;
        }

        public void ConfigureGrabDistance(float maxGrabDistance)
        {
            m_MaxGrabDistance = Mathf.Max(0.2f, maxGrabDistance);
            EnsureGrabDistanceFilter();
        }

        public void FreezeForGameOver()
        {
            m_IsGrabbed = true;
            m_ResumeChaseAtTime = Time.time + 999f;

            if (m_GrabInteractable != null)
                m_GrabInteractable.enabled = false;

            var allColliders = GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < allColliders.Length; i++)
            {
                if (allColliders[i] != null)
                    allColliders[i].enabled = false;
            }

            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
                m_Rigidbody.useGravity = false;
                m_Rigidbody.isKinematic = true;
                m_Rigidbody.detectCollisions = false;
            }
        }

        void Awake()
        {
            m_CurrentHealth = m_MaxHealth;
            m_Rigidbody = GetComponent<Rigidbody>();
            m_CapsuleCollider = GetComponent<CapsuleCollider>();
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
            if (m_GrabInteractable == null)
                m_GrabInteractable = gameObject.AddComponent<XRGrabInteractable>();

            m_Rigidbody.useGravity = true;
            m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            m_Rigidbody.linearDamping = 0.08f;
            m_Rigidbody.angularDamping = 0.06f;

            m_GrabInteractable.trackPosition = true;
            m_GrabInteractable.trackRotation = true;
            m_GrabInteractable.throwOnDetach = true;
            m_GrabInteractable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
            m_GrabInteractable.selectExited.AddListener(OnSelectExited);
            EnsureGrabDistanceFilter();

            if (m_Renderer == null)
                m_Renderer = GetComponentInChildren<Renderer>();

            if (m_Renderer != null)
                m_RuntimeMaterial = m_Renderer.material;

            SetupSplatterParticles();
            UpdateColor();
        }

        void EnsureGrabDistanceFilter()
        {
            if (m_GrabInteractable == null)
                return;

            if (m_GrabDistanceFilter == null)
                m_GrabDistanceFilter = GetComponent<MaxGrabDistanceSelectFilter>();
            if (m_GrabDistanceFilter == null)
                m_GrabDistanceFilter = gameObject.AddComponent<MaxGrabDistanceSelectFilter>();

            m_GrabDistanceFilter.Configure(m_MaxGrabDistance);
            AddSelectFilterIfMissing(m_GrabInteractable, m_GrabDistanceFilter);
        }

        static void AddSelectFilterIfMissing(XRBaseInteractable interactable, IXRSelectFilter filter)
        {
            if (interactable == null || filter == null)
                return;

            var existingFilters = new List<IXRSelectFilter>();
            interactable.selectFilters.GetAll(existingFilters);
            for (var i = 0; i < existingFilters.Count; i++)
            {
                if (ReferenceEquals(existingFilters[i], filter))
                    return;
            }

            interactable.selectFilters.Add(filter);
        }

        void Update()
        {
            UpdateColor();
        }

        void FixedUpdate()
        {
            if (m_IsDying)
                return;

            ApplyKnockbackMovement(Time.fixedDeltaTime);

            if (m_Target == null || m_IsGrabbed || Time.time < m_ResumeChaseAtTime)
                return;

            var chaseResumeSpeed = Mathf.Max(0.01f, m_ChaseResumeKnockbackSpeed);
            if (m_KnockbackVelocity.sqrMagnitude > chaseResumeSpeed * chaseResumeSpeed)
                return;

            var currentPosition = m_Rigidbody.position;
            var targetPosition = m_Target.position;
            targetPosition.y = currentPosition.y;

            var toTarget = targetPosition - currentPosition;
            if (toTarget.sqrMagnitude < 0.0025f)
                return;

            var desiredDirection = toTarget.normalized;
            var moveDirection = CalculateMoveDirection(currentPosition, desiredDirection);
            var step = moveDirection * (m_MoveSpeed * Time.fixedDeltaTime);
            m_Rigidbody.MovePosition(currentPosition + step);
        }

        Vector3 CalculateMoveDirection(Vector3 currentPosition, Vector3 desiredDirection)
        {
            desiredDirection.y = 0f;
            if (desiredDirection.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            desiredDirection.Normalize();
            var moveDirection = desiredDirection;

            var probeStart = currentPosition + Vector3.up * 0.35f;
            if (Physics.SphereCast(
                    probeStart,
                    m_ObstacleProbeRadius,
                    desiredDirection,
                    out var hit,
                    m_ObstacleCheckDistance,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                if (!ShouldIgnoreObstacle(hit.collider))
                {
                    var tangentRight = Vector3.Cross(Vector3.up, hit.normal).normalized;
                    var tangentLeft = -tangentRight;
                    var preferredTangent = Vector3.Dot(tangentRight, desiredDirection) > Vector3.Dot(tangentLeft, desiredDirection)
                        ? tangentRight
                        : tangentLeft;
                    moveDirection = (desiredDirection + preferredTangent * m_ObstacleAvoidanceStrength).normalized;
                }
            }

            var overlapCount = Physics.OverlapSphereNonAlloc(
                currentPosition,
                m_SeparationRadius,
                m_SeparationBuffer,
                ~0,
                QueryTriggerInteraction.Ignore);

            if (overlapCount > 0)
            {
                var separation = Vector3.zero;
                for (var i = 0; i < overlapCount; i++)
                {
                    var otherCollider = m_SeparationBuffer[i];
                    if (otherCollider == null)
                        continue;

                    var otherEnemy = otherCollider.GetComponentInParent<CapsuleEnemy>();
                    if (otherEnemy == null || otherEnemy == this)
                        continue;

                    var away = currentPosition - otherEnemy.transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.0001f)
                        continue;

                    separation += away.normalized / Mathf.Max(away.magnitude, 0.05f);
                }

                if (separation.sqrMagnitude > 0.0001f)
                    moveDirection = (moveDirection + separation.normalized * m_SeparationStrength).normalized;
            }

            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude < 0.0001f)
                return desiredDirection;

            return moveDirection;
        }

        bool ShouldIgnoreObstacle(Collider collider)
        {
            if (collider == null)
                return true;

            if (collider.transform == transform || collider.transform.IsChildOf(transform))
                return true;

            if (collider.GetComponentInParent<CapsuleEnemy>() != null)
                return true;

            if (collider.GetComponentInParent<PlayerDamageReceiver>() != null)
                return true;

            return false;
        }

        void ApplyDamageKnockback(float damageAmount, Vector3 hitPoint, GameObject source)
        {
            if (m_IsGrabbed || m_IsDying || damageAmount <= 0f)
                return;

            var knockbackDirection = source != null
                ? transform.position - source.transform.position
                : transform.position - hitPoint;
            knockbackDirection.y = 0f;
            if (knockbackDirection.sqrMagnitude < 0.0001f)
                knockbackDirection = transform.forward;
            if (knockbackDirection.sqrMagnitude < 0.0001f)
                knockbackDirection = Vector3.forward;

            knockbackDirection.Normalize();

            var normalizedDamage = Mathf.Clamp01(damageAmount / Mathf.Max(0.01f, m_MaxHealth * 0.45f));
            var impulse = Mathf.Lerp(
                Mathf.Max(0.01f, m_DamageKnockbackImpulse),
                Mathf.Max(m_DamageKnockbackImpulse, m_MaxDamageKnockbackImpulse),
                normalizedDamage);
            m_KnockbackVelocity += knockbackDirection * impulse;
        }

        void ApplyKnockbackMovement(float deltaTime)
        {
            if (m_Rigidbody == null)
                return;

            if (m_KnockbackVelocity.sqrMagnitude < 0.0001f)
            {
                m_KnockbackVelocity = Vector3.zero;
                return;
            }

            var simulatedPosition = m_Rigidbody.position;
            var remainingDisplacement = m_KnockbackVelocity * Mathf.Max(0f, deltaTime);
            var bounceRetention = Mathf.Clamp(m_KnockbackBounceRetainedSpeed, 0.1f, 0.98f);

            for (var bounceIndex = 0; bounceIndex < 3; bounceIndex++)
            {
                if (remainingDisplacement.sqrMagnitude < 0.0000001f)
                    break;

                var direction = remainingDisplacement.normalized;
                var distance = remainingDisplacement.magnitude;
                if (!TryGetKnockbackObstacleHit(simulatedPosition, direction, distance, out var hit))
                {
                    simulatedPosition += remainingDisplacement;
                    break;
                }

                var travelDistance = Mathf.Max(0f, hit.distance - m_KnockbackCollisionSkin);
                if (travelDistance > 0f)
                    simulatedPosition += direction * travelDistance;

                var remainingDistance = Mathf.Max(0f, distance - travelDistance);
                var reflectedDirection = Vector3.ProjectOnPlane(Vector3.Reflect(direction, hit.normal), Vector3.up);
                if (reflectedDirection.sqrMagnitude < 0.0001f)
                {
                    m_KnockbackVelocity = Vector3.zero;
                    break;
                }

                reflectedDirection.Normalize();
                m_KnockbackVelocity = reflectedDirection * (m_KnockbackVelocity.magnitude * bounceRetention);
                remainingDisplacement = reflectedDirection * (remainingDistance * bounceRetention);
            }

            if ((simulatedPosition - m_Rigidbody.position).sqrMagnitude > 0.0000001f)
                m_Rigidbody.MovePosition(simulatedPosition);

            var damping = Mathf.Max(0.01f, m_KnockbackDamping);
            m_KnockbackVelocity = Vector3.Lerp(m_KnockbackVelocity, Vector3.zero, damping * deltaTime);
        }

        bool TryGetKnockbackObstacleHit(Vector3 basePosition, Vector3 direction, float distance, out RaycastHit bestHit)
        {
            bestHit = default;
            if (direction.sqrMagnitude < 0.0001f || distance <= 0.0001f)
                return false;

            GetCapsuleCastEndpoints(basePosition, out var point1, out var point2, out var radius);
            var castDistance = distance + Mathf.Max(0.001f, m_KnockbackCollisionSkin);
            var hitCount = Physics.CapsuleCastNonAlloc(
                point1,
                point2,
                radius,
                direction,
                m_KnockbackHitBuffer,
                castDistance,
                ~0,
                QueryTriggerInteraction.Ignore);

            var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var hit = m_KnockbackHitBuffer[i];
                if (ShouldIgnoreKnockbackCollider(hit.collider))
                    continue;

                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                bestHit = hit;
            }

            return bestDistance < float.PositiveInfinity;
        }

        void GetCapsuleCastEndpoints(Vector3 basePosition, out Vector3 point1, out Vector3 point2, out float radius)
        {
            if (m_CapsuleCollider == null)
            {
                radius = 0.24f;
                point1 = basePosition + Vector3.up * 0.5f;
                point2 = basePosition - Vector3.up * 0.5f;
                return;
            }

            var scale = transform.lossyScale;
            var horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            radius = Mathf.Max(0.06f, m_CapsuleCollider.radius * horizontalScale);
            var height = Mathf.Max(radius * 2f + 0.01f, m_CapsuleCollider.height * Mathf.Abs(scale.y));
            var center = basePosition + transform.rotation * m_CapsuleCollider.center;
            var halfLine = Mathf.Max(0f, height * 0.5f - radius);
            point1 = center + transform.up * halfLine;
            point2 = center - transform.up * halfLine;
        }

        bool ShouldIgnoreKnockbackCollider(Collider collider)
        {
            if (collider == null)
                return true;

            if (collider.transform == transform || collider.transform.IsChildOf(transform))
                return true;

            if (ShouldIgnoreObstacle(collider))
                return true;

            if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                return true;

            return false;
        }

        public void ApplyDamage(float amount, Vector3 hitPoint, GameObject source)
        {
            if (amount <= 0f || m_IsDying)
                return;

            m_CurrentHealth -= amount;
            m_LastHitTime = Time.time;
            EmitBloodSplatter(hitPoint, amount);
            EmitBleedDecals(hitPoint, amount);
            ApplyDamageKnockback(amount, hitPoint, source);

            if (m_CurrentHealth <= 0f)
            {
                BeginDeathSequence(source, hitPoint);
                return;
            }

            UpdateColor();
        }

        public void HandlePlayerContact(PlayerDamageReceiver playerDamageReceiver)
        {
            if (playerDamageReceiver == null || m_IsGrabbed || m_IsDying)
                return;

            if (Time.time - m_LastPlayerContactTime < m_PlayerContactCooldown)
                return;

            m_LastPlayerContactTime = Time.time;
            playerDamageReceiver.ReceiveDamage(m_ContactDamage);

            var knockbackDirection = playerDamageReceiver.transform.position - transform.position;
            knockbackDirection.y = 0f;
            playerDamageReceiver.ApplyKnockback(
                knockbackDirection,
                m_PlayerKnockbackAtFullHealth,
                m_PlayerKnockbackAtZeroHealth);
        }

        void OnDestroy()
        {
            if (m_DidDestroyCleanup)
                return;

            m_DidDestroyCleanup = true;

            if (m_GrabInteractable == null)
                return;

            m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
            m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (m_IsDying)
                return;

            m_IsGrabbed = true;
            m_Rigidbody.constraints = RigidbodyConstraints.None;
            m_Rigidbody.useGravity = true;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_KnockbackVelocity = Vector3.zero;
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_IsDying)
                return;

            m_IsGrabbed = false;
            m_ResumeChaseAtTime = Time.time + m_ResumeChaseDelayAfterRelease;
            m_Rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            m_Rigidbody.useGravity = true;
        }

        void BeginDeathSequence(GameObject source, Vector3 hitPoint)
        {
            if (m_IsDying)
                return;

            m_IsDying = true;
            m_IsGrabbed = false;

            if (m_GrabInteractable != null)
                m_GrabInteractable.enabled = false;

            m_Rigidbody.constraints = RigidbodyConstraints.None;
            m_Rigidbody.useGravity = true;
            m_Rigidbody.linearDamping = Mathf.Max(m_Rigidbody.linearDamping, 0.12f);
            m_Rigidbody.angularDamping = Mathf.Max(m_Rigidbody.angularDamping, 0.12f);

            var away = source != null ? transform.position - source.transform.position : Random.onUnitSphere;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = transform.right;
            away.Normalize();

            var impulseDirection = (away + Vector3.up * 0.35f).normalized;
            m_Rigidbody.AddForce(impulseDirection * m_DeathToppleImpulse, ForceMode.Impulse);

            var toppleAxis = Vector3.Cross(Vector3.up, away).normalized;
            if (toppleAxis.sqrMagnitude < 0.0001f)
                toppleAxis = transform.right;

            m_Rigidbody.AddTorque(toppleAxis * m_DeathToppleTorque, ForceMode.Impulse);
            m_Rigidbody.AddTorque(Random.insideUnitSphere * m_DeathSpinRandomTorque, ForceMode.Impulse);

            EmitBloodSplatter(hitPoint, m_MaxHealth * 0.35f);
            EmitBleedDecals(hitPoint, m_MaxHealth * 0.12f);
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            yield return new WaitForSeconds(m_DeathDespawnDelay);
            Destroy(gameObject);
        }

        void UpdateColor()
        {
            if (m_RuntimeMaterial == null)
                return;

            var normalizedHealth = Mathf.Clamp01(m_CurrentHealth / Mathf.Max(m_MaxHealth, 0.0001f));
            var baseColor = Color.Lerp(m_DamagedColor, m_HealthyColor, normalizedHealth);

            var flashElapsed = Time.time - m_LastHitTime;
            var flashStrength = flashElapsed < m_HitFlashDuration
                ? 1f - flashElapsed / Mathf.Max(m_HitFlashDuration, 0.0001f)
                : 0f;

            var finalColor = Color.Lerp(baseColor, m_HitFlashColor, flashStrength);
            SetMaterialColor(m_RuntimeMaterial, finalColor);
        }

        void SetupSplatterParticles()
        {
            var splatterObject = new GameObject("Blood Splatter");
            splatterObject.transform.SetParent(transform, false);
            m_SplatterParticles = splatterObject.AddComponent<ParticleSystem>();

            var main = m_SplatterParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = m_BleedColor;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 1.7f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.72f, 1.15f);
            main.maxParticles = Mathf.Max(24, m_MaxConcurrentSplatterParticles);

            var emission = m_SplatterParticles.emission;
            emission.enabled = false;

            var shape = m_SplatterParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.015f;
            shape.length = 0.05f;

            var renderer = m_SplatterParticles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            var material = GetOrCreateBleedSplatterMaterial(m_BleedColor);
            if (material != null)
                renderer.sharedMaterial = material;
        }

        void EmitBloodSplatter(Vector3 hitPoint, float damageAmount)
        {
            if (m_SplatterParticles == null)
                return;

            m_SplatterParticles.transform.position = hitPoint;
            var randomDirection = Random.insideUnitSphere;
            if (randomDirection.sqrMagnitude < 0.0001f)
                randomDirection = Vector3.up;
            m_SplatterParticles.transform.rotation = Quaternion.LookRotation(randomDirection.normalized, Vector3.up);

            var maxCount = Mathf.Max(m_MinSplatterParticlesPerHit, m_MaxSplatterParticlesPerHit);
            var count = Mathf.Clamp(
                Mathf.RoundToInt(damageAmount * 0.45f),
                Mathf.Max(1, m_MinSplatterParticlesPerHit),
                maxCount);

            m_SplatterParticles.Emit(count);
        }

        void EmitBleedDecals(Vector3 hitPoint, float damageAmount)
        {
            s_GlobalMaxBloodDecals = Mathf.Clamp(m_MaxBloodDecals, 10, 600);

            var count = Mathf.Clamp(
                Mathf.RoundToInt(damageAmount * 0.12f),
                Mathf.Max(1, m_MinDecalsPerHit),
                Mathf.Max(m_MinDecalsPerHit, m_MaxDecalsPerHit));

            for (var i = 0; i < count; i++)
            {
                var jitter = Random.insideUnitSphere * 0.08f;
                SpawnBloodDecal(hitPoint + jitter);
            }
        }

        void SpawnBloodDecal(Vector3 sourcePoint)
        {
            var rayOrigin = sourcePoint + Vector3.up * 0.25f;
            if (!Physics.Raycast(rayOrigin, Vector3.down, out var hit, 1.8f, ~0, QueryTriggerInteraction.Ignore))
                return;

            if (hit.collider != null && hit.collider.GetComponentInParent<CapsuleEnemy>() != null)
                return;

            if (hit.collider != null && hit.collider.attachedRigidbody != null)
                return;

            var decalObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            decalObject.name = "Blood Decal";

            var decalCollider = decalObject.GetComponent<Collider>();
            if (decalCollider != null)
                Destroy(decalCollider);

            decalObject.transform.position = hit.point + hit.normal * 0.006f;
            decalObject.transform.rotation = Quaternion.FromToRotation(Vector3.forward, hit.normal);
            decalObject.transform.Rotate(Vector3.forward, Random.Range(0f, 360f), Space.Self);

            var decalSize = Random.Range(m_MinDecalSize, m_MaxDecalSize);
            decalObject.transform.localScale = new Vector3(decalSize, decalSize, 1f);

            var renderer = decalObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = GetOrCreateBleedDecalMaterial(m_BleedColor);
            }

            s_BloodDecalQueue.Enqueue(decalObject);
            while (s_BloodDecalQueue.Count > s_GlobalMaxBloodDecals)
            {
                var oldest = s_BloodDecalQueue.Dequeue();
                if (oldest != null)
                    Destroy(oldest);
            }
        }

        static Material GetOrCreateBleedDecalMaterial(Color color)
        {
            if (s_BleedDecalMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");

                if (shader == null)
                    return null;

                s_BleedDecalMaterial = new Material(shader)
                {
                    name = "Runtime Blood Decal Material"
                };
            }

            SetMaterialColor(s_BleedDecalMaterial, color);
            return s_BleedDecalMaterial;
        }

        static Material GetOrCreateBleedSplatterMaterial(Color color)
        {
            if (s_BleedSplatterMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null)
                    shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    return null;

                s_BleedSplatterMaterial = new Material(shader)
                {
                    name = "Runtime Blood Splatter Material"
                };
            }

            SetMaterialColor(s_BleedSplatterMaterial, color);
            return s_BleedSplatterMaterial;
        }

        static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);

            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, color);
        }
    }
}
