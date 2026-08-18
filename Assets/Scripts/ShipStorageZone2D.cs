using UnityEngine;

/// <summary>
/// Minimal ship-storage validation zone. Stored entities remain part of the
/// planet state but are inactive in the surface scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShipStorageZone2D : MonoBehaviour
{
    [SerializeField] private Vector2 zoneSize = new(4f, 2.5f);
    [SerializeField] private Color zoneColor = new(0.2f, 1f, 0.45f, 0.85f);
    [SerializeField, Min(0.01f)] private float borderWidth = 0.06f;

    private LineRenderer border;
    private Material borderMaterial;
    private PlayerPickupController2D playerPickup;

    private void Awake()
    {
        BuildBorder();
        playerPickup = FindFirstObjectByType<PlayerPickupController2D>();
    }

    public bool Contains(Vector2 worldPosition)
    {
        Vector2 local = transform.InverseTransformPoint(worldPosition);
        Vector2 halfSize = zoneSize * 0.5f;
        return Mathf.Abs(local.x) <= halfSize.x &&
               Mathf.Abs(local.y) <= halfSize.y;
    }

    public bool TryGetStoredEntity(out PersistentEntity2D storedEntity)
    {
        storedEntity = null;
        foreach (PersistentEntity2D candidate in FindObjectsByType<PersistentEntity2D>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (!candidate.Stored) continue;
            if (storedEntity == null || string.CompareOrdinal(
                    candidate.PersistentId,
                    storedEntity.PersistentId) < 0)
                storedEntity = candidate;
        }
        return storedEntity != null;
    }

    public int StoredCount
    {
        get
        {
            int count = 0;
            foreach (PersistentEntity2D candidate in FindObjectsByType<PersistentEntity2D>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (candidate.Stored) count++;
            return count;
        }
    }

    private void BuildBorder()
    {
        border = gameObject.AddComponent<LineRenderer>();
        border.useWorldSpace = false;
        border.loop = true;
        border.positionCount = 4;
        border.startWidth = borderWidth;
        border.endWidth = borderWidth;
        border.startColor = zoneColor;
        border.endColor = zoneColor;
        border.sortingOrder = 4;

        Vector2 half = zoneSize * 0.5f;
        border.SetPosition(0, new Vector3(-half.x, -half.y, 0f));
        border.SetPosition(1, new Vector3(-half.x, half.y, 0f));
        border.SetPosition(2, new Vector3(half.x, half.y, 0f));
        border.SetPosition(3, new Vector3(half.x, -half.y, 0f));

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            borderMaterial = new Material(shader);
            border.material = borderMaterial;
        }
    }

    private void OnGUI()
    {
        if (playerPickup == null || !Contains(playerPickup.InteractionPosition)) return;

        string message = playerPickup.IsCarrying
            ? "[E] Store carried object"
            : StoredCount > 0
                ? $"[E] Take stored object ({StoredCount})"
                : "Ship storage is empty";
        GUI.Box(new Rect(12f, 112f, 260f, 30f), message);
    }

    private void OnDestroy()
    {
        if (borderMaterial != null)
            Destroy(borderMaterial);
    }
}
