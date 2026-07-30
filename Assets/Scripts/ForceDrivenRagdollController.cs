using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A force-profile experiment inspired by physics-first character animation.
/// It never changes transforms, creates anchors, or enables joint motors. The
/// standing and walking behaviours are only continuous forces and torques.
/// </summary>
[DisallowMultipleComponent]
public sealed class ForceDrivenRagdollController : MonoBehaviour
{
    [Header("Standing force profile")]
    [SerializeField, Min(0f)] private float torsoUprightTorque = 18f;
    [SerializeField, Min(0f)] private float torsoAngularDamping = 3f;
    [SerializeField, Min(0f)] private float getUpForce = 90f;
    [SerializeField, Min(0.1f)] private float standingTorsoHeight = 3.1f;
    [SerializeField, Min(0f)] private float footPlantForce = 10f;
    [SerializeField, Min(0f)] private float supportCenterForce = 28f;
    [SerializeField, Min(0f)] private float supportCenterDamping = 6f;
    [SerializeField, Min(0f)] private float maximumSupportCorrection = 20f;

    [Header("Knee muscle profile")]
    [SerializeField, Range(0f, 35f)] private float kneeRestAngle = 4f;
    [SerializeField, Min(0f)] private float kneeSpringStrength = 0.2f;
    [SerializeField, Min(0f)] private float kneeDamping = 0.08f;
    [SerializeField, Min(0f)] private float maximumKneeTorque = 4f;
    [SerializeField, Range(0f, 85f)] private float swingKneeAngle = 55f;
    [SerializeField, Min(0f)] private float stanceKneeStrength = 0.35f;
    [SerializeField, Min(0f)] private float swingKneeStrength = 0.16f;
    [SerializeField, Min(0f)] private float walkingMaximumKneeTorque = 5f;

    [Header("Ankle support profile")]
    [SerializeField, Range(-25f, 30f)] private float ankleRestAngle = 0f;
    [SerializeField, Min(0f)] private float standingAnkleStrength = 0.1f;
    [SerializeField, Min(0f)] private float stanceAnkleStrength = 0.28f;
    [SerializeField, Min(0f)] private float ankleDamping = 0.1f;
    [SerializeField, Min(0f)] private float maximumAnkleTorque = 2.5f;

    [Header("Walk force profile")]
    [SerializeField, Min(0f)] private float swingForwardForce = 15f;
    [SerializeField, Min(0f)] private float swingLiftForce = 18f;
    [SerializeField, Min(0f)] private float stanceBackwardForce = 14f;
    [SerializeField, Min(0.05f)] private float minimumSwingTime = 0.22f;

    private Rigidbody2D torso;
    private Rigidbody2D leftFoot;
    private Rigidbody2D rightFoot;
    private HingeJoint2D leftKnee;
    private HingeJoint2D rightKnee;
    private HingeJoint2D leftAnkle;
    private HingeJoint2D rightAnkle;
    private int bodyPartLayer;
    private float horizontalInput;
    private bool leftLegIsSwinging = true;
    private float swingElapsed;
    private bool swingFootHasClearedGround;

    private void Awake()
    {
        Transform rig = transform.Find("Physics");
        torso = FindBody(rig, "Body");
        leftFoot = FindBody(rig, "LeftFoot");
        rightFoot = FindBody(rig, "RightFoot");
        leftKnee = FindJoint(rig, "LeftCalf");
        rightKnee = FindJoint(rig, "RightCalf");
        leftAnkle = FindJoint(rig, "LeftFoot");
        rightAnkle = FindJoint(rig, "RightFoot");
        bodyPartLayer = LayerMask.NameToLayer("BodyPart");
        if (torso == null || leftFoot == null || rightFoot == null)
        {
            Debug.LogError("ForceDrivenRagdollController needs torso and both feet.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            horizontalInput = 0f;
            return;
        }

        horizontalInput = (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed ? 1f : 0f) -
                          (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed ? 1f : 0f);
    }

    private void FixedUpdate()
    {
        ApplyStandingProfile();
        if (Mathf.Abs(horizontalInput) > 0.01f)
            ApplyWalkProfile();
        else
        {
            swingElapsed = 0f;
            swingFootHasClearedGround = false;
            ApplyKneeMuscle(leftKnee, kneeRestAngle, kneeSpringStrength, maximumKneeTorque);
            ApplyKneeMuscle(rightKnee, kneeRestAngle, kneeSpringStrength, maximumKneeTorque);
            ApplyAnkleSupport(leftAnkle, standingAnkleStrength);
            ApplyAnkleSupport(rightAnkle, standingAnkleStrength);
        }
    }

    private void ApplyStandingProfile()
    {
        float angleToUpright = Mathf.DeltaAngle(torso.rotation, 0f);
        torso.AddTorque(angleToUpright * torsoUprightTorque - torso.angularVelocity * torsoAngularDamping);

        RaycastHit2D ground = Physics2D.Raycast(torso.worldCenterOfMass, Vector2.down, standingTorsoHeight + 1f, ~(1 << bodyPartLayer));
        if (ground.collider != null)
        {
            float heightError = standingTorsoHeight - ground.distance;
            if (heightError > 0f)
                torso.AddForce(Vector2.up * (heightError * getUpForce));
        }

        bool leftGrounded = IsGrounded(leftFoot);
        bool rightGrounded = IsGrounded(rightFoot);
        if (leftGrounded) leftFoot.AddForce(Vector2.down * footPlantForce);
        if (rightGrounded) rightFoot.AddForce(Vector2.down * footPlantForce);

        // In walking mode the planted foot, rather than a body push, provides
        // the horizontal drive. Do not hold the torso at the two-foot centre.
        if (Mathf.Abs(horizontalInput) < 0.01f)
            ApplySupportCentering(leftGrounded, rightGrounded);


    }

    private void ApplyWalkProfile()
    {
        float direction = Mathf.Sign(horizontalInput);
        Rigidbody2D swingFoot = leftLegIsSwinging ? leftFoot : rightFoot;
        Rigidbody2D stanceFoot = leftLegIsSwinging ? rightFoot : leftFoot;
        HingeJoint2D swingKnee = leftLegIsSwinging ? leftKnee : rightKnee;
        HingeJoint2D stanceKnee = leftLegIsSwinging ? rightKnee : leftKnee;
        HingeJoint2D stanceAnkle = leftLegIsSwinging ? rightAnkle : leftAnkle;

        // A step is permitted only while the opposite foot is a real support.
        // If it loses contact, do not start the other leg's swing and create a
        // two-feet-in-the-air state.
        if (!IsGrounded(stanceFoot))
        {
            ApplyKneeMuscle(stanceKnee, kneeRestAngle, stanceKneeStrength, walkingMaximumKneeTorque);
            ApplyKneeMuscle(swingKnee, kneeRestAngle, kneeSpringStrength, maximumKneeTorque);
            return;
        }

        swingElapsed += Time.fixedDeltaTime;
        ApplyKneeMuscle(stanceKnee, kneeRestAngle, stanceKneeStrength, walkingMaximumKneeTorque);
        ApplyKneeMuscle(swingKnee, swingKneeAngle, swingKneeStrength, walkingMaximumKneeTorque);
        ApplyAnkleSupport(stanceAnkle, stanceAnkleStrength);

        // The planted leg is the only propulsion source. It presses into the
        // floor while its knee remains near straight.
        stanceFoot.AddForce(Vector2.down * footPlantForce);
        stanceFoot.AddForce(Vector2.left * (direction * stanceBackwardForce));

        // Lift only until the foot has cleared the floor, then carry it ahead.
        bool swingGrounded = IsGrounded(swingFoot);
        if (swingGrounded)
            swingFoot.AddForce(Vector2.up * swingLiftForce);
        else
            swingFootHasClearedGround = true;
        swingFoot.AddForce(Vector2.right * (direction * swingForwardForce));

        // Contact after an actual lift, not a timer alone, decides when the
        // other leg may begin its step.
        if (swingElapsed >= minimumSwingTime && swingFootHasClearedGround && swingGrounded)
        {
            leftLegIsSwinging = !leftLegIsSwinging;
            swingElapsed = 0f;
            swingFootHasClearedGround = false;
        }
    }

    private bool IsGrounded(Rigidbody2D foot)
    {
        Collider2D[] contacts = new Collider2D[8];
        foreach (Collider2D collider in foot.GetComponents<Collider2D>())
        {
            int count = collider.GetContacts(contacts);
            for (int i = 0; i < count; i++)
                if (contacts[i] != null && contacts[i].gameObject.layer != bodyPartLayer)
                    return true;
        }
        return false;
    }

    private void ApplyKneeMuscle(HingeJoint2D knee, float targetAngle, float strength, float torqueLimit)
    {
        if (knee == null || knee.connectedBody == null) return;

        // Equal and opposite torque makes this an internal joint force:
        // it corrects the calf-to-thigh angle without rotating the entire
        // character toward a world-space pose.
        float angleError = Mathf.DeltaAngle(knee.jointAngle, targetAngle);
        float torque = Mathf.Clamp(
            angleError * strength - knee.jointSpeed * kneeDamping,
            -torqueLimit,
            torqueLimit);

        knee.attachedRigidbody.AddTorque(torque);
        knee.connectedBody.AddTorque(-torque);
    }

    private void ApplyAnkleSupport(HingeJoint2D ankle, float strength)
    {
        if (ankle == null || ankle.connectedBody == null) return;

        float angleError = Mathf.DeltaAngle(ankle.jointAngle, ankleRestAngle);
        float torque = Mathf.Clamp(
            angleError * strength - ankle.jointSpeed * ankleDamping,
            -maximumAnkleTorque,
            maximumAnkleTorque);

        ankle.attachedRigidbody.AddTorque(torque);
        ankle.connectedBody.AddTorque(-torque);
    }

    private void ApplySupportCentering(bool leftGrounded, bool rightGrounded)
    {
        if (!leftGrounded || !rightGrounded) return;

        // Keep the torso's mass over the centre of its two planted feet.
        // This is a bounded horizontal force, not a position correction or
        // a foot anchor; when either foot lifts it is completely disabled.
        float supportX = (leftFoot.worldCenterOfMass.x + rightFoot.worldCenterOfMass.x) * 0.5f;
        float offset = torso.worldCenterOfMass.x - supportX;
        float force = Mathf.Clamp(
            -offset * supportCenterForce - torso.linearVelocity.x * supportCenterDamping,
            -maximumSupportCorrection,
            maximumSupportCorrection);
        torso.AddForce(Vector2.right * force);
    }

    private static Rigidbody2D FindBody(Transform rig, string partName)
    {
        if (rig == null) return null;
        foreach (Rigidbody2D body in rig.GetComponentsInChildren<Rigidbody2D>(true))
            if (body.name.Trim() == partName)
                return body;
        return null;
    }

    private static HingeJoint2D FindJoint(Transform rig, string partName)
    {
        Rigidbody2D body = FindBody(rig, partName);
        return body != null ? body.GetComponent<HingeJoint2D>() : null;
    }
}
