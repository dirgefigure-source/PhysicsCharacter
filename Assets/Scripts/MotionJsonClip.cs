using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Immutable, runtime-ready view of an exported motion JSON file. Parsing,
/// validation, angle unwrapping, and frame interpolation live here so motion
/// consumers do not depend on the export DTO structure.
/// </summary>
public sealed class MotionJsonClip
{
    [Serializable]
    private sealed class JsonData
    {
        public string clipName;
        public float durationSeconds;
        public List<JsonFrame> frames;
    }

    [Serializable]
    private sealed class JsonFrame
    {
        public List<JsonJoint> joints;
    }

    [Serializable]
    private sealed class JsonJoint
    {
        public string name;
        public float relativeAngleDeg;
    }

    private readonly Dictionary<string, float[]> jointAngles;

    public string Name { get; }
    public float DurationSeconds { get; }
    public int FrameCount { get; }

    private MotionJsonClip(
        string name,
        float durationSeconds,
        int frameCount,
        Dictionary<string, float[]> jointAngles)
    {
        Name = name;
        DurationSeconds = durationSeconds;
        FrameCount = frameCount;
        this.jointAngles = jointAngles;
    }

    public static MotionJsonClip Parse(TextAsset jsonAsset, string label)
    {
        if (jsonAsset == null)
            throw new InvalidOperationException($"{label} Motion JSON is not assigned.");

        JsonData data = JsonUtility.FromJson<JsonData>(jsonAsset.text);
        if (data?.frames == null || data.frames.Count < 2 || data.durationSeconds <= 0f)
            throw new InvalidOperationException($"{label} Motion JSON has no usable frames.");
        if (data.frames[0].joints == null || data.frames[0].joints.Count == 0)
            throw new InvalidOperationException($"{label} Motion JSON has no joints.");

        int frameCount = data.frames.Count;
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        for (int jointIndex = 0; jointIndex < data.frames[0].joints.Count; jointIndex++)
        {
            JsonJoint firstJoint = data.frames[0].joints[jointIndex];
            if (string.IsNullOrEmpty(firstJoint.name))
                throw new InvalidOperationException($"{label} Motion JSON contains an unnamed joint.");
            if (result.ContainsKey(firstJoint.name))
                throw new InvalidOperationException(
                    $"{label} Motion JSON contains duplicate joint '{firstJoint.name}'.");

            var angles = new float[frameCount];
            angles[0] = firstJoint.relativeAngleDeg;
            for (int frameIndex = 1; frameIndex < frameCount; frameIndex++)
            {
                List<JsonJoint> joints = data.frames[frameIndex].joints;
                if (joints == null || jointIndex >= joints.Count ||
                    joints[jointIndex].name != firstJoint.name)
                {
                    throw new InvalidOperationException(
                        $"{label} Motion JSON joint order changes at frame {frameIndex}.");
                }

                float previousWrapped = data.frames[frameIndex - 1]
                    .joints[jointIndex].relativeAngleDeg;
                float currentWrapped = joints[jointIndex].relativeAngleDeg;
                angles[frameIndex] = angles[frameIndex - 1]
                    + Mathf.DeltaAngle(previousWrapped, currentWrapped);
            }
            result.Add(firstJoint.name, angles);
        }

        string clipName = string.IsNullOrEmpty(data.clipName) ? label : data.clipName;
        return new MotionJsonClip(clipName, data.durationSeconds, frameCount, result);
    }

    public bool HasJoint(string jointName) =>
        !string.IsNullOrEmpty(jointName) && jointAngles.ContainsKey(jointName);

    public float Sample(string jointName, float normalizedTime)
    {
        float[] angles = GetAngles(jointName);
        float framePosition = Mathf.Clamp01(normalizedTime) * (FrameCount - 1);
        int aIndex = Mathf.Min(Mathf.FloorToInt(framePosition), FrameCount - 2);
        int bIndex = aIndex + 1;
        return Mathf.Lerp(angles[aIndex], angles[bIndex], framePosition - aIndex);
    }

    public float[] GetAngles(string jointName)
    {
        if (!jointAngles.TryGetValue(jointName, out float[] angles))
            throw new InvalidOperationException(
                $"Motion '{Name}' is missing JSON joint '{jointName}'.");
        return angles;
    }
}
