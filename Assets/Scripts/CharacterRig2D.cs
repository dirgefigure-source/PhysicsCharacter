using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resolves the Player's articulated bodies and builds deterministic FK poses.
/// Grounding and IK intentionally remain outside this class.
/// </summary>
public sealed class CharacterRig2D
{
    public sealed class JointBinding
    {
        public string bodyName;
        public string jsonJoint;
        public HingeJoint2D joint;
        public float offset;
        public float restJointAngle;
        public float hingeReference;
        public float transitionStartAngle;
        public float lastTargetAngle;
    }

    public readonly struct Pose2D
    {
        public readonly Vector2 position;
        public readonly float rotation;

        public Pose2D(Vector2 position, float rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }

    private readonly Transform root;
    private readonly JointBinding[] bindings;
    private readonly Dictionary<Rigidbody2D, Pose2D> poses = new();

    public CharacterRig2D(Transform root)
    {
        this.root = root != null
            ? root
            : throw new ArgumentNullException(nameof(root));

        Transform bodyTransform = FindTransform("Body");
        if (bodyTransform == null || !bodyTransform.TryGetComponent(out Rigidbody2D centralBody))
            throw new InvalidOperationException("Player central body 'Body' has no Rigidbody2D.");
        CentralBody = centralBody;

        bindings = CreateBindings();
        foreach (JointBinding binding in bindings)
            InitializeBinding(binding);

        AllBodies = root.GetComponentsInChildren<Rigidbody2D>(true);
    }

    public JointBinding[] Bindings => bindings;
    public Dictionary<Rigidbody2D, Pose2D> Poses => poses;
    public Rigidbody2D CentralBody { get; }
    public Rigidbody2D[] AllBodies { get; }

    public JointBinding FindBinding(string bodyName)
    {
        foreach (JointBinding binding in bindings)
            if (binding.bodyName == bodyName) return binding;
        throw new InvalidOperationException($"Binding '{bodyName}' is missing.");
    }

    public Transform FindTransform(string objectName)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(candidate.name.Trim(), objectName, StringComparison.Ordinal))
                return candidate;
        }
        return null;
    }

    public void BeginPose(Vector2 bodyPosition, float bodyRotation)
    {
        poses.Clear();
        poses[CentralBody] = new Pose2D(bodyPosition, bodyRotation);
    }

    public void SetJointPose(JointBinding binding, float targetJointAngle)
    {
        Rigidbody2D parent = binding.joint.connectedBody;
        Rigidbody2D child = binding.joint.attachedRigidbody;
        Pose2D parentPose;
        if (parent == null)
            parentPose = new Pose2D(Vector2.zero, 0f);
        else if (!poses.TryGetValue(parent, out parentPose))
            parentPose = new Pose2D(parent.position, parent.rotation);

        float childRotation = parentPose.rotation
                            + binding.hingeReference - targetJointAngle;
        Vector2 parentAnchor = parentPose.position
                             + Rotate(binding.joint.connectedAnchor, parentPose.rotation);
        Vector2 rotatedChildAnchor = Rotate(binding.joint.anchor, childRotation);
        poses[child] = new Pose2D(parentAnchor - rotatedChildAnchor, childRotation);
    }

    private static JointBinding[] CreateBindings() => new JointBinding[]
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

    private void InitializeBinding(JointBinding binding)
    {
        Transform body = FindTransform(binding.bodyName);
        if (body == null || !body.TryGetComponent(out binding.joint))
            throw new InvalidOperationException($"Player body '{binding.bodyName}' has no HingeJoint2D.");

        binding.restJointAngle = binding.joint.jointAngle;
        binding.lastTargetAngle = binding.restJointAngle;
        binding.transitionStartAngle = binding.restJointAngle;
        float childRotation = binding.joint.attachedRigidbody.rotation;
        float parentRotation = binding.joint.connectedBody != null
            ? binding.joint.connectedBody.rotation
            : 0f;
        binding.hingeReference = Mathf.DeltaAngle(parentRotation, childRotation)
                               + binding.joint.jointAngle;
        if (!string.IsNullOrEmpty(binding.jsonJoint))
            binding.offset = CalculateStaticAxisOffset(binding);
    }

    private static float CalculateStaticAxisOffset(JointBinding binding)
    {
        float childAxisLocal = binding.bodyName.EndsWith("Foot", StringComparison.Ordinal)
            ? 180f
            : -90f;

        float parentAxisLocal = 90f;
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
        return Mathf.DeltaAngle(0f, parentAxisLocal - childAxisLocal - hingeReference);
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(cos * vector.x - sin * vector.y, sin * vector.x + cos * vector.y);
    }
}
