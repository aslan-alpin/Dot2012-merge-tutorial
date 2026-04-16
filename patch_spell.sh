cat << 'INNER_EOF' > /tmp/PlayerSpellLoadout_patch.cs
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

            castDirection = ResolveCastDirection(
                handTransform,
                controllerTransform,
                hasTrackedRotation ? resolvedRotation * Vector3.forward : Vector3.zero);
            
            // Reverse direction!
            castDirection = -castDirection;
                
            castOrigin = ResolveCastOrigin(
                ResolveCastOriginPosition(
                    handTransform,
                    controllerTransform,
                    hasTrackedPosition ? resolvedPosition : (Vector3?)null),
                castDirection);
        }
INNER_EOF
