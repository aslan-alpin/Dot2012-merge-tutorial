using System.Collections.Generic;
using UnityEngine;

namespace VRCombat.Combat
{
    [DisallowMultipleComponent]
    public sealed class RiggedChainWeapon : MonoBehaviour
    {
        const string ArmatureNameToken = "armature";
        const string BoneNameToken = "bone";
        const string BoneEndNameToken = "_end";
        const int MaxSimulatedBones = 14;
        const int ActiveTailCollisionSegments = 5;
        const int ActiveTailDamageSegments = 10;
        const float HoldSettleDurationSeconds = 0.04f;

        readonly List<Rigidbody> m_SegmentBodies = new List<Rigidbody>();
        readonly List<Transform> m_SimulatedBones = new List<Transform>();
        readonly List<Vector3> m_RestLocalPositions = new List<Vector3>();
        readonly List<Quaternion> m_RestLocalRotations = new List<Quaternion>();
        readonly List<Collider> m_SegmentColliders = new List<Collider>();
        readonly List<ConfigurableJoint> m_SegmentJoints = new List<ConfigurableJoint>();
        static PhysicsMaterial s_TailCollisionMaterial;

        Rigidbody m_HandleBody;
        Collider m_HoldCollider;
        readonly List<SwingDamageDealer> m_DamageDealers = new List<SwingDamageDealer>();
        Transform m_SimulationRoot;
        bool m_IsInitialized;
        bool m_IsMounted;
        bool m_IsHeld;
        float m_SettleUntilTime;

        public bool Initialize(
            Rigidbody handleBody,
            Collider holdCollider,
            int boneStep,
            float segmentRadius,
            float tipRadius,
            float segmentMass,
            float baseDamage,
            float minSwingSpeed,
            float maxSwingSpeedForScaling,
            float hitCooldownSeconds,
            float rootSpring,
            float segmentSpring,
            float damper)
        {
            if (handleBody == null)
                return false;

            m_HandleBody = handleBody;
            m_HoldCollider = holdCollider;
            DisableImportedAnimation();
            m_SimulationRoot = FindSimulationRoot();

            var chainBones = CollectChainBones(m_SimulationRoot != null ? m_SimulationRoot : transform);
            if (chainBones.Count < 3)
            {
                Debug.LogWarning($"{nameof(RiggedChainWeapon)} on {name} could not find a usable chain rig.");
                return false;
            }

            m_SegmentBodies.Clear();
            m_SimulatedBones.Clear();
            m_RestLocalPositions.Clear();
            m_RestLocalRotations.Clear();
            m_SegmentColliders.Clear();
            m_SegmentJoints.Clear();
            m_DamageDealers.Clear();

            var sampledBones = SampleChainBones(chainBones, boneStep);
            var simulatedBones = LimitSampledBones(sampledBones, MaxSimulatedBones);
            if (simulatedBones.Count < 3)
            {
                Debug.LogWarning($"{nameof(RiggedChainWeapon)} on {name} does not have enough sampled bones to simulate.");
                return false;
            }

            var previousBody = handleBody;
            for (var i = 0; i < simulatedBones.Count; i++)
            {
                var bone = simulatedBones[i];
                var isTip = i == simulatedBones.Count - 1;
                var colliderRadius = isTip
                    ? Mathf.Max(segmentRadius, tipRadius)
                    : Mathf.Max(0.005f, EstimateSegmentRadius(bone, simulatedBones, i, segmentRadius));

                var body = EnsureSegmentBody(bone, colliderRadius, segmentMass, isTip);
                var joint = ConfigureJoint(
                    bone,
                    body,
                    previousBody,
                    i == 0 ? 95f : 165f);

                m_SegmentBodies.Add(body);
                m_SimulatedBones.Add(bone);
                m_RestLocalPositions.Add(bone.localPosition);
                m_RestLocalRotations.Add(bone.localRotation);
                m_SegmentColliders.Add(body.GetComponent<Collider>());
                if (joint != null)
                    m_SegmentJoints.Add(joint);
                previousBody = body;
            }

            IgnoreInternalCollisions();
            ConfigureTailDamage(
                simulatedBones,
                baseDamage,
                minSwingSpeed,
                maxSwingSpeedForScaling,
                hitCooldownSeconds,
                tipRadius * 1.8f);

            m_IsInitialized = true;
            m_IsMounted = false;
            m_IsHeld = false;
            SetMountedState(true);
            return true;
        }

        public void SetMountedState(bool mounted)
        {
            if (!m_IsInitialized || m_IsMounted == mounted)
                return;

            m_IsMounted = mounted;
            if (mounted)
            {
                RestoreRestPose();
                SyncBodiesToBonePose(resetVelocity: true);
                SetSegmentBodiesDynamicState(false, held: false, resetVelocity: true);
                SetTailCollidersEnabled(false);
                SetDamageDealersEnabled(false);

                Physics.SyncTransforms();
                return;
            }

            RestoreRestPose();
            SyncBodiesToBonePose(resetVelocity: true);
            ApplyLiveState(resetVelocity: true);
            m_SettleUntilTime = Time.time + HoldSettleDurationSeconds;
            Physics.SyncTransforms();
        }

        public void SetHeldState(bool held)
        {
            if (!m_IsInitialized || m_IsHeld == held)
                return;

            m_IsHeld = held;
            if (!m_IsMounted)
            {
                ApplyLiveState(resetVelocity: false);
                m_SettleUntilTime = Time.time + HoldSettleDurationSeconds;
            }
        }

        void FixedUpdate()
        {
            if (!m_IsInitialized || m_IsMounted)
                return;

            if (Time.time < m_SettleUntilTime)
                ApplySettleDamping();

            var maxLinearVelocity = m_IsHeld ? 120f : 160f;
            var maxAngularVelocity = m_IsHeld ? 180f : 260f;
            for (var i = 0; i < m_SegmentBodies.Count; i++)
            {
                var body = m_SegmentBodies[i];
                if (body == null || body.isKinematic)
                    continue;

                body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, maxLinearVelocity);
                body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, maxAngularVelocity);
            }
        }

        void ApplyLiveState(bool resetVelocity)
        {
            SetSegmentBodiesDynamicState(true, m_IsHeld, resetVelocity);
            ConfigureJointsForState(m_IsHeld);
            SetTailCollidersEnabled(true);
            SetDamageDealersEnabled(true);
        }

        Transform FindSimulationRoot()
        {
            var allTransforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate != null &&
                    candidate.name.IndexOf(ArmatureNameToken, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        List<Transform> CollectChainBones(Transform searchRoot)
        {
            if (searchRoot == null)
                return new List<Transform>();

            var allTransforms = searchRoot.GetComponentsInChildren<Transform>(true);
            Transform armature = searchRoot.name.IndexOf(ArmatureNameToken, System.StringComparison.OrdinalIgnoreCase) >= 0
                ? searchRoot
                : null;
            if (armature == null)
            {
                for (var i = 0; i < allTransforms.Length; i++)
                {
                    var candidate = allTransforms[i];
                    if (candidate != null &&
                        candidate.name.IndexOf(ArmatureNameToken, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        armature = candidate;
                        break;
                    }
                }
            }

            Transform rootBone = null;
            var shallowestDepth = int.MaxValue;
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (!IsChainBone(candidate))
                    continue;

                if (armature != null && !candidate.IsChildOf(armature))
                    continue;

                if (IsChainBone(candidate.parent))
                    continue;

                var depth = GetDepth(candidate);
                if (depth >= shallowestDepth)
                    continue;

                shallowestDepth = depth;
                rootBone = candidate;
            }

            var chainBones = new List<Transform>();
            var current = rootBone;
            while (current != null)
            {
                chainBones.Add(current);
                current = GetPrimaryBoneChild(current);
            }

            return chainBones;
        }

        static List<Transform> SampleChainBones(IReadOnlyList<Transform> chainBones, int boneStep)
        {
            var sampledBones = new List<Transform>();
            if (chainBones == null || chainBones.Count == 0)
                return sampledBones;

            boneStep = Mathf.Max(1, boneStep);
            for (var i = 0; i < chainBones.Count; i += boneStep)
                sampledBones.Add(chainBones[i]);

            var lastBone = chainBones[chainBones.Count - 1];
            if (!ReferenceEquals(sampledBones[sampledBones.Count - 1], lastBone))
                sampledBones.Add(lastBone);

            return sampledBones;
        }

        static List<Transform> LimitSampledBones(IReadOnlyList<Transform> sampledBones, int maxBones)
        {
            var limitedBones = new List<Transform>();
            if (sampledBones == null || sampledBones.Count == 0)
                return limitedBones;

            maxBones = Mathf.Max(3, maxBones);
            if (sampledBones.Count <= maxBones)
            {
                limitedBones.AddRange(sampledBones);
                return limitedBones;
            }

            var step = (sampledBones.Count - 1f) / (maxBones - 1f);
            for (var i = 0; i < maxBones; i++)
            {
                var index = Mathf.Clamp(Mathf.RoundToInt(step * i), 0, sampledBones.Count - 1);
                var bone = sampledBones[index];
                if (limitedBones.Count == 0 || !ReferenceEquals(limitedBones[limitedBones.Count - 1], bone))
                    limitedBones.Add(bone);
            }

            if (!ReferenceEquals(limitedBones[limitedBones.Count - 1], sampledBones[sampledBones.Count - 1]))
                limitedBones.Add(sampledBones[sampledBones.Count - 1]);

            return limitedBones;
        }

        Rigidbody EnsureSegmentBody(Transform bone, float colliderRadius, float segmentMass, bool isTip)
        {
            var body = bone.GetComponent<Rigidbody>();
            if (body == null)
                body = bone.gameObject.AddComponent<Rigidbody>();

            body.mass = Mathf.Max(0.01f, isTip ? segmentMass * 0.9f : segmentMass);
            body.useGravity = true;
            body.linearDamping = isTip ? 0.035f : 0.05f;
            body.angularDamping = isTip ? 0.04f : 0.06f;
            body.solverIterations = 24;
            body.solverVelocityIterations = 10;
            body.maxAngularVelocity = 260f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.None;

            var sphereCollider = bone.GetComponent<SphereCollider>();
            if (sphereCollider == null)
                sphereCollider = bone.gameObject.AddComponent<SphereCollider>();

            sphereCollider.radius = colliderRadius;
            sphereCollider.isTrigger = false;
            sphereCollider.contactOffset = 0.0045f;
            return body;
        }

        static ConfigurableJoint ConfigureJoint(
            Transform bone,
            Rigidbody body,
            Rigidbody connectedBody,
            float angularLimitDegrees)
        {
            if (bone == null || body == null || connectedBody == null)
                return null;

            var joint = bone.GetComponent<ConfigurableJoint>();
            if (joint == null)
                joint = bone.gameObject.AddComponent<ConfigurableJoint>();

            joint.connectedBody = connectedBody;
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = Vector3.zero;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.projectionMode = JointProjectionMode.None;
            joint.enablePreprocessing = true;
            joint.breakForce = float.PositiveInfinity;
            joint.breakTorque = float.PositiveInfinity;
            joint.massScale = 1f;
            joint.connectedMassScale = 1f;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 0f,
                positionDamper = 0f,
                maximumForce = 0f
            };
            joint.lowAngularXLimit = new SoftJointLimit { limit = -angularLimitDegrees };
            joint.highAngularXLimit = new SoftJointLimit { limit = angularLimitDegrees };
            joint.angularYLimit = new SoftJointLimit { limit = angularLimitDegrees };
            joint.angularZLimit = new SoftJointLimit { limit = angularLimitDegrees };
            return joint;
        }

        void IgnoreInternalCollisions()
        {
            for (var i = 0; i < m_SegmentColliders.Count; i++)
            {
                var current = m_SegmentColliders[i];
                if (current == null)
                    continue;

                if (m_HoldCollider != null)
                    Physics.IgnoreCollision(m_HoldCollider, current, true);

                for (var j = i + 1; j < m_SegmentColliders.Count; j++)
                {
                    var other = m_SegmentColliders[j];
                    if (other != null)
                        Physics.IgnoreCollision(current, other, true);
                }
            }
        }

        void ConfigureTailDamage(
            IReadOnlyList<Transform> simulatedBones,
            float baseDamage,
            float minSwingSpeed,
            float maxSwingSpeedForScaling,
            float hitCooldownSeconds,
            float proximityFallbackRadius)
        {
            if (simulatedBones == null || simulatedBones.Count == 0)
                return;

            var firstDamageIndex = Mathf.Max(0, simulatedBones.Count - ActiveTailDamageSegments);
            for (var i = firstDamageIndex; i < simulatedBones.Count; i++)
            {
                var damageBone = simulatedBones[i];
                if (damageBone == null)
                    continue;

                var damageDealer = damageBone.GetComponent<SwingDamageDealer>();
                if (damageDealer == null)
                    damageDealer = damageBone.gameObject.AddComponent<SwingDamageDealer>();

                damageDealer.SetDamageGate(null);
                damageDealer.Configure(
                    baseDamage,
                    minSwingSpeed,
                    maxSwingSpeedForScaling,
                    hitCooldownSeconds,
                    Mathf.Max(0.05f, proximityFallbackRadius));
                m_DamageDealers.Add(damageDealer);
            }
        }

        static PhysicsMaterial GetTailCollisionMaterial()
        {
            if (s_TailCollisionMaterial != null)
                return s_TailCollisionMaterial;

            s_TailCollisionMaterial = new PhysicsMaterial("Runtime Chain Tail Material")
            {
                dynamicFriction = 0.08f,
                staticFriction = 0.04f,
                bounciness = 0.08f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Average
            };
            return s_TailCollisionMaterial;
        }

        void DisableImportedAnimation()
        {
            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null)
                animator.enabled = false;

            var animation = GetComponentInChildren<Animation>(true);
            if (animation != null)
                animation.enabled = false;
        }

        static float EstimateSegmentRadius(Transform bone, IReadOnlyList<Transform> sampledBones, int index, float fallbackRadius)
        {
            var nextBone = index < sampledBones.Count - 1 ? sampledBones[index + 1] : null;
            if (bone == null || nextBone == null)
                return Mathf.Max(0.005f, fallbackRadius);

            var distance = Vector3.Distance(bone.position, nextBone.position);
            if (distance <= 0.0001f)
                return Mathf.Max(0.005f, fallbackRadius);

            return Mathf.Max(fallbackRadius, distance * 0.24f);
        }

        static bool IsChainBone(Transform transformCandidate)
        {
            if (transformCandidate == null)
                return false;

            var lowerName = transformCandidate.name.ToLowerInvariant();
            return lowerName.Contains(BoneNameToken) && !lowerName.Contains(BoneEndNameToken);
        }

        static Transform GetPrimaryBoneChild(Transform parentBone)
        {
            if (parentBone == null)
                return null;

            Transform bestChild = null;
            for (var i = 0; i < parentBone.childCount; i++)
            {
                var child = parentBone.GetChild(i);
                if (!IsChainBone(child))
                    continue;

                if (bestChild == null || string.CompareOrdinal(child.name, bestChild.name) < 0)
                    bestChild = child;
            }

            return bestChild;
        }

        static int GetDepth(Transform transformCandidate)
        {
            var depth = 0;
            while (transformCandidate != null)
            {
                depth++;
                transformCandidate = transformCandidate.parent;
            }

            return depth;
        }

        void RestoreRestPose()
        {
            for (var i = 0; i < m_SimulatedBones.Count; i++)
            {
                var bone = m_SimulatedBones[i];
                if (bone == null)
                    continue;

                bone.localPosition = m_RestLocalPositions[i];
                bone.localRotation = m_RestLocalRotations[i];
            }
        }

        void SyncBodiesToBonePose(bool resetVelocity)
        {
            for (var i = 0; i < m_SegmentBodies.Count; i++)
            {
                var body = m_SegmentBodies[i];
                var bone = i < m_SimulatedBones.Count ? m_SimulatedBones[i] : null;
                if (body == null || bone == null)
                    continue;

                body.position = bone.position;
                body.rotation = bone.rotation;
                if (!resetVelocity)
                    continue;

                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        void SetSegmentBodiesDynamicState(bool enabled, bool held, bool resetVelocity)
        {
            for (var i = 0; i < m_SegmentBodies.Count; i++)
            {
                var body = m_SegmentBodies[i];
                if (body == null)
                    continue;

                if (resetVelocity)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = !enabled;
                body.useGravity = enabled;
                body.linearDamping = enabled
                    ? held ? (i >= m_SegmentBodies.Count - 2 ? 0.035f : 0.05f) : (i >= m_SegmentBodies.Count - 1 ? 0.04f : 0.065f)
                    : 1.6f;
                body.angularDamping = enabled
                    ? held ? (i >= m_SegmentBodies.Count - 2 ? 0.045f : 0.065f) : (i >= m_SegmentBodies.Count - 1 ? 0.05f : 0.08f)
                    : 1.75f;
                body.maxAngularVelocity = held ? 180f : 260f;
                body.collisionDetectionMode = enabled
                    ? CollisionDetectionMode.ContinuousDynamic
                    : CollisionDetectionMode.ContinuousSpeculative;
                body.interpolation = RigidbodyInterpolation.None;

                if (enabled)
                    body.WakeUp();
            }
        }

        void SetTailCollidersEnabled(bool enabled)
        {
            var firstActiveIndex = Mathf.Max(0, m_SegmentColliders.Count - ActiveTailCollisionSegments);
            for (var i = 0; i < m_SegmentColliders.Count; i++)
            {
                var collider = m_SegmentColliders[i];
                if (collider == null)
                    continue;

                var shouldEnable = enabled && i >= firstActiveIndex;
                collider.enabled = shouldEnable;
                collider.sharedMaterial = shouldEnable ? GetTailCollisionMaterial() : null;
            }
        }

        void SetDamageDealersEnabled(bool enabled)
        {
            for (var i = 0; i < m_DamageDealers.Count; i++)
            {
                var damageDealer = m_DamageDealers[i];
                if (damageDealer != null)
                    damageDealer.enabled = enabled;
            }
        }

        void ConfigureJointsForState(bool held)
        {
            for (var i = 0; i < m_SegmentJoints.Count; i++)
            {
                var joint = m_SegmentJoints[i];
                if (joint == null)
                    continue;

                var angularLimit = held
                    ? (i == 0 ? 100f : 160f)
                    : (i == 0 ? 120f : 170f);
                joint.enablePreprocessing = true;
                joint.projectionMode = JointProjectionMode.None;
                joint.lowAngularXLimit = new SoftJointLimit { limit = -angularLimit };
                joint.highAngularXLimit = new SoftJointLimit { limit = angularLimit };
                joint.angularYLimit = new SoftJointLimit { limit = angularLimit };
                joint.angularZLimit = new SoftJointLimit { limit = angularLimit };
            }
        }

        void ApplySettleDamping()
        {
            for (var i = 0; i < m_SegmentBodies.Count; i++)
            {
                var body = m_SegmentBodies[i];
                if (body == null || body.isKinematic)
                    continue;

                body.linearVelocity *= 0.88f;
                body.angularVelocity *= 0.82f;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class DetachedRuntimeOwner : MonoBehaviour
    {
        public GameObject OwnerRoot { get; private set; }

        public void Configure(GameObject ownerRoot)
        {
            OwnerRoot = ownerRoot;
        }
    }
}
