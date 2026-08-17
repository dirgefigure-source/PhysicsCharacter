using UnityEngine;
using Binding = CharacterRig2D.JointBinding;

/// <summary>
/// Owns Idle/Walk/Run playback time, pose weight and transition state.
/// </summary>
public sealed class LocomotionPlayer
{
    public enum State
    {
        Idle,
        Walk,
        Run
    }

    public struct Settings
    {
        public float walkPlaybackSpeed;
        public float runPlaybackSpeed;
        public float walkMoveSpeed;
        public float runMoveSpeed;
        public float transitionDuration;
        public float returnToRestDuration;
        public float projectionAngleSign;
    }

    private readonly MotionJsonClip walkClip;
    private readonly MotionJsonClip runClip;
    private MotionJsonClip activeClip;
    private float transitionElapsed;
    private float transitionStartMoveSpeed;

    public LocomotionPlayer(MotionJsonClip walkClip, MotionJsonClip runClip)
    {
        this.walkClip = walkClip;
        this.runClip = runClip;
        activeClip = walkClip;
        StateValue = State.Idle;
    }

    public State StateValue { get; private set; }
    public bool IsPlaying => StateValue != State.Idle;
    public float NormalizedTime { get; private set; }
    public float PoseWeight { get; private set; }
    public float CurrentMoveSpeed { get; private set; }

    public void Reset(float transitionDuration)
    {
        StateValue = State.Idle;
        activeClip = walkClip;
        NormalizedTime = 0f;
        PoseWeight = 0f;
        CurrentMoveSpeed = 0f;
        transitionStartMoveSpeed = 0f;
        transitionElapsed = transitionDuration;
    }

    public void ChangeState(State nextState, Binding[] bindings)
    {
        State previousState = StateValue;
        StateValue = nextState;

        if (nextState == State.Idle)
        {
            CurrentMoveSpeed = 0f;
            return;
        }

        foreach (Binding binding in bindings)
            binding.transitionStartAngle = binding.lastTargetAngle;

        activeClip = GetClip(nextState);
        transitionStartMoveSpeed = previousState == State.Idle
            ? 0f
            : CurrentMoveSpeed;
        transitionElapsed = 0f;
    }

    public void TickAnimated(float deltaTime, Settings settings)
    {
        if (IsPlaying)
        {
            AdvanceTime(deltaTime, settings);
            PoseWeight = Mathf.MoveTowards(
                PoseWeight, 1f, deltaTime / settings.transitionDuration);
            transitionElapsed = Mathf.Min(
                transitionElapsed + deltaTime, settings.transitionDuration);
            CurrentMoveSpeed = Mathf.Lerp(
                transitionStartMoveSpeed,
                GetMoveSpeed(StateValue, settings),
                GetTransitionBlend(settings.transitionDuration));
        }
        else
        {
            PoseWeight = Mathf.MoveTowards(
                PoseWeight, 0f, deltaTime / settings.returnToRestDuration);
            if (PoseWeight <= 0f)
                NormalizedTime = 0f;
        }
    }

    public void TickDynamic(float deltaTime, Settings settings)
    {
        if (IsPlaying)
            AdvanceTime(deltaTime, settings);
    }

    public float SampleJoint(Binding binding, Settings settings, bool blendFromRest)
    {
        if (string.IsNullOrEmpty(binding.jsonJoint))
            return binding.restJointAngle;

        float source = activeClip.Sample(binding.jsonJoint, NormalizedTime);
        float target = NormalizeJointAngle(binding.offset + settings.projectionAngleSign * source);
        float transitionBlend = GetTransitionBlend(settings.transitionDuration);
        if (transitionBlend < 1f)
            target = Mathf.LerpAngle(binding.transitionStartAngle, target, transitionBlend);
        return blendFromRest
            ? Mathf.LerpAngle(binding.restJointAngle, target, PoseWeight)
            : target;
    }

    public void SetNormalizedTime(float value)
    {
        NormalizedTime = Mathf.Repeat(value, 1f);
    }

    private void AdvanceTime(float deltaTime, Settings settings)
    {
        float nextTime = NormalizedTime + deltaTime
            * GetPlaybackSpeed(StateValue, settings) / activeClip.DurationSeconds;
        NormalizedTime = Mathf.Repeat(nextTime, 1f);
    }

    private float GetTransitionBlend(float duration)
    {
        float linear = Mathf.Clamp01(transitionElapsed / duration);
        return linear * linear * (3f - 2f * linear);
    }

    private MotionJsonClip GetClip(State state) =>
        state == State.Run ? runClip : walkClip;

    private static float GetPlaybackSpeed(State state, Settings settings) =>
        state == State.Run ? settings.runPlaybackSpeed : settings.walkPlaybackSpeed;

    private static float GetMoveSpeed(State state, Settings settings) => state switch
    {
        State.Run => settings.runMoveSpeed,
        State.Walk => settings.walkMoveSpeed,
        _ => 0f
    };

    private static float NormalizeJointAngle(float angle) => Mathf.DeltaAngle(0f, angle);
}
