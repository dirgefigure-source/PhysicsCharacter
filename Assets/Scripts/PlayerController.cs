using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public Rigidbody2D torso;

    public float moveForce = 10f;
    
    private Vector2 _direction;
    
    public void OnMove(InputAction.CallbackContext context)
    {
        _direction = context.ReadValue<Vector2>();
    }
    
    void Update()
    {

    }
    
    void FixedUpdate()
    {
        torso.AddForce(_direction * moveForce);
    }
}