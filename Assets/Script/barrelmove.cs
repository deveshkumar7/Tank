using UnityEngine;

public class barrelmove : MonoBehaviour
{
    public float mouseSensitivity = 5f;       
    public float keySensitivity = 30f;       
    public float minXRotation = -10f;         
    public float maxXRotation = 8f;          
    public float minYRotation = -60f;         
    public float maxYRotation = 60f;          

    private float currentXRotation = 0f;      
    private float currentYRotation = 0f;     

    void Update()
    {
        // 180 degree 
        float mouseX = Input.GetAxis("Mouse X");
        currentYRotation += mouseX * mouseSensitivity;
        currentYRotation = Mathf.Clamp(currentYRotation, minYRotation, maxYRotation);

        // up-down
        if (Input.GetKey(KeyCode.Q))
        {
            currentXRotation += keySensitivity * Time.deltaTime;
        }
        if (Input.GetKey(KeyCode.E))
        {
            currentXRotation -= keySensitivity * Time.deltaTime;
        }

        currentXRotation = Mathf.Clamp(currentXRotation, minXRotation, maxXRotation);

        //key
        transform.localRotation = Quaternion.Euler(currentXRotation, currentYRotation, 0f);

         Debug.Log("Vertical Rotation (X-axis): " + currentXRotation + "°");
    
    }
}
