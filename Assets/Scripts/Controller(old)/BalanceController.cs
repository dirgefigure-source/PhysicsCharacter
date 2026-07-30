using UnityEngine;

public class BalanceController : MonoBehaviour
{
    public Rigidbody2D torso;

    public HingeJoint2D leftThigh;
    public HingeJoint2D rightThigh;


    public float strength = 2f;


    void FixedUpdate()
    {
        float angle = torso.rotation;

        float speed = -angle * strength;


        JointMotor2D motor;


        motor = leftThigh.motor;
        motor.motorSpeed = speed;
        leftThigh.motor = motor;


        motor = rightThigh.motor;
        motor.motorSpeed = speed;
        rightThigh.motor = motor;
    }
}