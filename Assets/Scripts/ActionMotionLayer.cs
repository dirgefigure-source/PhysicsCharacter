using System;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public sealed class ActionMotionDefinition
{
    public string actionName = "Action";
    public InputAction inputAction = new(
        name: "Action",
        type: InputActionType.Button);
    public MotionLayerDefinition motion = new();
}

/// <summary>
/// Connects one serialized action definition to its parsed clip and playback state.
/// </summary>
public sealed class ActionMotionRuntime
{
    private bool requested;
    private long requestedSequence;

    public ActionMotionRuntime(ActionMotionDefinition definition)
    {
        Definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        Definition.motion ??= new MotionLayerDefinition();
        string clipName = string.IsNullOrWhiteSpace(Definition.motion.layerName)
            ? Definition.actionName
            : Definition.motion.layerName;
        Clip = MotionJsonClip.Parse(Definition.motion.motionJson, clipName);
        Player = new MotionLayerPlayer(Definition.motion);
    }

    public ActionMotionDefinition Definition { get; }
    public MotionJsonClip Clip { get; }
    public MotionLayerPlayer Player { get; }
    public long ActivationSequence { get; private set; }

    public void Request(ref long nextSequence)
    {
        requested = true;
        requestedSequence = ++nextSequence;
    }

    public void Tick(float deltaTime)
    {
        if (requested)
        {
            requested = false;
            ActivationSequence = requestedSequence;
            Player.Play();
        }
        Player.Tick(deltaTime, Clip.DurationSeconds);
    }

    public void Reset()
    {
        requested = false;
        requestedSequence = 0;
        ActivationSequence = 0;
        Player.Reset();
    }
}
