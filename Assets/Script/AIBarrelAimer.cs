using UnityEngine;

public class AIBarrelYawTracker : MonoBehaviour
{
    public Transform barrelTransform;    // The part that should rotate horizontally (e.g., turret or barrel base)
    public Transform player;             // The player to track
    public float rotationSpeed = 3f;     // How fast the rotation happens
    public float maxYawAngle = 180f;     // Optional: limit horizontal rotation

    void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
        }
    }

    void Update()
    {
        if (player == null || barrelTransform == null) return;

        // Step 1: Direction to player on horizontal plane
        Vector3 directionToPlayer = player.position - barrelTransform.position;
        directionToPlayer.y = 0f;  // Ignore vertical axis for horizontal rotation

        if (directionToPlayer.sqrMagnitude < 0.001f)
            return;

        // Step 2: Get target rotation toward player
        Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);

        // Step 3: Smooth rotation
        barrelTransform.rotation = Quaternion.Slerp(
            barrelTransform.rotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );

        // Step 4 (optional): Clamp the yaw if needed
        Vector3 currentEuler = barrelTransform.localEulerAngles;
        float yaw = currentEuler.y > 180 ? currentEuler.y - 360 : currentEuler.y;
        float clampedYaw = Mathf.Clamp(yaw, -maxYawAngle, maxYawAngle);
        barrelTransform.localEulerAngles = new Vector3(0, clampedYaw, 0);

        // Debugging output
        Debug.Log($"AI Barrel yaw: {clampedYaw:F2}°");
    }
}
