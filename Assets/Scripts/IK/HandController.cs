using UnityEngine;


public class HandController : MonoBehaviour
{

    public Transform target;


    Rigidbody2D rb;


    void Awake()
    {
        rb =
            GetComponent<Rigidbody2D>();
    }



    void FixedUpdate()
    {

        rb.MovePosition(
            target.position
        );

    }

}