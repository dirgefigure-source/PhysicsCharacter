using UnityEngine;


public class HandReachLimiter : MonoBehaviour
{
    public HingeJoint2D shoulderJoint;

    public Transform mouseTarget;
    
    public float maxDistance = 1.5f;
    
    void LateUpdate()
    {
        Vector3 shoulderPos = shoulderJoint.transform.TransformPoint(shoulderJoint.anchor);
        
        Vector2 dir =
            mouseTarget.position
            -
            shoulderPos;

        if(dir.magnitude > maxDistance)
        {
            dir =
                dir.normalized
                *
                maxDistance;
        }
        
        transform.position =
            shoulderPos
            +
            (Vector3)dir;
    }
}