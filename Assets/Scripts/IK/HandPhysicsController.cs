using UnityEngine;


[RequireComponent(typeof(Rigidbody2D))]
public class HandPhysicsController : MonoBehaviour
{
    public Transform target;
    
    public float followForce = 30f;
    
    public float maxForce = 200f;
    
    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        Vector2 direction =
            target.position
            -
            transform.position;
        
        Vector2 force =
            direction
            *
            followForce;
        
        force =
            Vector2.ClampMagnitude(
                force,
                maxForce
            );
        
        rb.AddForce(force);
    }
}