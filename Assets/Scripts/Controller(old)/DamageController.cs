using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class DamageController : MonoBehaviour
{
    public Rigidbody2D torso;

    public PlayerStateController stateController;
    
    public float hitForce = 300f;

    void Update()
    {
        if(Keyboard.current.fKey.wasPressedThisFrame)
        {
            Hit();
        }
    }


    void Hit()
    {
        stateController.ChangeState(PlayerStateController.PlayerState.Hit);
        
        Vector2 force =
            new Vector2(-hitForce, hitForce * 0.8f);

        torso.AddForce(force, ForceMode2D.Impulse);
    }
}