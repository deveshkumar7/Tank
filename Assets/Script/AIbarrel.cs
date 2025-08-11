using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AITurretFSM : MonoBehaviour
{
    [Header("Turret Components")]
    public Transform turretBarrel;  // The barrel that rotates towards player
    public Transform turretBase;    // Optional: base that rotates horizontally

    [Header("Rotation Settings")]
    public float barrelRotationSpeed = 60.0f;
    public float baseRotationSpeed = 45.0f;
    public bool canRotateBase = true;  // Whether the base can rotate or only barrel
    public float maxBarrelAngle = 45.0f;  // Max vertical angle for barrel
    public float minBarrelAngle = -10.0f; // Min vertical angle for barrel

    [Header("AI Settings")]
    public float detectionRange = 15.0f;
    public float attackRange = 12.0f;
    public float loseTargetTime = 3.0f;  // Time before losing target after losing sight
    public float scanSpeed = 30.0f;  // Speed when scanning for targets
    public float scanAngle = 90.0f;  // Total angle to scan (left and right)

    [Header("Combat Settings")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float timeBetweenBullets = 0.5f;
    public float bulletSpeed = 15.0f;
    public int maxAmmo = 20;
    public float reloadTime = 5.0f;
    public float accuracy = 0.95f;  // 1.0 = perfect accuracy, lower = less accurate

    [Header("Alert Settings")]
    public float alertCooldown = 5.0f;  // Time to stay alert after losing target
    public bool predictTargetMovement = true;  // Lead target based on velocity
    public float predictionTime = 0.5f;  // How far ahead to predict target position

    // FSM States
    public enum TurretState
    {
        Idle,
        Scanning,
        Tracking,
        Attack,
        Reloading,
        Alert
    }

    // Private variables
    private TurretState currentState;
    private Transform player;
    private float lastFireTime;
    private float lastSeenPlayerTime;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 lastPlayerVelocity;

    // Combat variables
    private int currentAmmo;
    private bool isReloading;
    private float reloadTimer;

    // Scanning variables
    private float scanDirection = 1f;
    private float currentScanAngle = 0f;
    private Quaternion initialBarrelRotation;
    private Quaternion initialBaseRotation;

    // Alert variables
    private float alertTimer;
    private Vector3 alertSearchPosition;

    // Rotation tracking
    private Quaternion targetBarrelRotation;
    private Quaternion targetBaseRotation;
    private float currentBarrelAngle;
    private bool hasLineOfSight;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        currentState = TurretState.Idle;
        currentAmmo = maxAmmo;
        isReloading = false;

        // Store initial rotations for scanning behavior
        if (turretBarrel != null)
            initialBarrelRotation = turretBarrel.localRotation;
        if (turretBase != null)
            initialBaseRotation = turretBase.rotation;
        else if (canRotateBase)
            initialBaseRotation = transform.rotation;

        targetBarrelRotation = initialBarrelRotation;
        targetBaseRotation = initialBaseRotation;

        // Start in scanning mode
        ChangeState(TurretState.Scanning);
    }

    void Update()
    {
        // Update reload timer
        UpdateReload();

        // Update FSM
        UpdateFSM();

        // Smooth rotation updates
        UpdateRotations();

        // Update player tracking info
        UpdatePlayerTracking();
    }

    void UpdateReload()
    {
        if (isReloading)
        {
            reloadTimer -= Time.deltaTime;
            if (reloadTimer <= 0f)
            {
                isReloading = false;
                currentAmmo = maxAmmo;
                Debug.Log("Turret reloaded!");

                // Return to appropriate state after reloading
                if (CanSeePlayer())
                {
                    ChangeState(TurretState.Tracking);
                }
                else if (Time.time - lastSeenPlayerTime < alertCooldown)
                {
                    ChangeState(TurretState.Alert);
                }
                else
                {
                    ChangeState(TurretState.Scanning);
                }
            }
        }
    }

    void UpdateFSM()
    {
        switch (currentState)
        {
            case TurretState.Idle:
                IdleBehavior();
                break;
            case TurretState.Scanning:
                ScanningBehavior();
                break;
            case TurretState.Tracking:
                TrackingBehavior();
                break;
            case TurretState.Attack:
                AttackBehavior();
                break;
            case TurretState.Reloading:
                ReloadingBehavior();
                break;
            case TurretState.Alert:
                AlertBehavior();
                break;
        }
    }

    void IdleBehavior()
    {
        // Check for player detection
        if (CanSeePlayer())
        {
            ChangeState(TurretState.Tracking);
            return;
        }

        // After a delay, start scanning
        if (Time.time - lastSeenPlayerTime > 2.0f)
        {
            ChangeState(TurretState.Scanning);
        }
    }

    void ScanningBehavior()
    {
        // Check for player detection
        if (CanSeePlayer())
        {
            ChangeState(TurretState.Tracking);
            return;
        }

        // Perform scanning motion
        currentScanAngle += scanDirection * scanSpeed * Time.deltaTime;

        // Reverse direction at scan limits
        if (Mathf.Abs(currentScanAngle) >= scanAngle / 2f)
        {
            scanDirection *= -1f;
            currentScanAngle = Mathf.Clamp(currentScanAngle, -scanAngle / 2f, scanAngle / 2f);
        }

        // Apply scanning rotation
        if (canRotateBase)
        {
            // Rotate the base horizontally
            Transform rotateTarget = turretBase != null ? turretBase : transform;
            targetBaseRotation = initialBaseRotation * Quaternion.Euler(0, currentScanAngle, 0);
        }
        else if (turretBarrel != null)
        {
            // If base can't rotate, scan with barrel only
            targetBarrelRotation = initialBarrelRotation * Quaternion.Euler(0, currentScanAngle, 0);
        }
    }

    void TrackingBehavior()
    {
        // Check if we still have sight of player
        if (!CanSeePlayer())
        {
            // Lost sight - go to alert mode
            ChangeState(TurretState.Alert);
            return;
        }

        // Update last known position
        lastKnownPlayerPosition = player.position;
        lastSeenPlayerTime = Time.time;

        // Check if within attack range
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (distanceToPlayer <= attackRange && !isReloading)
        {
            ChangeState(TurretState.Attack);
            return;
        }

        // Track the player
        AimAtTarget(GetPredictedTargetPosition());
    }

    void AttackBehavior()
    {
        // Check ammo
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(TurretState.Reloading);
            return;
        }

        // Check if we still have sight of player
        if (!CanSeePlayer())
        {
            ChangeState(TurretState.Alert);
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // Check if player moved out of range
        if (distanceToPlayer > attackRange)
        {
            ChangeState(TurretState.Tracking);
            return;
        }

        // Update last known position
        lastKnownPlayerPosition = player.position;
        lastSeenPlayerTime = Time.time;

        // Aim at predicted position
        Vector3 targetPosition = GetPredictedTargetPosition();
        AimAtTarget(targetPosition);

        // Fire if ready and aimed
        if (!isReloading && currentAmmo > 0 && Time.time - lastFireTime >= timeBetweenBullets)
        {
            if (IsAimedAtTarget(targetPosition))
            {
                FireAtTarget(targetPosition);
                lastFireTime = Time.time;
            }
        }
    }

    void ReloadingBehavior()
    {
        // Continue tracking player if visible during reload
        if (CanSeePlayer())
        {
            AimAtTarget(GetPredictedTargetPosition());
            lastKnownPlayerPosition = player.position;
            lastSeenPlayerTime = Time.time;
        }
        else if (Time.time - lastSeenPlayerTime < loseTargetTime)
        {
            // Track last known position
            AimAtTarget(lastKnownPlayerPosition);
        }

        // State change handled in UpdateReload()
    }

    void AlertBehavior()
    {
        alertTimer += Time.deltaTime;

        // Check if player is visible again
        if (CanSeePlayer())
        {
            alertTimer = 0f;
            ChangeState(TurretState.Tracking);
            return;
        }

        // Search around last known position
        float searchAngle = Mathf.Sin(Time.time * 2f) * 30f;
        Vector3 searchDirection = Quaternion.Euler(0, searchAngle, 0) * (lastKnownPlayerPosition - transform.position).normalized;
        Vector3 searchPosition = transform.position + searchDirection * Vector3.Distance(transform.position, lastKnownPlayerPosition);

        AimAtTarget(searchPosition);

        // Return to scanning after alert cooldown
        if (alertTimer >= alertCooldown)
        {
            alertTimer = 0f;
            ChangeState(TurretState.Scanning);
        }
    }

    void UpdatePlayerTracking()
    {
        if (player != null && CanSeePlayer())
        {
            // Calculate player velocity for prediction
            Vector3 currentPlayerVelocity = (player.position - lastKnownPlayerPosition) / Time.deltaTime;
            lastPlayerVelocity = Vector3.Lerp(lastPlayerVelocity, currentPlayerVelocity, Time.deltaTime * 5f);
        }
    }

    Vector3 GetPredictedTargetPosition()
    {
        if (!predictTargetMovement || player == null)
            return player != null ? player.position : lastKnownPlayerPosition;

        // Calculate time for bullet to reach target
        float distance = Vector3.Distance(transform.position, player.position);
        float bulletTravelTime = distance / bulletSpeed;

        // Predict where player will be
        Vector3 predictedPosition = player.position + lastPlayerVelocity * bulletTravelTime * predictionTime;

        return predictedPosition;
    }

    void AimAtTarget(Vector3 targetPosition)
    {
        if (turretBarrel == null && !canRotateBase) return;

        Vector3 directionToTarget = targetPosition - transform.position;

        // Horizontal rotation (base or full turret)
        if (canRotateBase)
        {
            Vector3 horizontalDirection = new Vector3(directionToTarget.x, 0, directionToTarget.z);
            if (horizontalDirection != Vector3.zero)
            {
                Quaternion horizontalRotation = Quaternion.LookRotation(horizontalDirection);

                if (turretBase != null)
                {
                    targetBaseRotation = horizontalRotation;
                }
                else
                {
                    targetBaseRotation = horizontalRotation;
                }
            }
        }

        // Vertical rotation (barrel)
        if (turretBarrel != null)
        {
            // Calculate barrel angle
            Vector3 barrelDirection = turretBarrel.InverseTransformDirection(directionToTarget);
            float angleToTarget = Mathf.Atan2(barrelDirection.y,
                new Vector3(barrelDirection.x, 0, barrelDirection.z).magnitude) * Mathf.Rad2Deg;

            // Clamp barrel angle
            angleToTarget = Mathf.Clamp(angleToTarget, minBarrelAngle, maxBarrelAngle);
            currentBarrelAngle = angleToTarget;

            // Apply barrel rotation
            if (canRotateBase)
            {
                // If base rotates, barrel only needs to handle elevation
                targetBarrelRotation = Quaternion.Euler(-angleToTarget, 0, 0);
            }
            else
            {
                // If base doesn't rotate, barrel handles both horizontal and vertical
                Quaternion horizontalRotation = Quaternion.LookRotation(new Vector3(directionToTarget.x, 0, directionToTarget.z));
                Quaternion verticalRotation = Quaternion.Euler(-angleToTarget, 0, 0);
                targetBarrelRotation = horizontalRotation * verticalRotation;
            }
        }
    }

    void UpdateRotations()
    {
        // Smooth base rotation
        if (canRotateBase)
        {
            if (turretBase != null)
            {
                turretBase.rotation = Quaternion.Slerp(turretBase.rotation, targetBaseRotation,
                    baseRotationSpeed * Time.deltaTime / 100f);
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, targetBaseRotation,
                    baseRotationSpeed * Time.deltaTime / 100f);
            }
        }

        // Smooth barrel rotation
        if (turretBarrel != null)
        {
            if (canRotateBase)
            {
                // Local rotation for barrel when base rotates
                turretBarrel.localRotation = Quaternion.Slerp(turretBarrel.localRotation, targetBarrelRotation,
                    barrelRotationSpeed * Time.deltaTime / 100f);
            }
            else
            {
                // World rotation for barrel when it handles all rotation
                turretBarrel.rotation = Quaternion.Slerp(turretBarrel.rotation, targetBarrelRotation,
                    barrelRotationSpeed * Time.deltaTime / 100f);
            }
        }
    }

    bool IsAimedAtTarget(Vector3 targetPosition)
    {
        if (firePoint == null) return false;

        Vector3 directionToTarget = (targetPosition - firePoint.position).normalized;
        float angle = Vector3.Angle(firePoint.forward, directionToTarget);

        // Check if we're aimed close enough
        return angle < 5f; // Within 5 degrees
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > detectionRange) return false;

        // Raycast to check line of sight
        Vector3 rayDirection = (player.position - transform.position).normalized;
        RaycastHit hit;

        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        if (firePoint != null)
            rayOrigin = firePoint.position;

        if (Physics.Raycast(rayOrigin, rayDirection, out hit, detectionRange))
        {
            hasLineOfSight = hit.collider.CompareTag("Player");
            return hasLineOfSight;
        }

        hasLineOfSight = false;
        return false;
    }

    void FireAtTarget(Vector3 targetPosition)
    {
        if (bulletPrefab != null && firePoint != null && currentAmmo > 0)
        {
            // Add accuracy variance
            Vector3 accuracyOffset = Random.insideUnitSphere * (1f - accuracy) * 2f;
            accuracyOffset.y *= 0.5f; // Less vertical spread

            Vector3 fireDirection = (targetPosition - firePoint.position).normalized + accuracyOffset;

            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, Quaternion.LookRotation(fireDirection));
            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null)
            {
                bulletRb.velocity = fireDirection.normalized * bulletSpeed;
            }

            // Decrease ammo
            currentAmmo--;
            Debug.Log($"Turret fired! Ammo remaining: {currentAmmo}/{maxAmmo}");

            // Destroy bullet after some time
            Destroy(bullet, 5.0f);
        }
    }

    void StartReload()
    {
        isReloading = true;
        reloadTimer = reloadTime;
        Debug.Log("Turret is reloading...");
    }

    void ChangeState(TurretState newState)
    {
        // Exit current state
        switch (currentState)
        {
            case TurretState.Scanning:
                currentScanAngle = 0f;
                break;
            case TurretState.Alert:
                alertTimer = 0f;
                break;
        }

        currentState = newState;

        // Enter new state
        switch (newState)
        {
            case TurretState.Scanning:
                scanDirection = 1f;
                currentScanAngle = 0f;
                break;
            case TurretState.Alert:
                alertTimer = 0f;
                alertSearchPosition = lastKnownPlayerPosition;
                break;
            case TurretState.Reloading:
                // Reloading handled separately
                break;
        }

        Debug.Log($"Turret state changed to: {newState}");
    }

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Current aim direction
        if (firePoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(firePoint.position, firePoint.forward * 5f);
        }

        // Line of sight to player
        if (Application.isPlaying && player != null)
        {
            Gizmos.color = hasLineOfSight ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, player.position);

            // Show last known position when in alert mode
            if (currentState == TurretState.Alert)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(lastKnownPlayerPosition, 0.5f);
            }

            // Show predicted position when tracking
            if (currentState == TurretState.Tracking || currentState == TurretState.Attack)
            {
                if (predictTargetMovement)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(GetPredictedTargetPosition(), 0.3f);
                }
            }
        }

        // Scan angle visualization
        if (currentState == TurretState.Scanning)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Vector3 leftScan = Quaternion.Euler(0, -scanAngle / 2f, 0) * transform.forward;
            Vector3 rightScan = Quaternion.Euler(0, scanAngle / 2f, 0) * transform.forward;
            Gizmos.DrawRay(transform.position, leftScan * detectionRange);
            Gizmos.DrawRay(transform.position, rightScan * detectionRange);
        }
    }

    // Public method to get current state info (for UI debugging)
    public string GetStateInfo()
    {
        return $"State: {currentState}\n" +
               $"Ammo: {currentAmmo}/{maxAmmo}\n" +
               $"Reloading: {isReloading}\n" +
               $"Reload Timer: {(isReloading ? reloadTimer.ToString("F1") : "N/A")}\n" +
               $"Has Line of Sight: {hasLineOfSight}\n" +
               $"Barrel Angle: {currentBarrelAngle:F1}°\n" +
               $"Alert Timer: {(currentState == TurretState.Alert ? alertTimer.ToString("F1") : "N/A")}";
    }
}