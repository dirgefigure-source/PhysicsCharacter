using UnityEngine;


public class PoseRecorder : MonoBehaviour
{

    public JointMotorController[] motors;


    public PoseData targetPose;



    [ContextMenu("Record Current Pose")]
    public void Record()
    {

        targetPose.joints =
            new JointPose[motors.Length];


        for(int i=0;i<motors.Length;i++)
        {

            JointMotorController motor =
                motors[i];


            targetPose.joints[i]
                =
                new JointPose();


            targetPose.joints[i].jointName =
                motor.jointName;


            targetPose.joints[i].angle =
                motor.joint.jointAngle;


            Debug.Log(
                motor.jointName
                +
                " = "
                +
                motor.joint.jointAngle
            );

        }


#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetPose);
#endif


    }

}