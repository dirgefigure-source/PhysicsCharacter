using System;
using UnityEngine;

public class MuscleController : MonoBehaviour
{
    [Header("腿部关节")]
    public HingeJoint2D leftThigh;
    public HingeJoint2D rightThigh;

    public HingeJoint2D leftCalf;
    public HingeJoint2D rightCalf;
    
    [Header("关节力量")]
    public float thighForce = 180f;

    public float calfForce = 180f;

    void FixedUpdate()
    {
        ApplyMuscle(leftThigh, thighForce);
        ApplyMuscle(rightThigh, thighForce);
        
        ApplyMuscle(leftCalf, calfForce);
        ApplyMuscle(rightCalf, calfForce);
    }

    void ApplyMuscle(HingeJoint2D joint, float force)
    {
        if (!joint) return;

        JointMotor2D motor = joint.motor;
        motor.maxMotorTorque = force;
        motor.motorSpeed = 0;
        joint.motor = motor;
        joint.useMotor = true;
    }
}
