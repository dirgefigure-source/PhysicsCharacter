using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class HitFly : MonoBehaviour
{
    public Rigidbody2D rb;
    
    private bool _isSpace;
    
    public void OnSpaceKeydown(InputAction.CallbackContext context)
    {
        _isSpace = context.ReadValueAsButton();
    }

    private void LateUpdate()
    {
        if (_isSpace)
        {
            rb.AddForce(Vector2.up * 100);
        }
    }
}
