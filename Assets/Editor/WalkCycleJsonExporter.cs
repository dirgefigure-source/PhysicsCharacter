using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class WalkCycleJsonExporter
{
    private const float SampleRate = 60f;

    [Serializable] private sealed class ExportData
    {
        public string sourceAsset;
        public string clipName;
        public float durationSeconds;
        public float sampleRate;
        public int sampleCount;
        public string projection;
        public string characterForward;
        public string cameraView;
        public string angleConvention;
        public List<Frame> frames = new();
    }

    [Serializable] private sealed class Frame
    {
        public int index;
        public float normalizedTime;
        public float timeSeconds;
        public List<JointSample> joints = new();
    }

    [Serializable] private sealed class JointSample
    {
        public string name;
        public string bone;
        public string childBone;
        public float projectedX;
        public float projectedY;
        public float sourceLocalAngleDeg;
        public float relativeAngleDeg;
    }

    private readonly struct JointDefinition
    {
        public readonly string Name;
        public readonly HumanBodyBones Bone;
        public readonly HumanBodyBones Child;
        public readonly int ParentSegment;

        public JointDefinition(string name, HumanBodyBones bone, HumanBodyBones child, int parentSegment = -1)
        {
            Name = name;
            Bone = bone;
            Child = child;
            ParentSegment = parentSegment;
        }
    }

    // Segment direction is Bone -> Child. ParentSegment supplies the segment whose
    // projected direction is the reference for the relative HingeJoint2D angle.
    private static readonly JointDefinition[] Joints =
    {
        new("hips", HumanBodyBones.Hips, HumanBodyBones.Spine),
        new("spine", HumanBodyBones.Spine, HumanBodyBones.Chest, 0),
        new("chest", HumanBodyBones.Chest, HumanBodyBones.Neck, 1),
        new("leftUpperArm", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 2),
        new("leftLowerArm", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 3),
        new("rightUpperArm", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 2),
        new("rightLowerArm", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 5),
        new("leftUpperLeg", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0),
        new("leftLowerLeg", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 7),
        new("leftFoot", HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, 8),
        new("rightUpperLeg", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0),
        new("rightLowerLeg", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 10),
        new("rightFoot", HumanBodyBones.RightFoot, HumanBodyBones.RightToes, 11),
    };

    [MenuItem("Tools/Physics Character Lab/Export Selected FBX Motion JSON")]
    public static void ExportSelected()
    {
        string sourcePath = GetSelectedFbxPath();
        if (string.IsNullOrEmpty(sourcePath))
        {
            EditorUtility.DisplayDialog(
                "Export Motion JSON",
                "Select one FBX animation asset in the Project window, then run this command again.",
                "OK");
            return;
        }

        Export(sourcePath);
    }

    [MenuItem("Tools/Physics Character Lab/Export Selected FBX Motion JSON", true)]
    private static bool ValidateExportSelected() => !string.IsNullOrEmpty(GetSelectedFbxPath());

    public static string Export(string sourcePath)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
            throw new FileNotFoundException("The selected FBX does not contain a model root.", sourcePath);

        AnimationClip clip = FindMotionClip(sourcePath);
        string outputPath = GetOutputPath(sourcePath);
        GameObject instance = UnityEngine.Object.Instantiate(source);
        instance.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            int sampleCount = Mathf.Max(2, Mathf.RoundToInt(clip.length * SampleRate) + 1);
            var data = new ExportData
            {
                sourceAsset = sourcePath,
                clipName = clip.name,
                durationSeconds = clip.length,
                sampleRate = SampleRate,
                sampleCount = sampleCount,
                projection = "Unity world XY plane after rotating the character so visual forward is +X",
                characterForward = "+X",
                cameraView = "camera on -Z looking toward +Z; character right side is visible",
                angleConvention = "degrees CCW from +X; relativeAngleDeg = DeltaAngle(parent segment, child segment)"
            };

            // Mixamo/Unity humanoids face local +Z. Rotating +90 degrees around Y maps
            // that visual forward to +X and exposes the character's right side to a -Z camera.
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));

            for (int i = 0; i < sampleCount; i++)
            {
                float normalizedTime = i / (float)(sampleCount - 1);
                float time = normalizedTime * clip.length;
                clip.SampleAnimation(instance, time);

                var frame = new Frame { index = i, normalizedTime = normalizedTime, timeSeconds = time };
                var angles = new float[Joints.Length];

                for (int j = 0; j < Joints.Length; j++)
                {
                    JointDefinition definition = Joints[j];
                    Transform bone = RequireBone(instance.transform, definition.Bone);
                    Transform child = RequireBone(instance.transform, definition.Child);
                    Vector2 projected = new(child.position.x - bone.position.x, child.position.y - bone.position.y);
                    if (projected.sqrMagnitude < 1e-10f)
                        throw new InvalidOperationException($"Projected segment {definition.Name} has zero length at t={time}.");

                    float angle = Mathf.Atan2(projected.y, projected.x) * Mathf.Rad2Deg;
                    angles[j] = angle;
                    frame.joints.Add(new JointSample
                    {
                        name = definition.Name,
                        bone = definition.Bone.ToString(),
                        childBone = definition.Child.ToString(),
                        projectedX = projected.x,
                        projectedY = projected.y,
                        sourceLocalAngleDeg = angle,
                        relativeAngleDeg = definition.ParentSegment < 0
                            ? angle
                            : Mathf.DeltaAngle(angles[definition.ParentSegment], angle)
                    });
                }

                data.frames.Add(frame);
            }

            File.WriteAllText(outputPath, JsonUtility.ToJson(data, true));
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(outputPath);
            Debug.Log(
                $"Exported motion '{clip.name}' from '{sourcePath}' " +
                $"({data.sampleCount} samples at {data.sampleRate:F0} Hz) to '{outputPath}'.");
            return outputPath;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static string GetSelectedFbxPath()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        return !string.IsNullOrEmpty(path) &&
               string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    private static string GetOutputPath(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
        string fileName = Path.GetFileNameWithoutExtension(sourcePath) + ".motion.json";
        return string.IsNullOrEmpty(directory) ? fileName : directory + "/" + fileName;
    }

    private static AnimationClip FindMotionClip(string assetPath)
    {
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                return clip;
        }
        throw new InvalidOperationException(
            $"No animation clip was found in '{assetPath}'. Enable Import Animation in the FBX importer.");
    }

    private static Transform RequireBone(Transform root, HumanBodyBones bone)
    {
        string mixamoName = bone switch
        {
            HumanBodyBones.Hips => "Hips",
            HumanBodyBones.Spine => "Spine",
            HumanBodyBones.Chest => "Spine2",
            HumanBodyBones.Neck => "Neck",
            HumanBodyBones.LeftUpperArm => "LeftArm",
            HumanBodyBones.LeftLowerArm => "LeftForeArm",
            HumanBodyBones.LeftHand => "LeftHand",
            HumanBodyBones.RightUpperArm => "RightArm",
            HumanBodyBones.RightLowerArm => "RightForeArm",
            HumanBodyBones.RightHand => "RightHand",
            HumanBodyBones.LeftUpperLeg => "LeftUpLeg",
            HumanBodyBones.LeftLowerLeg => "LeftLeg",
            HumanBodyBones.LeftFoot => "LeftFoot",
            HumanBodyBones.LeftToes => "LeftToeBase",
            HumanBodyBones.RightUpperLeg => "RightUpLeg",
            HumanBodyBones.RightLowerLeg => "RightLeg",
            HumanBodyBones.RightFoot => "RightFoot",
            HumanBodyBones.RightToes => "RightToeBase",
            _ => bone.ToString()
        };

        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == mixamoName || candidate.name == "mixamorig:" + mixamoName)
                return candidate;
        }

        throw new InvalidOperationException($"Mixamo bone '{mixamoName}' ({bone}) is missing.");
    }
}
