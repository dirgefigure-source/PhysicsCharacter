using UnityEngine;
using Binding = CharacterRig2D.JointBinding;

/// <summary>
/// Owns Rigidbody2D mode switching, joint motors and upright-body forces.
/// </summary>
public sealed class CharacterMotorDriver2D
{
    public struct JointSettings
    {
        public float proportionalGain;
        public float derivativeGain;
        public float maxMotorSpeed;
        public float maxMotorTorque;
        public bool clampToJointLimits;
    }

    public struct UprightSettings
    {
        public bool enabled;
        public float worldAngle;
        public float proportionalGain;
        public float derivativeGain;
        public float maxAngularAcceleration;
        public float upwardForce;
    }

    private readonly CharacterRig2D rig;
    private readonly RigidbodyType2D[] originalBodyTypes;
    private readonly Rigidbody2D leftFootBody;
    private readonly Rigidbody2D rightFootBody;
    private readonly RigidbodyConstraints2D leftFootOriginalConstraints;
    private readonly RigidbodyConstraints2D rightFootOriginalConstraints;

    public CharacterMotorDriver2D(CharacterRig2D rig)
    {
        this.rig = rig;
        originalBodyTypes = new RigidbodyType2D[rig.AllBodies.Length];
        for (int i = 0; i < rig.AllBodies.Length; i++)
            originalBodyTypes[i] = rig.AllBodies[i].bodyType;

        leftFootBody = rig.FindBinding("LeftFoot").joint.attachedRigidbody;
        rightFootBody = rig.FindBinding("RightFoot").joint.attachedRigidbody;
        leftFootOriginalConstraints = leftFootBody.constraints;
        rightFootOriginalConstraints = rightFootBody.constraints;
    }

    public void SetMotorsEnabled(bool enabled)
    {
        foreach (Binding binding in rig.Bindings)
        {
            if (binding.joint != null)
                binding.joint.useMotor = enabled;
        }
    }

    public void DriveJoint(Binding binding, float target, JointSettings settings)
    {
        if (settings.clampToJointLimits && binding.joint.useLimits)
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
        float speed = settings.proportionalGain * error
                    - settings.derivativeGain * relativeAngularVelocity;

        JointMotor2D motor = binding.joint.motor;
        motor.motorSpeed = Mathf.Clamp(
            speed, -settings.maxMotorSpeed, settings.maxMotorSpeed);
        motor.maxMotorTorque = settings.maxMotorTorque;
        binding.joint.motor = motor;
    }

    public void ApplyUpright(UprightSettings settings)
    {
        if (!settings.enabled || rig.CentralBody == null) return;

        rig.CentralBody.AddForce(Vector2.up * settings.upwardForce, ForceMode2D.Force);
        float angleError = Mathf.DeltaAngle(rig.CentralBody.rotation, settings.worldAngle);
        float angularAcceleration = settings.proportionalGain * angleError
                                  - settings.derivativeGain * rig.CentralBody.angularVelocity;
        angularAcceleration = Mathf.Clamp(
            angularAcceleration,
            -settings.maxAngularAcceleration,
            settings.maxAngularAcceleration);
        float torque = angularAcceleration * Mathf.Deg2Rad * rig.CentralBody.inertia;
        rig.CentralBody.AddTorque(torque, ForceMode2D.Force);
    }

    public void ApplyCharacterMode(bool animatedMode)
    {
        for (int i = 0; i < rig.AllBodies.Length; i++)
        {
            Rigidbody2D body = rig.AllBodies[i];
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = animatedMode ? RigidbodyType2D.Kinematic : originalBodyTypes[i];
        }

        leftFootBody.constraints = leftFootOriginalConstraints;
        rightFootBody.constraints = rightFootOriginalConstraints;
        if (animatedMode)
            SetMotorsEnabled(false);
    }

    public void RestoreBodyTypes()
    {
        for (int i = 0; i < rig.AllBodies.Length && i < originalBodyTypes.Length; i++)
            rig.AllBodies[i].bodyType = originalBodyTypes[i];
    }
}
