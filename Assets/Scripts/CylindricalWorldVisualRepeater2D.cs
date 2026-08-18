using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates render-only copies of static sprite scenery on both sides of a wrapped world.
/// No colliders, rigidbodies, or gameplay scripts are copied.
/// </summary>
[DisallowMultipleComponent]
public sealed class CylindricalWorldVisualRepeater2D : MonoBehaviour
{
    [SerializeField] private CylindricalWorld2D world;
    [SerializeField] private GameObject[] sourceRoots = Array.Empty<GameObject>();

    private readonly List<GameObject> generatedRoots = new();

    private void Awake()
    {
        if (world == null)
            world = GetComponent<CylindricalWorld2D>();
        if (world == null)
            throw new InvalidOperationException("Visual Repeater requires a CylindricalWorld2D.");

        Rebuild();
    }

    [ContextMenu("Rebuild Visual Copies")]
    public void Rebuild()
    {
        ClearGeneratedCopies();
        CreateSideCopies(-world.Width, "Left");
        CreateSideCopies(world.Width, "Right");
    }

    private void CreateSideCopies(float offsetX, string sideName)
    {
        GameObject sideRoot = new($"Cylindrical Visual Copies ({sideName})");
        sideRoot.transform.SetParent(transform, false);
        generatedRoots.Add(sideRoot);

        foreach (GameObject sourceRoot in sourceRoots)
        {
            if (sourceRoot == null) continue;
            foreach (SpriteRenderer source in sourceRoot.GetComponentsInChildren<SpriteRenderer>(true))
                CreateSpriteCopy(source, sideRoot.transform, offsetX);
        }
    }

    private static void CreateSpriteCopy(
        SpriteRenderer source,
        Transform sideRoot,
        float offsetX)
    {
        GameObject copyObject = new($"{source.name} (Visual Copy)");
        copyObject.layer = source.gameObject.layer;
        copyObject.transform.SetParent(sideRoot, false);
        copyObject.transform.SetPositionAndRotation(
            source.transform.position + Vector3.right * offsetX,
            source.transform.rotation);
        copyObject.transform.localScale = source.transform.lossyScale;

        SpriteRenderer copy = copyObject.AddComponent<SpriteRenderer>();
        copy.sprite = source.sprite;
        copy.color = source.color;
        copy.flipX = source.flipX;
        copy.flipY = source.flipY;
        copy.drawMode = source.drawMode;
        copy.size = source.size;
        copy.tileMode = source.tileMode;
        copy.maskInteraction = source.maskInteraction;
        copy.spriteSortPoint = source.spriteSortPoint;
        copy.sortingLayerID = source.sortingLayerID;
        copy.sortingOrder = source.sortingOrder;
        copy.sharedMaterials = source.sharedMaterials;
        copy.enabled = source.enabled && source.gameObject.activeInHierarchy;

        MaterialPropertyBlock properties = new();
        source.GetPropertyBlock(properties);
        copy.SetPropertyBlock(properties);
    }

    private void ClearGeneratedCopies()
    {
        foreach (GameObject generatedRoot in generatedRoots)
        {
            if (generatedRoot != null)
            {
                if (Application.isPlaying)
                    Destroy(generatedRoot);
                else
                    DestroyImmediate(generatedRoot);
            }
        }
        generatedRoots.Clear();
    }

    private void OnDestroy()
    {
        ClearGeneratedCopies();
    }
}
