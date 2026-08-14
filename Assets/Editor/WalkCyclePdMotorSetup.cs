using UnityEditor;
using UnityEngine;

public static class WalkCyclePdMotorSetup
{
    private const string JsonPath = "Assets/Animations/Humanoid/Y Bot@Standard Walk.walk-cycle.json";

    [MenuItem("Tools/Physics Character Lab/Attach PD Walk Driver to Player")]
    private static void Attach()
    {
        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogError("No active GameObject named 'Player' was found.");
            return;
        }

        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null)
        {
            Debug.LogError($"Walk-cycle JSON was not found at {JsonPath}.");
            return;
        }

        WalkCyclePdMotorDriver driver = player.GetComponent<WalkCyclePdMotorDriver>();
        if (driver == null) driver = Undo.AddComponent<WalkCyclePdMotorDriver>(player);

        SerializedObject serializedDriver = new(driver);
        serializedDriver.FindProperty("walkCycleJson").objectReferenceValue = json;
        serializedDriver.ApplyModifiedProperties();
        EditorUtility.SetDirty(player);
        Debug.Log("Attached and configured WalkCyclePdMotorDriver on Player.");
        Selection.activeGameObject = player;
    }
}
