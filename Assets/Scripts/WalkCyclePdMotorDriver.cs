using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class WalkCyclePdMotorDriver : MonoBehaviour
{
    [Serializable] private sealed class WalkData { public float durationSeconds; public List<Frame> frames; }
    [Serializable] private sealed class Frame { public float normalizedTime; public List<JointSample> joints; }
    [Serializable] private sealed class JointSample { public string name; public float relativeAngleDeg; }

    [Serializable]
    private sealed class Binding
    {
        public string bodyName;
        public string jsonJoint;
        [NonSerialized] public HingeJoint2D joint;
        [NonSerialized] public int jsonIndex;
        [NonSerialized] public float offset;
        [NonSerialized] public float[] unwrappedAngles;
        [NonSerialized] public float restJointAngle;
        [NonSerialized] public float hingeReference;
    }

    [Header("Animation")]
    [SerializeField] private TextAsset walkCycleJson;
    [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;
    [Tooltip("Kinematic FK: the complete character follows the sampled animation pose.")]
    [SerializeField] private bool animatedMode = true;
    [SerializeField, Min(0f)] private float animatedMoveSpeed = 2f;
    [Tooltip("The camera views the XY plane from -Z, opposite atan2's +Z viewpoint.")]
    [SerializeField] private float projectionAngleSign = -1f;

    [Header("Animated Grounding")]
    [Tooltip("Layers that the animated feet may use as ground. Ground is currently on Default.")]
    [SerializeField] private LayerMask groundLayers = 1;
    [SerializeField, Min(0f)] private float groundProbeHeight = 1f;
    [SerializeField, Min(0.01f)] private float groundProbeDistance = 5f;
    [SerializeField, Min(0f)] private float footGroundClearance = 0.01f;

    [Header("Input System")]
    [SerializeField] private InputAction walkAction = new(
        name: "Walk",
        type: InputActionType.Button,
        binding: "<Keyboard>/d");
    [Tooltip("Continuous world +X force applied to Body while D is held.")]
    [SerializeField, Min(0f)] private float moveForce = 50f;

    [Header("Foot Pin Test")]
    [Tooltip("Freezes the LeftFoot Rigidbody2D at its initial world pose.")]
    [SerializeField] private bool pinLeftFootToGround = true;
    [Tooltip("Freezes the RightFoot Rigidbody2D at its initial world pose.")]
    [SerializeField] private bool pinRightFootToGround = true;

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

    private WalkData data;
    private readonly Dictionary<Rigidbody2D, KinematicPose> kinematicPoses = new();
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
        isPlaying = false;
        Initialize();
        SetMotorsEnabled(false);
    }

    private void OnEnable()
    {
        walkAction.Enable();
    }

    private void OnDisable()
    {
        walkAction.Disable();
        isPlaying = false;
        if (initialized) SetMotorsEnabled(false);
        if (leftFootBody != null)
            leftFootBody.constraints = leftFootOriginalConstraints;
        if (rightFootBody != null)
            rightFootBody.constraints = rightFootOriginalConstraints;
        RestoreBodyTypes();
    }

    public void Initialize()
    {
        if (walkCycleJson == null)
            throw new InvalidOperationException("Walk Cycle JSON is not assigned.");

        data = JsonUtility.FromJson<WalkData>(walkCycleJson.text);
        if (data?.frames == null || data.frames.Count < 2 || data.durationSeconds <= 0f)
            throw new InvalidOperationException("Walk Cycle JSON has no usable frames.");

        Transform bodyTransform = FindDescendant(transform, "Body");
        if (bodyTransform == null || !bodyTransform.TryGetComponent(out centralBody))
            throw new InvalidOperationException("Player central body 'Body' has no Rigidbody2D.");

        foreach (Binding binding in bindings)
        {
            Transform body = FindDescendant(transform, binding.bodyName);
            if (body == null || !body.TryGetComponent(out binding.joint))
                throw new InvalidOperationException($"Player body '{binding.bodyName}' has no HingeJoint2D.");

            binding.restJointAngle = binding.joint.jointAngle;
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
                binding.jsonIndex = FindJointIndex(data.frames[0], binding.jsonJoint);
                binding.unwrappedAngles = UnwrapAngles(binding.jsonIndex);
                binding.offset = CalculateStaticAxisOffset(binding);
                WarnIfLimitsCannotRepresentCycle(binding);
            }
            binding.joint.useMotor = false;
        }

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

        allBodies = GetComponentsInChildren<Rigidbody2D>(true);
        originalBodyTypes = new RigidbodyType2D[allBodies.Length];
        for (int i = 0; i < allBodies.Length; i++)
            originalBodyTypes[i] = allBodies[i].bodyType;
        ApplyCharacterMode();

        elapsed = 0f;
        normalizedTime = 0f;
        initialized = true;
    }

    private void FixedUpdate()
    {
        if (!initialized) return;

        bool shouldPlay = walkAction.IsPressed();
        if (shouldPlay != isPlaying)
        {
            isPlaying = shouldPlay;
            SetMotorsEnabled(!animatedMode && isPlaying);
        }

        if (animatedMode)
        {
            if (!isPlaying) return;
            UpdateAnimationTime();
            DriveKinematicPose();
            return;
        }

        if (!isPlaying) return;

        ApplyUprightBalance();

        centralBody.AddForce(Vector2.right * moveForce, ForceMode2D.Force);
        UpdateAnimationTime();
        GetFramePair(normalizedTime, out Frame a, out Frame b, out float t);

        foreach (Binding binding in bindings)
        {
            int aIndex = data.frames.IndexOf(a);
            int bIndex = data.frames.IndexOf(b);
            float source = Mathf.Lerp(
                binding.unwrappedAngles[aIndex], binding.unwrappedAngles[bIndex], t);
            float target = NormalizeJointAngle(binding.offset + projectionAngleSign * source);

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
        elapsed = Mathf.Repeat(elapsed + Time.fixedDeltaTime * playbackSpeed, data.durationSeconds);
        normalizedTime = elapsed / data.durationSeconds;
    }

    private void DriveKinematicPose()
    {
        Vector2 bodyTarget = centralBody.position + Vector2.right * animatedMoveSpeed * Time.fixedDeltaTime;
        kinematicPoses.Clear();
        kinematicPoses[centralBody] = new KinematicPose(bodyTarget, uprightWorldAngle);

        GetFramePair(normalizedTime, out Frame a, out Frame b, out float t);
        int aIndex = data.frames.IndexOf(a);
        int bIndex = data.frames.IndexOf(b);

        foreach (Binding binding in bindings)
        {
            float targetJointAngle = binding.restJointAngle;
            if (binding.unwrappedAngles != null)
            {
                float source = Mathf.Lerp(
                    binding.unwrappedAngles[aIndex], binding.unwrappedAngles[bIndex], t);
                targetJointAngle = NormalizeJointAngle(binding.offset + projectionAngleSign * source);
            }

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

        ApplyAnimatedGroundCorrection();

        foreach (KeyValuePair<Rigidbody2D, KinematicPose> item in kinematicPoses)
        {
            item.Key.MovePosition(item.Value.position);
            item.Key.MoveRotation(item.Value.rotation);
        }
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

        targetBottom = GetColliderBottomAtPose(foot, footCollider, pose);
        Vector2 origin = new Vector2(pose.position.x, targetBottom + groundProbeHeight);
        float distance = groundProbeHeight + groundProbeDistance;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, distance, groundLayers);
        if (hit.collider == null) return false;

        correction = hit.point.y + footGroundClearance - targetBottom;
        return true;
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

    private float[] UnwrapAngles(int jsonIndex)
    {
        var result = new float[data.frames.Count];
        result[0] = SourceAngle(data.frames[0], jsonIndex);
        for (int i = 1; i < result.Length; i++)
        {
            float previousWrapped = SourceAngle(data.frames[i - 1], jsonIndex);
            float currentWrapped = SourceAngle(data.frames[i], jsonIndex);
            result[i] = result[i - 1] + Mathf.DeltaAngle(previousWrapped, currentWrapped);
        }
        return result;
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

    private void WarnIfLimitsCannotRepresentCycle(Binding binding)
    {
        if (!logLimitWarnings || !binding.joint.useLimits) return;
        JointAngleLimits2D limits = binding.joint.limits;
        float minTarget = float.PositiveInfinity;
        float maxTarget = float.NegativeInfinity;
        foreach (float source in binding.unwrappedAngles)
        {
            float target = NormalizeJointAngle(binding.offset + projectionAngleSign * source);
            minTarget = Mathf.Min(minTarget, target);
            maxTarget = Mathf.Max(maxTarget, target);
        }

        if (minTarget < limits.min || maxTarget > limits.max)
        {
            Debug.LogWarning(
                $"Walk retarget: '{binding.bodyName}' requires [{minTarget:F1}, {maxTarget:F1}] deg, " +
                $"but its HingeJoint2D limits are [{limits.min:F1}, {limits.max:F1}] deg. " +
                (clampToJointLimits ? "Targets outside this range will be clamped." : "Limit collision will prevent exact tracking."),
                binding.joint);
        }
    }

    private void GetFramePair(float normalizedTime, out Frame a, out Frame b, out float t)
    {
        float framePosition = normalizedTime * (data.frames.Count - 1);
        int index = Mathf.Min(Mathf.FloorToInt(framePosition), data.frames.Count - 2);
        a = data.frames[index];
        b = data.frames[index + 1];
        t = framePosition - index;
    }

    private static float SourceAngle(Frame frame, int index) => frame.joints[index].relativeAngleDeg;

    private static float NormalizeJointAngle(float angle) => Mathf.DeltaAngle(0f, angle);

    private static int FindJointIndex(Frame frame, string name)
    {
        for (int i = 0; i < frame.joints.Count; i++)
            if (frame.joints[i].name == name) return i;
        throw new InvalidOperationException($"JSON joint '{name}' is missing.");
    }

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
            ApplyFootPins();
        }
    }

    private void RestoreBodyTypes()
    {
        if (allBodies == null || originalBodyTypes == null) return;
        for (int i = 0; i < allBodies.Length && i < originalBodyTypes.Length; i++)
            allBodies[i].bodyType = originalBodyTypes[i];
    }

    private void ApplyFootPins()
    {
        ApplyFootPin(leftFootBody, pinLeftFootToGround, leftFootOriginalConstraints);
        ApplyFootPin(rightFootBody, pinRightFootToGround, rightFootOriginalConstraints);
    }

    private static void ApplyFootPin(
        Rigidbody2D foot,
        bool pinned,
        RigidbodyConstraints2D originalConstraints)
    {
        if (foot == null) return;
        foot.constraints = pinned ? RigidbodyConstraints2D.FreezeAll : originalConstraints;
        if (!pinned) return;

        foot.linearVelocity = Vector2.zero;
        foot.angularVelocity = 0f;
    }

    public void Play()
    {
        isPlaying = true;
        SetMotorsEnabled(!animatedMode);
    }
    public void Pause()
    {
        isPlaying = false;
        SetMotorsEnabled(false);
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
        elapsed = normalizedTime * data.durationSeconds;
    }
    public void Restart()
    {
        elapsed = 0f;
        if (initialized) Initialize();
        isPlaying = true;
        SetMotorsEnabled(!animatedMode);
    }
}
