using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Physics Character Lab/World Object Catalog")]
public sealed class WorldObjectCatalog : ScriptableObject
{
    // Definitions are embedded so Unity only imports this ScriptableObject as a native asset type.
    [Serializable]
    public sealed class Entry
    {
        [SerializeField] private string typeId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;

        public string TypeId => typeId;
        public string DisplayName => displayName;
        public GameObject Prefab => prefab;
    }

    [SerializeField] private Entry[] definitions = Array.Empty<Entry>();

    public bool TryGet(string typeId, out Entry definition)
    {
        foreach (Entry candidate in definitions)
        {
            if (candidate != null &&
                string.Equals(candidate.TypeId, typeId, StringComparison.Ordinal))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }

    private void OnValidate()
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (Entry definition in definitions)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.TypeId))
                continue;
            if (!ids.Add(definition.TypeId))
                Debug.LogError($"Duplicate world object type ID '{definition.TypeId}'.", this);
        }
    }
}
