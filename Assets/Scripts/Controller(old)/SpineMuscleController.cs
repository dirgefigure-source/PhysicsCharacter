using UnityEngine;

public class SpineMuscleController : MonoBehaviour
{
    public Rigidbody2D torso;
    public HingeJoint2D hipJoint;

    [Header("Target")]
    public float targetAngle = 0f;

    [Header("PD")]
    public float p = 5f;
    public float d = 0.5f;


    void FixedUpdate()
    {
        float angle = torso.rotation;

        float error = targetAngle - angle;

        float output =
            error * p -
            torso.angularVelocity * d;


        JointMotor2D motor = hipJoint.motor;

        motor.motorSpeed = output;

        hipJoint.motor = motor;
    }
}