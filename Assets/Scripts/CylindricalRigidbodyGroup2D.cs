using System;
using UnityEngine;

/// <summary>
/// Wraps an articulated Rigidbody2D character as one unit across a cylindrical seam.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class CylindricalRigidbodyGroup2D : MonoBehaviour
{
    [SerializeField] private CylindricalWorld2D world;
    [SerializeField] private Rigidbody2D anchorBody;
    [SerializeField] private bool wrapDynamicBodiesAutomatically = true;

    private Rigidbody2D[] bodies;
    private RigidbodyInterpolation2D[] savedInterpolationModes;
    private bool interpolationSuppressed;
    private float interpolationSuppressedAtFixedTime;
    private bool kinematicTeleportPending;

    public CylindricalWorld2D World => world;

    private void Awake()
    {
        bodies = GetComponentsInChildren<Rigidbody2D>(true);
        savedInterpolationModes = new RigidbodyInterpolation2D[bodies.Length];
        if (anchorBody == null)
        {
            Transform bodyTransform = FindDescendant(transform, "Body");
            if (bodyTransform != null)
                bodyTransform.TryGetComponent(out anchorBody);
        }

        if (world == null)
            world = FindFirstObjectByType<CylindricalWorld2D>();
        if (world == null)
            throw new InvalidOperationException("No CylindricalWorld2D exists in the scene.");
        if (anchorBody == null)
            throw new InvalidOperationException("Cylindrical Rigidbody Group has no anchor Rigidbody2D.");
    }

    private void FixedUpdate()
    {
        RestoreInterpolationAfterTeleportFrame();

        if (!wrapDynamicBodiesAutomatically || anchorBody.bodyType == RigidbodyType2D.Kinematic)
            return;
        WrapCurrentBodies();
    }

    public Vector2 WrapKinematicTarget(Vector2 target)
    {
        kinematicTeleportPending = false;
        if (world == null || !world.TryGetWrapOffset(target.x, out float offsetX))
            return target;

        SuppressInterpolationForTeleportFrame();
        kinematicTeleportPending = true;
        target.x += offsetX;
        return target;
    }

    /// <summary>
    /// Returns true once for a kinematic seam crossing. The pose driver must
    /// teleport with Rigidbody2D.position instead of MovePosition so Box2D
    /// does not interpret the world-width jump as an extreme collision speed.
    /// </summary>
    public bool ConsumeKinematicTeleport()
    {
        bool pending = kinematicTeleportPending;
        kinematicTeleportPending = false;
        return pending;
    }

    public bool WrapCurrentBodies()
    {
        if (world == null || anchorBody == null ||
            !world.TryGetWrapOffset(anchorBody.position.x, out float offsetX))
            return false;

        SuppressInterpolationForTeleportFrame();
        Vector2 offset = Vector2.right * offsetX;
        foreach (Rigidbody2D body in bodies)
            body.position += offset;
        Physics2D.SyncTransforms();
        return true;
    }

    private void SuppressInterpolationForTeleportFrame()
    {
        if (interpolationSuppressed)
            return;

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody2D body = bodies[i];
            if (body == null) continue;
            savedInterpolationModes[i] = body.interpolation;
            body.interpolation = RigidbodyInterpolation2D.None;
        }

        interpolationSuppressed = true;
        interpolationSuppressedAtFixedTime = Time.fixedTime;
    }

    private void RestoreInterpolationAfterTeleportFrame()
    {
        if (!interpolationSuppressed ||
            Time.fixedTime <= interpolationSuppressedAtFixedTime + Time.fixedDeltaTime * 0.5f)
            return;

        RestoreInterpolation();
    }

    private void RestoreInterpolation()
    {
        if (!interpolationSuppressed)
            return;

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody2D body = bodies[i];
            if (body != null)
                body.interpolation = savedInterpolationModes[i];
        }
        interpolationSuppressed = false;
    }

    private void OnDisable()
    {
        RestoreInterpolation();
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(candidate.name.Trim(), objectName, StringComparison.Ordinal))
                return candidate;
        }
        return null;
    }
}
