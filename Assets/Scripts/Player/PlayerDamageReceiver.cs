using System;
using UnityEngine;
using VRCombat.Enemies;

namespace VRCombat.Player
{
    [DisallowMultipleComponent]
    public class PlayerDamageReceiver : MonoBehaviour
    {
        [SerializeField]
        float m_MaxHealth = 100f;

        [SerializeField]
        [Tooltip("Short grace period to prevent multiple hits in one overlap event.")]
        float m_DamageCooldownSeconds = 0.25f;

        [SerializeField]
        DamageVignetteFeedback m_DamageVignetteFeedback;

        [SerializeField]
        [Tooltip("Total damage taken value where knockback reaches its configured maximum strength.")]
        float m_DamageTakenForMaxKnockback = 220f;

        [SerializeField]
        [Tooltip("Duration used to convert knockback distance into smooth knockback velocity.")]
        float m_KnockbackDurationSeconds = 0.18f;

        [SerializeField]
        [Tooltip("How quickly knockback velocity decays after each hit.")]
        float m_KnockbackDamping = 9f;

        [SerializeField]
        [Tooltip("Multiplier applied to incoming knockback distance.")]
        float m_KnockbackDistanceMultiplier = 1.55f;

        [SerializeField]
        [Tooltip("How much speed is kept after bouncing off an obstacle.")]
        [Range(0.1f, 0.98f)]
        float m_KnockbackBounceRetainedSpeed = 0.72f;

        [SerializeField]
        [Tooltip("Small skin used when resolving knockback collision sweeps.")]
        float m_KnockbackCollisionSkin = 0.02f;

        Transform m_PlayerRootTransform;
        CapsuleCollider m_HurtboxCollider;
        float m_CurrentHealth;
        float m_TotalDamageTaken;
        float m_LastDamageTime = -100f;
        bool m_IsDead;
        bool m_KnockbackEnabled = true;
        Vector3 m_KnockbackVelocity;
        readonly RaycastHit[] m_KnockbackHitBuffer = new RaycastHit[10];

        public float CurrentHealth => m_CurrentHealth;
        public float MaxHealth => m_MaxHealth;
        public float TotalDamageTaken => m_TotalDamageTaken;
        public bool IsDead => m_IsDead;

        public event Action<float, float> HealthChanged;
        public event Action<float> DamageTaken;
        public event Action Died;

        public void SetDamageFeedback(DamageVignetteFeedback damageVignetteFeedback)
        {
            m_DamageVignetteFeedback = damageVignetteFeedback;
        }

        public void SetPlayerRootTransform(Transform playerRootTransform)
        {
            m_PlayerRootTransform = playerRootTransform;
        }

        public void SetKnockbackEnabled(bool enabled)
        {
            m_KnockbackEnabled = enabled;
            if (!enabled)
                m_KnockbackVelocity = Vector3.zero;
        }

        public void ResetState()
        {
            m_CurrentHealth = m_MaxHealth;
            m_TotalDamageTaken = 0f;
            m_KnockbackVelocity = Vector3.zero;
            m_IsDead = false;
            m_LastDamageTime = -100f;
            HealthChanged?.Invoke(m_CurrentHealth, m_MaxHealth);
        }

        public void RestoreFullHealth()
        {
            if (m_IsDead)
                return;

            m_CurrentHealth = m_MaxHealth;
            m_LastDamageTime = -100f;
            HealthChanged?.Invoke(m_CurrentHealth, m_MaxHealth);
        }

        void Awake()
        {
            m_CurrentHealth = m_MaxHealth;
            m_TotalDamageTaken = 0f;
            m_IsDead = false;
            m_HurtboxCollider = GetComponent<CapsuleCollider>();

            if (m_DamageVignetteFeedback == null)
                m_DamageVignetteFeedback = GetComponent<DamageVignetteFeedback>();

            HealthChanged?.Invoke(m_CurrentHealth, m_MaxHealth);
        }

        void Update()
        {
            if (m_IsDead || m_PlayerRootTransform == null || !m_KnockbackEnabled)
                return;

            if (m_KnockbackVelocity.sqrMagnitude < 0.0001f)
                return;

            ApplyKnockbackMovement(Time.deltaTime);
            var damping = Mathf.Max(0.01f, m_KnockbackDamping);
            m_KnockbackVelocity = Vector3.Lerp(m_KnockbackVelocity, Vector3.zero, damping * Time.deltaTime);
        }

        public void ReceiveDamage(float amount)
        {
            if (m_IsDead || amount <= 0f || Time.time - m_LastDamageTime < m_DamageCooldownSeconds)
                return;

            m_LastDamageTime = Time.time;
            var previousHealth = m_CurrentHealth;
            m_CurrentHealth = Mathf.Max(0f, m_CurrentHealth - amount);
            var appliedDamage = Mathf.Max(0f, previousHealth - m_CurrentHealth);
            m_TotalDamageTaken += appliedDamage;
            m_DamageVignetteFeedback?.PlayFlash();
            HealthChanged?.Invoke(m_CurrentHealth, m_MaxHealth);
            if (appliedDamage > 0f)
                DamageTaken?.Invoke(appliedDamage);

            if (m_CurrentHealth <= 0f)
            {
                m_IsDead = true;
                Died?.Invoke();
            }
        }

        public void ForceKill()
        {
            if (m_IsDead)
                return;

            m_CurrentHealth = 0f;
            m_IsDead = true;
            m_LastDamageTime = -100f;
            m_KnockbackVelocity = Vector3.zero;
            m_DamageVignetteFeedback?.PlayFlash();
            HealthChanged?.Invoke(m_CurrentHealth, m_MaxHealth);
            Died?.Invoke();
        }

        public void ApplyKnockback(Vector3 worldDirection, float distanceAtFullHealth, float distanceAtZeroHealth)
        {
            if (m_IsDead || m_PlayerRootTransform == null || !m_KnockbackEnabled)
                return;

            var horizontalDirection = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
            if (horizontalDirection.sqrMagnitude < 0.0001f)
                horizontalDirection = m_PlayerRootTransform.forward;

            if (horizontalDirection.sqrMagnitude < 0.0001f)
                horizontalDirection = Vector3.forward;

            horizontalDirection.Normalize();

            var minDistance = Mathf.Min(distanceAtFullHealth, distanceAtZeroHealth);
            var maxDistance = Mathf.Max(distanceAtFullHealth, distanceAtZeroHealth);
            var damageNormalized = Mathf.Clamp01(m_TotalDamageTaken / Mathf.Max(1f, m_DamageTakenForMaxKnockback));
            var knockbackDistance = Mathf.Lerp(minDistance, maxDistance, damageNormalized);
            knockbackDistance *= Mathf.Max(0.05f, m_KnockbackDistanceMultiplier);
            var duration = Mathf.Max(0.05f, m_KnockbackDurationSeconds);
            var impulseVelocity = horizontalDirection * (knockbackDistance / duration);
            m_KnockbackVelocity += impulseVelocity;
        }

        void ApplyKnockbackMovement(float deltaTime)
        {
            var remainingDisplacement = m_KnockbackVelocity * Mathf.Max(0f, deltaTime);
            if (remainingDisplacement.sqrMagnitude < 0.0000001f)
                return;

            var bounceRetention = Mathf.Clamp(m_KnockbackBounceRetainedSpeed, 0.1f, 0.98f);
            for (var bounceIndex = 0; bounceIndex < 3; bounceIndex++)
            {
                if (remainingDisplacement.sqrMagnitude < 0.0000001f)
                    break;

                var direction = remainingDisplacement.normalized;
                var distance = remainingDisplacement.magnitude;
                if (!TryGetKnockbackObstacleHit(direction, distance, out var hit))
                {
                    m_PlayerRootTransform.position += remainingDisplacement;
                    return;
                }

                var travelDistance = Mathf.Max(0f, hit.distance - m_KnockbackCollisionSkin);
                if (travelDistance > 0f)
                    m_PlayerRootTransform.position += direction * travelDistance;

                var remainingDistance = Mathf.Max(0f, distance - travelDistance);
                var reflectedDirection = Vector3.ProjectOnPlane(Vector3.Reflect(direction, hit.normal), Vector3.up);
                if (reflectedDirection.sqrMagnitude < 0.0001f)
                {
                    m_KnockbackVelocity = Vector3.zero;
                    return;
                }

                reflectedDirection.Normalize();
                m_KnockbackVelocity = reflectedDirection * (m_KnockbackVelocity.magnitude * bounceRetention);
                remainingDisplacement = reflectedDirection * (remainingDistance * bounceRetention);
            }
        }

        bool TryGetKnockbackObstacleHit(Vector3 direction, float distance, out RaycastHit bestHit)
        {
            bestHit = default;
            if (direction.sqrMagnitude < 0.0001f || distance <= 0.0001f)
                return false;

            var castRadius = m_HurtboxCollider != null
                ? Mathf.Max(0.08f, m_HurtboxCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z))
                : 0.2f;
            var castDistance = distance + Mathf.Max(0.001f, m_KnockbackCollisionSkin);
            var hitCount = Physics.SphereCastNonAlloc(
                transform.position,
                castRadius,
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

        bool ShouldIgnoreKnockbackCollider(Collider collider)
        {
            if (collider == null)
                return true;

            if (collider.transform == transform || collider.transform.IsChildOf(transform))
                return true;

            if (m_PlayerRootTransform != null &&
                (collider.transform == m_PlayerRootTransform || collider.transform.IsChildOf(m_PlayerRootTransform)))
            {
                return true;
            }

            if (collider.GetComponentInParent<PlayerDamageReceiver>() != null)
                return true;

            if (collider.GetComponentInParent<CapsuleEnemy>() != null)
                return true;

            if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                return true;

            return false;
        }

        void OnTriggerEnter(Collider other)
        {
            TryConsumeEnemyHit(other);
        }

        void OnCollisionEnter(Collision collision)
        {
            TryConsumeEnemyHit(collision.collider);
        }

        void TryConsumeEnemyHit(Collider other)
        {
            if (other == null)
                return;

            var enemy = other.GetComponentInParent<CapsuleEnemy>();
            enemy?.HandlePlayerContact(this);
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerHurtboxFollower : MonoBehaviour
    {
        [SerializeField]
        float m_CapsuleRadius = 0.24f;

        [SerializeField]
        float m_CapsuleHeight = 1.35f;

        [SerializeField]
        float m_HeadToCenterOffset = 0.68f;

        Transform m_HeadTransform;
        CapsuleCollider m_CapsuleCollider;
        bool m_HasLoggedMissingHeadTransform;

        public void Configure(Transform headTransform, float capsuleRadius)
        {
            m_HeadTransform = headTransform;
            m_HasLoggedMissingHeadTransform = false;
            m_CapsuleRadius = Mathf.Max(0.12f, capsuleRadius);
            m_CapsuleHeight = Mathf.Max(m_CapsuleHeight, m_CapsuleRadius * 2f + 0.1f);
            EnsureCollider();
            UpdateCapsuleTransform();
        }

        void Awake()
        {
            EnsureCollider();
        }

        void LateUpdate()
        {
            if (m_HeadTransform == null)
            {
                if (!m_HasLoggedMissingHeadTransform)
                {
                    m_HasLoggedMissingHeadTransform = true;
                    Debug.LogWarning("[VRCombat] PlayerHurtboxFollower has no head transform assigned; hurtbox updates are suspended.");
                }

                return;
            }

            UpdateCapsuleTransform();
        }

        void EnsureCollider()
        {
            if (m_CapsuleCollider == null)
                m_CapsuleCollider = GetComponent<CapsuleCollider>();

            m_CapsuleCollider.isTrigger = true;
            m_CapsuleCollider.direction = 1;
            m_CapsuleCollider.center = Vector3.zero;
            m_CapsuleCollider.radius = m_CapsuleRadius;
            m_CapsuleCollider.height = m_CapsuleHeight;
        }

        void UpdateCapsuleTransform()
        {
            var centerPosition = m_HeadTransform.position - Vector3.up * m_HeadToCenterOffset;
            transform.position = centerPosition;
            transform.rotation = Quaternion.identity;

            m_CapsuleCollider.radius = m_CapsuleRadius;
            m_CapsuleCollider.height = Mathf.Max(m_CapsuleHeight, m_CapsuleRadius * 2f + 0.1f);
            m_CapsuleCollider.center = Vector3.zero;
        }
    }
}
