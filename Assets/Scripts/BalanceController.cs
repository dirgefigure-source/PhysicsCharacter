using UnityEngine;

public class BalanceController : MonoBehaviour
{
    public Rigidbody2D torso;

    public float balanceForce = 50f;


    void FixedUpdate()
    {
        float angle = torso.rotation;

        float correction = -angle * balanceForce;

        torso.AddTorque(correction);
    }
}