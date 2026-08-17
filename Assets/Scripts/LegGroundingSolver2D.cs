using UnityEngine;
using Binding = CharacterRig2D.JointBinding;
using KinematicPose = CharacterRig2D.Pose2D;

/// <summary>
/// Applies ground contact, support selection and two-bone leg IK to an FK pose.
/// </summary>
public sealed class LegGroundingSolver2D
{
    public struct Settings
    {
        public bool supportLegIkEnabled;
        public LayerMask groundLayers;
        public float groundProbeHeight;
        public float groundProbeDistance;
        public float footGroundClearance;
        public float plantedFootWorldAngle;
        public float footPlantBlendSpeed;
        public float virtualSoleEndInset;
        public float fixedBodyHeightOffset;
        public float supportSwitchHysteresis;
        public float supportIkBlendSpeed;
        public float penetrationToKnee;
        public float maxPenetrationToKnee;
        public bool preventSwingFootPenetration;
        public float swingFootClearanceIkWeight;
        public float swingFootPenetrationTolerance;
        public bool logUnreachableIkTargets;
    }

    private readonly CharacterRig2D rig;
    private readonly Binding leftThigh;
    private readonly Binding leftCalf;
    private readonly Binding leftFoot;
    private readonly Binding rightThigh;
    private readonly Binding rightCalf;
    private readonly Binding rightFoot;
    private readonly Rigidbody2D leftFootBody;
    private readonly Rigidbody2D rightFootBody;
    private readonly Collider2D leftFootCollider;
    private readonly Collider2D rightFootCollider;
    private readonly Vector2 defaultBodyWorldPosition;
    private readonly Vector2 defaultLeftAnkle;
    private readonly Vector2 defaultRightAnkle;

    private Settings settings;
    private float leftFootPlantWeight;
    private float rightFootPlantWeight;
    private readonly float lockedBodyWorldY;
    private int supportLeg;
    private bool leftReachWarningIssued;
    private bool rightReachWarningIssued;
    private float leftKneeBendSign;
    private float rightKneeBendSign;
    private bool stoppingFootLockActive;
    private int stoppingSupportLeg;
    private Vector2 stoppingSupportAnkle;
    private Vector2 stoppingBodyStart;
    private Vector2 stoppingBodyTarget;

    public LegGroundingSolver2D(CharacterRig2D rig)
    {
        this.rig = rig;
        leftThigh = rig.FindBinding("LeftThigh");
        leftCalf = rig.FindBinding("LeftCalf");
        leftFoot = rig.FindBinding("LeftFoot");
        rightThigh = rig.FindBinding("RightThigh");
        rightCalf = rig.FindBinding("RightCalf");
        rightFoot = rig.FindBinding("RightFoot");

        leftFootBody = leftFoot.joint.attachedRigidbody;
        rightFootBody = rightFoot.joint.attachedRigidbody;
        if (!leftFootBody.TryGetComponent(out leftFootCollider))
            throw new System.InvalidOperationException("Player body 'LeftFoot' has no Collider2D.");
        if (!rightFootBody.TryGetComponent(out rightFootCollider))
            throw new System.InvalidOperationException("Player body 'RightFoot' has no Collider2D.");

        defaultBodyWorldPosition = rig.CentralBody.position;
        defaultLeftAnkle = GetCurrentAnkle(leftFoot);
        defaultRightAnkle = GetCurrentAnkle(rightFoot);
        lockedBodyWorldY = rig.CentralBody.position.y;
        ResetRuntimeState();
    }

    public bool StoppingFootLockActive => stoppingFootLockActive;

    public void Configure(Settings value) => settings = value;

    public void ResetRuntimeState()
    {
        leftFootPlantWeight = 0f;
        rightFootPlantWeight = 0f;
        supportLeg = 0;
        leftReachWarningIssued = false;
        rightReachWarningIssued = false;
        leftKneeBendSign = 0f;
        rightKneeBendSign = 0f;
        stoppingFootLockActive = false;
        stoppingSupportLeg = 0;
        stoppingSupportAnkle = Vector2.zero;
        stoppingBodyStart = rig.CentralBody.position;
        stoppingBodyTarget = rig.CentralBody.position;
    }

    public void BeginLocomotion(float preferredKneeBendSign)
    {
        stoppingFootLockActive = false;
        stoppingSupportLeg = 0;
        leftKneeBendSign = preferredKneeBendSign;
        rightKneeBendSign = preferredKneeBendSign;
    }

    public void BeginStopTransition(float preferredKneeBendSign)
    {
        leftKneeBendSign = preferredKneeBendSign;
        rightKneeBendSign = preferredKneeBendSign;

        bool hasLeftGround = TryGetAnkleGroundTarget(
            leftFoot, leftFootCollider,
            out Vector2 leftGroundAnkle, out float leftGroundScore, out _);
        bool hasRightGround = TryGetAnkleGroundTarget(
            rightFoot, rightFootCollider,
            out Vector2 rightGroundAnkle, out float rightGroundScore, out _);

        if (hasLeftGround || hasRightGround)
        {
            stoppingSupportLeg = hasLeftGround &&
                (!hasRightGround || leftGroundScore <= rightGroundScore) ? -1 : 1;
        }
        else
        {
            stoppingSupportLeg = supportLeg;
            if (stoppingSupportLeg == 0)
                stoppingSupportLeg = leftFootPlantWeight >= rightFootPlantWeight ? -1 : 1;
        }

        Binding footBinding = stoppingSupportLeg == -1 ? leftFoot : rightFoot;
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        if (foot == null)
        {
            stoppingFootLockActive = false;
            stoppingSupportLeg = 0;
            return;
        }

        stoppingSupportAnkle = stoppingSupportLeg == -1 && hasLeftGround
            ? leftGroundAnkle
            : stoppingSupportLeg == 1 && hasRightGround
                ? rightGroundAnkle
                : GetCurrentAnkle(footBinding);
        Vector2 defaultSupportAnkle = stoppingSupportLeg == -1
            ? defaultLeftAnkle
            : defaultRightAnkle;
        stoppingBodyStart = rig.CentralBody.position;
        stoppingBodyTarget = defaultBodyWorldPosition
                           + (stoppingSupportAnkle - defaultSupportAnkle);
        stoppingFootLockActive = true;
    }

    public Vector2 EvaluateStoppingBodyPosition(float walkPoseWeight)
    {
        float returnProgress = 1f - walkPoseWeight;
        float smoothProgress = returnProgress * returnProgress * (3f - 2f * returnProgress);
        return Vector2.Lerp(stoppingBodyStart, stoppingBodyTarget, smoothProgress);
    }

    public void ConstrainBodyHeight(ref Vector2 bodyTarget)
    {
        if (settings.supportLegIkEnabled && !stoppingFootLockActive)
            bodyTarget.y = lockedBodyWorldY + settings.fixedBodyHeightOffset;
    }

    public void Apply(float walkPoseWeight, float deltaTime)
    {
        if (settings.supportLegIkEnabled)
            ApplySupportLegIk(walkPoseWeight, deltaTime);
        else
        {
            ApplySupportFootFlattening(deltaTime);
            ApplyAnimatedGroundCorrection();
        }
    }

    private void ApplySupportLegIk(float walkPoseWeight, float deltaTime)
    {
        if (stoppingFootLockActive)
        {
            ApplyStoppedPoseIk(walkPoseWeight);
            return;
        }

        bool hasLeft = TryGetAnkleGroundTarget(
            leftFoot, leftFootCollider, out Vector2 leftTarget, out float leftScore,
            out bool leftRawPenetrating);
        bool hasRight = TryGetAnkleGroundTarget(
            rightFoot, rightFootCollider, out Vector2 rightTarget, out float rightScore,
            out bool rightRawPenetrating);

        if (supportLeg == -1 && (!hasLeft ||
            (hasRight && rightScore + settings.supportSwitchHysteresis < leftScore)))
            supportLeg = hasRight ? 1 : 0;
        else if (supportLeg == 1 && (!hasRight ||
            (hasLeft && leftScore + settings.supportSwitchHysteresis < rightScore)))
            supportLeg = hasLeft ? -1 : 0;
        else if (supportLeg == 0)
            supportLeg = hasLeft && (!hasRight || leftScore <= rightScore) ? -1 : hasRight ? 1 : 0;

        float blendStep = settings.supportIkBlendSpeed * deltaTime;
        leftFootPlantWeight = Mathf.MoveTowards(
            leftFootPlantWeight, supportLeg == -1 ? 1f : 0f, blendStep);
        rightFootPlantWeight = Mathf.MoveTowards(
            rightFootPlantWeight, supportLeg == 1 ? 1f : 0f, blendStep);

        bool leftPenetrating = hasLeft && settings.preventSwingFootPenetration && leftRawPenetrating;
        if (hasLeft && (leftFootPlantWeight > 0f || leftPenetrating))
        {
            float weight = leftPenetrating
                ? Mathf.Max(leftFootPlantWeight, settings.swingFootClearanceIkWeight)
                : leftFootPlantWeight;
            SolveLegToGround(leftThigh, leftCalf, leftFoot, leftFootCollider, leftTarget,
                weight, leftFootPlantWeight, leftPenetrating,
                ref leftReachWarningIssued, ref leftKneeBendSign);
        }

        bool rightPenetrating = hasRight && settings.preventSwingFootPenetration && rightRawPenetrating;
        if (hasRight && (rightFootPlantWeight > 0f || rightPenetrating))
        {
            float weight = rightPenetrating
                ? Mathf.Max(rightFootPlantWeight, settings.swingFootClearanceIkWeight)
                : rightFootPlantWeight;
            SolveLegToGround(rightThigh, rightCalf, rightFoot, rightFootCollider, rightTarget,
                weight, rightFootPlantWeight, rightPenetrating,
                ref rightReachWarningIssued, ref rightKneeBendSign);
        }
    }

    private void ApplyStoppedPoseIk(float walkPoseWeight)
    {
        if (walkPoseWeight <= 0.0001f) return;

        bool lockLeft = stoppingSupportLeg == -1;
        Binding supportThigh = lockLeft ? leftThigh : rightThigh;
        Binding supportCalf = lockLeft ? leftCalf : rightCalf;
        Binding supportFoot = lockLeft ? leftFoot : rightFoot;
        Collider2D supportCollider = lockLeft ? leftFootCollider : rightFootCollider;

        if (lockLeft)
        {
            leftFootPlantWeight = 1f;
            rightFootPlantWeight = 0f;
            supportLeg = -1;
            SolveLegToGround(
                supportThigh, supportCalf, supportFoot, supportCollider,
                stoppingSupportAnkle, 1f, 1f, false,
                ref leftReachWarningIssued, ref leftKneeBendSign);
        }
        else
        {
            leftFootPlantWeight = 0f;
            rightFootPlantWeight = 1f;
            supportLeg = 1;
            SolveLegToGround(
                supportThigh, supportCalf, supportFoot, supportCollider,
                stoppingSupportAnkle, 1f, 1f, false,
                ref rightReachWarningIssued, ref rightKneeBendSign);
        }

        Binding swingThigh = lockLeft ? rightThigh : leftThigh;
        Binding swingCalf = lockLeft ? rightCalf : leftCalf;
        Binding swingFoot = lockLeft ? rightFoot : leftFoot;
        Collider2D swingCollider = lockLeft ? rightFootCollider : leftFootCollider;
        bool hasGround = TryGetAnkleGroundTarget(
            swingFoot, swingCollider, out Vector2 swingTarget, out _, out bool swingPenetrating);
        if (!hasGround || !settings.preventSwingFootPenetration || !swingPenetrating) return;

        if (lockLeft)
        {
            SolveLegToGround(
                swingThigh, swingCalf, swingFoot, swingCollider, swingTarget,
                settings.swingFootClearanceIkWeight, 0f, true,
                ref rightReachWarningIssued, ref rightKneeBendSign);
        }
        else
        {
            SolveLegToGround(
                swingThigh, swingCalf, swingFoot, swingCollider, swingTarget,
                settings.swingFootClearanceIkWeight, 0f, true,
                ref leftReachWarningIssued, ref leftKneeBendSign);
        }
    }

    private bool TryGetAnkleGroundTarget(
        Binding footBinding,
        Collider2D footCollider,
        out Vector2 target,
        out float score,
        out bool penetrating)
    {
        target = Vector2.zero;
        score = float.PositiveInfinity;
        penetrating = false;
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        if (!rig.Poses.TryGetValue(foot, out KinematicPose footPose)) return false;

        Vector2 rawAnkle = footPose.position + Rotate(footBinding.joint.anchor, footPose.rotation);
        Vector2 origin = rawAnkle + Vector2.up * settings.groundProbeHeight;
        RaycastHit2D hit = Physics2D.Raycast(
            origin, Vector2.down,
            settings.groundProbeHeight + settings.groundProbeDistance,
            settings.groundLayers);
        if (hit.collider == null) return false;

        KinematicPose flatFootAtZeroAnkle = new(
            -Rotate(footBinding.joint.anchor, settings.plantedFootWorldAngle),
            settings.plantedFootWorldAngle);
        float soleBelowAnkle = GetVirtualSoleBottomAtPose(
            foot, footCollider, flatFootAtZeroAnkle);
        target = new Vector2(
            rawAnkle.x,
            hit.point.y + settings.footGroundClearance - soleBelowAnkle);
        score = rawAnkle.y - target.y;
        float colliderBottom = GetColliderBottomAtPose(foot, footCollider, footPose);
        penetrating = hit.point.y + settings.footGroundClearance - colliderBottom
                    > settings.swingFootPenetrationTolerance;
        return true;
    }

    private void SolveLegToGround(
        Binding thighBinding,
        Binding calfBinding,
        Binding footBinding,
        Collider2D footCollider,
        Vector2 ankleTarget,
        float ikWeight,
        float footPlantWeight,
        bool enforceFullColliderClearance,
        ref bool reachWarningIssued,
        ref float lockedBendSign)
    {
        Rigidbody2D thigh = thighBinding.joint.attachedRigidbody;
        Rigidbody2D calf = calfBinding.joint.attachedRigidbody;
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        if (!rig.Poses.TryGetValue(thigh, out KinematicPose rawThigh) ||
            !rig.Poses.TryGetValue(calf, out KinematicPose rawCalf) ||
            !rig.Poses.TryGetValue(foot, out KinematicPose rawFoot)) return;

        Rigidbody2D hipBody = thighBinding.joint.connectedBody;
        if (hipBody == null || !rig.Poses.TryGetValue(hipBody, out KinematicPose bodyPose)) return;

        Vector2 hip = bodyPose.position
                    + Rotate(thighBinding.joint.connectedAnchor, bodyPose.rotation);
        Vector2 thighAxis = calfBinding.joint.connectedAnchor - thighBinding.joint.anchor;
        Vector2 calfAxis = footBinding.joint.connectedAnchor - calfBinding.joint.anchor;
        float upperLength = thighAxis.magnitude;
        float lowerLength = calfAxis.magnitude;
        if (upperLength <= 0.0001f || lowerLength <= 0.0001f) return;

        float footRotation = Mathf.LerpAngle(
            rawFoot.rotation, settings.plantedFootWorldAngle, footPlantWeight);
        float flatSoleBelowAnkle = GetSoleBelowAnkleAtRotation(
            footBinding, footCollider, settings.plantedFootWorldAngle);
        float rotatedSoleBelowAnkle = enforceFullColliderClearance
            ? GetColliderBelowAnkleAtRotation(footBinding, footCollider, footRotation)
            : GetSoleBelowAnkleAtRotation(footBinding, footCollider, footRotation);
        float extraSoleDrop = Mathf.Max(0f, flatSoleBelowAnkle - rotatedSoleBelowAnkle);
        float transferredHeight = Mathf.Min(
            extraSoleDrop * settings.penetrationToKnee,
            settings.maxPenetrationToKnee);
        ankleTarget.y += transferredHeight;

        Vector2 toTarget = ankleTarget - hip;
        float requestedDistance = toTarget.magnitude;
        if (requestedDistance <= 0.0001f) toTarget = Vector2.down * 0.0001f;
        float minReach = Mathf.Abs(upperLength - lowerLength) + 0.0001f;
        float maxReach = upperLength + lowerLength - 0.0001f;
        float distance = Mathf.Clamp(toTarget.magnitude, minReach, maxReach);
        Vector2 direction = toTarget.normalized;
        ankleTarget = hip + direction * distance;

        bool unreachable = requestedDistance < minReach || requestedDistance > maxReach;
        if (unreachable && settings.logUnreachableIkTargets && !reachWarningIssued)
        {
            Debug.LogWarning(
                $"Support leg IK target for '{foot.name}' is outside the leg reach " +
                $"({requestedDistance:F3} vs [{minReach:F3}, {maxReach:F3}]). The target is clamped.",
                foot);
            reachWarningIssued = true;
        }
        else if (!unreachable)
        {
            reachWarningIssued = false;
        }

        Vector2 rawKnee = rawThigh.position
                        + Rotate(calfBinding.joint.connectedAnchor, rawThigh.rotation);
        float bendCross = Cross(direction, rawKnee - hip);
        if (lockedBendSign == 0f)
            lockedBendSign = Mathf.Abs(bendCross) > 0.0001f ? Mathf.Sign(bendCross) : 1f;
        float targetDirection = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float cosine = Mathf.Clamp(
            (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
            (2f * upperLength * distance), -1f, 1f);
        float shoulderAngle = Mathf.Acos(cosine) * Mathf.Rad2Deg;
        float solvedThighRotation = targetDirection + lockedBendSign * shoulderAngle
                                  - Mathf.Atan2(thighAxis.y, thighAxis.x) * Mathf.Rad2Deg;

        float thighRotation = Mathf.LerpAngle(rawThigh.rotation, solvedThighRotation, ikWeight);
        Vector2 thighPosition = hip - Rotate(thighBinding.joint.anchor, thighRotation);
        Vector2 knee = thighPosition
                     + Rotate(calfBinding.joint.connectedAnchor, thighRotation);
        float solvedCalfRotation = Mathf.Atan2(
            ankleTarget.y - knee.y, ankleTarget.x - knee.x) * Mathf.Rad2Deg
                                  - Mathf.Atan2(calfAxis.y, calfAxis.x) * Mathf.Rad2Deg;
        float calfRotation = Mathf.LerpAngle(rawCalf.rotation, solvedCalfRotation, ikWeight);
        Vector2 calfPosition = knee - Rotate(calfBinding.joint.anchor, calfRotation);
        Vector2 ankle = calfPosition
                      + Rotate(footBinding.joint.connectedAnchor, calfRotation);

        Vector2 footPosition = ankle - Rotate(footBinding.joint.anchor, footRotation);
        rig.Poses[thigh] = new KinematicPose(thighPosition, thighRotation);
        rig.Poses[calf] = new KinematicPose(calfPosition, calfRotation);
        rig.Poses[foot] = new KinematicPose(footPosition, footRotation);
    }

    private float GetSoleBelowAnkleAtRotation(
        Binding footBinding,
        Collider2D footCollider,
        float footRotation)
    {
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        KinematicPose footAtZeroAnkle = new(
            -Rotate(footBinding.joint.anchor, footRotation), footRotation);
        return GetVirtualSoleBottomAtPose(foot, footCollider, footAtZeroAnkle);
    }

    private static float GetColliderBelowAnkleAtRotation(
        Binding footBinding,
        Collider2D footCollider,
        float footRotation)
    {
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        KinematicPose footAtZeroAnkle = new(
            -Rotate(footBinding.joint.anchor, footRotation), footRotation);
        return GetColliderBottomAtPose(foot, footCollider, footAtZeroAnkle);
    }

    private void ApplySupportFootFlattening(float deltaTime)
    {
        bool hasLeft = TryGetFootGroundCorrection(
            leftFootBody, leftFootCollider, out float leftBottom, out _);
        bool hasRight = TryGetFootGroundCorrection(
            rightFootBody, rightFootCollider, out float rightBottom, out _);

        bool plantLeft = hasLeft && (!hasRight || leftBottom <= rightBottom);
        bool plantRight = hasRight && !plantLeft;
        float blendStep = settings.footPlantBlendSpeed * deltaTime;
        leftFootPlantWeight = Mathf.MoveTowards(
            leftFootPlantWeight, plantLeft ? 1f : 0f, blendStep);
        rightFootPlantWeight = Mathf.MoveTowards(
            rightFootPlantWeight, plantRight ? 1f : 0f, blendStep);

        FlattenFootAroundAnkle(leftFootBody, leftFootPlantWeight);
        FlattenFootAroundAnkle(rightFootBody, rightFootPlantWeight);
    }

    private void FlattenFootAroundAnkle(Rigidbody2D foot, float weight)
    {
        if (foot == null || weight <= 0f ||
            !rig.Poses.TryGetValue(foot, out KinematicPose pose)) return;

        HingeJoint2D ankle = foot.GetComponent<HingeJoint2D>();
        if (ankle == null) return;
        Vector2 ankleWorld = pose.position + Rotate(ankle.anchor, pose.rotation);
        float flattenedRotation = Mathf.LerpAngle(
            pose.rotation, settings.plantedFootWorldAngle, weight);
        Vector2 flattenedPosition = ankleWorld - Rotate(ankle.anchor, flattenedRotation);
        rig.Poses[foot] = new KinematicPose(flattenedPosition, flattenedRotation);
    }

    private void ApplyAnimatedGroundCorrection()
    {
        bool hasLeft = TryGetFootGroundCorrection(
            leftFootBody, leftFootCollider, out float leftBottom, out float leftCorrection);
        bool hasRight = TryGetFootGroundCorrection(
            rightFootBody, rightFootCollider, out float rightBottom, out float rightCorrection);
        if (!hasLeft && !hasRight) return;

        float correction = !hasRight || (hasLeft && leftBottom <= rightBottom)
            ? leftCorrection
            : rightCorrection;
        foreach (Rigidbody2D body in rig.AllBodies)
        {
            if (!rig.Poses.TryGetValue(body, out KinematicPose pose)) continue;
            rig.Poses[body] = new KinematicPose(
                pose.position + Vector2.up * correction, pose.rotation);
        }
    }

    private bool TryGetFootGroundCorrection(
        Rigidbody2D foot,
        Collider2D footCollider,
        out float targetBottom,
        out float correction)
    {
        targetBottom = 0f;
        correction = 0f;
        if (foot == null || footCollider == null ||
            !rig.Poses.TryGetValue(foot, out KinematicPose pose)) return false;

        targetBottom = GetVirtualSoleBottomAtPose(foot, footCollider, pose);
        Vector2 origin = new(pose.position.x, targetBottom + settings.groundProbeHeight);
        float distance = settings.groundProbeHeight + settings.groundProbeDistance;
        RaycastHit2D hit = Physics2D.Raycast(
            origin, Vector2.down, distance, settings.groundLayers);
        if (hit.collider == null) return false;
        correction = hit.point.y + settings.footGroundClearance - targetBottom;
        return true;
    }

    private float GetVirtualSoleBottomAtPose(
        Rigidbody2D body,
        Collider2D collider,
        KinematicPose pose)
    {
        if (collider is BoxCollider2D box && collider.transform == body.transform)
        {
            Vector2 scale = body.transform.lossyScale;
            scale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            Vector2 centerLocal = Vector2.Scale(box.offset, scale);
            float halfLength = box.size.x * scale.x * 0.5f * (1f - settings.virtualSoleEndInset);
            float soleY = centerLocal.y - box.size.y * scale.y * 0.5f;
            Vector2 left = pose.position + Rotate(
                new Vector2(centerLocal.x - halfLength, soleY), pose.rotation);
            Vector2 right = pose.position + Rotate(
                new Vector2(centerLocal.x + halfLength, soleY), pose.rotation);
            return Mathf.Min(left.y, right.y);
        }
        return GetColliderBottomAtPose(body, collider, pose);
    }

    private static float GetColliderBottomAtPose(
        Rigidbody2D body,
        Collider2D collider,
        KinematicPose pose)
    {
        if (collider is BoxCollider2D box && collider.transform == body.transform)
        {
            Vector2 scale = body.transform.lossyScale;
            scale = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            Vector2 center = pose.position
                           + Rotate(Vector2.Scale(box.offset, scale), pose.rotation);
            Vector2 halfSize = Vector2.Scale(box.size * 0.5f, scale);
            float radians = pose.rotation * Mathf.Deg2Rad;
            float verticalExtent = Mathf.Abs(Mathf.Sin(radians)) * halfSize.x
                                 + Mathf.Abs(Mathf.Cos(radians)) * halfSize.y;
            return center.y - verticalExtent;
        }
        return pose.position.y + collider.bounds.min.y - body.position.y;
    }

    private static Vector2 GetCurrentAnkle(Binding footBinding)
    {
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        return foot.position + Rotate(footBinding.joint.anchor, foot.rotation);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(cos * vector.x - sin * vector.y, sin * vector.x + cos * vector.y);
    }
}
