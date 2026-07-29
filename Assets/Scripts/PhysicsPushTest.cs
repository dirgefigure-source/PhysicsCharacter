using UnityEngine;
using UnityEngine.InputSystem;

public class PhysicsPushTest : MonoBehaviour
{
    public Rigidbody2D target;

    public float force = 5f;

    // Update is called once per frame
    void FixedUpdate()
    {
        if (Keyboard.current.spaceKey.isPressed)
        {
            target.AddForce(Vector2.up * force);
        }

        if (Keyboard.current.aKey.isPressed)
        {
            target.AddForce(Vector2.left * force);
        }
    }
}
