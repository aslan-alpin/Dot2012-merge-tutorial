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

        readonly List<Rigidbody> m_SegmentBodies = new List<Rigidbody>();
        readonly List<Transform> m_SimulatedBones = new List<Transform>();
        readonly List<Vector3> m_RestLocalPositions = new List<Vector3>();
        readonly List<Quaternion> m_RestLocalRotations = new List<Quaternion>();
        readonly List<Collider> m_SegmentColliders = new List<Collider>();
        SwingDamageDealer m_TipDamageDealer;
        Transform m_SimulationRoot;
        Vector3 m_SimulationRootLocalPosition;
        Quaternion m_SimulationRootLocalRotation = Quaternion.identity;
        bool m_HasDetachedSimulationRoot;
        bool m_IsInitialized;
        bool m_IsMounted;

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

            DisableImportedAnimation();
            DetachSimulationRootIfNeeded();

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

            var simulatedBones = SampleChainBones(chainBones, boneStep);
            if (simulatedBones.Count < 3)
            {
                Debug.LogWarning($"{nameof(RiggedChainWeapon)} on {name} does not have enough sampled bones to simulate.");
                return false;
            }

            var previousBody = handleBody;
            for (var i = 0; i < simulatedBones.Count; i++)
            {
                var bone = simulatedBones[i];
                var colliderRadius = i == simulatedBones.Count - 1
                    ? Mathf.Max(segmentRadius, tipRadius)
                    : Mathf.Max(0.005f, EstimateSegmentRadius(bone, simulatedBones, i, segmentRadius));

                var body = EnsureSegmentBody(bone, colliderRadius, segmentMass, i == simulatedBones.Count - 1);
                ConfigureJoint(
                    bone,
                    body,
                    previousBody,
                    i == 0 ? 42f : 58f,
                    i == 0 ? rootSpring : segmentSpring,
                    damper);

                m_SegmentBodies.Add(body);
                m_SimulatedBones.Add(bone);
                m_RestLocalPositions.Add(bone.localPosition);
                m_RestLocalRotations.Add(bone.localRotation);
                previousBody = body;
            }

            IgnoreInternalCollisions(holdCollider);
            m_TipDamageDealer = ConfigureTipDamage(
                simulatedBones[simulatedBones.Count - 1],
                baseDamage,
                minSwingSpeed,
                maxSwingSpeedForScaling,
                hitCooldownSeconds,
                tipRadius * 1.8f);
            m_IsInitialized = true;
            SetMountedState(true);

            return true;
        }

        public void SetMountedState(bool mounted)
        {
            if (!m_IsInitialized)
                return;

            if (m_IsMounted == mounted)
                return;

            m_IsMounted = mounted;
            if (mounted)
            {
                SetSegmentBodiesDynamicState(false);
                SyncSimulationRootToHandlePose();
                RestoreRestPose();
                SetSegmentCollidersEnabled(false);
                if (m_TipDamageDealer != null)
                    m_TipDamageDealer.enabled = false;
                Physics.SyncTransforms();
                return;
            }

            SyncSimulationRootToHandlePose();
            RestoreRestPose();
            Physics.SyncTransforms();
            SetSegmentCollidersEnabled(true);
            if (m_TipDamageDealer != null)
                m_TipDamageDealer.enabled = true;
            SetSegmentBodiesDynamicState(true);
        }

        void OnDestroy()
        {
            if (m_HasDetachedSimulationRoot && m_SimulationRoot != null)
                Destroy(m_SimulationRoot.gameObject);
        }

        void DetachSimulationRootIfNeeded()
        {
            if (m_HasDetachedSimulationRoot)
                return;

            var simulationRoot = FindSimulationRoot();
            if (simulationRoot == null || simulationRoot == transform)
                return;

            m_SimulationRoot = simulationRoot;
            m_SimulationRootLocalPosition = transform.InverseTransformPoint(simulationRoot.position);
            m_SimulationRootLocalRotation = Quaternion.Inverse(transform.rotation) * simulationRoot.rotation;

            var ownership = simulationRoot.GetComponent<DetachedRuntimeOwner>();
            if (ownership == null)
                ownership = simulationRoot.gameObject.AddComponent<DetachedRuntimeOwner>();
            ownership.Configure(gameObject);

            simulationRoot.SetParent(null, true);
            m_HasDetachedSimulationRoot = true;
            SyncSimulationRootToHandlePose();
        }

        Transform FindSimulationRoot()
        {
            var allTransforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null)
                    continue;

                if (candidate.name.IndexOf(ArmatureNameToken, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            }

            return null;
        }

        void SyncSimulationRootToHandlePose()
        {
            if (m_SimulationRoot == null)
                return;

            m_SimulationRoot.SetPositionAndRotation(
                transform.TransformPoint(m_SimulationRootLocalPosition),
                transform.rotation * m_SimulationRootLocalRotation);
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
                    if (candidate == null)
                        continue;

                    if (candidate.name.IndexOf(ArmatureNameToken, System.StringComparison.OrdinalIgnoreCase) >= 0)
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
                if (depth < shallowestDepth)
                {
                    shallowestDepth = depth;
                    rootBone = candidate;
                }
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

        Rigidbody EnsureSegmentBody(Transform bone, float colliderRadius, float segmentMass, bool isTip)
        {
            var body = bone.GetComponent<Rigidbody>();
            if (body == null)
                body = bone.gameObject.AddComponent<Rigidbody>();

            body.mass = Mathf.Max(0.01f, isTip ? segmentMass * 0.9f : segmentMass);
            body.useGravity = true;
            body.linearDamping = isTip ? 0.2f : 0.5f;
            body.angularDamping = isTip ? 0.1f : 0.25f;
            body.solverIterations = 32;
            body.solverVelocityIterations = 16;
            body.maxAngularVelocity = 50f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var sphereCollider = bone.GetComponent<SphereCollider>();
            if (sphereCollider == null)
                sphereCollider = bone.gameObject.AddComponent<SphereCollider>();

            sphereCollider.radius = colliderRadius;
            sphereCollider.isTrigger = false;
            m_SegmentColliders.Add(sphereCollider);

            return body;
        }

        static void ConfigureJoint(
            Transform bone,
            Rigidbody body,
            Rigidbody connectedBody,
            float angularLimitDegrees,
            float spring,
            float damper)
        {
            if (bone == null || body == null || connectedBody == null)
                return;

            var joint = bone.GetComponent<ConfigurableJoint>();
            if (joint == null)
                joint = bone.gameObject.AddComponent<ConfigurableJoint>();

            joint.connectedBody = connectedBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = connectedBody.transform.InverseTransformPoint(bone.position);
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.projectionMode = JointProjectionMode.None;
            joint.projectionDistance = 0f;
            joint.projectionAngle = 0f;
            joint.enablePreprocessing = true;
            joint.massScale = 1f;
            joint.connectedMassScale = 1f;

            var softJointLimit = new SoftJointLimit
            {
                limit = angularLimitDegrees
            };

            var softJointLimitSpring = new SoftJointLimitSpring
            {
                spring = spring,
                damper = damper
            };

            joint.linearLimitSpring = softJointLimitSpring;
            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce = float.MaxValue
            };
            joint.angularXDrive = drive;
            joint.angularYZDrive = drive;

            joint.lowAngularXLimit = new SoftJointLimit { limit = -angularLimitDegrees };
            joint.highAngularXLimit = new SoftJointLimit { limit = angularLimitDegrees };
            joint.angularYLimit = new SoftJointLimit { limit = angularLimitDegrees };
            joint.angularZLimit = new SoftJointLimit { limit = angularLimitDegrees };
        }

        void IgnoreInternalCollisions(Collider holdCollider)
        {
            for (var i = 0; i < m_SegmentColliders.Count; i++)
            {
                var current = m_SegmentColliders[i];
                if (current == null)
                    continue;

                if (holdCollider != null)
                    Physics.IgnoreCollision(holdCollider, current, true);

                for (var j = i + 1; j < m_SegmentColliders.Count; j++)
                {
                    var other = m_SegmentColliders[j];
                    if (other == null)
                        continue;

                    Physics.IgnoreCollision(current, other, true);
                }
            }
        }

        static SwingDamageDealer ConfigureTipDamage(
            Transform tipBone,
            float baseDamage,
            float minSwingSpeed,
            float maxSwingSpeedForScaling,
            float hitCooldownSeconds,
            float proximityFallbackRadius)
        {
            if (tipBone == null)
                return null;

            var damageDealer = tipBone.GetComponent<SwingDamageDealer>();
            if (damageDealer == null)
                damageDealer = tipBone.gameObject.AddComponent<SwingDamageDealer>();

            damageDealer.SetDamageGate(null);
            damageDealer.Configure(
                baseDamage,
                minSwingSpeed,
                maxSwingSpeedForScaling,
                hitCooldownSeconds,
                Mathf.Max(0.05f, proximityFallbackRadius));
            return damageDealer;
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

            return Mathf.Max(fallbackRadius, distance * 0.38f);
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

        void SetSegmentBodiesDynamicState(bool enabled)
        {
            for (var i = 0; i < m_SegmentBodies.Count; i++)
            {
                var body = m_SegmentBodies[i];
                if (body == null)
                    continue;

                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = !enabled;
                body.useGravity = enabled;
                body.collisionDetectionMode = enabled
                    ? CollisionDetectionMode.ContinuousDynamic
                    : CollisionDetectionMode.ContinuousSpeculative;

                if (enabled)
                    body.WakeUp();
            }
        }

        void SetSegmentCollidersEnabled(bool enabled)
        {
            for (var i = 0; i < m_SegmentColliders.Count; i++)
            {
                if (m_SegmentColliders[i] != null)
                    m_SegmentColliders[i].enabled = enabled;
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
