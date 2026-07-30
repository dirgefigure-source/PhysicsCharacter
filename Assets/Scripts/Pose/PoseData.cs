using UnityEngine;


[CreateAssetMenu(
    menuName="Character/Pose Data"
)]
public class PoseData : ScriptableObject
{
    public JointPose[] joints;
}

[System.Serializable]
public class JointPose
{
    public string jointName;
    
    public float angle;
}