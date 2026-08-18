using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maintains render-only copies of a runtime object one world-width to either
/// side. The authoritative object keeps the only colliders and Rigidbody2D.
/// </summary>
[DisallowMultipleComponent]
public sealed class CylindricalObjectVisualCopies2D : MonoBehaviour
{
    private sealed class SpriteCopy
    {
        public SpriteRenderer source;
        public SpriteRenderer left;
        public SpriteRenderer right;
    }

    private readonly List<SpriteCopy> copies = new();
    private MaterialPropertyBlock propertyBlock;
    private CylindricalWorld2D world;
    private GameObject leftRoot;
    private GameObject rightRoot;

    private void Awake()
    {
        EnsurePropertyBlock();
    }

    public void Initialize(CylindricalWorld2D cylindricalWorld)
    {
        // Initialize can be called while a stored entity is inactive. Unity
        // delays Awake for inactive components, so do not depend on Awake here.
        EnsurePropertyBlock();
        world = cylindricalWorld;
        RebuildCopies();
    }

    private void OnEnable()
    {
        SetCopiesEnabled(true);
    }

    private void OnDisable()
    {
        SetCopiesEnabled(false);
    }

    private void LateUpdate()
    {
        if (world == null) return;

        foreach (SpriteCopy copy in copies)
        {
            if (copy.source == null) continue;
            UpdateCopy(copy.source, copy.left, -world.Width);
            UpdateCopy(copy.source, copy.right, world.Width);
        }
    }

    private void RebuildCopies()
    {
        ClearCopies();
        if (world == null) return;

        leftRoot = new GameObject($"{name} (Left Visual Copy)");
        rightRoot = new GameObject($"{name} (Right Visual Copy)");
        // Keep copies under the authoritative object so inactive entities that
        // never receive OnDestroy still clean up their visual lifetime.
        leftRoot.transform.SetParent(transform, false);
        rightRoot.transform.SetParent(transform, false);

        foreach (SpriteRenderer source in GetComponentsInChildren<SpriteRenderer>(true))
        {
            SpriteCopy copy = new()
            {
                source = source,
                left = CreateCopy(source, leftRoot.transform),
                right = CreateCopy(source, rightRoot.transform)
            };
            copies.Add(copy);
            UpdateCopy(source, copy.left, -world.Width);
            UpdateCopy(source, copy.right, world.Width);
        }
    }

    private static SpriteRenderer CreateCopy(SpriteRenderer source, Transform root)
    {
        GameObject copyObject = new($"{source.name} (Visual Copy)");
        copyObject.layer = source.gameObject.layer;
        copyObject.transform.SetParent(root, false);

        SpriteRenderer copy = copyObject.AddComponent<SpriteRenderer>();
        copy.sharedMaterials = source.sharedMaterials;
        copy.drawMode = source.drawMode;
        copy.tileMode = source.tileMode;
        copy.maskInteraction = source.maskInteraction;
        copy.spriteSortPoint = source.spriteSortPoint;
        copy.sortingLayerID = source.sortingLayerID;
        copy.sortingOrder = source.sortingOrder;
        return copy;
    }

    private void UpdateCopy(
        SpriteRenderer source,
        SpriteRenderer copy,
        float offsetX)
    {
        if (copy == null) return;
        EnsurePropertyBlock();

        copy.transform.SetPositionAndRotation(
            source.transform.position + Vector3.right * offsetX,
            source.transform.rotation);
        copy.transform.localScale = DivideScale(
            source.transform.lossyScale,
            copy.transform.parent.lossyScale);
        copy.sprite = source.sprite;
        copy.color = source.color;
        copy.flipX = source.flipX;
        copy.flipY = source.flipY;
        copy.size = source.size;
        copy.enabled = source.enabled && source.gameObject.activeInHierarchy;

        source.GetPropertyBlock(propertyBlock);
        copy.SetPropertyBlock(propertyBlock);
    }

    private static Vector3 DivideScale(Vector3 worldScale, Vector3 parentWorldScale)
    {
        return new Vector3(
            SafeDivide(worldScale.x, parentWorldScale.x),
            SafeDivide(worldScale.y, parentWorldScale.y),
            SafeDivide(worldScale.z, parentWorldScale.z));
    }

    private static float SafeDivide(float value, float divisor) =>
        Mathf.Approximately(divisor, 0f) ? value : value / divisor;

    private void EnsurePropertyBlock()
    {
        propertyBlock ??= new MaterialPropertyBlock();
    }

    private void ClearCopies()
    {
        copies.Clear();
        DestroyRoot(leftRoot);
        DestroyRoot(rightRoot);
        leftRoot = null;
        rightRoot = null;
    }

    private void SetCopiesEnabled(bool enabled)
    {
        foreach (SpriteCopy copy in copies)
        {
            if (copy.left != null) copy.left.enabled = enabled;
            if (copy.right != null) copy.right.enabled = enabled;
        }
    }

    private static void DestroyRoot(GameObject root)
    {
        if (root == null) return;
        // Detach during an explicit rebuild so deferred Destroy objects are not
        // found by the next GetComponentsInChildren scan.
        root.transform.SetParent(null, true);
        if (Application.isPlaying)
            Destroy(root);
        else
            DestroyImmediate(root);
    }

    private void OnDestroy()
    {
        // The roots are children and Unity destroys them with this GameObject.
        // Do not detach them here, especially for components that never Awoke.
        copies.Clear();
        leftRoot = null;
        rightRoot = null;
    }
}
