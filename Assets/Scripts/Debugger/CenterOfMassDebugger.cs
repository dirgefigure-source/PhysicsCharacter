using UnityEngine;

public class CenterOfMassDebugger : MonoBehaviour
{
    public Rigidbody2D[] bodies;


    void OnDrawGizmos()
    {
        if(bodies == null || bodies.Length == 0)
            return;


        Vector2 center = Vector2.zero;
        float mass = 0;


        foreach(var rb in bodies)
        {
            center += rb.worldCenterOfMass * rb.mass;
            mass += rb.mass;
        }


        center /= mass;


        Gizmos.color = Color.red;
        Gizmos.DrawSphere(center,0.08f);
    }
}