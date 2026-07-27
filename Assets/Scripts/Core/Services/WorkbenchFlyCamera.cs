using UnityEngine;
using UnityEngine.InputSystem;

// Minimal self-contained flycam for the test/workbench scenes. Unlike FreeCameraController it needs
// no world services (input map, celestial, planet), so it works in a bare scene in play mode.
// Right-mouse drag to look; WASD to move; Q/E down/up; hold Shift to sprint.
public sealed class WorkbenchFlyCamera : MonoBehaviour
{
    public float MoveSpeed = 12f;
    public float SprintMultiplier = 4f;
    public float LookSensitivity = 0.12f;

    float _yaw;
    float _pitch;

    void Start()
    {
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = e.x;
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (mouse.rightButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue();
            _yaw += d.x * LookSensitivity;
            _pitch = Mathf.Clamp(_pitch - d.y * LookSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        float speed = MoveSpeed * (kb.leftShiftKey.isPressed ? SprintMultiplier : 1f);
        Vector3 move = Vector3.zero;
        if (kb.wKey.isPressed) move += transform.forward;
        if (kb.sKey.isPressed) move -= transform.forward;
        if (kb.dKey.isPressed) move += transform.right;
        if (kb.aKey.isPressed) move -= transform.right;
        if (kb.eKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed) move -= Vector3.up;
        if (move.sqrMagnitude > 0.0001f)
            transform.position += move.normalized * speed * Time.deltaTime;
    }
}
