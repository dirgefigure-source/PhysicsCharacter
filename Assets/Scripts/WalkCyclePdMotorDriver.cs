using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Binding = CharacterRig2D.JointBinding;
using KinematicPose = CharacterRig2D.Pose2D;

public sealed class WalkCyclePdMotorDriver : MonoBehaviour
{
    private enum MotionState
    {
        Idle,
        Walk,
        Run
    }

    [Header("Animation")]
    [FormerlySerializedAs("walkCycleJson")]
    [SerializeField] private TextAsset walkMotionJson;
    [SerializeField] private TextAsset runMotionJson;
    [Header("Action Motion Layers")]
    [SerializeField] private ActionMotionDefinition[] actionLayers =
        new ActionMotionDefinition[0];

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
    [FormerlySerializedAs("walkAction")]
    [SerializeField] private InputAction moveRightAction = new(
        name: "Move Right",
        type: InputActionType.Button,
        binding: "<Keyboard>/d");
    [SerializeField] private InputAction moveLeftAction = new(
        name: "Move Left",
        type: InputActionType.Button,
        binding: "<Keyboard>/a");
    [SerializeField] private InputAction runAction = new(
        name: "Run",
        type: InputActionType.Button,
        binding: "<Keyboard>/leftShift");
    [Tooltip("Continuous horizontal force applied to Body while A or D is held.")]
    [SerializeField, Min(0f)] private float moveForce = 50f;

    [Header("Stop Transition")]
    [Tooltip("Seconds used to blend the animated limbs back to their startup joint angles after movement input is released.")]
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
    [SerializeField, HideInInspector] private int facingDirection = 1;

    private CharacterRig2D rig;
    private LegGroundingSolver2D groundingSolver;
    private LocomotionPlayer locomotionPlayer;
    private CharacterMotorDriver2D motorDriver;
    private CylindricalRigidbodyGroup2D cylindricalWrap;
    private Binding[] bindings => rig.Bindings;
    private Dictionary<Rigidbody2D, KinematicPose> kinematicPoses => rig.Poses;
    private MotionJsonClip walkClip;
    private MotionJsonClip runClip;
    private Rigidbody2D centralBody;
    private ActionMotionRuntime[] actionRuntimes;
    private long nextActionSequence;
    private bool initialized;
    private FacingRendererState[] facingRenderers;

    private sealed class FacingRendererState
    {
        public SpriteRenderer renderer;
        public bool originalFlipX;
        public int originalSortingOrder;
        public SpriteRenderer oppositeRenderer;
        public int oppositeOriginalSortingOrder;
        public bool mirrorLocalTransform;
        public Vector3 originalLocalPosition;
        public Quaternion originalLocalRotation;
    }

    public int FacingDirection => facingDirection < 0 ? -1 : 1;

    private void Awake()
    {
        motionState = MotionState.Idle;
        isPlaying = false;
        Initialize();
        motorDriver.SetMotorsEnabled(false);
    }

    private void OnEnable()
    {
        moveRightAction.Enable();
        moveLeftAction.Enable();
        runAction.Enable();
        SetActionInputsEnabled(true);
    }

    private void OnDisable()
    {
        moveRightAction.Disable();
        moveLeftAction.Disable();
        runAction.Disable();
        SetActionInputsEnabled(false);
        ResetActionLayers();
        if (initialized)
        {
            locomotionPlayer.ChangeState(LocomotionPlayer.State.Idle, bindings);
            SyncLocomotionRuntimeFields();
            motorDriver.SetMotorsEnabled(false);
            motorDriver.RestoreBodyTypes();
        }
        else
        {
            motionState = MotionState.Idle;
            isPlaying = false;
        }
    }

    private void OnActionPerformed(InputAction.CallbackContext context)
    {
        if (actionRuntimes == null) return;
        foreach (ActionMotionRuntime runtime in actionRuntimes)
        {
            if (runtime.Definition.inputAction == context.action)
            {
                runtime.Request(ref nextActionSequence);
                return;
            }
        }
    }

    private void SetActionInputsEnabled(bool enabled)
    {
        if (actionRuntimes == null) return;
        foreach (ActionMotionRuntime runtime in actionRuntimes)
        {
            InputAction action = runtime.Definition.inputAction;
            if (action == null) continue;
            if (enabled)
            {
                action.performed += OnActionPerformed;
                action.Enable();
            }
            else
            {
                action.performed -= OnActionPerformed;
                action.Disable();
            }
        }
    }

    private void UpdateActionLayers()
    {
        foreach (ActionMotionRuntime runtime in actionRuntimes)
            runtime.Tick(Time.fixedDeltaTime);
    }

    private void ResetActionLayers()
    {
        nextActionSequence = 0;
        if (actionRuntimes == null) return;
        foreach (ActionMotionRuntime runtime in actionRuntimes)
            runtime.Reset();
    }

    public void Initialize()
    {
        actionLayers ??= new ActionMotionDefinition[0];
        walkClip = MotionJsonClip.Parse(walkMotionJson, "Walk");
        runClip = MotionJsonClip.Parse(runMotionJson, "Run");
        actionRuntimes = new ActionMotionRuntime[actionLayers.Length];
        for (int i = 0; i < actionLayers.Length; i++)
            actionRuntimes[i] = new ActionMotionRuntime(actionLayers[i]);
        locomotionPlayer = new LocomotionPlayer(walkClip, runClip);
        locomotionPlayer.Reset(locomotionTransitionDuration);

        rig = new CharacterRig2D(transform);
        InitializeFacingPresentation();
        SetFacingDirection(facingDirection);
        centralBody = rig.CentralBody;
        TryGetComponent(out cylindricalWrap);
        motorDriver = new CharacterMotorDriver2D(rig);
        groundingSolver = new LegGroundingSolver2D(rig);
        groundingSolver.Configure(CreateGroundingSettings());

        foreach (Binding binding in bindings)
        {
            if (!string.IsNullOrEmpty(binding.jsonJoint))
            {
                walkClip.GetAngles(binding.jsonJoint);
                runClip.GetAngles(binding.jsonJoint);
                WarnIfLimitsCannotRepresentCycle(binding, walkClip, "Walk");
                WarnIfLimitsCannotRepresentCycle(binding, runClip, "Run");
                ValidateActionLayersForBinding(binding);
            }
            binding.joint.useMotor = false;
        }

        motorDriver.ApplyCharacterMode(animatedMode);

        groundingSolver.ResetRuntimeState();
        ResetActionLayers();
        SyncLocomotionRuntimeFields();
        initialized = true;
        if (animatedMode && !isPlaying)
            BeginStopTransition();
    }

    private void FixedUpdate()
    {
        if (!initialized) return;

        UpdateActionLayers();

        int moveDirection = GetMoveDirection();
        if (moveDirection != 0)
            SetFacingDirection(moveDirection);

        MotionState desiredState = moveDirection == 0
            ? MotionState.Idle
            : runAction.IsPressed() ? MotionState.Run : MotionState.Walk;
        if (desiredState != motionState)
            ChangeMotionState(desiredState);

        if (animatedMode)
        {
            locomotionPlayer.TickAnimated(
                Time.fixedDeltaTime, CreateLocomotionSettings());
            SyncLocomotionRuntimeFields();
            DriveKinematicPose();
            return;
        }

        if (!isPlaying) return;

        motorDriver.ApplyUpright(CreateUprightSettings());

        if (motionState == MotionState.Walk || motionState == MotionState.Run)
            centralBody.AddForce(
                Vector2.right * (moveForce * facingDirection),
                ForceMode2D.Force);
        locomotionPlayer.TickDynamic(
            Time.fixedDeltaTime, CreateLocomotionSettings());
        SyncLocomotionRuntimeFields();

        LocomotionPlayer.Settings locomotionSettings = CreateLocomotionSettings();
        CharacterMotorDriver2D.JointSettings motorSettings = CreateJointMotorSettings();
        foreach (Binding binding in bindings)
        {
            float target = locomotionPlayer.SampleJoint(
                binding, locomotionSettings, false);
            target = ApplyLimbMotionLayer(binding, target);
            binding.lastTargetAngle = target;
            target = ApplyFacingToDynamicJointAngle(binding, target);
            motorDriver.DriveJoint(binding, target, motorSettings);
        }
    }

    private void ChangeMotionState(MotionState nextState)
    {
        MotionState previousState = motionState;
        motionState = nextState;
        locomotionPlayer.ChangeState(
            (LocomotionPlayer.State)nextState, bindings);
        SyncLocomotionRuntimeFields();
        motorDriver.SetMotorsEnabled(!animatedMode && isPlaying);

        if (nextState == MotionState.Idle)
        {
            if (animatedMode) BeginStopTransition();
            return;
        }

        if (animatedMode && previousState == MotionState.Idle)
        {
            // The rest pose is nearly straight, so deriving the IK branch from
            // its tiny transient knee offset is numerically unstable and can
            // choose the backward-bending solution for the first few frames.
            // Both source motions keep one consistent forward bend branch.
            groundingSolver.BeginLocomotion(GetPreferredKneeBendSign());
        }
    }

    private float ApplyLimbMotionLayer(Binding binding, float baseAngle)
    {
        if (string.IsNullOrEmpty(binding.jsonJoint)) return baseAngle;

        ActionMotionRuntime winner = null;
        foreach (ActionMotionRuntime runtime in actionRuntimes)
        {
            if (runtime.Player.Weight <= 0f ||
                !LimbMaskUtility.ContainsJoint(
                    runtime.Definition.motion.targetLimbs,
                    binding.jsonJoint)) continue;

            if (winner == null || IsHigherPriority(runtime, winner))
                winner = runtime;
        }
        if (winner == null) return baseAngle;

        float source = winner.Clip.Sample(
            binding.jsonJoint, winner.Player.NormalizedTime);
        float layerAngle = NormalizeJointAngle(
            binding.offset + projectionAngleSign * source);
        return Mathf.LerpAngle(baseAngle, layerAngle, winner.Player.Weight);
    }

    private static bool IsHigherPriority(
        ActionMotionRuntime candidate,
        ActionMotionRuntime current)
    {
        int candidatePriority = candidate.Definition.motion.priority;
        int currentPriority = current.Definition.motion.priority;
        return candidatePriority > currentPriority ||
            candidatePriority == currentPriority &&
            candidate.ActivationSequence > current.ActivationSequence;
    }

    private void ValidateActionLayersForBinding(Binding binding)
    {
        foreach (ActionMotionRuntime runtime in actionRuntimes)
        {
            MotionLayerDefinition motion = runtime.Definition.motion;
            if (!LimbMaskUtility.ContainsJoint(motion.targetLimbs, binding.jsonJoint))
                continue;
            runtime.Clip.GetAngles(binding.jsonJoint);
            WarnIfLimitsCannotRepresentCycle(binding, runtime.Clip, motion.layerName);
        }
    }

    private void BeginStopTransition()
    {
        groundingSolver.Configure(CreateGroundingSettings());
        groundingSolver.BeginStopTransition(GetPreferredKneeBendSign());
    }

    private float GetPreferredKneeBendSign() =>
        Mathf.Approximately(stoppingKneeBendSign, 0f)
            ? 1f
            : Mathf.Sign(stoppingKneeBendSign);

    private LocomotionPlayer.Settings CreateLocomotionSettings() => new()
    {
        walkPlaybackSpeed = walkPlaybackSpeed,
        runPlaybackSpeed = runPlaybackSpeed,
        walkMoveSpeed = walkMoveSpeed,
        runMoveSpeed = runMoveSpeed,
        transitionDuration = locomotionTransitionDuration,
        returnToRestDuration = returnToRestDuration,
        projectionAngleSign = projectionAngleSign
    };

    private CharacterMotorDriver2D.JointSettings CreateJointMotorSettings() => new()
    {
        proportionalGain = proportionalGain,
        derivativeGain = derivativeGain,
        maxMotorSpeed = maxMotorSpeed,
        maxMotorTorque = maxMotorTorque,
        clampToJointLimits = clampToJointLimits
    };

    private CharacterMotorDriver2D.UprightSettings CreateUprightSettings() => new()
    {
        enabled = uprightEnabled,
        worldAngle = uprightWorldAngle,
        proportionalGain = uprightProportionalGain,
        derivativeGain = uprightDerivativeGain,
        maxAngularAcceleration = maxUprightAngularAcceleration,
        upwardForce = uprightForce
    };

    private void SyncLocomotionRuntimeFields()
    {
        motionState = (MotionState)locomotionPlayer.StateValue;
        isPlaying = locomotionPlayer.IsPlaying;
        normalizedTime = locomotionPlayer.NormalizedTime;
    }

    private LegGroundingSolver2D.Settings CreateGroundingSettings() => new()
    {
        supportLegIkEnabled = supportLegIkEnabled,
        groundLayers = groundLayers,
        groundProbeHeight = groundProbeHeight,
        groundProbeDistance = groundProbeDistance,
        footGroundClearance = footGroundClearance,
        plantedFootWorldAngle = plantedFootWorldAngle,
        footPlantBlendSpeed = footPlantBlendSpeed,
        virtualSoleEndInset = virtualSoleEndInset,
        fixedBodyHeightOffset = fixedBodyHeightOffset,
        supportSwitchHysteresis = supportSwitchHysteresis,
        supportIkBlendSpeed = supportIkBlendSpeed,
        penetrationToKnee = penetrationToKnee,
        maxPenetrationToKnee = maxPenetrationToKnee,
        preventSwingFootPenetration = preventSwingFootPenetration,
        swingFootClearanceIkWeight = swingFootClearanceIkWeight,
        swingFootPenetrationTolerance = swingFootPenetrationTolerance,
        logUnreachableIkTargets = logUnreachableIkTargets
    };

    private void DriveKinematicPose()
    {
        groundingSolver.Configure(CreateGroundingSettings());
        Vector2 bodyTarget = centralBody.position;
        if (isPlaying)
            bodyTarget += Vector2.right * (
                facingDirection * locomotionPlayer.CurrentMoveSpeed * Time.fixedDeltaTime);
        else if (groundingSolver.StoppingFootLockActive)
            bodyTarget = groundingSolver.EvaluateStoppingBodyPosition(
                locomotionPlayer.PoseWeight);
        if (cylindricalWrap != null)
            bodyTarget = cylindricalWrap.WrapKinematicTarget(bodyTarget);
        groundingSolver.ConstrainBodyHeight(ref bodyTarget);
        rig.BeginPose(bodyTarget, uprightWorldAngle);

        LocomotionPlayer.Settings locomotionSettings = CreateLocomotionSettings();
        foreach (Binding binding in bindings)
        {
            float targetJointAngle = locomotionPlayer.SampleJoint(
                binding, locomotionSettings, true);
            targetJointAngle = ApplyLimbMotionLayer(binding, targetJointAngle);
            binding.lastTargetAngle = targetJointAngle;

            rig.SetJointPose(binding, targetJointAngle);
        }

        groundingSolver.Apply(locomotionPlayer.PoseWeight, Time.fixedDeltaTime);
        if (facingDirection < 0)
            rig.MirrorPoseAcrossBodyVertical();

        bool teleportPose = cylindricalWrap != null &&
            cylindricalWrap.ConsumeKinematicTeleport();
        foreach (KeyValuePair<Rigidbody2D, KinematicPose> item in kinematicPoses)
        {
            if (teleportPose)
            {
                item.Key.position = item.Value.position;
                item.Key.rotation = item.Value.rotation;
                item.Key.linearVelocity = Vector2.zero;
                item.Key.angularVelocity = 0f;
            }
            else
            {
                item.Key.MovePosition(item.Value.position);
                item.Key.MoveRotation(item.Value.rotation);
            }
        }
        if (teleportPose)
            Physics2D.SyncTransforms();
    }

    private int GetMoveDirection()
    {
        bool left = moveLeftAction.IsPressed();
        bool right = moveRightAction.IsPressed();
        if (left == right) return 0;
        return right ? 1 : -1;
    }

    private float ApplyFacingToDynamicJointAngle(Binding binding, float targetAngle)
    {
        if (facingDirection >= 0) return targetAngle;
        float restDelta = Mathf.DeltaAngle(binding.restJointAngle, targetAngle);
        return binding.restJointAngle - restDelta;
    }

    private void InitializeFacingPresentation()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        Dictionary<string, SpriteRenderer> byName = new();
        foreach (SpriteRenderer renderer in renderers)
            byName.TryAdd(renderer.gameObject.name.Trim(), renderer);

        facingRenderers = new FacingRendererState[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            string objectName = renderer.gameObject.name.Trim();
            string oppositeName = objectName.StartsWith("Left", System.StringComparison.Ordinal)
                ? "Right" + objectName.Substring(4)
                : objectName.StartsWith("Right", System.StringComparison.Ordinal)
                    ? "Left" + objectName.Substring(5)
                    : null;

            SpriteRenderer opposite = null;
            if (oppositeName != null)
                byName.TryGetValue(oppositeName, out opposite);

            facingRenderers[i] = new FacingRendererState
            {
                renderer = renderer,
                originalFlipX = renderer.flipX,
                originalSortingOrder = renderer.sortingOrder,
                oppositeRenderer = opposite,
                oppositeOriginalSortingOrder = opposite != null
                    ? opposite.sortingOrder
                    : renderer.sortingOrder,
                mirrorLocalTransform = renderer.GetComponent<Rigidbody2D>() == null &&
                    renderer.transform.parent != null &&
                    renderer.transform.parent.TryGetComponent(out Rigidbody2D _),
                originalLocalPosition = renderer.transform.localPosition,
                originalLocalRotation = renderer.transform.localRotation
            };
        }
    }

    private void SetFacingDirection(int direction)
    {
        int nextDirection = direction < 0 ? -1 : 1;
        bool changed = facingDirection != nextDirection;
        facingDirection = nextDirection;
        if (facingRenderers == null) return;

        bool faceLeft = facingDirection < 0;
        foreach (FacingRendererState state in facingRenderers)
        {
            if (state.renderer == null) continue;
            state.renderer.flipX = faceLeft
                ? !state.originalFlipX
                : state.originalFlipX;
            state.renderer.sortingOrder = faceLeft && state.oppositeRenderer != null
                ? state.oppositeOriginalSortingOrder
                : state.originalSortingOrder;

            if (state.mirrorLocalTransform)
            {
                Vector3 localPosition = state.originalLocalPosition;
                if (faceLeft) localPosition.x = -localPosition.x;
                state.renderer.transform.localPosition = localPosition;

                Vector3 localEuler = state.originalLocalRotation.eulerAngles;
                if (faceLeft) localEuler.z = -localEuler.z;
                state.renderer.transform.localRotation = Quaternion.Euler(localEuler);
            }
        }

        if (changed && groundingSolver != null && isPlaying)
            groundingSolver.BeginLocomotion(GetPreferredKneeBendSign());
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
        locomotionPlayer.SetNormalizedTime(value);
        SyncLocomotionRuntimeFields();
    }
    public void Restart()
    {
        if (!initialized) Initialize();
        locomotionPlayer.SetNormalizedTime(0f);
        SyncLocomotionRuntimeFields();
        ChangeMotionState(MotionState.Walk);
    }
}
