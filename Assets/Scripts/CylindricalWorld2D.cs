using UnityEngine;

/// <summary>
/// Defines a horizontally wrapped 2D world and provides all seam-aware math.
/// Canonical X coordinates are in [MinX, MaxX).
/// </summary>
public sealed class CylindricalWorld2D : MonoBehaviour
{
    [SerializeField] private float minX = -9.45f;
    [SerializeField] private float maxX = 4.63f;
    [SerializeField] private bool drawBounds = true;
    [SerializeField] private Color boundsColor = new(0.2f, 0.9f, 1f, 0.8f);

    public float MinX => minX;
    public float MaxX => maxX;
    public float Width => Mathf.Max(0.0001f, maxX - minX);

    public float WrapX(float x)
    {
        return minX + Mathf.Repeat(x - minX, Width);
    }

    public Vector2 WrapPosition(Vector2 position)
    {
        position.x = WrapX(position.x);
        return position;
    }

    public float ShortestDeltaX(float fromX, float toX)
    {
        float delta = toX - fromX;
        float halfWidth = Width * 0.5f;
        return Mathf.Repeat(delta + halfWidth, Width) - halfWidth;
    }

    public Vector2 ShortestDelta(Vector2 from, Vector2 to)
    {
        return new Vector2(ShortestDeltaX(from.x, to.x), to.y - from.y);
    }

    public float ShortestDistance(Vector2 from, Vector2 to)
    {
        return ShortestDelta(from, to).magnitude;
    }

    public float GetNearestEquivalentX(float referenceX, float targetX)
    {
        return referenceX + ShortestDeltaX(referenceX, targetX);
    }

    public bool TryGetWrapOffset(float x, out float offset)
    {
        float wrappedX = WrapX(x);
        offset = wrappedX - x;
        return !Mathf.Approximately(offset, 0f);
    }

    private void OnValidate()
    {
        if (maxX <= minX)
            maxX = minX + 0.01f;
    }

    private void OnDrawGizmos()
    {
        if (!drawBounds) return;
        Gizmos.color = boundsColor;
        Vector3 center = transform.position;
        Gizmos.DrawLine(
            new Vector3(minX, center.y - 1000f, center.z),
            new Vector3(minX, center.y + 1000f, center.z));
        Gizmos.DrawLine(
            new Vector3(maxX, center.y - 1000f, center.z),
            new Vector3(maxX, center.y + 1000f, center.z));
    }
}
