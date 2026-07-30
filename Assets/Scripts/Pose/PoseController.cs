using UnityEngine;


public class PoseController : MonoBehaviour
{
    public PoseData currentPose;
    
    public JointMotorController[] motors;

    void FixedUpdate()
    {
        ApplyPose();
    }
    
    public void ApplyPose()
    {
        if(currentPose==null)
            return;
        
        foreach(var poseJoint in currentPose.joints)
        {
            foreach(var motor in motors)
            {
                if(string.Equals(motor.jointName.Trim(), poseJoint.jointName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {   
                    motor.SetTargetAngle(
                        poseJoint.angle
                    );
                    motor.motorStrength =
                        1
                        *
                        100;
                }
            }
        }
    }
    
    public void SetPose(PoseData pose)
    {
        currentPose = pose;
    }
}