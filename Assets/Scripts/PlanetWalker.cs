using UnityEngine;

public class PlanetWalker : MonoBehaviour
{
    public float speed = 5f;
    public float jumpForce = 5f;
    CharacterController controller;
    GravityAttractor planet;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        planet = FindFirstObjectByType<GravityAttractor>();
    }

    void Update()
    {
        Vector3 move = transform.right * Input.GetAxis("Horizontal") + transform.forward * Input.GetAxis("Vertical");
        controller.Move(move * speed * Time.deltaTime);

        if (Input.GetButtonDown("Jump") && controller.isGrounded)
        {
            Vector3 gravityUp = (transform.position - planet.transform.position).normalized;
            controller.Move(gravityUp * jumpForce * Time.deltaTime);
        }
    }
}