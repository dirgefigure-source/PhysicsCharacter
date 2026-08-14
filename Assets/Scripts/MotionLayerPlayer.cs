using System;
using UnityEngine;

[Flags]
public enum LimbMask
{
    None = 0,
    LeftArm = 1 << 0,
    RightArm = 1 << 1,
    LeftLeg = 1 << 2,
    RightLeg = 1 << 3,
    All = LeftArm | RightArm | LeftLeg | RightLeg
}

public static class LimbMaskUtility
{
    public static bool ContainsJoint(LimbMask mask, string jointName)
    {
        LimbMask jointMask = jointName switch
        {
            "leftUpperArm" or "leftLowerArm" => LimbMask.LeftArm,
            "rightUpperArm" or "rightLowerArm" => LimbMask.RightArm,
            "leftUpperLeg" or "leftLowerLeg" or "leftFoot" => LimbMask.LeftLeg,
            "rightUpperLeg" or "rightLowerLeg" or "rightFoot" => LimbMask.RightLeg,
            _ => LimbMask.None
        };
        return (mask & jointMask) != 0;
    }
}

[Serializable]
public sealed class MotionLayerDefinition
{
    public string layerName = "Action";
    public TextAsset motionJson;
    public LimbMask targetLimbs = LimbMask.RightArm;
    [Min(0.01f)] public float playbackSpeed = 1f;
    [Min(0.01f)] public float blendInDuration = 0.08f;
    [Min(0.01f)] public float blendOutDuration = 0.15f;
    public int priority;
}

/// <summary>
/// Runtime state for a reusable one-shot motion layer. It owns no rig or JSON
/// parsing logic; it only produces normalized time and blend weight.
/// </summary>
public sealed class MotionLayerPlayer
{
    public MotionLayerDefinition Definition { get; }
    public bool IsActive { get; private set; }
    public float NormalizedTime { get; private set; }
    public float Weight { get; private set; }

    private float elapsed;

    public MotionLayerPlayer(MotionLayerDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public void Play()
    {
        IsActive = true;
        elapsed = 0f;
        NormalizedTime = 0f;
        Weight = 0f;
    }

    public void Reset()
    {
        IsActive = false;
        elapsed = 0f;
        NormalizedTime = 0f;
        Weight = 0f;
    }

    public void Tick(float deltaTime, float clipDuration)
    {
        if (!IsActive)
        {
            Weight = 0f;
            return;
        }

        float speed = Mathf.Max(0.01f, Definition.playbackSpeed);
        float duration = Mathf.Max(0.0001f, clipDuration);
        elapsed += Mathf.Max(0f, deltaTime);
        float sampledSeconds = elapsed * speed;
        NormalizedTime = Mathf.Clamp01(sampledSeconds / duration);

        float fadeIn = Mathf.Clamp01(
            elapsed / Mathf.Max(0.01f, Definition.blendInDuration));
        float remainingSeconds = Mathf.Max(0f, (duration - sampledSeconds) / speed);
        float fadeOut = Mathf.Clamp01(
            remainingSeconds / Mathf.Max(0.01f, Definition.blendOutDuration));
        Weight = Mathf.Min(fadeIn, fadeOut);

        if (NormalizedTime >= 1f)
            Reset();
    }
}
