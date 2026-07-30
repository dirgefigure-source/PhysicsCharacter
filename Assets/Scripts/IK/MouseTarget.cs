using UnityEngine;
using UnityEngine.InputSystem;


public class MouseTarget : MonoBehaviour
{
    public Camera cam;

    public Transform target;


    void Awake()
    {
        cam = Camera.main;
    }


    void Update()
    {

        Vector3 mouse = Mouse.current.position.ReadValue();


        mouse.z =
            -cam.transform.position.z;


        target.position =
            cam.ScreenToWorldPoint(mouse);

    }
}