using UnityEngine;

public class PlayerStateController : MonoBehaviour
{
    public enum PlayerState
    {
        Standing,
        Hit,
        Ragdoll,
        Recovering
    }


    public PlayerState CurrentState { get; private set; }


    [Header("Controllers")]
    public BalanceController balance;
    public MuscleController muscle;


    [Header("Physics")]
    public Rigidbody2D torso;


    [Header("Timing")]
    public float hitDuration = 0.25f;
    public float recoverDelay = 1f;
    
    private float stateTimer;

    [Header("Recover Settings")]
    public float muscleRecoverTime = 2f;
    private float recoverTimer;
    public float balanceDelay = 1f;
    private float balanceTimer;
    
    
    void Start()
    {
        ChangeState(PlayerState.Standing);
    }



    void Update()
    {
        switch(CurrentState)
        {

            case PlayerState.Hit:

                UpdateHit();

                break;


            case PlayerState.Ragdoll:

                UpdateRagdoll();

                break;


            case PlayerState.Recovering:

                UpdateRecovering();

                break;
        }
    }



    public void ChangeState(PlayerState newState)
    {
        if(CurrentState == newState)
            return;


        CurrentState = newState;


        switch(CurrentState)
        {
            case PlayerState.Standing:
                EnterStanding();
                break;


            case PlayerState.Hit:
                EnterHit();
                break;


            case PlayerState.Ragdoll:
                EnterRagdoll();
                break;


            case PlayerState.Recovering:
                EnterRecovering();
                break;
        }
    }



    void EnterStanding()
    {
        balance.balanceStrength = 1f;
        muscle.muscleMultiplier = 1f;
    }



    void EnterHit()
    {
        balance.balanceStrength = 0f;
        muscle.muscleMultiplier = 0f;

        stateTimer = hitDuration;
    }
    
    void EnterRagdoll()
    {
        balance.balanceStrength = 0f;
        muscle.muscleMultiplier = 0f;

        stateTimer = 0;
    }

    void EnterRecovering()
    {
        recoverTimer = 0;
        balanceTimer = 0;
        balance.balanceStrength = 0f;
        muscle.muscleMultiplier = 0f;
    }

    void UpdateHit()
    {
        stateTimer -= Time.deltaTime;


        if(stateTimer <= 0)
        {
            ChangeState(PlayerState.Ragdoll);
        }
    }

    void UpdateRagdoll()
    {
        float speed = torso.linearVelocity.magnitude;
        float angular = Mathf.Abs(torso.angularVelocity);


        if(speed < 0.3f && angular < 5f)
        {
            stateTimer += Time.deltaTime;


            if(stateTimer > recoverDelay)
            {
                ChangeState(PlayerState.Recovering);
            }
        }
        else
        {
            stateTimer = 0;
        }
    }

    void UpdateRecovering()
    {
        recoverTimer += Time.deltaTime;
        
        float value = Mathf.Clamp01(recoverTimer / muscleRecoverTime);
        
        muscle.muscleMultiplier = value;

        if (value >= 1f)
        {
            balanceTimer += Time.deltaTime;
            if (balanceTimer > balanceDelay)
            {
                ChangeState(PlayerState.Standing);
            }
        }
    }
}