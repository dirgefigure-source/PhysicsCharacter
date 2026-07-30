using UnityEngine;

[RequireComponent(typeof(HingeJoint2D))]
public class JointMotorController : MonoBehaviour
{

    public string jointName;


    public HingeJoint2D joint;


    [Header("Current Pose")]
    public float targetAngle;
    
    [Header("关节力量")]
    public float motorStrength = 100;
    [Header("旋转速度")]
    public float motorSpeed = 50;
    [Header("最大关节力度")]
    public float maxTorque = 200;
    [Header("当前肌肉状态")]
    [Range(0,1)]
    public float muscleStrength = 1;
    


    void Awake()
    {
        joint = GetComponent<HingeJoint2D>();
        jointName = gameObject.name;
    }



    void FixedUpdate()
    {

        float error =
            Mathf.DeltaAngle(
                joint.jointAngle,
                targetAngle
            );


        JointMotor2D motor =
            joint.motor;


        motor.motorSpeed =
            Mathf.Clamp(
                error * motorSpeed,
                -200,
                200
            );


        motor.maxMotorTorque =
            maxTorque * muscleStrength;;


        joint.motor = motor;

    }



    public void SetTargetAngle(float angle)
    {
        targetAngle = angle;
    }


}