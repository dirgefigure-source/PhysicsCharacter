using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a planet's runtime objects from initial JSON or saved JSON.
/// Only transform and active state are persisted in this prototype.
/// </summary>
public sealed class PlanetPersistencePrototype : MonoBehaviour
{
    [Serializable]
    private sealed class EntityState
    {
        public string instanceId;
        public string typeId;
        public Vector3 position;
        public float rotation;
        public bool active;
        public bool stored;
    }

    [Serializable]
    private sealed class PlanetState
    {
        public int version = 2;
        public string planetId;
        public List<EntityState> entities = new();
    }

    [SerializeField] private string planetId = "prototype-planet-01";
    [SerializeField] private WorldObjectCatalog objectCatalog;
    [SerializeField] private TextAsset initialPlanetJson;
    [SerializeField] private bool loadSavedStateOnStart = true;
    [Header("Automatic Persistence")]
    [SerializeField] private bool autoSaveEnabled = true;
    [SerializeField, Min(0.1f)] private float changePollInterval = 0.25f;
    [SerializeField, Min(0.1f)] private float autoSaveDelay = 0.75f;

    private CylindricalWorld2D cylindricalWorld;
    private string lastObservedStateJson;
    private float pollTimer;
    private float dirtySince;
    private bool autosaveDirty;
    private bool persistenceReady;
    private bool suppressSaveOnDisable;
    private int baselineRefreshFrames;

    private readonly InputAction saveAction = new(
        "Save Planet", InputActionType.Button, "<Keyboard>/f5");
    private readonly InputAction loadAction = new(
        "Load Planet", InputActionType.Button, "<Keyboard>/f9");
    private readonly InputAction reenterAction = new(
        "Leave And Re-enter", InputActionType.Button, "<Keyboard>/f10");
    private readonly InputAction clearAction = new(
        "Clear Planet Save", InputActionType.Button, "<Keyboard>/f8");

    private string status = "Planet data loader ready";
    private string SavePath => Path.Combine(
        Application.persistentDataPath,
        $"planet-state-{planetId}.json");

    private void OnEnable()
    {
        saveAction.Enable();
        loadAction.Enable();
        reenterAction.Enable();
        clearAction.Enable();
    }

    private void Start()
    {
        if (objectCatalog == null || initialPlanetJson == null)
            throw new InvalidOperationException(
                "Planet Persistence Prototype requires an object catalog and initial planet JSON.");

        cylindricalWorld = FindFirstObjectByType<CylindricalWorld2D>();
        if (cylindricalWorld == null)
            throw new InvalidOperationException(
                "Planet Persistence Prototype requires a CylindricalWorld2D.");

        if (loadSavedStateOnStart && TryReadStateFile(SavePath, out PlanetState savedState))
            RebuildPlanet(savedState, "saved JSON");
        else if (TryParseState(initialPlanetJson.text, out PlanetState initialState))
            RebuildPlanet(initialState, "initial planet JSON");
    }

    private void Update()
    {
        if (saveAction.WasPressedThisFrame())
            SavePlanetState();
        if (loadAction.WasPressedThisFrame())
            LoadSavedState();
        if (clearAction.WasPressedThisFrame())
            ClearAndReload();
        if (reenterAction.WasPressedThisFrame())
            SaveAndReenter();

        TickAutomaticSave();
    }

    private void OnDisable()
    {
        // Unity may destroy inactive stored entities before this component is
        // disabled. Never rescan the teardown hierarchy here; persist only the
        // last snapshot captured while the scene was valid.
        if (autoSaveEnabled && persistenceReady && autosaveDirty &&
            !suppressSaveOnDisable)
            WriteLastObservedSnapshot();

        saveAction.Disable();
        loadAction.Disable();
        reenterAction.Disable();
        clearAction.Disable();
    }

    private void SavePlanetState(bool announce = true)
    {
        PlanetState state = CapturePlanetState(announce);
        File.WriteAllText(SavePath, JsonUtility.ToJson(state, true));
        lastObservedStateJson = JsonUtility.ToJson(state, false);
        autosaveDirty = false;
        dirtySince = 0f;
        status = announce
            ? $"Saved {state.entities.Count} generated object(s)"
            : $"Autosaved {state.entities.Count} generated object(s)";
        if (announce)
            Debug.Log($"{status}: {SavePath}", this);
    }

    public void NotifyWorldStateChanged()
    {
        if (!persistenceReady || suppressSaveOnDisable) return;

        PlanetState state = CapturePlanetState(false);
        lastObservedStateJson = JsonUtility.ToJson(state, false);
        autosaveDirty = true;
        dirtySince = Time.unscaledTime;
    }

    private void WriteLastObservedSnapshot()
    {
        if (string.IsNullOrEmpty(lastObservedStateJson)) return;

        PlanetState state = JsonUtility.FromJson<PlanetState>(lastObservedStateJson);
        if (state == null) return;
        File.WriteAllText(SavePath, JsonUtility.ToJson(state, true));
        autosaveDirty = false;
    }

    private PlanetState CapturePlanetState(bool logValidation)
    {
        PersistentEntity2D[] entities = FindObjectsByType<PersistentEntity2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        PlanetState state = new() { planetId = planetId };
        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (PersistentEntity2D entity in entities)
        {
            if (string.IsNullOrWhiteSpace(entity.PersistentId) ||
                string.IsNullOrWhiteSpace(entity.TypeId))
            {
                if (logValidation)
                    Debug.LogWarning(
                        $"Persistent object '{entity.name}' has incomplete identity and was not saved.",
                        entity);
                continue;
            }
            if (!ids.Add(entity.PersistentId))
            {
                if (logValidation)
                    Debug.LogError(
                        $"Duplicate persistent ID '{entity.PersistentId}'. The duplicate was not saved.",
                        entity);
                continue;
            }

            state.entities.Add(new EntityState
            {
                instanceId = entity.PersistentId,
                typeId = entity.TypeId,
                position = entity.transform.position,
                rotation = entity.transform.eulerAngles.z,
                active = entity.gameObject.activeSelf,
                stored = entity.Stored
            });
        }

        state.entities.Sort((left, right) => string.CompareOrdinal(
            left.instanceId,
            right.instanceId));
        return state;
    }

    private void TickAutomaticSave()
    {
        if (!autoSaveEnabled || suppressSaveOnDisable) return;

        if (baselineRefreshFrames > 0)
        {
            baselineRefreshFrames--;
            if (baselineRefreshFrames == 0)
            {
                lastObservedStateJson = JsonUtility.ToJson(
                    CapturePlanetState(false), false);
                autosaveDirty = false;
                persistenceReady = true;
                pollTimer = changePollInterval;
            }
            return;
        }
        if (!persistenceReady) return;

        pollTimer -= Time.unscaledDeltaTime;
        if (pollTimer > 0f) return;
        pollTimer = changePollInterval;

        PlanetState currentState = CapturePlanetState(false);
        string currentJson = JsonUtility.ToJson(currentState, false);
        if (!string.Equals(currentJson, lastObservedStateJson, StringComparison.Ordinal))
        {
            lastObservedStateJson = currentJson;
            autosaveDirty = true;
            dirtySince = Time.unscaledTime;
            return;
        }

        if (autosaveDirty && Time.unscaledTime - dirtySince >= autoSaveDelay)
            SavePlanetState(false);
    }

    private void LoadSavedState()
    {
        if (TryReadStateFile(SavePath, out PlanetState state))
            RebuildPlanet(state, "saved JSON");
        else
            status = "No compatible saved planet state";
    }

    private bool TryReadStateFile(string path, out PlanetState state)
    {
        state = null;
        return File.Exists(path) && TryParseState(File.ReadAllText(path), out state);
    }

    private bool TryParseState(string json, out PlanetState state)
    {
        state = JsonUtility.FromJson<PlanetState>(json);
        if (state != null && state.version == 2 && state.planetId == planetId)
            return true;

        state = null;
        return false;
    }

    private void RebuildPlanet(PlanetState state, string sourceName)
    {
        persistenceReady = false;
        autosaveDirty = false;
        CleanupLegacyOrphanVisualCopies();
        foreach (PersistentEntity2D existing in FindObjectsByType<PersistentEntity2D>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            existing.gameObject.SetActive(false);
            Destroy(existing.gameObject);
        }

        HashSet<string> instanceIds = new(StringComparer.Ordinal);
        int generated = 0;
        foreach (EntityState entityState in state.entities)
        {
            if (string.IsNullOrWhiteSpace(entityState.instanceId) ||
                !instanceIds.Add(entityState.instanceId))
            {
                Debug.LogError(
                    $"Planet '{planetId}' contains an empty or duplicate instance ID.", this);
                continue;
            }
            if (!objectCatalog.TryGet(entityState.typeId, out WorldObjectCatalog.Entry definition))
            {
                Debug.LogError(
                    $"Planet object '{entityState.instanceId}' uses unknown type '{entityState.typeId}'.",
                    this);
                continue;
            }
            if (definition.Prefab == null)
            {
                Debug.LogError(
                    $"World object type '{entityState.typeId}' has no valid Prefab reference.",
                    this);
                continue;
            }

            GameObject instance = Instantiate(
                definition.Prefab,
                entityState.position,
                Quaternion.Euler(0f, 0f, entityState.rotation));
            instance.name = $"{definition.DisplayName} [{entityState.instanceId}]";

            if (!instance.TryGetComponent(out PersistentEntity2D persistentEntity))
            {
                Debug.LogError(
                    $"World object prefab '{definition.Prefab.name}' has no PersistentEntity2D.",
                    definition.Prefab);
                Destroy(instance);
                continue;
            }

            persistentEntity.Initialize(
                entityState.instanceId,
                entityState.typeId,
                entityState.stored);
            instance.SetActive(entityState.active && !entityState.stored);
            CylindricalObjectVisualCopies2D visualCopies =
                instance.GetComponent<CylindricalObjectVisualCopies2D>();
            if (visualCopies == null)
                visualCopies = instance.AddComponent<CylindricalObjectVisualCopies2D>();
            visualCopies.Initialize(cylindricalWorld);
            generated++;
        }

        Physics2D.SyncTransforms();
        status = $"Generated {generated} object(s) from {sourceName}";
        Debug.Log(status, this);
        // Destroy() removes the previous generation at the end of the frame.
        // Delay the baseline capture so old and new IDs are never observed together.
        baselineRefreshFrames = 2;
    }

    private static void CleanupLegacyOrphanVisualCopies()
    {
        foreach (Transform candidate in FindObjectsByType<Transform>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (candidate.parent != null) continue;
            string objectName = candidate.name;
            if (!objectName.EndsWith(" (Left Visual Copy)", StringComparison.Ordinal) &&
                !objectName.EndsWith(" (Right Visual Copy)", StringComparison.Ordinal))
                continue;

            candidate.gameObject.SetActive(false);
            Destroy(candidate.gameObject);
        }
    }

    private void SaveAndReenter()
    {
        SavePlanetState();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void ClearAndReload()
    {
        suppressSaveOnDisable = true;
        if (File.Exists(SavePath))
            File.Delete(SavePath);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(12f, 12f, 430f, 92f), "JSON-Driven Planet Prototype");
        GUI.Label(new Rect(24f, 38f, 405f, 22f),
            "The cyan crate does not exist in the Scene; planet JSON creates it.");
        GUI.Label(new Rect(24f, 58f, 405f, 22f),
            "Autosave ON   F5 Save   F9 Load   F10 Re-enter   F8 Clear");
        GUI.Label(new Rect(24f, 78f, 405f, 22f), status);
    }
}
