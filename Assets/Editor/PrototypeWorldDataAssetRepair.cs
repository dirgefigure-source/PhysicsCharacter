using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PrototypeWorldDataAssetRepair
{
    private const string CatalogPath = "Assets/WorldData/WorldObjectCatalog.asset";
    private const string PrefabPath = "Assets/WorldData/CargoCrate.prefab";

    static PrototypeWorldDataAssetRepair()
    {
        EditorApplication.delayCall += EnsurePrototypeAssets;
    }

    [MenuItem("Tools/World Data/Repair Prototype Catalog")]
    public static void EnsurePrototypeAssets()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        WorldObjectCatalog catalog =
            AssetDatabase.LoadAssetAtPath<WorldObjectCatalog>(CatalogPath);

        if (prefab == null)
            prefab = RebuildCargoCratePrefab();
        if (prefab == null) return;
        if (catalog == null)
        {
            Debug.LogError($"Cannot load world object Catalog at '{CatalogPath}'.");
            return;
        }

        SerializedObject serializedCatalog = new(catalog);
        SerializedProperty definitions = serializedCatalog.FindProperty("definitions");
        definitions.arraySize = 1;
        SerializedProperty entry = definitions.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("typeId").stringValue = "cargo_crate";
        entry.FindPropertyRelative("displayName").stringValue = "Cargo Crate";
        entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;

        bool changed = serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
        if (changed)
        {
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        if (!catalog.TryGet("cargo_crate", out WorldObjectCatalog.Entry resolved))
            Debug.LogError("Imported WorldObjectCatalog still cannot resolve 'cargo_crate'.");
        else if (resolved.Prefab == null)
            Debug.LogError("Imported 'cargo_crate' definition still has a null Prefab reference.");
        else
            Debug.Log($"WorldObjectCatalog validated: cargo_crate -> {resolved.Prefab.name}.");
    }

    private static GameObject RebuildCargoCratePrefab()
    {
        AssetDatabase.DeleteAsset(PrefabPath);

        GameObject root = new("CargoCrate");
        try
        {
            root.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

            SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadSquareSprite();
            renderer.color = new Color(0.15f, 0.85f, 1f, 1f);
            renderer.sortingOrder = 2;

            root.AddComponent<BoxCollider2D>();
            Rigidbody2D body = root.AddComponent<Rigidbody2D>();
            body.linearDamping = 0.5f;
            body.angularDamping = 0.1f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            root.AddComponent<PersistentEntity2D>();
            CylindricalRigidbodyGroup2D wrap =
                root.AddComponent<CylindricalRigidbodyGroup2D>();
            SerializedObject serializedWrap = new(wrap);
            serializedWrap.FindProperty("anchorBody").objectReferenceValue = body;
            serializedWrap.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            if (saved == null)
                Debug.LogError($"Unity failed to create prototype Prefab at '{PrefabPath}'.");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Sprite LoadSquareSprite()
    {
        const string squarePath =
            "Packages/com.unity.2d.sprite/Editor/ObjectMenuCreation/DefaultAssets/Textures/v2/Square.png";
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(squarePath))
        {
            if (asset is Sprite sprite)
                return sprite;
        }

        Debug.LogError($"Cannot load the Unity square Sprite at '{squarePath}'.");
        return null;
    }
}
