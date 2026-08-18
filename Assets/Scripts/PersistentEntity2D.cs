using UnityEngine;

/// <summary>
/// Gives a scene object a stable identity for the persistence prototype.
/// </summary>
[DisallowMultipleComponent]
public sealed class PersistentEntity2D : MonoBehaviour
{
    [SerializeField] private string persistentId;
    [SerializeField] private string typeId;
    [SerializeField] private bool stored;

    public string PersistentId => persistentId;
    public string TypeId => typeId;
    public bool Stored => stored;

    public void Initialize(string instanceId, string objectTypeId, bool isStored = false)
    {
        persistentId = instanceId;
        typeId = objectTypeId;
        stored = isStored;
    }

    public void SetStored(bool value) => stored = value;
}
