using System;
using UnityEngine;

/// <summary>
/// Keeps the camera's screen-space relationship to a wrapped target stable.
/// The camera receives the same world-width jump as the target at the seam.
/// </summary>
[DefaultExecutionOrder(2000)]
public sealed class CylindricalCameraFollow2D : MonoBehaviour
{
    [SerializeField] private CylindricalWorld2D world;
    [SerializeField] private Transform target;
    [SerializeField] private bool followVertical;

    private Vector3 startingOffset;

    private void Awake()
    {
        if (world == null)
            world = FindFirstObjectByType<CylindricalWorld2D>();
        if (world == null)
            throw new InvalidOperationException("No CylindricalWorld2D exists for the camera.");
        if (target == null)
            throw new InvalidOperationException("Cylindrical Camera Follow has no target.");

        startingOffset = transform.position - target.position;
    }

    private void LateUpdate()
    {
        Vector3 position = transform.position;
        position.x = target.position.x + startingOffset.x;
        if (followVertical)
            position.y = target.position.y + startingOffset.y;
        transform.position = position;
    }
}
