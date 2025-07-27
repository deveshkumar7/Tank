using UnityEngine;

public class barrelmove : MonoBehaviour
{
    public float mouseSensitivity = 5f;       // For horizontal rotation
    public float keySensitivity = 30f;        // For vertical rotation using Q/E
    public float minXRotation = -10f;         // Downward clamp
    public float maxXRotation = 8f;          // Upward clamp
    public float minYRotation = -60f;         // Left clamp
    public float maxYRotation = 60f;          // Right clamp

    private float currentXRotation = 0f;      // Vertical rotation (X-axis)
    private float currentYRotation = 0f;      // Horizontal rotation (Y-axis)

    void Update()
    {
        // Horizontal rotation with mouse
        float mouseX = Input.GetAxis("Mouse X");
        currentYRotation += mouseX * mouseSensitivity;
        currentYRotation = Mathf.Clamp(currentYRotation, minYRotation, maxYRotation);

        // Vertical rotation with Q/E
        if (Input.GetKey(KeyCode.Q))
        {
            currentXRotation += keySensitivity * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.E))
        {
            currentXRotation -= keySensitivity * Time.deltaTime;
        }

        currentXRotation = Mathf.Clamp(currentXRotation, minXRotation, maxXRotation);

        // Apply combined rotation: pitch (X) and yaw (Y)
        transform.localRotation = Quaternion.Euler(currentXRotation, currentYRotation, 0f);

         Debug.Log("Vertical Rotation (X-axis): " + currentXRotation + "°");
    
    }
}
