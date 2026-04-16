using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VRCombat.Enemies
{
    [DisallowMultipleComponent]
    public sealed class GoblinAnimationDriver : MonoBehaviour
    {
        [SerializeField]
        AnimationClip m_LocomotionClip;

        [SerializeField]
        AnimationClip m_AttackClip;

        Animator m_Animator;
        PlayableGraph m_PlayableGraph;
        AnimationMixerPlayable m_MixerPlayable;
        AnimationClipPlayable m_LocomotionPlayable;
        AnimationClipPlayable m_AttackPlayable;
        Renderer[] m_Renderers;
        Quaternion m_BaseLocalRotation;
        Vector3 m_BaseLocalPosition;
        bool m_IsGraphCreated;
        bool m_HasAttackPlayable;
        bool m_IsAttackGraphActive;
        bool m_HasGroundBaseline;
        float m_MoveBlend;
        float m_AttackStartedAt = -100f;
        float m_AttackDuration;
        float m_GroundBaselineWorldY;

        public void Configure(AnimationClip locomotionClip, AnimationClip attackClip = null)
        {
            m_LocomotionClip = locomotionClip;
            m_AttackClip = attackClip;
            RebuildGraph();
        }

        public void SetMoveBlend(float speed01)
        {
            m_MoveBlend = Mathf.Clamp01(speed01);
        }

        public void PlayAttack(float durationSeconds)
        {
            m_AttackDuration = Mathf.Max(0.1f, durationSeconds);
            m_AttackStartedAt = Time.time;
            if (m_HasAttackPlayable)
                m_AttackPlayable.SetTime(0d);
        }

        Transform m_Spine;
        Transform m_RightArm;
        Transform m_LeftArm;
        Transform m_RightLeg;
        Transform m_LeftLeg;
        Transform m_RightFoot;
        Transform m_LeftFoot;
        Quaternion[] m_BaseBoneRots = new Quaternion[5];

        void Awake()
        {
            m_Animator = GetComponent<Animator>();
            m_BaseLocalPosition = transform.localPosition;
            m_BaseLocalRotation = transform.localRotation;
            m_Renderers = GetComponentsInChildren<Renderer>(true);

            if (m_LocomotionClip == null)
                m_LocomotionClip = ResolveLocomotionClip();
            if (m_AttackClip == null)
                m_AttackClip = ResolveAttackClip();

            FindBones();
        }

        void FindBones()
        {
            if (m_Animator != null && m_Animator.isHuman)
            {
                m_Spine = m_Animator.GetBoneTransform(HumanBodyBones.Spine);
                m_RightArm = m_Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                m_LeftArm = m_Animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                m_RightLeg = m_Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                m_LeftLeg = m_Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                m_RightFoot = m_Animator.GetBoneTransform(HumanBodyBones.RightFoot);
                m_LeftFoot = m_Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            }
            else
            {
                foreach (var t in GetComponentsInChildren<Transform>())
                {
                    var name = t.name.ToLowerInvariant();
                    if (m_Spine == null && name.Contains("spine")) m_Spine = t;
                    if (m_RightArm == null && (name.Contains("upperarm.r") || name.Contains("arm_r"))) m_RightArm = t;
                    if (m_LeftArm == null && (name.Contains("upperarm.l") || name.Contains("arm_l"))) m_LeftArm = t;
                    if (m_RightLeg == null && (name.Contains("leg.r") || name.Contains("leg_r"))) m_RightLeg = t;
                    if (m_LeftLeg == null && (name.Contains("leg.l") || name.Contains("leg_l"))) m_LeftLeg = t;
                    if (m_RightFoot == null && (name.Contains("foot.r") || name.Contains("foot_r"))) m_RightFoot = t;
                    if (m_LeftFoot == null && (name.Contains("foot.l") || name.Contains("foot_l"))) m_LeftFoot = t;
                }
            }

            if (m_Spine != null) m_BaseBoneRots[0] = m_Spine.localRotation;
            if (m_RightArm != null) m_BaseBoneRots[1] = m_RightArm.localRotation;
            if (m_LeftArm != null) m_BaseBoneRots[2] = m_LeftArm.localRotation;
            if (m_RightLeg != null) m_BaseBoneRots[3] = m_RightLeg.localRotation;
            if (m_LeftLeg != null) m_BaseBoneRots[4] = m_LeftLeg.localRotation;
        }

        void OnEnable()
        {
            RebuildGraph();
        }

        void Update()
        {
            if (m_IsGraphCreated)
            {
                UpdateGraphPlayback();
                return;
            }

            ApplyFallbackPose();
        }

        void OnDisable()
        {
            DestroyGraph();
            ResetFallbackPose();
        }

        void OnDestroy()
        {
            DestroyGraph();
            ResetFallbackPose();
        }

        void RebuildGraph()
        {
            DestroyGraph();
            ResetFallbackPose();
            if (m_Animator == null)
                return;

            if (m_LocomotionClip == null)
                m_LocomotionClip = ResolveLocomotionClip();
            if (m_AttackClip == null)
                m_AttackClip = ResolveAttackClip();

            if (m_LocomotionClip == null)
                return;

            m_Animator.enabled = true;
            m_Animator.applyRootMotion = false;
            m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            m_Animator.updateMode = AnimatorUpdateMode.Normal;
            m_Animator.Rebind();
            m_Animator.Update(0f);

            m_PlayableGraph = PlayableGraph.Create($"{name}-GoblinAnimation");
            var playableOutput = AnimationPlayableOutput.Create(m_PlayableGraph, "GoblinAnimation", m_Animator);

            if (m_AttackClip != null)
            {
                m_MixerPlayable = AnimationMixerPlayable.Create(m_PlayableGraph, 2);
                m_LocomotionPlayable = AnimationClipPlayable.Create(m_PlayableGraph, m_LocomotionClip);
                m_LocomotionPlayable.SetApplyFootIK(false);
                m_LocomotionPlayable.SetDuration(double.MaxValue);
                m_LocomotionPlayable.SetTime(0d);
                m_AttackPlayable = AnimationClipPlayable.Create(m_PlayableGraph, m_AttackClip);
                m_AttackPlayable.SetApplyFootIK(false);
                m_AttackPlayable.SetTime(0d);
                m_PlayableGraph.Connect(m_LocomotionPlayable, 0, m_MixerPlayable, 0);
                m_PlayableGraph.Connect(m_AttackPlayable, 0, m_MixerPlayable, 1);
                m_MixerPlayable.SetInputWeight(0, 1f);
                m_MixerPlayable.SetInputWeight(1, 0f);
                playableOutput.SetSourcePlayable(m_MixerPlayable);
                m_HasAttackPlayable = true;
            }
            else
            {
                m_LocomotionPlayable = AnimationClipPlayable.Create(m_PlayableGraph, m_LocomotionClip);
                m_LocomotionPlayable.SetApplyFootIK(false);
                m_LocomotionPlayable.SetDuration(double.MaxValue);
                m_LocomotionPlayable.SetTime(0d);
                playableOutput.SetSourcePlayable(m_LocomotionPlayable);
                m_HasAttackPlayable = false;
            }

            m_PlayableGraph.Play();
            m_IsGraphCreated = true;
            CacheGroundBaseline();
        }

        void UpdateGraphPlayback()
        {
            var locomotionSpeed = Mathf.Lerp(0.25f, 1.12f, m_MoveBlend);
            m_LocomotionPlayable.SetSpeed(locomotionSpeed);

            if (m_LocomotionClip != null && !m_LocomotionClip.isLooping)
            {
                var time = m_LocomotionPlayable.GetTime();
                if (time >= m_LocomotionClip.length)
                {
                    m_LocomotionPlayable.SetTime(time % m_LocomotionClip.length);
                }
            }

            m_IsAttackGraphActive = Time.time - m_AttackStartedAt < m_AttackDuration;
            if (m_HasAttackPlayable)
            {
                m_MixerPlayable.SetInputWeight(0, m_IsAttackGraphActive ? 0.2f : 1f);
                m_MixerPlayable.SetInputWeight(1, m_IsAttackGraphActive ? 1f : 0f);
                if (!m_IsAttackGraphActive)
                    m_AttackPlayable.SetTime(0d);
            }
            
            // Wait for LateUpdate to apply the procedural rig animation
        }

        void LateUpdate()
        {
            if (m_IsGraphCreated)
            {
                // Let the Animator and PlayableGraph handle the pose entirely. 
                // We shouldn't conflict with ApplyProceduralCrouchStance or GroundCompensation.
                return;
            }

            var isAttacking = Time.time - m_AttackStartedAt < m_AttackDuration;
            ResetFallbackPose();

            if (!m_HasAttackPlayable && isAttacking)
            {
                ApplyProceduralAttackPose();
            }
            else if (!m_HasAttackPlayable && m_MoveBlend > 0.01f)
            {
                ApplyProceduralLocomotionOverlay();
            }

            // Always apply a crouch offset to account for default stance
            ApplyProceduralCrouchStance();
        }

        void ApplyProceduralCrouchStance()
        {
            if (m_Spine != null)
                m_Spine.localRotation = m_BaseBoneRots[0] * Quaternion.Euler(15f, 0f, 0f); // Lean forward

            if (m_RightLeg != null)
                m_RightLeg.localRotation = m_BaseBoneRots[3] * Quaternion.Euler(-25f, 0f, 0f); // Bend knees

            if (m_LeftLeg != null)
                m_LeftLeg.localRotation = m_BaseBoneRots[4] * Quaternion.Euler(-25f, 0f, 0f); // Bend knees
        }

        void ApplyFallbackPose()
        {
            // Handled efficiently in LateUpdate now
        }

        void ApplyProceduralLocomotionOverlay()
        {
            var phase = Time.time * Mathf.Lerp(2.2f, 5.4f, m_MoveBlend) + GetInstanceID() * 0.013f;
            
            // Add a crouched bob
            transform.localPosition = m_BaseLocalPosition + Vector3.up * (Mathf.Sin(phase * 2f) * 0.04f * m_MoveBlend);
            transform.localRotation = m_BaseLocalRotation * Quaternion.Euler(
                Mathf.Sin(phase) * 3f * m_MoveBlend,
                Mathf.Sin(phase * 0.5f) * 5f * m_MoveBlend,
                Mathf.Sin(phase) * 1.5f * m_MoveBlend);

            if (m_RightArm != null && m_LeftArm != null)
            {
                // Swing arms while crouching
                var armSwing = Mathf.Sin(phase);
                m_RightArm.localRotation = m_BaseBoneRots[1] * Quaternion.Euler(armSwing * 30f * m_MoveBlend, 0f, 0f);
                m_LeftArm.localRotation = m_BaseBoneRots[2] * Quaternion.Euler(-armSwing * 30f * m_MoveBlend, 0f, 0f);
            }
        }

        void ApplyProceduralAttackPose()
        {
            var normalized = Mathf.Clamp01((Time.time - m_AttackStartedAt) / Mathf.Max(0.01f, m_AttackDuration));
            var lunge = Mathf.Sin(normalized * Mathf.PI);
            var recoil = Mathf.Sin(normalized * Mathf.PI * 1.2f);
            
            transform.localPosition = m_BaseLocalPosition + new Vector3(0f, -0.05f * recoil, 0.12f * lunge);
            transform.localRotation = m_BaseLocalRotation * Quaternion.Euler(-18f * lunge, 0f, 8f * recoil);

            if (m_RightArm != null && m_LeftArm != null && m_Spine != null)
            {
                var windup = Mathf.Clamp01(Mathf.Sin(normalized * Mathf.PI * 0.5f));
                var strike = Mathf.Clamp01(Mathf.Sin(normalized * Mathf.PI));

                // Right arm strike forward from crouch
                m_RightArm.localRotation = m_BaseBoneRots[1] * Quaternion.Euler(-60f * windup + 80f * strike, 20f * strike, 0f);
                // Left arm counterbalance
                m_LeftArm.localRotation = m_BaseBoneRots[2] * Quaternion.Euler(-30f * strike, 0f, 0f);
                // Twist spine
                m_Spine.localRotation = m_BaseBoneRots[0] * Quaternion.Euler(10f * strike, 30f * windup - 40f * strike, 0f);
            }
        }

        void DestroyGraph()
        {
            if (!m_IsGraphCreated)
                return;

            m_PlayableGraph.Destroy();
            m_IsGraphCreated = false;
            m_HasAttackPlayable = false;
            m_IsAttackGraphActive = false;
            m_HasGroundBaseline = false;
        }

        void ResetFallbackPose()
        {
            transform.localPosition = m_BaseLocalPosition;
            transform.localRotation = m_BaseLocalRotation;

            if (!m_HasAttackPlayable)
            {
                if (m_Spine != null) m_Spine.localRotation = m_BaseBoneRots[0];
                if (m_RightArm != null) m_RightArm.localRotation = m_BaseBoneRots[1];
                if (m_LeftArm != null) m_LeftArm.localRotation = m_BaseBoneRots[2];
                if (m_RightLeg != null) m_RightLeg.localRotation = m_BaseBoneRots[3];
                if (m_LeftLeg != null) m_LeftLeg.localRotation = m_BaseBoneRots[4];
            }
        }

        void CacheGroundBaseline()
        {
            if (!TryGetCurrentGroundWorldY(out m_GroundBaselineWorldY))
            {
                m_HasGroundBaseline = false;
                return;
            }

            m_HasGroundBaseline = true;
        }

        void ApplyGraphGroundCompensation()
        {
            if (!m_HasGroundBaseline || m_MoveBlend <= 0.01f || m_IsAttackGraphActive)
            {
                transform.localPosition = m_BaseLocalPosition;
                return;
            }

            if (!TryGetCurrentGroundWorldY(out var currentGroundWorldY))
            {
                transform.localPosition = m_BaseLocalPosition;
                return;
            }

            var worldYOffset = m_GroundBaselineWorldY - currentGroundWorldY;
            var localYOffset = worldYOffset;
            if (transform.parent != null)
                localYOffset = transform.parent.InverseTransformVector(Vector3.up * worldYOffset).y;

            transform.localPosition = m_BaseLocalPosition + Vector3.up * localYOffset;
        }

        bool TryGetCurrentGroundWorldY(out float groundWorldY)
        {
            groundWorldY = float.PositiveInfinity;

            if (m_LeftFoot != null)
                groundWorldY = Mathf.Min(groundWorldY, m_LeftFoot.position.y);

            if (m_RightFoot != null)
                groundWorldY = Mathf.Min(groundWorldY, m_RightFoot.position.y);

            if (m_Renderers != null)
            {
                for (var i = 0; i < m_Renderers.Length; i++)
                {
                    var renderer = m_Renderers[i];
                    if (renderer == null || !renderer.enabled)
                        continue;

                    groundWorldY = Mathf.Min(groundWorldY, renderer.bounds.min.y);
                }
            }

            return !float.IsInfinity(groundWorldY);
        }

        static AnimationClip ResolveLocomotionClip()
        {
            var clips = Resources.LoadAll<AnimationClip>("CombatModels/WalkingHumanoid");
            AnimationClip bestClip = null;
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null ||
                    clip.length <= 0.05f ||
                    clip.name.IndexOf("__preview__", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                var score = 0;
                var lowerName = clip.name.ToLowerInvariant();
                if (lowerName.Contains("walk"))
                    score += 6;
                if (lowerName.Contains("run"))
                    score += 4;
                score += Mathf.RoundToInt(clip.length * 10f);
                if (bestClip == null || score > ScoreLocomotionClip(bestClip))
                    bestClip = clip;
            }

            return bestClip;
        }

        static int ScoreLocomotionClip(AnimationClip clip)
        {
            if (clip == null)
                return int.MinValue;

            var score = 0;
            var lowerName = clip.name.ToLowerInvariant();
            if (lowerName.Contains("walk"))
                score += 6;
            if (lowerName.Contains("run"))
                score += 4;
            score += Mathf.RoundToInt(clip.length * 10f);
            return score;
        }

        static AnimationClip ResolveAttackClip()
        {
            var clips = Resources.LoadAll<AnimationClip>("CombatModels");
            AnimationClip bestClip = null;
            var bestScore = int.MinValue;
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null || clip.length <= 0.05f)
                    continue;

                var lowerName = clip.name.ToLowerInvariant();
                var score = 0;
                if (lowerName.Contains("attack"))
                    score += 8;
                if (lowerName.Contains("punch"))
                    score += 6;
                if (lowerName.Contains("hit"))
                    score += 4;
                if (lowerName.Contains("slash"))
                    score += 4;
                if (lowerName.Contains("walk") || lowerName.Contains("run"))
                    score -= 8;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestClip = clip;
            }

            return bestScore > 0 ? bestClip : null;
        }
    }
}
