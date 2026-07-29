using UnityEngine;

public class BalanceController : MonoBehaviour
{
    public MuscleController muscle;


    [Range(0f,1f)]
    public float balanceStrength = 1f;


    void FixedUpdate()
    {
        if(!muscle)
            return;
        
        muscle.muscleMultiplier = balanceStrength;
    }
}