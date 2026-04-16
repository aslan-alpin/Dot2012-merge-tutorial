using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using VRCombat.Enemies;

namespace VRCombat.Combat
{
    public interface ISwingHitModifier
    {
        void BeforeHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, ref float effectiveSwingSpeed, ref float damage, ref bool allowHit);
        void AfterHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, IDamageable damageable, float appliedDamage);
    }

    [DisallowMultipleComponent]
    public sealed class ThrownDaggerWeapon : MonoBehaviour, ISwingHitModifier
    {
        [SerializeField]
        float m_ArmSpeed = 2.8f;

        [SerializeField]
        float m_ThrownImpactDamage = 32f;

        XRGrabInteractable m_Interactable;
        Rigidbody m_Rigidbody;
        bool m_IsSubscribed;
        bool m_IsThrownArmed;
        bool m_IsStuckInTarget;

        public void Configure(float armSpeed, float thrownImpactDamage)
        {
            m_ArmSpeed = Mathf.Max(0.2f, armSpeed);
            m_ThrownImpactDamage = Mathf.Max(1f, thrownImpactDamage);
        }

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            m_IsThrownArmed = false;
            m_IsStuckInTarget = false;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!m_IsThrownArmed || collision == null)
                return;

            var enemy = collision.collider != null ? collision.collider.GetComponentInParent<CapsuleEnemy>() : null;
            if (enemy != null)
            {
                var contactPoint = collision.contactCount > 0
                    ? collision.GetContact(0).point
                    : collision.collider.ClosestPoint(transform.position);
                StickIntoTarget(collision.collider.transform, contactPoint);
                return;
            }

            var damageable = collision.collider != null ? collision.collider.GetComponentInParent<IDamageable>() : null;
            if (damageable == null)
                m_IsThrownArmed = false;
        }

        public void BeforeHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, ref float effectiveSwingSpeed, ref float damage, ref bool allowHit)
        {
            if (!m_IsThrownArmed)
                return;

            damage = Mathf.Max(damage, m_ThrownImpactDamage);
        }

        public void AfterHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, IDamageable damageable, float appliedDamage)
        {
            if (!m_IsThrownArmed)
                return;

            var enemyTransform = other != null
                ? other.GetComponentInParent<CapsuleEnemy>()?.transform
                : null;
            if (enemyTransform != null)
            {
                StickIntoTarget(enemyTransform, hitPoint);
                return;
            }

            m_IsThrownArmed = false;
        }

        void ResolveReferences()
        {
            if (m_Interactable == null)
                m_Interactable = GetComponent<XRGrabInteractable>();
            if (m_Rigidbody == null)
                m_Rigidbody = GetComponent<Rigidbody>();
        }

        void Subscribe()
        {
            if (m_IsSubscribed || m_Interactable == null)
                return;

            m_Interactable.selectEntered.AddListener(OnSelectEntered);
            m_Interactable.selectExited.AddListener(OnSelectExited);
            m_IsSubscribed = true;
        }

        void Unsubscribe()
        {
            if (!m_IsSubscribed || m_Interactable == null)
                return;

            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
            m_Interactable.selectExited.RemoveListener(OnSelectExited);
            m_IsSubscribed = false;
        }

        void OnSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
        {
            if (m_IsStuckInTarget)
            {
                transform.SetParent(null, true);
                if (m_Rigidbody != null)
                {
                    m_Rigidbody.isKinematic = false;
                    m_Rigidbody.useGravity = false;
                    m_Rigidbody.linearVelocity = Vector3.zero;
                    m_Rigidbody.angularVelocity = Vector3.zero;
                }
            }

            m_IsStuckInTarget = false;
            m_IsThrownArmed = false;
        }

        void OnSelectExited(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
        {
            if (m_Rigidbody == null)
                return;

            var releaseSpeed = Mathf.Max(
                m_Rigidbody.linearVelocity.magnitude,
                m_Rigidbody.angularVelocity.magnitude * 0.18f);
            m_IsThrownArmed = releaseSpeed >= m_ArmSpeed;
        }

        void StickIntoTarget(Transform target, Vector3 hitPoint)
        {
            ResolveReferences();
            if (target == null || m_Rigidbody == null || m_IsStuckInTarget)
            {
                m_IsThrownArmed = false;
                return;
            }

            var travelDirection = m_Rigidbody.linearVelocity.sqrMagnitude > 0.01f
                ? m_Rigidbody.linearVelocity.normalized
                : transform.forward;
            if (travelDirection.sqrMagnitude < 0.0001f)
                travelDirection = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;

            transform.SetParent(target, true);
            transform.position = hitPoint - travelDirection * 0.08f;
            transform.rotation = Quaternion.LookRotation(travelDirection, Vector3.up);

            m_Rigidbody.isKinematic = true;
            m_Rigidbody.useGravity = false;
            m_Rigidbody.linearVelocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_IsStuckInTarget = true;
            m_IsThrownArmed = false;
        }
    }

    [DisallowMultipleComponent]
    public sealed class HeavyImpactWeapon : MonoBehaviour, ISwingHitModifier
    {
        [SerializeField]
        float m_HeavySwingSpeed = 1.35f;

        [SerializeField]
        float m_StunDuration = 0.45f;

        [SerializeField]
        float m_ImpactImpulse = 1.75f;

        public void Configure(float heavySwingSpeed, float stunDuration, float impactImpulse)
        {
            m_HeavySwingSpeed = Mathf.Max(0.1f, heavySwingSpeed);
            m_StunDuration = Mathf.Max(0f, stunDuration);
            m_ImpactImpulse = Mathf.Max(0f, impactImpulse);
        }

        public void BeforeHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, ref float effectiveSwingSpeed, ref float damage, ref bool allowHit)
        {
        }

        public void AfterHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, IDamageable damageable, float appliedDamage)
        {
            if (dealer == null || damageable == null || dealer.CurrentSwingSpeed < m_HeavySwingSpeed)
                return;

            var enemy = (damageable as Component)?.GetComponentInParent<CapsuleEnemy>();
            if (enemy == null)
                return;

            if (m_StunDuration > 0f)
                enemy.ApplyStun(m_StunDuration);

            if (m_ImpactImpulse > 0f)
            {
                var direction = enemy.transform.position - dealer.transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                    direction = dealer.transform.forward;

                enemy.ApplyImpactImpulse(direction.normalized, m_ImpactImpulse);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class SweepSlashWeapon : MonoBehaviour, ISwingHitModifier
    {
        [SerializeField]
        float m_SweepRadius = 0.55f;

        [SerializeField]
        float m_SweepDamageMultiplier = 0.55f;

        [SerializeField]
        float m_TargetCooldownSeconds = 0.12f;

        readonly Collider[] m_OverlapBuffer = new Collider[12];
        readonly Dictionary<int, float> m_LastSweepHitTimeByTarget = new Dictionary<int, float>();

        public void Configure(float sweepRadius, float sweepDamageMultiplier)
        {
            m_SweepRadius = Mathf.Max(0.05f, sweepRadius);
            m_SweepDamageMultiplier = Mathf.Clamp(sweepDamageMultiplier, 0f, 1f);
        }

        public void BeforeHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, ref float effectiveSwingSpeed, ref float damage, ref bool allowHit)
        {
        }

        public void AfterHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, IDamageable damageable, float appliedDamage)
        {
            if (dealer == null || appliedDamage <= 0f || m_SweepRadius <= 0.01f || m_SweepDamageMultiplier <= 0.01f)
                return;

            var overlapCount = Physics.OverlapSphereNonAlloc(
                hitPoint,
                m_SweepRadius,
                m_OverlapBuffer,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < overlapCount; i++)
            {
                var collider = m_OverlapBuffer[i];
                if (collider == null || collider == other)
                    continue;

                var splashTarget = collider.GetComponentInParent<IDamageable>();
                var targetComponent = splashTarget as Component;
                if (targetComponent == null || splashTarget == damageable)
                    continue;

                var targetId = targetComponent.gameObject.GetInstanceID();
                if (m_LastSweepHitTimeByTarget.TryGetValue(targetId, out var lastHitTime) &&
                    Time.time - lastHitTime < m_TargetCooldownSeconds)
                {
                    continue;
                }

                m_LastSweepHitTimeByTarget[targetId] = Time.time;
                var splashPoint = collider.ClosestPoint(hitPoint);
                splashTarget.ApplyDamage(appliedDamage * m_SweepDamageMultiplier, splashPoint, dealer.gameObject);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class TipThrustWeapon : MonoBehaviour, ISwingHitModifier
    {
        [SerializeField]
        Transform m_TipTransform;

        [SerializeField]
        float m_TipReach = 0.18f;

        public void Configure(Transform tipTransform, float tipReach)
        {
            m_TipTransform = tipTransform;
            m_TipReach = Mathf.Max(0.04f, tipReach);
        }

        public void BeforeHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, ref float effectiveSwingSpeed, ref float damage, ref bool allowHit)
        {
            var tipTransform = m_TipTransform != null ? m_TipTransform : transform;
            if (tipTransform == null)
                return;

            if ((hitPoint - tipTransform.position).sqrMagnitude > m_TipReach * m_TipReach)
                allowHit = false;
        }

        public void AfterHit(SwingDamageDealer dealer, Collider other, Vector3 hitPoint, IDamageable damageable, float appliedDamage)
        {
        }
    }
}
