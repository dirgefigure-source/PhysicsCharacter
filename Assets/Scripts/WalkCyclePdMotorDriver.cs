using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public sealed class WalkCyclePdMotorDriver : MonoBehaviour
{
    private enum MotionState
    {
        Idle,
        Walk,
        Run
    }

    [Serializable]
    private sealed class Binding
    {
        public string bodyName;
        public string jsonJoint;
        [NonSerialized] public HingeJoint2D joint;
        [NonSerialized] public float offset;
        [NonSerialized] public float restJointAngle;
        [NonSerialized] public float hingeReference;
        [NonSerialized] public float transitionStartAngle;
        [NonSerialized] public float lastTargetAngle;
    }

    [Header("Animation")]
    [FormerlySerializedAs("walkCycleJson")]
    [SerializeField] private TextAsset walkMotionJson;
    [SerializeField] private TextAsset runMotionJson;
    [Header("Limb Motion Layer")]
    [SerializeField] private MotionLayerDefinition punchLayer = new()
    {
        layerName = "Punch",
        targetLimbs = LimbMask.RightArm
    };

    [Header("Locomotion")]
    [FormerlySerializedAs("playbackSpeed")]
    [SerializeField, Min(0.01f)] private float walkPlaybackSpeed = 1f;
    [SerializeField, Min(0.01f)] private float runPlaybackSpeed = 1f;
    [Tooltip("Kinematic FK: the complete character follows the sampled animation pose.")]
    [SerializeField] private bool animatedMode = true;
    [FormerlySerializedAs("animatedMoveSpeed")]
    [SerializeField, Min(0f)] private float walkMoveSpeed = 2f;
    [SerializeField, Min(0f)] private float runMoveSpeed = 4f;
    [Tooltip("Seconds used to blend joint angles and speed between Walk and Run.")]
    [SerializeField, Min(0.01f)] private float locomotionTransitionDuration = 0.15f;
    [Tooltip("The camera views the XY plane from -Z, opposite atan2's +Z viewpoint.")]
    [SerializeField] private float projectionAngleSign = -1f;

    [Header("Animated Grounding")]
    [Tooltip("Layers that the animated feet may use as ground. Ground is currently on Default.")]
    [SerializeField] private LayerMask groundLayers = 1;
    [SerializeField, Min(0f)] private float groundProbeHeight = 1f;
    [SerializeField, Min(0.01f)] private float groundProbeDistance = 5f;
    [SerializeField, Min(0f)] private float footGroundClearance = 0.01f;
    [Tooltip("World Z angle at which the one-piece foot artwork has a level sole.")]
    [SerializeField] private float plantedFootWorldAngle;
    [Tooltip("How quickly a support foot blends from the FK angle to a level sole.")]
    [SerializeField, Min(0.01f)] private float footPlantBlendSpeed = 10f;
    [Tooltip("Shortens the virtual sole at both ends to avoid balancing on a sprite tip.")]
    [SerializeField, Range(0f, 0.45f)] private float virtualSoleEndInset = 0.12f;

    [Header("Support Leg IK")]
    [Tooltip("Keeps Body Y fixed and corrects only the current support leg. Disable to restore the previous FK grounding.")]
    [SerializeField] private bool supportLegIkEnabled = true;
    [Tooltip("Offsets the fixed Body height captured when the component initializes. Negative values bend the knees more.")]
    [SerializeField] private float fixedBodyHeightOffset;
    [Tooltip("The other ankle must be this much closer to the ground before support changes sides.")]
    [SerializeField, Min(0f)] private float supportSwitchHysteresis = 0.05f;
    [SerializeField, Min(0.01f)] private float supportIkBlendSpeed = 8f;
    [Tooltip("Converts the rotated sole's extra drop into a higher ankle target, which is absorbed by hip and knee bend.")]
    [SerializeField, Range(0f, 1f)] private float penetrationToKnee = 1f;
    [Tooltip("Safety cap for the vertical correction transferred into one IK solve.")]
    [SerializeField, Min(0f)] private float maxPenetrationToKnee = 0.3f;
    [Tooltip("Applies the same hip/knee correction to the swing leg only while its sole is below ground.")]
    [SerializeField] private bool preventSwingFootPenetration = true;
    [SerializeField, Range(0f, 1f)] private float swingFootClearanceIkWeight = 1f;
    [SerializeField, Min(0f)] private float swingFootPenetrationTolerance;
    [SerializeField] private bool logUnreachableIkTargets = true;

    [Header("Input System")]
    [SerializeField] private InputAction walkAction = new(
        name: "Walk",
        type: InputActionType.Button,
        binding: "<Keyboard>/d");
    [SerializeField] private InputAction runAction = new(
        name: "Run",
        type: InputActionType.Button,
        binding: "<Keyboard>/leftShift");
    [SerializeField] private InputAction punchAction = new(
        name: "Punch",
        type: InputActionType.Button,
        binding: "<Mouse>/leftButton");
    [Tooltip("Continuous world +X force applied to Body while D is held.")]
    [SerializeField, Min(0f)] private float moveForce = 50f;

    [Header("Stop Transition")]
    [Tooltip("Seconds used to blend the animated limbs back to their startup joint angles after D is released.")]
    [SerializeField, Min(0.01f)] private float returnToRestDuration = 0.3f;
    [Tooltip("Stable knee bend side used during starts and stops. +1 bends forward for this +X-facing character.")]
    [SerializeField] private float stoppingKneeBendSign = 1f;

    [Header("PD Motor")]
    [SerializeField, Min(0f)] private float proportionalGain = 12f;
    [SerializeField, Min(0f)] private float derivativeGain = 0.8f;
    [SerializeField, Min(0f)] private float maxMotorSpeed = 500f;
    [SerializeField, Min(0f)] private float maxMotorTorque = 1000f;
    [SerializeField] private bool clampToJointLimits = true;
    [SerializeField] private bool logLimitWarnings = true;

    [Header("Upright Balance")]
    [Tooltip("When enabled, applies a PD torque to keep the central Body upright.")]
    [SerializeField] private bool uprightEnabled;
    [SerializeField] private float uprightWorldAngle;
    [SerializeField, Min(0f)] private float uprightProportionalGain = 80f;
    [SerializeField, Min(0f)] private float uprightDerivativeGain = 14f;
    [SerializeField, Min(0f)] private float maxUprightAngularAcceleration = 2000f;
    [Tooltip("Continuous force applied to Body along world +Y while Upright Enabled is true.")]
    [SerializeField, Min(0f)] private float uprightForce;

    [Header("Runtime")]
    [SerializeField] private MotionState motionState = MotionState.Idle;
    [SerializeField] private bool isPlaying;
    [SerializeField, Range(0f, 1f)] private float normalizedTime;

    private readonly Binding[] bindings =
    {
        new() { bodyName = "Head" },
        new() { bodyName = "LeftUpperArm", jsonJoint = "leftUpperArm" },
        new() { bodyName = "LeftForearm", jsonJoint = "leftLowerArm" },
        new() { bodyName = "LeftHand" },
        new() { bodyName = "RightUpperArm", jsonJoint = "rightUpperArm" },
        new() { bodyName = "RightForearm", jsonJoint = "rightLowerArm" },
        new() { bodyName = "RightHand" },
        new() { bodyName = "LeftThigh", jsonJoint = "leftUpperLeg" },
        new() { bodyName = "LeftCalf", jsonJoint = "leftLowerLeg" },
        new() { bodyName = "LeftFoot", jsonJoint = "leftFoot" },
        new() { bodyName = "RightThigh", jsonJoint = "rightUpperLeg" },
        new() { bodyName = "RightCalf", jsonJoint = "rightLowerLeg" },
        new() { bodyName = "RightFoot", jsonJoint = "rightFoot" },
    };

    private MotionJsonClip activeClip;
    private MotionJsonClip walkClip;
    private MotionJsonClip runClip;
    private MotionJsonClip punchClip;
    private readonly Dictionary<Rigidbody2D, KinematicPose> kinematicPoses = new();
    private Binding leftThighBinding;
    private Binding leftCalfBinding;
    private Binding leftFootBinding;
    private Binding rightThighBinding;
    private Binding rightCalfBinding;
    private Binding rightFootBinding;
    private Rigidbody2D centralBody;
    private Rigidbody2D leftFootBody;
    private Rigidbody2D rightFootBody;
    private Collider2D leftFootCollider;
    private Collider2D rightFootCollider;
    private RigidbodyConstraints2D leftFootOriginalConstraints;
    private RigidbodyConstraints2D rightFootOriginalConstraints;
    private Rigidbody2D[] allBodies;
    private RigidbodyType2D[] originalBodyTypes;
    private float elapsed;
    private float locomotionTransitionElapsed;
    private float transitionStartMoveSpeed;
    private float currentMoveSpeed;
    private bool punchRequested;
    private MotionLayerPlayer punchLayerPlayer;
    private float leftFootPlantWeight;
    private float rightFootPlantWeight;
    private float lockedBodyWorldY;
    private int supportLeg;
    private bool leftIkReachWarningIssued;
    private bool rightIkReachWarningIssued;
    private float leftKneeBendSign;
    private float rightKneeBendSign;
    private float walkPoseWeight;
    private bool stoppingFootLockActive;
    private int stoppingSupportLeg;
    private Vector2 stoppingSupportAnkle;
    private Vector2 defaultBodyWorldPosition;
    private Vector2 defaultLeftAnkle;
    private Vector2 defaultRightAnkle;
    private Vector2 stoppingBodyStart;
    private Vector2 stoppingBodyTarget;
    private bool initialized;

    private readonly struct KinematicPose
    {
        public readonly Vector2 position;
        public readonly float rotation;

        public KinematicPose(Vector2 position, float rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }

    private void Awake()
    {
        motionState = MotionState.Idle;
        isPlaying = false;
        Initialize();
        SetMotorsEnabled(false);
    }

    private void OnEnable()
    {
        punchAction.performed += OnPunchPerformed;
        walkAction.Enable();
        runAction.Enable();
        punchAction.Enable();
    }

    private void OnDisable()
    {
        punchAction.performed -= OnPunchPerformed;
        walkAction.Disable();
        runAction.Disable();
        punchAction.Disable();
        punchRequested = false;
        punchLayerPlayer?.Reset();
        motionState = MotionState.Idle;
        isPlaying = false;
        if (initialized) SetMotorsEnabled(false);
        if (leftFootBody != null)
            leftFootBody.constraints = leftFootOriginalConstraints;
        if (rightFootBody != null)
            rightFootBody.constraints = rightFootOriginalConstraints;
        RestoreBodyTypes();
    }

    private void OnPunchPerformed(InputAction.CallbackContext context)
    {
        punchRequested = true;
    }

    private void StartPunchLayer()
    {
        punchLayerPlayer.Play();
    }

    private void UpdatePunchLayer()
    {
        punchLayerPlayer.Tick(Time.fixedDeltaTime, punchClip.DurationSeconds);
    }

    public void Initialize()
    {
        punchLayer ??= new MotionLayerDefinition
        {
            layerName = "Punch",
            targetLimbs = LimbMask.RightArm
        };
        walkClip = MotionJsonClip.Parse(walkMotionJson, "Walk");
        runClip = MotionJsonClip.Parse(runMotionJson, "Run");
        punchClip = MotionJsonClip.Parse(punchLayer.motionJson, punchLayer.layerName);
        punchLayerPlayer = new MotionLayerPlayer(punchLayer);
        activeClip = motionState == MotionState.Run ? runClip : walkClip;

        Transform bodyTransform = FindDescendant(transform, "Body");
        if (bodyTransform == null || !bodyTransform.TryGetComponent(out centralBody))
            throw new InvalidOperationException("Player central body 'Body' has no Rigidbody2D.");

        foreach (Binding binding in bindings)
        {
            Transform body = FindDescendant(transform, binding.bodyName);
            if (body == null || !body.TryGetComponent(out binding.joint))
                throw new InvalidOperationException($"Player body '{binding.bodyName}' has no HingeJoint2D.");

            binding.restJointAngle = binding.joint.jointAngle;
            binding.lastTargetAngle = binding.restJointAngle;
            binding.transitionStartAngle = binding.restJointAngle;
            float childRotation = binding.joint.attachedRigidbody.rotation;
            float parentRotation = binding.joint.connectedBody != null
                ? binding.joint.connectedBody.rotation
                : 0f;
            // HingeJoint2D jointAngle uses the opposite sign from the child's
            // world Z rotation relative to its connected body.
            binding.hingeReference = Mathf.DeltaAngle(parentRotation, childRotation)
                                   + binding.joint.jointAngle;
            if (!string.IsNullOrEmpty(binding.jsonJoint))
            {
                walkClip.GetAngles(binding.jsonJoint);
                runClip.GetAngles(binding.jsonJoint);
                punchClip.GetAngles(binding.jsonJoint);
                binding.offset = CalculateStaticAxisOffset(binding);
                WarnIfLimitsCannotRepresentCycle(binding, walkClip, "Walk");
                WarnIfLimitsCannotRepresentCycle(binding, runClip, "Run");
                if (LimbMaskUtility.ContainsJoint(punchLayer.targetLimbs, binding.jsonJoint))
                    WarnIfLimitsCannotRepresentCycle(
                        binding, punchClip, punchLayer.layerName);
            }
            binding.joint.useMotor = false;
        }

        leftThighBinding = FindBinding("LeftThigh");
        leftCalfBinding = FindBinding("LeftCalf");
        leftFootBinding = FindBinding("LeftFoot");
        rightThighBinding = FindBinding("RightThigh");
        rightCalfBinding = FindBinding("RightCalf");
        rightFootBinding = FindBinding("RightFoot");

        Transform leftFootTransform = FindDescendant(transform, "LeftFoot");
        if (leftFootTransform == null || !leftFootTransform.TryGetComponent(out leftFootBody))
            throw new InvalidOperationException("Player body 'LeftFoot' has no Rigidbody2D.");
        if (!leftFootTransform.TryGetComponent(out leftFootCollider))
            throw new InvalidOperationException("Player body 'LeftFoot' has no Collider2D.");
        leftFootOriginalConstraints = leftFootBody.constraints;

        Transform rightFootTransform = FindDescendant(transform, "RightFoot");
        if (rightFootTransform == null || !rightFootTransform.TryGetComponent(out rightFootBody))
            throw new InvalidOperationException("Player body 'RightFoot' has no Rigidbody2D.");
        if (!rightFootTransform.TryGetComponent(out rightFootCollider))
            throw new InvalidOperationException("Player body 'RightFoot' has no Collider2D.");
        rightFootOriginalConstraints = rightFootBody.constraints;

        defaultBodyWorldPosition = centralBody.position;
        defaultLeftAnkle = GetCurrentAnkle(leftFootBinding);
        defaultRightAnkle = GetCurrentAnkle(rightFootBinding);

        allBodies = GetComponentsInChildren<Rigidbody2D>(true);
        originalBodyTypes = new RigidbodyType2D[allBodies.Length];
        for (int i = 0; i < allBodies.Length; i++)
            originalBodyTypes[i] = allBodies[i].bodyType;
        ApplyCharacterMode();

        elapsed = 0f;
        normalizedTime = 0f;
        leftFootPlantWeight = 0f;
        rightFootPlantWeight = 0f;
        lockedBodyWorldY = centralBody.position.y;
        supportLeg = 0;
        leftIkReachWarningIssued = false;
        rightIkReachWarningIssued = false;
        leftKneeBendSign = 0f;
        rightKneeBendSign = 0f;
        walkPoseWeight = 0f;
        stoppingFootLockActive = false;
        stoppingSupportLeg = 0;
        stoppingSupportAnkle = Vector2.zero;
        stoppingBodyStart = centralBody.position;
        stoppingBodyTarget = centralBody.position;
        locomotionTransitionElapsed = locomotionTransitionDuration;
        transitionStartMoveSpeed = 0f;
        currentMoveSpeed = 0f;
        punchLayerPlayer.Reset();
        initialized = true;
        if (animatedMode && !isPlaying)
            BeginStopTransition();
    }

    private void FixedUpdate()
    {
        if (!initialized) return;

        bool punchPressed = punchRequested;
        punchRequested = false;
        if (punchPressed) StartPunchLayer();
        UpdatePunchLayer();

        MotionState desiredState = !walkAction.IsPressed()
            ? MotionState.Idle
            : runAction.IsPressed() ? MotionState.Run : MotionState.Walk;
        if (desiredState != motionState)
            ChangeMotionState(desiredState);

        if (animatedMode)
        {
            if (isPlaying)
            {
                UpdateAnimationTime();
                walkPoseWeight = Mathf.MoveTowards(
                    walkPoseWeight, 1f, Time.fixedDeltaTime / locomotionTransitionDuration);
                UpdateLocomotionTransition();
            }
            else
            {
                walkPoseWeight = Mathf.MoveTowards(
                    walkPoseWeight, 0f, Time.fixedDeltaTime / returnToRestDuration);
                if (walkPoseWeight <= 0f)
                {
                    elapsed = 0f;
                    normalizedTime = 0f;
                }
            }
            DriveKinematicPose();
            return;
        }

        if (!isPlaying) return;

        ApplyUprightBalance();

        if (motionState == MotionState.Walk || motionState == MotionState.Run)
            centralBody.AddForce(Vector2.right * moveForce, ForceMode2D.Force);
        UpdateAnimationTime();

        foreach (Binding binding in bindings)
        {
            float target = binding.restJointAngle;
            if (!string.IsNullOrEmpty(binding.jsonJoint))
            {
                float source = activeClip.Sample(binding.jsonJoint, normalizedTime);
                target = NormalizeJointAngle(binding.offset + projectionAngleSign * source);
                target = BlendLocomotionTarget(binding, target);
            }
            target = ApplyLimbMotionLayer(binding, target);
            binding.lastTargetAngle = target;

            if (clampToJointLimits && binding.joint.useLimits)
            {
                JointAngleLimits2D limits = binding.joint.limits;
                target = Mathf.Clamp(target, limits.min, limits.max);
            }

            float error = Mathf.DeltaAngle(binding.joint.jointAngle, target);
            float parentAngularVelocity = binding.joint.connectedBody != null
                ? binding.joint.connectedBody.angularVelocity
                : 0f;
            float relativeAngularVelocity =
                binding.joint.attachedRigidbody.angularVelocity - parentAngularVelocity;
            float speed = proportionalGain * error
                          - derivativeGain * relativeAngularVelocity;

            JointMotor2D motor = binding.joint.motor;
            motor.motorSpeed = Mathf.Clamp(speed, -maxMotorSpeed, maxMotorSpeed);
            motor.maxMotorTorque = maxMotorTorque;
            binding.joint.motor = motor;
        }
    }

    private void UpdateAnimationTime()
    {
        float nextTime = normalizedTime + Time.fixedDeltaTime *
            GetPlaybackSpeed(motionState) / activeClip.DurationSeconds;
        normalizedTime = Mathf.Repeat(nextTime, 1f);
        elapsed = normalizedTime * activeClip.DurationSeconds;
    }

    private void ChangeMotionState(MotionState nextState)
    {
        MotionState previousState = motionState;
        motionState = nextState;
        isPlaying = nextState != MotionState.Idle;
        SetMotorsEnabled(!animatedMode && isPlaying);

        if (nextState == MotionState.Idle)
        {
            currentMoveSpeed = 0f;
            if (animatedMode) BeginStopTransition();
            return;
        }

        foreach (Binding binding in bindings)
            binding.transitionStartAngle = binding.lastTargetAngle;

        activeClip = GetMotionClip(nextState);
        elapsed = normalizedTime * activeClip.DurationSeconds;
        transitionStartMoveSpeed = previousState == MotionState.Idle
            ? 0f
            : currentMoveSpeed;
        locomotionTransitionElapsed = 0f;

        if (animatedMode && previousState == MotionState.Idle)
        {
            stoppingFootLockActive = false;
            stoppingSupportLeg = 0;
            // The rest pose is nearly straight, so deriving the IK branch from
            // its tiny transient knee offset is numerically unstable and can
            // choose the backward-bending solution for the first few frames.
            // Both source motions keep one consistent forward bend branch.
            float startBendSign = GetPreferredKneeBendSign();
            leftKneeBendSign = startBendSign;
            rightKneeBendSign = startBendSign;
        }
    }

    private void UpdateLocomotionTransition()
    {
        locomotionTransitionElapsed = Mathf.Min(
            locomotionTransitionElapsed + Time.fixedDeltaTime,
            locomotionTransitionDuration);
        float blend = GetLocomotionBlend();
        currentMoveSpeed = Mathf.Lerp(
            transitionStartMoveSpeed,
            GetMoveSpeed(motionState),
            blend);
    }

    private float BlendLocomotionTarget(Binding binding, float target)
    {
        float blend = GetLocomotionBlend();
        return blend >= 1f
            ? target
            : Mathf.LerpAngle(binding.transitionStartAngle, target, blend);
    }

    private float GetLocomotionBlend()
    {
        float linear = Mathf.Clamp01(
            locomotionTransitionElapsed / locomotionTransitionDuration);
        return linear * linear * (3f - 2f * linear);
    }

    private float GetPlaybackSpeed(MotionState state) =>
        state == MotionState.Run ? runPlaybackSpeed : walkPlaybackSpeed;

    private float GetMoveSpeed(MotionState state) => state switch
    {
        MotionState.Run => runMoveSpeed,
        MotionState.Walk => walkMoveSpeed,
        _ => 0f
    };

    private MotionJsonClip GetMotionClip(MotionState state) => state switch
    {
        MotionState.Run => runClip,
        _ => walkClip
    };

    private float ApplyLimbMotionLayer(Binding binding, float baseAngle)
    {
        if (punchLayerPlayer.Weight <= 0f || string.IsNullOrEmpty(binding.jsonJoint) ||
            !LimbMaskUtility.ContainsJoint(
                punchLayer.targetLimbs, binding.jsonJoint)) return baseAngle;

        float source = punchClip.Sample(
            binding.jsonJoint,
            punchLayerPlayer.NormalizedTime);
        float layerAngle = NormalizeJointAngle(
            binding.offset + projectionAngleSign * source);
        return Mathf.LerpAngle(baseAngle, layerAngle, punchLayerPlayer.Weight);
    }

    private void BeginStopTransition()
    {
        float stopBendSign = GetPreferredKneeBendSign();
        leftKneeBendSign = stopBendSign;
        rightKneeBendSign = stopBendSign;

        // Re-evaluate contact at the exact release pose. During a support-leg
        // handoff, supportLeg and the plant weights can still refer to the foot
        // that has already started lifting. Locking that airborne ankle would
        // translate the final rest pose upward by the same amount.
        bool hasLeftGround = TryGetAnkleGroundTarget(
            leftFootBinding, leftFootCollider,
            out Vector2 leftGroundAnkle, out float leftGroundScore, out _);
        bool hasRightGround = TryGetAnkleGroundTarget(
            rightFootBinding, rightFootCollider,
            out Vector2 rightGroundAnkle, out float rightGroundScore, out _);

        if (hasLeftGround || hasRightGround)
        {
            stoppingSupportLeg = hasLeftGround &&
                (!hasRightGround || leftGroundScore <= rightGroundScore)
                ? -1
                : 1;
        }
        else
        {
            // Startup fallback: kinematicPoses is not populated until the first
            // driven frame, so preserve the original selection behavior.
            stoppingSupportLeg = supportLeg;
            if (stoppingSupportLeg == 0)
                stoppingSupportLeg = leftFootPlantWeight >= rightFootPlantWeight ? -1 : 1;
        }

        Binding footBinding = stoppingSupportLeg == -1
            ? leftFootBinding
            : rightFootBinding;
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
                : foot.position + Rotate(footBinding.joint.anchor, foot.rotation);
        Vector2 defaultSupportAnkle = stoppingSupportLeg == -1
            ? defaultLeftAnkle
            : defaultRightAnkle;
        stoppingBodyStart = centralBody.position;
        stoppingBodyTarget = defaultBodyWorldPosition
                           + (stoppingSupportAnkle - defaultSupportAnkle);
        stoppingFootLockActive = true;
    }

    private float GetPreferredKneeBendSign() =>
        Mathf.Approximately(stoppingKneeBendSign, 0f)
            ? 1f
            : Mathf.Sign(stoppingKneeBendSign);

    private static Vector2 GetCurrentAnkle(Binding footBinding)
    {
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        return foot.position + Rotate(footBinding.joint.anchor, foot.rotation);
    }

    private void DriveKinematicPose()
    {
        Vector2 bodyTarget = centralBody.position;
        if (isPlaying)
            bodyTarget += Vector2.right * currentMoveSpeed * Time.fixedDeltaTime;
        else if (stoppingFootLockActive)
        {
            float returnProgress = 1f - walkPoseWeight;
            float smoothProgress = returnProgress * returnProgress * (3f - 2f * returnProgress);
            bodyTarget = Vector2.Lerp(stoppingBodyStart, stoppingBodyTarget, smoothProgress);
        }
        if (supportLegIkEnabled && !stoppingFootLockActive)
            bodyTarget.y = lockedBodyWorldY + fixedBodyHeightOffset;
        kinematicPoses.Clear();
        kinematicPoses[centralBody] = new KinematicPose(bodyTarget, uprightWorldAngle);

        foreach (Binding binding in bindings)
        {
            float targetJointAngle = binding.restJointAngle;
            if (!string.IsNullOrEmpty(binding.jsonJoint))
            {
                float source = activeClip.Sample(binding.jsonJoint, normalizedTime);
                float animatedJointAngle = NormalizeJointAngle(
                    binding.offset + projectionAngleSign * source);
                animatedJointAngle = BlendLocomotionTarget(binding, animatedJointAngle);
                targetJointAngle = Mathf.LerpAngle(
                    binding.restJointAngle, animatedJointAngle, walkPoseWeight);
            }
            targetJointAngle = ApplyLimbMotionLayer(binding, targetJointAngle);
            binding.lastTargetAngle = targetJointAngle;

            Rigidbody2D parent = binding.joint.connectedBody;
            Rigidbody2D child = binding.joint.attachedRigidbody;
            KinematicPose parentPose = new KinematicPose(Vector2.zero, 0f);
            if (parent == null)
                parentPose = new KinematicPose(Vector2.zero, 0f);
            else if (!kinematicPoses.TryGetValue(parent, out parentPose))
                parentPose = new KinematicPose(parent.position, parent.rotation);

            float childRotation = parentPose.rotation
                                + binding.hingeReference - targetJointAngle;
            Vector2 parentAnchor = parentPose.position
                                 + Rotate(binding.joint.connectedAnchor, parentPose.rotation);
            Vector2 rotatedChildAnchor = Rotate(binding.joint.anchor, childRotation);
            kinematicPoses[child] = new KinematicPose(
                parentAnchor - rotatedChildAnchor,
                childRotation);
        }

        if (supportLegIkEnabled)
        {
            ApplySupportLegIk();
        }
        else
        {
            ApplySupportFootFlattening();
            ApplyAnimatedGroundCorrection();
        }

        foreach (KeyValuePair<Rigidbody2D, KinematicPose> item in kinematicPoses)
        {
            item.Key.MovePosition(item.Value.position);
            item.Key.MoveRotation(item.Value.rotation);
        }
    }

    private void ApplySupportLegIk()
    {
        Binding leftThigh = leftThighBinding;
        Binding leftCalf = leftCalfBinding;
        Binding leftFoot = leftFootBinding;
        Binding rightThigh = rightThighBinding;
        Binding rightCalf = rightCalfBinding;
        Binding rightFoot = rightFootBinding;

        if (stoppingFootLockActive)
        {
            ApplyStoppedPoseIk(
                leftThigh, leftCalf, leftFoot,
                rightThigh, rightCalf, rightFoot);
            return;
        }

        bool hasLeft = TryGetAnkleGroundTarget(
            leftFoot, leftFootCollider, out Vector2 leftTarget, out float leftScore,
            out bool leftRawPenetrating);
        bool hasRight = TryGetAnkleGroundTarget(
            rightFoot, rightFootCollider, out Vector2 rightTarget, out float rightScore,
            out bool rightRawPenetrating);

        if (supportLeg == -1 && (!hasLeft ||
            (hasRight && rightScore + supportSwitchHysteresis < leftScore)))
            supportLeg = hasRight ? 1 : 0;
        else if (supportLeg == 1 && (!hasRight ||
            (hasLeft && leftScore + supportSwitchHysteresis < rightScore)))
            supportLeg = hasLeft ? -1 : 0;
        else if (supportLeg == 0)
            supportLeg = hasLeft && (!hasRight || leftScore <= rightScore) ? -1 : hasRight ? 1 : 0;

        float blendStep = supportIkBlendSpeed * Time.fixedDeltaTime;
        leftFootPlantWeight = Mathf.MoveTowards(
            leftFootPlantWeight, supportLeg == -1 ? 1f : 0f, blendStep);
        rightFootPlantWeight = Mathf.MoveTowards(
            rightFootPlantWeight, supportLeg == 1 ? 1f : 0f, blendStep);

        bool leftPenetrating = hasLeft && preventSwingFootPenetration && leftRawPenetrating;
        if (hasLeft && (leftFootPlantWeight > 0f || leftPenetrating))
        {
            float leftIkWeight = leftPenetrating
                ? Mathf.Max(leftFootPlantWeight, swingFootClearanceIkWeight)
                : leftFootPlantWeight;
            SolveLegToGround(leftThigh, leftCalf, leftFoot, leftFootCollider, leftTarget,
                leftIkWeight, leftFootPlantWeight, leftPenetrating,
                ref leftIkReachWarningIssued, ref leftKneeBendSign);
        }

        bool rightPenetrating = hasRight && preventSwingFootPenetration && rightRawPenetrating;
        if (hasRight && (rightFootPlantWeight > 0f || rightPenetrating))
        {
            float rightIkWeight = rightPenetrating
                ? Mathf.Max(rightFootPlantWeight, swingFootClearanceIkWeight)
                : rightFootPlantWeight;
            SolveLegToGround(rightThigh, rightCalf, rightFoot, rightFootCollider, rightTarget,
                rightIkWeight, rightFootPlantWeight, rightPenetrating,
                ref rightIkReachWarningIssued, ref rightKneeBendSign);
        }
    }

    private void ApplyStoppedPoseIk(
        Binding leftThigh,
        Binding leftCalf,
        Binding leftFoot,
        Binding rightThigh,
        Binding rightCalf,
        Binding rightFoot)
    {
        // At the end of the transition the translated startup FK pose already
        // places the support ankle exactly at its lock point. Leaving it
        // untouched preserves the perfectly straight authored idle stance.
        if (walkPoseWeight <= 0.0001f) return;

        bool lockLeft = stoppingSupportLeg == -1;
        Binding supportThigh = lockLeft ? leftThigh : rightThigh;
        Binding supportCalf = lockLeft ? leftCalf : rightCalf;
        Binding supportFoot = lockLeft ? leftFoot : rightFoot;
        Collider2D supportCollider = lockLeft ? leftFootCollider : rightFootCollider;

        // The support ankle remains at the release-frame world position. The
        // hip and knee are solved around it while every other joint returns to rest.
        if (lockLeft)
        {
            leftFootPlantWeight = 1f;
            rightFootPlantWeight = 0f;
            supportLeg = -1;
        }
        else
        {
            leftFootPlantWeight = 0f;
            rightFootPlantWeight = 1f;
            supportLeg = 1;
        }

        if (lockLeft)
        {
            SolveLegToGround(
                supportThigh, supportCalf, supportFoot, supportCollider,
                stoppingSupportAnkle, 1f, 1f, false,
                ref leftIkReachWarningIssued, ref leftKneeBendSign);
        }
        else
        {
            SolveLegToGround(
                supportThigh, supportCalf, supportFoot, supportCollider,
                stoppingSupportAnkle, 1f, 1f, false,
                ref rightIkReachWarningIssued, ref rightKneeBendSign);
        }

        Binding swingThigh = lockLeft ? rightThigh : leftThigh;
        Binding swingCalf = lockLeft ? rightCalf : leftCalf;
        Binding swingFoot = lockLeft ? rightFoot : leftFoot;
        Collider2D swingCollider = lockLeft ? rightFootCollider : leftFootCollider;
        bool hasGround = TryGetAnkleGroundTarget(
            swingFoot, swingCollider, out Vector2 swingTarget, out _,
            out bool swingPenetrating);
        if (!hasGround || !preventSwingFootPenetration || !swingPenetrating) return;

        if (lockLeft)
        {
            SolveLegToGround(
                swingThigh, swingCalf, swingFoot, swingCollider, swingTarget,
                swingFootClearanceIkWeight, 0f, true,
                ref rightIkReachWarningIssued, ref rightKneeBendSign);
        }
        else
        {
            SolveLegToGround(
                swingThigh, swingCalf, swingFoot, swingCollider, swingTarget,
                swingFootClearanceIkWeight, 0f, true,
                ref leftIkReachWarningIssued, ref leftKneeBendSign);
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
        if (!kinematicPoses.TryGetValue(foot, out KinematicPose footPose)) return false;

        Vector2 rawAnkle = footPose.position
                         + Rotate(footBinding.joint.anchor, footPose.rotation);
        Vector2 origin = rawAnkle + Vector2.up * groundProbeHeight;
        RaycastHit2D hit = Physics2D.Raycast(
            origin, Vector2.down, groundProbeHeight + groundProbeDistance, groundLayers);
        if (hit.collider == null) return false;

        KinematicPose flatFootAtZeroAnkle = new KinematicPose(
            -Rotate(footBinding.joint.anchor, plantedFootWorldAngle),
            plantedFootWorldAngle);
        float soleBelowAnkle = GetVirtualSoleBottomAtPose(
            foot, footCollider, flatFootAtZeroAnkle);
        target = new Vector2(
            rawAnkle.x,
            hit.point.y + footGroundClearance - soleBelowAnkle);
        score = rawAnkle.y - target.y;
        float colliderBottom = GetColliderBottomAtPose(foot, footCollider, footPose);
        penetrating = hit.point.y + footGroundClearance - colliderBottom
                    > swingFootPenetrationTolerance;
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
        if (!kinematicPoses.TryGetValue(thigh, out KinematicPose rawThigh) ||
            !kinematicPoses.TryGetValue(calf, out KinematicPose rawCalf) ||
            !kinematicPoses.TryGetValue(foot, out KinematicPose rawFoot)) return;

        Rigidbody2D hipBody = thighBinding.joint.connectedBody;
        if (hipBody == null || !kinematicPoses.TryGetValue(hipBody, out KinematicPose bodyPose)) return;

        Vector2 hip = bodyPose.position
                    + Rotate(thighBinding.joint.connectedAnchor, bodyPose.rotation);
        Vector2 thighAxis = calfBinding.joint.connectedAnchor - thighBinding.joint.anchor;
        Vector2 calfAxis = footBinding.joint.connectedAnchor - calfBinding.joint.anchor;
        float upperLength = thighAxis.magnitude;
        float lowerLength = calfAxis.magnitude;
        if (upperLength <= 0.0001f || lowerLength <= 0.0001f) return;

        float footRotation = Mathf.LerpAngle(
            rawFoot.rotation, plantedFootWorldAngle, footPlantWeight);
        float flatSoleBelowAnkle = GetSoleBelowAnkleAtRotation(
            footBinding, footCollider, plantedFootWorldAngle);
        float rotatedSoleBelowAnkle = enforceFullColliderClearance
            ? GetColliderBelowAnkleAtRotation(footBinding, footCollider, footRotation)
            : GetSoleBelowAnkleAtRotation(footBinding, footCollider, footRotation);
        float extraSoleDrop = Mathf.Max(0f, flatSoleBelowAnkle - rotatedSoleBelowAnkle);
        float transferredHeight = Mathf.Min(
            extraSoleDrop * penetrationToKnee,
            maxPenetrationToKnee);
        // Body stays fixed. Raising the ankle target shortens the hip-to-ankle
        // distance, so the two-bone solve absorbs this height in hip/knee bend.
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
        if (unreachable && logUnreachableIkTargets && !reachWarningIssued)
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
        kinematicPoses[thigh] = new KinematicPose(thighPosition, thighRotation);
        kinematicPoses[calf] = new KinematicPose(calfPosition, calfRotation);
        kinematicPoses[foot] = new KinematicPose(footPosition, footRotation);
    }

    private float GetSoleBelowAnkleAtRotation(
        Binding footBinding,
        Collider2D footCollider,
        float footRotation)
    {
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        KinematicPose footAtZeroAnkle = new KinematicPose(
            -Rotate(footBinding.joint.anchor, footRotation),
            footRotation);
        return GetVirtualSoleBottomAtPose(foot, footCollider, footAtZeroAnkle);
    }

    private static float GetColliderBelowAnkleAtRotation(
        Binding footBinding,
        Collider2D footCollider,
        float footRotation)
    {
        Rigidbody2D foot = footBinding.joint.attachedRigidbody;
        KinematicPose footAtZeroAnkle = new KinematicPose(
            -Rotate(footBinding.joint.anchor, footRotation),
            footRotation);
        return GetColliderBottomAtPose(foot, footCollider, footAtZeroAnkle);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private Binding FindBinding(string bodyName)
    {
        foreach (Binding binding in bindings)
            if (binding.bodyName == bodyName) return binding;
        throw new InvalidOperationException($"Binding '{bodyName}' is missing.");
    }

    private void ApplySupportFootFlattening()
    {
        bool hasLeft = TryGetFootGroundCorrection(
            leftFootBody, leftFootCollider, out float leftBottom, out _);
        bool hasRight = TryGetFootGroundCorrection(
            rightFootBody, rightFootCollider, out float rightBottom, out _);

        bool plantLeft = hasLeft && (!hasRight || leftBottom <= rightBottom);
        bool plantRight = hasRight && !plantLeft;
        float blendStep = footPlantBlendSpeed * Time.fixedDeltaTime;
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
            !kinematicPoses.TryGetValue(foot, out KinematicPose pose)) return;

        HingeJoint2D ankle = foot.GetComponent<HingeJoint2D>();
        if (ankle == null) return;

        // Preserve the FK ankle position while changing only the one-piece
        // foot angle. This avoids stretching or disconnecting the calf.
        Vector2 ankleWorld = pose.position + Rotate(ankle.anchor, pose.rotation);
        float flattenedRotation = Mathf.LerpAngle(
            pose.rotation, plantedFootWorldAngle, weight);
        Vector2 flattenedPosition = ankleWorld - Rotate(ankle.anchor, flattenedRotation);
        kinematicPoses[foot] = new KinematicPose(flattenedPosition, flattenedRotation);
    }

    private void ApplyAnimatedGroundCorrection()
    {
        bool hasLeft = TryGetFootGroundCorrection(
            leftFootBody, leftFootCollider, out float leftBottom, out float leftCorrection);
        bool hasRight = TryGetFootGroundCorrection(
            rightFootBody, rightFootCollider, out float rightBottom, out float rightCorrection);

        if (!hasLeft && !hasRight) return;

        // The lower valid foot is the support foot. Moving every FK target by
        // the same amount preserves all joint anchors and the authored pose.
        float correction = !hasRight || (hasLeft && leftBottom <= rightBottom)
            ? leftCorrection
            : rightCorrection;

        foreach (Rigidbody2D body in allBodies)
        {
            if (!kinematicPoses.TryGetValue(body, out KinematicPose pose)) continue;
            kinematicPoses[body] = new KinematicPose(
                pose.position + Vector2.up * correction,
                pose.rotation);
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
            !kinematicPoses.TryGetValue(foot, out KinematicPose pose)) return false;

        targetBottom = GetVirtualSoleBottomAtPose(foot, footCollider, pose);
        Vector2 origin = new Vector2(pose.position.x, targetBottom + groundProbeHeight);
        float distance = groundProbeHeight + groundProbeDistance;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, distance, groundLayers);
        if (hit.collider == null) return false;

        correction = hit.point.y + footGroundClearance - targetBottom;
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
            float halfLength = box.size.x * scale.x * 0.5f * (1f - virtualSoleEndInset);
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
            Vector2 center = pose.position + Rotate(Vector2.Scale(box.offset, scale), pose.rotation);
            Vector2 halfSize = Vector2.Scale(box.size * 0.5f, scale);
            float radians = pose.rotation * Mathf.Deg2Rad;
            float verticalExtent = Mathf.Abs(Mathf.Sin(radians)) * halfSize.x
                                 + Mathf.Abs(Mathf.Cos(radians)) * halfSize.y;
            return center.y - verticalExtent;
        }

        // Fallback for another Collider2D shape: retain its current bottom
        // offset from the Rigidbody position while evaluating the target pose.
        return pose.position.y + collider.bounds.min.y - body.position.y;
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(cos * vector.x - sin * vector.y, sin * vector.x + cos * vector.y);
    }

    private void ApplyUprightBalance()
    {
        if (!uprightEnabled || centralBody == null) return;

        centralBody.AddForce(Vector2.up * uprightForce, ForceMode2D.Force);

        float angleError = Mathf.DeltaAngle(centralBody.rotation, uprightWorldAngle);
        float angularAcceleration = uprightProportionalGain * angleError
                                  - uprightDerivativeGain * centralBody.angularVelocity;
        angularAcceleration = Mathf.Clamp(
            angularAcceleration,
            -maxUprightAngularAcceleration,
            maxUprightAngularAcceleration);

        // Rigidbody2D torque uses radians internally. Multiplying the requested
        // angular acceleration by inertia makes the response independent of mass distribution.
        float torque = angularAcceleration * Mathf.Deg2Rad * centralBody.inertia;
        centralBody.AddTorque(torque, ForceMode2D.Force);
    }

    private static float CalculateStaticAxisOffset(Binding binding)
    {
        // Limbs are authored along local -Y. In this Player artwork the feet's
        // proximal-to-distal visual bone axis is local -X.
        // These are visual bone axes, independent of collider/anchor asymmetry.
        float childAxisLocal = binding.bodyName.EndsWith("Foot", StringComparison.Ordinal)
            ? 180f
            : -90f;

        float parentAxisLocal = 90f; // Body/torso visual axis is local +Y.
        HingeJoint2D parentJoint = binding.joint.connectedBody != null
            ? binding.joint.connectedBody.GetComponent<HingeJoint2D>()
            : null;
        if (parentJoint != null)
        {
            parentAxisLocal = parentJoint.gameObject.name.Trim().EndsWith("Foot", StringComparison.Ordinal)
                ? 180f
                : -90f;
        }

        float childRotation = binding.joint.attachedRigidbody.rotation;
        float parentRotation = binding.joint.connectedBody != null
            ? binding.joint.connectedBody.rotation
            : 0f;
        float hingeReference = Mathf.DeltaAngle(parentRotation, childRotation)
                             - binding.joint.jointAngle;

        // desiredJoint = sourceChildVsParent - childLocalAxis + parentLocalAxis
        //                - the reference angle internal to HingeJoint2D.
        return Mathf.DeltaAngle(0f, parentAxisLocal - childAxisLocal - hingeReference);
    }

    private void WarnIfLimitsCannotRepresentCycle(
        Binding binding,
        MotionJsonClip motionClip,
        string motionName)
    {
        if (!logLimitWarnings || !binding.joint.useLimits) return;
        JointAngleLimits2D limits = binding.joint.limits;
        float minTarget = float.PositiveInfinity;
        float maxTarget = float.NegativeInfinity;
        foreach (float source in motionClip.GetAngles(binding.jsonJoint))
        {
            float target = NormalizeJointAngle(binding.offset + projectionAngleSign * source);
            minTarget = Mathf.Min(minTarget, target);
            maxTarget = Mathf.Max(maxTarget, target);
        }

        if (minTarget < limits.min || maxTarget > limits.max)
        {
            Debug.LogWarning(
                $"{motionName} retarget: '{binding.bodyName}' requires [{minTarget:F1}, {maxTarget:F1}] deg, " +
                $"but its HingeJoint2D limits are [{limits.min:F1}, {limits.max:F1}] deg. " +
                (clampToJointLimits ? "Targets outside this range will be clamped." : "Limit collision will prevent exact tracking."),
                binding.joint);
        }
    }

    private static float NormalizeJointAngle(float angle) => Mathf.DeltaAngle(0f, angle);

    private static Transform FindDescendant(Transform root, string name)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(candidate.name.Trim(), name, StringComparison.Ordinal)) return candidate;
        return null;
    }

    private void SetMotorsEnabled(bool enabled)
    {
        foreach (Binding binding in bindings)
        {
            if (binding.joint != null)
                binding.joint.useMotor = enabled;
        }
    }

    private void ApplyCharacterMode()
    {
        if (allBodies == null) return;

        for (int i = 0; i < allBodies.Length; i++)
        {
            Rigidbody2D body = allBodies[i];
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = animatedMode ? RigidbodyType2D.Kinematic : originalBodyTypes[i];
        }

        if (animatedMode)
        {
            leftFootBody.constraints = leftFootOriginalConstraints;
            rightFootBody.constraints = rightFootOriginalConstraints;
            SetMotorsEnabled(false);
        }
        else
        {
            leftFootBody.constraints = leftFootOriginalConstraints;
            rightFootBody.constraints = rightFootOriginalConstraints;
        }
    }

    private void RestoreBodyTypes()
    {
        if (allBodies == null || originalBodyTypes == null) return;
        for (int i = 0; i < allBodies.Length && i < originalBodyTypes.Length; i++)
            allBodies[i].bodyType = originalBodyTypes[i];
    }

    public void Play()
    {
        if (!initialized) Initialize();
        ChangeMotionState(MotionState.Walk);
    }
    public void Pause()
    {
        if (!initialized) return;
        ChangeMotionState(MotionState.Idle);
    }
    public bool UprightEnabled
    {
        get => uprightEnabled;
        set => uprightEnabled = value;
    }
    public void SetUprightEnabled(bool value) => uprightEnabled = value;
    public void SetNormalizedTime(float value)
    {
        if (!initialized) Initialize();
        normalizedTime = Mathf.Repeat(value, 1f);
        elapsed = normalizedTime * activeClip.DurationSeconds;
    }
    public void Restart()
    {
        elapsed = 0f;
        normalizedTime = 0f;
        if (!initialized) Initialize();
        ChangeMotionState(MotionState.Walk);
    }
}
