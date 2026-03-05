using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRCombat.Combat
{
    public interface IDamageGate
    {
        bool CanDealDamage();
    }

    [DisallowMultipleComponent]
    public class SwingDamageDealer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Damage before swing-speed scaling is applied.")]
        float m_BaseDamage = 10f;

        [SerializeField]
        [Tooltip("Minimum world-space speed required for a hit to count as a swing.")]
        float m_MinSwingSpeed = 0.12f;

        [SerializeField]
        [Tooltip("Speed at which damage scaling reaches its maximum.")]
        float m_MaxSwingSpeedForScaling = 4f;

        [SerializeField]
        [Tooltip("Prevents multiple damage events on the same target from one contact.")]
        float m_HitCooldownSeconds = 0.15f;

        [SerializeField]
        [Tooltip("Optional runtime gate used to suppress damage under certain conditions (e.g. hand not making a fist).")]
        MonoBehaviour m_DamageGateSource;

        [SerializeField]
        [Tooltip("Optional overlap-based fallback radius for hit detection when physics callbacks are missed.")]
        float m_ProximityFallbackRadius = 0f;

        readonly Dictionary<int, float> m_LastHitTimeByTarget = new Dictionary<int, float>();
        readonly Collider[] m_ProximityFallbackBuffer = new Collider[16];

        Vector3 m_LastPosition;
        float m_CurrentSpeed;
        IDamageGate m_DamageGate;

        public void SetDamageGate(MonoBehaviour damageGateSource)
        {
            m_DamageGateSource = damageGateSource;
            ResolveDamageGate();
        }

        public void Configure(
            float baseDamage,
            float minSwingSpeed,
            float maxSwingSpeedForScaling,
            float hitCooldownSeconds,
            float proximityFallbackRadius = -1f)
        {
            m_BaseDamage = Mathf.Max(0.1f, baseDamage);
            m_MinSwingSpeed = Mathf.Max(0.01f, minSwingSpeed);
            m_MaxSwingSpeedForScaling = Mathf.Max(m_MinSwingSpeed + 0.01f, maxSwingSpeedForScaling);
            m_HitCooldownSeconds = Mathf.Max(0.01f, hitCooldownSeconds);
            if (proximityFallbackRadius >= 0f)
                m_ProximityFallbackRadius = proximityFallbackRadius;
        }

        void Awake()
        {
            ResolveDamageGate();
        }

        void OnValidate()
        {
            ResolveDamageGate();
        }

        void OnEnable()
        {
            m_LastPosition = transform.position;
            m_LastHitTimeByTarget.Clear();
        }

        void Update()
        {
            var currentPosition = transform.position;
            var deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            m_CurrentSpeed = (currentPosition - m_LastPosition).magnitude / deltaTime;
            m_LastPosition = currentPosition;

            if (m_ProximityFallbackRadius > 0.001f)
                TryDealDamageWithProximityFallback();
        }

        void OnTriggerEnter(Collider other)
        {
            TryDealDamage(other, other.ClosestPoint(transform.position));
        }

        void OnCollisionEnter(Collision collision)
        {
            var hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.ClosestPoint(transform.position);

            TryDealDamage(collision.collider, hitPoint);
        }

        void OnTriggerStay(Collider other)
        {
            TryDealDamage(other, other.ClosestPoint(transform.position));
        }

        void OnCollisionStay(Collision collision)
        {
            var hitPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.ClosestPoint(transform.position);

            TryDealDamage(collision.collider, hitPoint);
        }

        void TryDealDamage(Collider other, Vector3 hitPoint)
        {
            if (!isActiveAndEnabled || other == null)
                return;

            var effectiveSwingSpeed = m_CurrentSpeed;
            if (effectiveSwingSpeed < m_MinSwingSpeed)
            {
                var rigidbody = GetComponent<Rigidbody>();
                if (rigidbody != null)
                    effectiveSwingSpeed = Mathf.Max(effectiveSwingSpeed, rigidbody.linearVelocity.magnitude);
            }

            if (effectiveSwingSpeed < m_MinSwingSpeed)
                return;

            if (m_DamageGate != null && !m_DamageGate.CanDealDamage())
                return;

            var damageable = other.GetComponentInParent<IDamageable>();
            var damageableComponent = damageable as Component;
            if (damageableComponent == null)
                return;

            var targetId = damageableComponent.gameObject.GetInstanceID();
            if (m_LastHitTimeByTarget.TryGetValue(targetId, out var lastHitTime) &&
                Time.time - lastHitTime < m_HitCooldownSeconds)
            {
                return;
            }

            m_LastHitTimeByTarget[targetId] = Time.time;

            var normalizedSwing = Mathf.InverseLerp(m_MinSwingSpeed, m_MaxSwingSpeedForScaling, effectiveSwingSpeed);
            var scaledDamage = m_BaseDamage * Mathf.Lerp(0.6f, 1.6f, normalizedSwing);
            damageable.ApplyDamage(scaledDamage, hitPoint, gameObject);
        }

        void ResolveDamageGate()
        {
            m_DamageGate = m_DamageGateSource as IDamageGate;
            if (m_DamageGateSource != null && m_DamageGate == null)
                Debug.LogWarning($"{nameof(SwingDamageDealer)} on {name} has a gate source that does not implement {nameof(IDamageGate)}.");
        }

        void TryDealDamageWithProximityFallback()
        {
            if (!isActiveAndEnabled)
                return;

            var effectiveSwingSpeed = m_CurrentSpeed;
            if (effectiveSwingSpeed < m_MinSwingSpeed)
            {
                var rigidbody = GetComponent<Rigidbody>();
                if (rigidbody != null)
                    effectiveSwingSpeed = Mathf.Max(effectiveSwingSpeed, rigidbody.linearVelocity.magnitude);
            }

            if (effectiveSwingSpeed < m_MinSwingSpeed)
                return;

            if (m_DamageGate != null && !m_DamageGate.CanDealDamage())
                return;

            var overlapCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                m_ProximityFallbackRadius,
                m_ProximityFallbackBuffer,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < overlapCount; i++)
            {
                var collider = m_ProximityFallbackBuffer[i];
                if (collider == null)
                    continue;

                TryDealDamage(collider, collider.ClosestPoint(transform.position));
            }
        }
    }

    [DisallowMultipleComponent]
    public class MaxGrabDistanceSelectFilter : MonoBehaviour, IXRSelectFilter
    {
        [SerializeField]
        float m_MaxDistance = 0.72f;

        [SerializeField]
        bool m_IgnorePokeInteractors = true;

        public bool canProcess => isActiveAndEnabled;

        public void Configure(float maxDistance)
        {
            m_MaxDistance = Mathf.Max(0.15f, maxDistance);
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor == null || interactable == null)
                return false;

            var interactorComponent = interactor as Component;
            var interactableComponent = interactable as Component;
            if (interactorComponent == null || interactableComponent == null)
                return true;

            if (m_IgnorePokeInteractors &&
                interactorComponent.name.IndexOf("poke", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (TryIsNearFieldSelection(interactorComponent, out var isNearFieldSelection) && isNearFieldSelection)
                return true;

            var sourcePosition = interactorComponent.transform.position;
            var bestDistanceSqr = (sourcePosition - interactableComponent.transform.position).sqrMagnitude;
            if (TryGetFarCasterOrigin(interactorComponent, out var farCasterOrigin))
            {
                sourcePosition = farCasterOrigin;
                bestDistanceSqr = (sourcePosition - interactableComponent.transform.position).sqrMagnitude;
            }

            if (interactor is IXRInteractor xrInteractor)
            {
                var attachTransform = xrInteractor.GetAttachTransform(interactable);
                if (attachTransform != null)
                {
                    var attachDistanceSqr = (attachTransform.position - interactableComponent.transform.position).sqrMagnitude;
                    if (attachDistanceSqr < bestDistanceSqr)
                    {
                        bestDistanceSqr = attachDistanceSqr;
                        sourcePosition = attachTransform.position;
                    }
                }
            }

            var targetPosition = interactableComponent.transform.position;
            var maxDistanceSqr = m_MaxDistance * m_MaxDistance;
            return bestDistanceSqr <= maxDistanceSqr || (sourcePosition - targetPosition).sqrMagnitude <= maxDistanceSqr;
        }

        static bool TryIsNearFieldSelection(Component interactorComponent, out bool isNearFieldSelection)
        {
            isNearFieldSelection = false;
            if (interactorComponent == null)
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var interactorType = interactorComponent.GetType();
            var selectionRegionProperty = interactorType.GetProperty("selectionRegion", flags);
            if (selectionRegionProperty == null)
                return false;

            var selectionRegionObject = selectionRegionProperty.GetValue(interactorComponent);
            if (selectionRegionObject == null)
                return false;

            var regionType = selectionRegionObject.GetType();
            var valueProperty = regionType.GetProperty("Value", flags) ?? regionType.GetProperty("value", flags);
            if (valueProperty == null)
                return false;

            var regionValue = valueProperty.GetValue(selectionRegionObject);
            if (regionValue == null)
                return false;

            var valueText = regionValue.ToString();
            if (string.IsNullOrEmpty(valueText))
                return false;

            if (valueText.IndexOf("Near", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isNearFieldSelection = true;
                return true;
            }

            if (valueText.IndexOf("Far", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isNearFieldSelection = false;
                return true;
            }

            return false;
        }

        static bool TryGetFarCasterOrigin(Component interactorComponent, out Vector3 origin)
        {
            origin = Vector3.zero;
            if (interactorComponent == null)
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var interactorType = interactorComponent.GetType();
            var farCasterProperty = interactorType.GetProperty("farInteractionCaster", flags);
            var farCaster = farCasterProperty?.GetValue(interactorComponent);
            if (farCaster == null)
                return false;

            var farCasterType = farCaster.GetType();
            var originProperty = farCasterType.GetProperty("effectiveCastOrigin", flags) ??
                                 farCasterType.GetProperty("castOrigin", flags);
            var originTransform = originProperty?.GetValue(farCaster) as Transform;
            if (originTransform == null)
                return false;

            origin = originTransform.position;
            return true;
        }
    }
}
