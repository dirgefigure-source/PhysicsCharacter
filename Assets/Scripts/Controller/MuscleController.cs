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
    public float baseThighForce = 180f;
    public float baseCalfForce = 180f;


    [Range(0,1)]
    public float muscleMultiplier = 1f;
    
    public enum MuscleMode
    {
        Passive,
        Active
    }

    public MuscleMode mode = MuscleMode.Passive;

    public float activeStrength = 5f;
    
    [Header("目标角度")]
    public float thighTargetAngle = 0f;
    public float calfTargetAngle = 0f;
    
    public float leftThighTarget;
    public float rightThighTarget;

    public float leftCalfTarget;
    public float rightCalfTarget;
    
    [ContextMenu("Save Stand Pose")]
    public void SaveStandPose()
    {
        leftThighTarget = leftThigh.jointAngle;
        rightThighTarget = rightThigh.jointAngle;

        leftCalfTarget = leftCalf.jointAngle;
        rightCalfTarget = rightCalf.jointAngle;
    }
    
    void FixedUpdate()
    {
        ApplyMuscle(leftThigh, baseThighForce * muscleMultiplier, leftThighTarget);
        ApplyMuscle(rightThigh, baseThighForce * muscleMultiplier, rightThighTarget);
        ApplyMuscle(leftCalf, baseCalfForce * muscleMultiplier, leftCalfTarget);
        ApplyMuscle(rightCalf, baseCalfForce * muscleMultiplier, rightCalfTarget);
    }

    void ApplyMuscle(
        HingeJoint2D joint,
        float force,
        float targetAngle
    )
    {
        if (!joint) return;


        JointMotor2D motor = joint.motor;


        motor.maxMotorTorque = force;


        if(mode == MuscleMode.Passive)
        {
            motor.motorSpeed = 0;
        }
        else
        {
            float error =
                Mathf.DeltaAngle(
                    joint.jointAngle,
                    targetAngle
                );


            motor.motorSpeed =
                error * activeStrength;
        }


        joint.motor = motor;
        joint.useMotor = true;
    }
}
