using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement")]
    public float MoveSpeed = 10f;
    public float FastMultiplier = 3f;
    public float ScrollSpeed = 50f;

    [Header("Look")]
    public float LookSensitivity = 2f;

    [Header("Debug Info")]
    public bool ShowDebugOverlay = true;
    public Transform TargetCenter;

    float _yaw;
    float _pitch;
    bool _looking;

    void Start()
    {
        _yaw = transform.eulerAngles.y;
        _pitch = transform.eulerAngles.x;
    }

    void Update()
    {
        HandleLook();
        HandleMovement();
    }

    void HandleLook()
    {
        if (Input.GetMouseButtonDown(1))
        {
            _looking = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Input.GetMouseButtonUp(1))
        {
            _looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (!_looking) return;

        _yaw += Input.GetAxis("Mouse X") * LookSensitivity;
        _pitch -= Input.GetAxis("Mouse Y") * LookSensitivity;
        _pitch = Mathf.Clamp(_pitch, -90f, 90f);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    void HandleMovement()
    {
        float speed = MoveSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) speed *= FastMultiplier;

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.D)) move += transform.right;
        if (Input.GetKey(KeyCode.E)) move += transform.up;
        if (Input.GetKey(KeyCode.Q)) move -= transform.up;

        transform.position += move.normalized * speed * Time.deltaTime;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
            transform.position += transform.forward * scroll * ScrollSpeed;
    }

    void OnGUI()
    {
        if (!ShowDebugOverlay) return;

        GUILayout.BeginArea(new Rect(10, 10, 350, 200));
        GUILayout.Label($"<b>Debug Camera</b>");
        GUILayout.Label($"Position: {transform.position:F1}");
        GUILayout.Label($"FPS: {1f / Time.unscaledDeltaTime:F0}");

        if (TargetCenter != null)
        {
            Vector3 dirToSurface = (transform.position - TargetCenter.position).normalized;
            var (lat, lon) = CoordinateConverter.UnitSphereToLatLong(dirToSurface);
            GUILayout.Label($"Lat: {lat * Mathf.Rad2Deg:F1}° Lon: {lon * Mathf.Rad2Deg:F1}°");

            float distToCenter = Vector3.Distance(transform.position, TargetCenter.position);
            GUILayout.Label($"Distance to center: {distToCenter:F1}");
        }

        GUILayout.Label("<i>WASD=Move, Shift=Fast, RMB=Look, QE=Up/Down</i>");
        GUILayout.EndArea();
    }
}
