using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using UnityEngine.UI;

public class AIMulti : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 2.0f;
    public float rotationSpeed = 80.0f;
    public GameObject[] leftWheels;
    public GameObject[] rightWheels;
    public float wheelRotationSpeed = 200.0f;

    [Header("Turn Settings")]
    public float sharpTurnAngle = 40.0f;
    public float slowTurnSpeedMultiplier = 0.3f;

    [Header("Sniper Settings")]
    public float sniperRange = 50.0f; // Very long range
    public float maxTrackingRange = 80.0f; // GPS tracking range
    public float aimingTime = 2.0f; // Time to aim before shooting
    public float repositionTime = 8.0f; // Time before changing position
    public bool requireLineOfSightToShoot = true;

    [Header("Positioning")]
    public float preferredSniperDistance = 40.0f; // Preferred distance from target
    public float minSniperDistance = 25.0f; // Minimum distance from target
    public float repositionRadius = 30.0f; // Radius to search for new positions
    public int positionSearchPoints = 12; // Number of positions to evaluate
    public float heightAdvantageBonus = 5.0f; // Bonus for higher positions

    [Header("Combat Settings")]
    public GameObject bulletPrefab;
    public Transform[] firePoints; // Multiple fire points for burst fire
    public float timeBetweenShots = 3.0f;
    public float timeBetweenBursts = 0.2f; // Time between bullets in a burst
    public int bulletsPerBurst = 3; // Number of bullets per burst
    public int maxAmmo = 24; // Increased for burst fire
    public float reloadTime = 4.0f;
    public float bulletDamage = 50f;
    public bool useBurstFire = true;

    [Header("Projectile Physics")]
    public bool useProjectilePhysics = true;
    public float minBulletSpeed = 15.0f;
    public float maxBulletSpeed = 50.0f;
    public float gravityScale = 1.0f; // Multiplier for Physics.gravity
    public bool predictPlayerMovement = true;
    public float predictionTime = 1.5f; // How far ahead to predict player movement

    [Header("Positioning Preferences")]
    public LayerMask groundLayerMask = -1;
    public LayerMask obstacleLayerMask = -1;
    public float coverSearchRadius = 25.0f;
    public bool preferHighGround = true;
    public float minPositionDistance = 5.0f; // Minimum distance between positions

    [Header("GPS Visualization")]
    public bool showSniperRange = true;
    public bool showTrajectoryPath = true;
    public bool showGPSConnection = true;
    public Color sniperRangeColor = new Color(1f, 0f, 0f, 0.3f);
    public Color trajectoryColor = Color.cyan;
    public Color gpsLineColor = new Color(1f, 0.5f, 0f);

    // FSM States
    public enum SniperState
    {
        Positioning,    // Moving to a good sniper position
        Aiming,        // Aiming at target
        Shooting,      // Taking the shot
        Relocating,    // Moving to new position after shooting
        Reloading,     // Out of ammo, reloading while finding cover
        Hunting        // GPS tracking but no line of sight
    }

    // Private variables
    private SniperState currentState;
    private SniperState previousState;
    private NavMeshAgent navAgent;
    private Transform player;
    private Vector3 currentSniperPosition;
    private Vector3 targetPosition;
    private float aimTimer;
    private float repositionTimer;
    private float lastShotTime;
    private float stateChangeTime;

    // GPS tracking variables
    private Vector3 playerGPSPosition;
    private Vector3 predictedPlayerPosition;
    private float lastGPSUpdate;
    private bool hasGPSLock;
    private float distanceToPlayer;
    private Vector3 lastPlayerPosition;
    private Vector3 playerVelocity;

    // Combat variables
    private int currentAmmo;
    private bool isReloading;
    private float reloadTimer;
    private bool hasGoodPosition;
    private bool isAiming;
    private bool isFiring;
    private int currentBurstCount;
    private float burstTimer;
    private Coroutine firingCoroutine;

    // Positioning variables
    private List<Vector3> evaluatedPositions;
    private Vector3 bestSniperPosition;
    private float bestPositionScore;

    // Movement tracking for realistic wheel rotation
    private Vector3 lastPosition;
    private float lastRotationY;
    private float currentForwardSpeed;
    private float currentTurnSpeed;
    private bool isSharpTurning;

    // Trajectory calculation
    private Vector3 calculatedVelocity;
    private float calculatedSpeed;
    private bool hasValidTrajectory;
    private List<Vector3> trajectoryPoints;

    // Reloading movement variables
    private float reloadMoveTimer;
    private Vector3 reloadMoveTarget;
    private bool isReloadMoving;
    private float reloadMoveInterval = 1.5f; // Change direction every 1.5 seconds while reloading

    [Header("Health Settings")]
    public float maxHealth = 80f;
    private float currentHealth;

    [Header("UI References")]
    public Image healthBarFill;
    public Image reloadBarFill;
    public TextMeshProUGUI stateText;

    void Start()
    {
        currentHealth = maxHealth;
        UpdateHealthUI();

        navAgent = GetComponent<NavMeshAgent>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        currentState = SniperState.Positioning;
        previousState = SniperState.Positioning;
        stateChangeTime = Time.time;

        currentAmmo = maxAmmo;
        isReloading = false;
        hasGoodPosition = false;
        isAiming = false;
        isFiring = false;
        currentBurstCount = 0;

        evaluatedPositions = new List<Vector3>();
        trajectoryPoints = new List<Vector3>();

        lastPosition = transform.position;
        lastRotationY = transform.eulerAngles.y;
        isReloadMoving = false;
        reloadMoveTimer = 0f;

        // Initialize GPS system
        hasGPSLock = false;
        lastGPSUpdate = 0f;
        lastPlayerPosition = player != null ? player.position : Vector3.zero;

        UpdateAmmoReloadUI();

        // Configure NavMeshAgent
        navAgent.speed = moveSpeed;
        navAgent.angularSpeed = rotationSpeed;
        navAgent.acceleration = 6.0f;
        navAgent.stoppingDistance = 1.0f;

        Debug.Log("Sniper AI initialized - Long range precision shooter!");
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();

        // If taking damage while aiming or positioning, relocate immediately
        if (currentState == SniperState.Aiming || currentState == SniperState.Positioning)
        {
            ChangeState(SniperState.Relocating);
        }

        if (currentHealth <= 0)
        {
            DestroySniper();
        }
    }

    void UpdateTurningBehavior()
    {
        if (navAgent.hasPath && navAgent.path.corners.Length > 1)
        {
            Vector3 directionToWaypoint = (navAgent.path.corners[1] - transform.position).normalized;
            Vector3 currentForward = transform.forward;

            float angleToTarget = Vector3.Angle(currentForward, directionToWaypoint);

            if (angleToTarget > sharpTurnAngle)
            {
                if (!isSharpTurning)
                {
                    isSharpTurning = true;
                    navAgent.speed = navAgent.speed * slowTurnSpeedMultiplier;
                }
            }
            else
            {
                if (isSharpTurning)
                {
                    isSharpTurning = false;
                    RestoreNormalSpeed();
                }
            }
        }
        else
        {
            if (isSharpTurning)
            {
                isSharpTurning = false;
                RestoreNormalSpeed();
            }
        }
    }

    void RestoreNormalSpeed()
    {
        switch (currentState)
        {
            case SniperState.Positioning:
                navAgent.speed = moveSpeed;
                break;
            case SniperState.Relocating:
                navAgent.speed = moveSpeed;
                break;
            case SniperState.Reloading:
                navAgent.speed = moveSpeed * 1.2f; // Faster during reload
                break;
            case SniperState.Hunting:
                navAgent.speed = moveSpeed * 0.8f; // Slower when hunting
                break;
            default:
                navAgent.speed = moveSpeed;
                break;
        }
    }

    void CalculateRealisticMovement()
    {
        Vector3 currentPosition = transform.position;
        float currentRotationY = transform.eulerAngles.y;

        Vector3 positionDelta = currentPosition - lastPosition;
        Vector3 localPositionDelta = transform.InverseTransformDirection(positionDelta);
        currentForwardSpeed = localPositionDelta.z / Time.deltaTime;

        float rotationDelta = Mathf.DeltaAngle(lastRotationY, currentRotationY);
        currentTurnSpeed = rotationDelta / Time.deltaTime;

        currentForwardSpeed = Mathf.Lerp(currentForwardSpeed, navAgent.velocity.magnitude, Time.deltaTime * 5f);
        currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, rotationDelta / Time.deltaTime, Time.deltaTime * 3f);

        lastPosition = currentPosition;
        lastRotationY = currentRotationY;
    }

    void RotateWheelsRealistically()
    {
        if (leftWheels == null || rightWheels == null) return;

        float wheelRotation = currentForwardSpeed * wheelRotationSpeed * Time.deltaTime;
        float turnEffect = currentTurnSpeed * 0.01f;

        foreach (GameObject wheel in leftWheels)
        {
            if (wheel != null)
            {
                float leftWheelSpeed = wheelRotation + (currentTurnSpeed > 0 ? -turnEffect : turnEffect);
                wheel.transform.Rotate(leftWheelSpeed, 0.0f, 0.0f);
            }
        }

        foreach (GameObject wheel in rightWheels)
        {
            if (wheel != null)
            {
                float rightWheelSpeed = wheelRotation + (currentTurnSpeed > 0 ? turnEffect : -turnEffect);
                wheel.transform.Rotate(rightWheelSpeed, 0.0f, 0.0f);
            }
        }
    }

    void DestroySniper()
    {
        ScoreManager scoreManager = FindObjectOfType<ScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.AddScore(25); // Higher score for sniper
            Debug.Log("Sniper destroyed! Added 25 points to score.");
        }

        Destroy(gameObject);
    }

    void UpdateHealthUI()
    {
        if (healthBarFill != null)
            healthBarFill.fillAmount = currentHealth / maxHealth;
    }

    void UpdateAmmoReloadUI()
    {
        if (reloadBarFill != null)
        {
            if (isReloading)
            {
                float reloadProgress = 1f - (reloadTimer / reloadTime);
                reloadBarFill.fillAmount = reloadProgress;
            }
            else
            {
                float ammoPercentage = (float)currentAmmo / maxAmmo;
                reloadBarFill.fillAmount = ammoPercentage;
            }
        }

        if (stateText != null)
        {
            string stateInfo = $"State: {currentState}\nAmmo: {currentAmmo}/{maxAmmo}";
            if (hasGPSLock)
            {
                stateInfo += $"\nRange: {distanceToPlayer:F1}m";
            }
            if (isAiming)
            {
                stateInfo += $"\nAiming: {aimTimer:F1}s";
            }
            stateText.text = stateInfo;
        }
    }

    void Update()
    {
        UpdateGPSTracking();
        UpdateReload();
        UpdateFSM();
        UpdateTurningBehavior();
        UpdateTrajectoryCalculation();
        CalculateRealisticMovement();
        RotateWheelsRealistically();
        UpdateAmmoReloadUI();
    }

    void UpdateGPSTracking()
    {
        if (player == null)
        {
            hasGPSLock = false;
            return;
        }

        // Update GPS position and calculate player velocity
        if (Time.time - lastGPSUpdate >= 0.1f) // More frequent updates for sniper
        {
            Vector3 currentPlayerPos = player.position;
            playerVelocity = (currentPlayerPos - lastPlayerPosition) / (Time.time - lastGPSUpdate);
            lastPlayerPosition = currentPlayerPos;

            playerGPSPosition = currentPlayerPos;
            distanceToPlayer = Vector3.Distance(transform.position, playerGPSPosition);

            // Calculate predicted position if enabled
            if (predictPlayerMovement)
            {
                predictedPlayerPosition = playerGPSPosition + (playerVelocity * predictionTime);
            }
            else
            {
                predictedPlayerPosition = playerGPSPosition;
            }

            // Check if player is within tracking range
            if (maxTrackingRange > 0 && distanceToPlayer > maxTrackingRange)
            {
                hasGPSLock = false;
            }
            else
            {
                hasGPSLock = true;
            }

            lastGPSUpdate = Time.time;
        }
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
                Debug.Log("Sniper reloaded!");

                // After reloading, go to positioning to find a good spot
                if (currentState == SniperState.Reloading)
                {
                    ChangeState(SniperState.Positioning);
                }
            }
        }
    }

    void UpdateFSM()
    {
        float timeSinceStateChange = Time.time - stateChangeTime;
        repositionTimer += Time.deltaTime;

        switch (currentState)
        {
            case SniperState.Positioning:
                PositioningBehavior();
                break;
            case SniperState.Aiming:
                AimingBehavior(timeSinceStateChange);
                break;
            case SniperState.Shooting:
                ShootingBehavior();
                break;
            case SniperState.Relocating:
                RelocatingBehavior();
                break;
            case SniperState.Reloading:
                ReloadingBehavior();
                break;
            case SniperState.Hunting:
                HuntingBehavior();
                break;
        }

        // Force repositioning after some time ONLY if not currently engaging
        if (repositionTimer > repositionTime &&
            currentState != SniperState.Reloading &&
            currentState != SniperState.Aiming &&
            currentState != SniperState.Shooting)
        {
            repositionTimer = 0f;
            hasGoodPosition = false;
            ChangeState(SniperState.Positioning);
        }
    }

    void PositioningBehavior()
    {
        // Check if out of ammo - ONLY reason to leave a good position
        if (currentAmmo <= 0)
        {
            StartReload();
            ChangeState(SniperState.Reloading);
            return;
        }

        // Check GPS lock
        if (!hasGPSLock)
        {
            // Stay in position or patrol nearby
            if (!navAgent.hasPath || navAgent.remainingDistance < 2f)
            {
                FindNearbyPosition();
            }
            return;
        }

        // If we're in shooting range and have ammo, start aiming immediately!
        if (distanceToPlayer <= sniperRange)
        {
            if (!requireLineOfSightToShoot || CanSeePlayer())
            {
                // Perfect! We can shoot from here
                navAgent.isStopped = true;
                ChangeState(SniperState.Aiming);
                return;
            }
            else if (hasGPSLock)
            {
                // We're in range but can't see player - try hunting to get line of sight
                ChangeState(SniperState.Hunting);
                return;
            }
        }

        // Only look for new position if we're too far or don't have a good position
        if (!hasGoodPosition || distanceToPlayer > sniperRange)
        {
            Vector3 bestPosition = FindBestSniperPosition();
            if (bestPosition != Vector3.zero)
            {
                currentSniperPosition = bestPosition;
                navAgent.SetDestination(currentSniperPosition);
                hasGoodPosition = true;
                Debug.Log($"Sniper moving to better position at {distanceToPlayer:F1}m from target");
            }
            else
            {
                // No good position found, try hunting
                ChangeState(SniperState.Hunting);
                return;
            }
        }

        // Check if we've reached our position
        if (hasGoodPosition && navAgent.remainingDistance < navAgent.stoppingDistance + 0.5f)
        {
            navAgent.isStopped = true;

            // Re-check if we can engage from this position
            if (distanceToPlayer <= sniperRange)
            {
                if (!requireLineOfSightToShoot || CanSeePlayer())
                {
                    ChangeState(SniperState.Aiming);
                }
                else
                {
                    // Can't see player from this "good" position, find better one
                    hasGoodPosition = false;
                    ChangeState(SniperState.Hunting);
                }
            }
        }
    }

    void AimingBehavior(float timeSinceStateChange)
    {
        // ONLY flee if out of ammo
        if (currentAmmo <= 0)
        {
            StartReload();
            ChangeState(SniperState.Reloading);
            return;
        }

        // If lost GPS, try to reacquire but don't flee
        if (!hasGPSLock)
        {
            Debug.Log("Lost GPS lock while aiming, switching to hunt mode");
            ChangeState(SniperState.Hunting);
            return;
        }

        // If target moved out of range, reposition but don't flee
        if (distanceToPlayer > sniperRange)
        {
            Debug.Log("Target moved out of range, repositioning");
            hasGoodPosition = false;
            ChangeState(SniperState.Positioning);
            return;
        }

        // Check line of sight if required
        if (requireLineOfSightToShoot && !CanSeePlayer())
        {
            Debug.Log("Lost line of sight while aiming, hunting");
            ChangeState(SniperState.Hunting);
            return;
        }

        // Stay put and keep aiming
        navAgent.isStopped = true;
        isAiming = true;

        // Rotate towards predicted target position
        RotateTowardsTarget(predictedPlayerPosition);

        aimTimer += Time.deltaTime;

        // After aiming time, take the shot
        if (aimTimer >= aimingTime)
        {
            ChangeState(SniperState.Shooting);
        }
    }

    void ShootingBehavior()
    {
        if (currentAmmo > 0 && Time.time - lastShotTime >= timeBetweenShots && !isFiring)
        {
            // Start firing sequence
            if (useBurstFire && bulletsPerBurst > 1)
            {
                StartCoroutine(FireBurst());
            }
            else
            {
                FireSniperShot();
                currentAmmo--;
            }

            lastShotTime = Time.time;

            Debug.Log($"Sniper fired! Ammo remaining: {currentAmmo}");

            // After shooting, if we still have ammo and target is in range, keep fighting!
            if (currentAmmo > 0 && hasGPSLock && distanceToPlayer <= sniperRange)
            {
                if (!requireLineOfSightToShoot || CanSeePlayer())
                {
                    // Can shoot again - go back to aiming
                    ChangeState(SniperState.Aiming);
                    return;
                }
                else
                {
                    // Lost line of sight, hunt for it
                    ChangeState(SniperState.Hunting);
                    return;
                }
            }

            // Only relocate if out of ammo or lost target
            if (currentAmmo <= 0)
            {
                StartReload();
                ChangeState(SniperState.Reloading);
            }
            else
            {
                // Still have ammo but target is out of range/lost GPS - reposition
                ChangeState(SniperState.Positioning);
            }
        }
        else if (currentAmmo <= 0)
        {
            StartReload();
            ChangeState(SniperState.Reloading);
        }
    }

    void RelocatingBehavior()
    {
        hasGoodPosition = false;

        // Move to a new position
        Vector3 newPosition = FindBestSniperPosition();
        if (newPosition != Vector3.zero)
        {
            currentSniperPosition = newPosition;
            navAgent.isStopped = false;
            navAgent.SetDestination(currentSniperPosition);

            // Once we start moving, go to positioning
            ChangeState(SniperState.Positioning);
        }
        else
        {
            // No good position, try hunting
            ChangeState(SniperState.Hunting);
        }
    }

    void ReloadingBehavior()
    {
        reloadMoveTimer += Time.deltaTime;

        // Keep moving around while reloading for better survivability
        if (!isReloadMoving || reloadMoveTimer >= reloadMoveInterval)
        {
            FindReloadMovePosition();
            reloadMoveTimer = 0f;
        }

        // Move to the reload target
        if (isReloadMoving && reloadMoveTarget != Vector3.zero)
        {
            float distanceToTarget = Vector3.Distance(transform.position, reloadMoveTarget);

            if (distanceToTarget > navAgent.stoppingDistance + 0.5f)
            {
                navAgent.isStopped = false;
                navAgent.SetDestination(reloadMoveTarget);
                navAgent.speed = moveSpeed * 1.2f; // Move slightly faster while reloading
            }
            else
            {
                // Reached target, find new one
                isReloadMoving = false;
            }
        }

        // Reloading is handled in UpdateReload()
    }

    void HuntingBehavior()
    {
        // ONLY flee if out of ammo
        if (currentAmmo <= 0)
        {
            StartReload();
            ChangeState(SniperState.Reloading);
            return;
        }

        // Check if lost GPS lock
        if (!hasGPSLock)
        {
            Debug.Log("Lost GPS during hunt, repositioning");
            ChangeState(SniperState.Positioning);
            return;
        }

        // If we can see player now, engage!
        if (CanSeePlayer())
        {
            if (distanceToPlayer <= sniperRange)
            {
                Debug.Log("Acquired visual target during hunt, engaging!");
                ChangeState(SniperState.Aiming);
            }
            else
            {
                Debug.Log("Can see target but too far, repositioning");
                hasGoodPosition = false;
                ChangeState(SniperState.Positioning);
            }
            return;
        }

        // Move closer to GPS position to try to get line of sight
        Vector3 huntPosition = FindHuntingPosition();
        if (huntPosition != Vector3.zero)
        {
            navAgent.isStopped = false;
            navAgent.SetDestination(huntPosition);

            // If we get close to hunt position, try different approach
            if (navAgent.remainingDistance < 2f)
            {
                hasGoodPosition = false; // Force new position search
                ChangeState(SniperState.Positioning);
            }
        }
        else
        {
            // No good hunting position, go back to positioning
            hasGoodPosition = false;
            ChangeState(SniperState.Positioning);
        }
    }

    Vector3 FindBestSniperPosition()
    {
        if (!hasGPSLock) return Vector3.zero;

        evaluatedPositions.Clear();
        Vector3 bestPosition = Vector3.zero;
        float bestScore = -1f;

        // Search for positions around the target
        for (int i = 0; i < positionSearchPoints; i++)
        {
            float angle = i * (360f / positionSearchPoints) * Mathf.Deg2Rad;

            // Try different distances
            float[] distances = { preferredSniperDistance, preferredSniperDistance * 0.8f, preferredSniperDistance * 1.2f };

            foreach (float distance in distances)
            {
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 testPosition = playerGPSPosition + direction * distance;

                // Sample position on NavMesh
                NavMeshHit hit;
                if (NavMesh.SamplePosition(testPosition, out hit, 10f, NavMesh.AllAreas))
                {
                    Vector3 candidatePosition = hit.position;

                    // Skip if too close to current position
                    if (Vector3.Distance(candidatePosition, transform.position) < minPositionDistance)
                        continue;

                    // Skip if already evaluated nearby
                    bool tooCloseToEvaluated = false;
                    foreach (Vector3 evaluated in evaluatedPositions)
                    {
                        if (Vector3.Distance(candidatePosition, evaluated) < minPositionDistance)
                        {
                            tooCloseToEvaluated = true;
                            break;
                        }
                    }
                    if (tooCloseToEvaluated) continue;

                    float score = EvaluateSniperPosition(candidatePosition);
                    evaluatedPositions.Add(candidatePosition);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPosition = candidatePosition;
                    }
                }
            }
        }

        bestSniperPosition = bestPosition;
        bestPositionScore = bestScore;

        return bestPosition;
    }

    float EvaluateSniperPosition(Vector3 position)
    {
        float score = 0f;

        if (!hasGPSLock) return 0f;

        float distanceToTarget = Vector3.Distance(position, playerGPSPosition);

        // Distance scoring - prefer optimal range
        if (distanceToTarget < minSniperDistance)
        {
            score -= 30f; // Too close
        }
        else if (distanceToTarget > sniperRange)
        {
            return 0f; // Out of range
        }
        else
        {
            // Optimal distance scoring
            float optimalDistance = preferredSniperDistance;
            float distanceScore = 1f - Mathf.Abs(distanceToTarget - optimalDistance) / optimalDistance;
            score += distanceScore * 40f;
        }

        // Line of sight check
        bool hasLineOfSight = HasLineOfSightToPosition(position, playerGPSPosition);
        if (requireLineOfSightToShoot && !hasLineOfSight)
        {
            return 0f; // Must have line of sight
        }
        if (hasLineOfSight)
        {
            score += 30f;
        }

        // Height advantage
        if (preferHighGround)
        {
            float heightDifference = position.y - playerGPSPosition.y;
            if (heightDifference > 0)
            {
                score += Mathf.Min(heightDifference * heightAdvantageBonus, 20f);
            }
        }

        // Cover availability (check for nearby obstacles)
        if (HasNearbyObstacles(position))
        {
            score += 15f;
        }

        // Distance from current position (prefer closer moves when repositioning)
        float moveDistance = Vector3.Distance(position, transform.position);
        if (moveDistance > 0)
        {
            score += Mathf.Clamp01(1f - (moveDistance / repositionRadius)) * 10f;
        }

        return score;
    }

    bool HasLineOfSightToPosition(Vector3 fromPosition, Vector3 toPosition)
    {
        Vector3 direction = (toPosition - fromPosition).normalized;
        float distance = Vector3.Distance(fromPosition, toPosition);

        RaycastHit hit;
        if (Physics.Raycast(fromPosition + Vector3.up * 1.5f, direction, out hit, distance, obstacleLayerMask))
        {
            if (player != null && toPosition == player.position)
            {
                return hit.collider.CompareTag("Player");
            }
            return false;
        }

        return true;
    }

    bool HasNearbyObstacles(Vector3 position)
    {
        Collider[] obstacles = Physics.OverlapSphere(position, 5f, obstacleLayerMask);
        return obstacles.Length > 0;
    }

    Vector3 FindHuntingPosition()
    {
        if (!hasGPSLock) return Vector3.zero;

        // Find position closer to player to get line of sight
        Vector3 directionToPlayer = (playerGPSPosition - transform.position).normalized;
        Vector3 huntPosition = transform.position + directionToPlayer * 10f;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(huntPosition, out hit, 15f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return Vector3.zero;
    }

    Vector3 FindCoverPosition()
    {
        Vector3 bestCover = Vector3.zero;
        float bestScore = -1f;

        // Search for cover positions
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 testPosition = transform.position + direction * coverSearchRadius;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(testPosition, out hit, 5f, NavMesh.AllAreas))
            {
                // Check if position provides cover from player
                bool hasCover = !HasLineOfSightToPosition(hit.position, playerGPSPosition);
                if (hasCover)
                {
                    float score = Vector3.Distance(hit.position, transform.position);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCover = hit.position;
                    }
                }
            }
        }

        return bestCover;
    }

    void FindReloadMovePosition()
    {
        Vector3 bestPosition = Vector3.zero;
        float bestScore = -1f;

        // Try multiple directions for movement while reloading
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));

            // Try different distances
            float[] distances = { 8f, 12f, 16f };

            foreach (float distance in distances)
            {
                Vector3 testPosition = transform.position + direction * distance;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(testPosition, out hit, 5f, NavMesh.AllAreas))
                {
                    float score = EvaluateReloadPosition(hit.position);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPosition = hit.position;
                    }
                }
            }
        }

        if (bestPosition != Vector3.zero)
        {
            reloadMoveTarget = bestPosition;
            isReloadMoving = true;
            Debug.Log("Sniper found reload movement position");
        }
        else
        {
            // Fallback: move to a nearby random position
            Vector2 randomCircle = Random.insideUnitCircle * 10f;
            Vector3 randomPoint = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 15f, NavMesh.AllAreas))
            {
                reloadMoveTarget = hit.position;
                isReloadMoving = true;
                Debug.Log("Sniper using fallback reload position");
            }
        }
    }

    float EvaluateReloadPosition(Vector3 position)
    {
        float score = 0f;

        // Prefer positions that provide some cover from player
        if (hasGPSLock && !HasLineOfSightToPosition(position, playerGPSPosition))
        {
            score += 30f;
        }

        // Prefer positions at medium distance from player (not too close, not too far)
        if (hasGPSLock)
        {
            float distanceToPlayer = Vector3.Distance(position, playerGPSPosition);
            if (distanceToPlayer > minSniperDistance && distanceToPlayer < sniperRange * 0.8f)
            {
                score += 20f;
            }
            else if (distanceToPlayer < minSniperDistance * 0.5f)
            {
                score -= 20f; // Too close
            }
        }

        // Prefer positions that are not too far from current position
        float moveDistance = Vector3.Distance(position, transform.position);
        score += Mathf.Clamp01(1f - (moveDistance / 20f)) * 15f;

        // Check for nearby obstacles (some cover is good)
        if (HasNearbyObstacles(position))
        {
            score += 10f;
        }

        return score;
    }

    Vector3 FindNearbyPosition()
    {
        // Find a random nearby position for patrolling
        Vector2 randomCircle = Random.insideUnitCircle * 15f;
        Vector3 randomPoint = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomPoint, out hit, 20f, NavMesh.AllAreas))
        {
            navAgent.SetDestination(hit.position);
            return hit.position;
        }

        return Vector3.zero;
    }

    void UpdateTrajectoryCalculation()
    {
        hasValidTrajectory = false;
        trajectoryPoints.Clear();

        if (!hasGPSLock || !useProjectilePhysics || firePoints == null || firePoints.Length == 0) return;

        Vector3 targetPos = predictedPlayerPosition;
        // Use first fire point for trajectory calculation (or average position)
        Vector3 startPos = firePoints[0].position;

        // Calculate trajectory
        Vector3 velocity = CalculateProjectileVelocity(startPos, targetPos);
        if (velocity != Vector3.zero)
        {
            calculatedVelocity = velocity;
            calculatedSpeed = velocity.magnitude;
            hasValidTrajectory = true;

            // Calculate trajectory points for visualization
            CalculateTrajectoryPoints(startPos, velocity);
        }
    }

    Vector3 CalculateProjectileVelocity(Vector3 startPos, Vector3 targetPos)
    {
        Vector3 displacement = targetPos - startPos;
        Vector3 horizontalDisplacement = new Vector3(displacement.x, 0, displacement.z);
        float horizontalDistance = horizontalDisplacement.magnitude;
        float verticalDistance = displacement.y;

        Vector3 gravity = Physics.gravity * gravityScale;
        float g = -gravity.y; // Gravity magnitude (positive)

        // Try different speeds to find optimal trajectory
        for (float speed = minBulletSpeed; speed <= maxBulletSpeed; speed += 2f)
        {
            // Calculate time to reach target horizontally
            float timeToTarget = horizontalDistance / speed;

            // Calculate required vertical velocity
            float verticalVelocity = (verticalDistance + 0.5f * g * timeToTarget * timeToTarget) / timeToTarget;

            // Check if this creates a valid trajectory (not too steep)
            float launchAngle = Mathf.Atan2(verticalVelocity, speed) * Mathf.Rad2Deg;
            if (launchAngle >= -45f && launchAngle <= 45f) // Reasonable launch angle
            {
                Vector3 horizontalVelocity = horizontalDisplacement.normalized * speed;
                Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;

                return velocity;
            }
        }

        // If no valid trajectory found, use direct trajectory with max speed
        Vector3 direction = displacement.normalized;
        return direction * maxBulletSpeed;
    }

    void CalculateTrajectoryPoints(Vector3 startPos, Vector3 velocity)
    {
        trajectoryPoints.Clear();

        Vector3 gravity = Physics.gravity * gravityScale;
        float timeStep = 0.1f;
        Vector3 currentPos = startPos;
        Vector3 currentVel = velocity;

        for (int i = 0; i < 100; i++) // Limit points
        {
            trajectoryPoints.Add(currentPos);

            currentPos += currentVel * timeStep;
            currentVel += gravity * timeStep;

            // Stop if trajectory goes too far down or too far away
            if (currentPos.y < playerGPSPosition.y - 10f ||
                Vector3.Distance(startPos, currentPos) > sniperRange * 1.5f)
            {
                break;
            }
        }
    }

    IEnumerator FireBurst()
    {
        isFiring = true;
        int bulletsToFire = Mathf.Min(bulletsPerBurst, currentAmmo);

        for (int i = 0; i < bulletsToFire; i++)
        {
            FireSniperShot();
            currentAmmo--;

            Debug.Log($"Burst shot {i + 1}/{bulletsToFire} fired! Ammo: {currentAmmo}");

            // Wait between burst shots (except for the last one)
            if (i < bulletsToFire - 1)
            {
                yield return new WaitForSeconds(timeBetweenBursts);

                // Re-check target during burst
                if (!hasGPSLock || (requireLineOfSightToShoot && !CanSeePlayer()))
                {
                    Debug.Log("Lost target during burst, stopping burst fire");
                    break;
                }

                // Slight aim adjustment for next shot in burst
                RotateTowardsTarget(predictedPlayerPosition);
            }
        }

        isFiring = false;
        Debug.Log($"Burst complete! Remaining ammo: {currentAmmo}");
    }

    void FireSniperShot()
    {
        if (bulletPrefab == null || firePoints == null || firePoints.Length == 0 || currentAmmo <= 0)
        {
            Debug.LogWarning("Missing bullet prefab or fire points!");
            return;
        }

        Vector3 targetPos = predictedPlayerPosition;

        // Choose fire point (cycle through them or use random)
        Transform selectedFirePoint = firePoints[currentBurstCount % firePoints.Length];
        Vector3 startPos = selectedFirePoint.position;

        // Slight spread for multiple fire points
        Vector3 spreadOffset = Vector3.zero;
        if (firePoints.Length > 1 && useBurstFire)
        {
            float spreadAmount = 0.5f; // Small spread for multiple barrels
            spreadOffset = new Vector3(
                Random.Range(-spreadAmount, spreadAmount),
                Random.Range(-spreadAmount * 0.5f, spreadAmount * 0.5f),
                Random.Range(-spreadAmount, spreadAmount)
            );
        }

        GameObject bullet = Instantiate(bulletPrefab, startPos, Quaternion.identity);

        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        if (bulletRb != null)
        {
            Vector3 adjustedTarget = targetPos + spreadOffset;

            if (useProjectilePhysics && hasValidTrajectory)
            {
                // Calculate trajectory for this specific fire point
                Vector3 velocity = CalculateProjectileVelocity(startPos, adjustedTarget);
                if (velocity != Vector3.zero)
                {
                    bulletRb.velocity = velocity;
                    bulletRb.useGravity = true;

                    Debug.Log($"Sniper fired from {selectedFirePoint.name} with projectile physics! Speed: {velocity.magnitude:F1} m/s");
                }
                else
                {
                    // Fallback to direct shot
                    Vector3 direction = (adjustedTarget - startPos).normalized;
                    bulletRb.velocity = direction * maxBulletSpeed;
                    bulletRb.useGravity = false;
                }
            }
            else
            {
                // Direct shot
                Vector3 direction = (adjustedTarget - startPos).normalized;
                bulletRb.velocity = direction * maxBulletSpeed;
                bulletRb.useGravity = false;

                Debug.Log($"Sniper fired from {selectedFirePoint.name} direct shot!");
            }
        }

        // Increment burst count for fire point cycling
        currentBurstCount++;

        // Set bullet damage - handle via bullet script or tags
        // You can add your own bullet damage handling here

        // Destroy bullet after some time
        Destroy(bullet, 8.0f);
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        return HasLineOfSightToPosition(transform.position, player.position);
    }

    void StartReload()
    {
        if (!isReloading)
        {
            isReloading = true;
            reloadTimer = reloadTime;
            Debug.Log("Sniper started reloading...");
        }
    }

    void RotateTowardsTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0; // Keep rotation horizontal

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            float rotationStep = rotationSpeed * Time.deltaTime / 100f;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationStep);
        }
    }

    void ChangeState(SniperState newState)
    {
        if (currentState != newState)
        {
            previousState = currentState;
            currentState = newState;
            stateChangeTime = Time.time;

            Debug.Log($"Sniper state changed from {previousState} to {newState}");

            // Reset state-specific variables
            switch (newState)
            {
                case SniperState.Positioning:
                    hasGoodPosition = false;
                    navAgent.isStopped = false;
                    isAiming = false;
                    break;
                case SniperState.Aiming:
                    aimTimer = 0f;
                    isAiming = true;
                    navAgent.isStopped = true;
                    break;
                case SniperState.Shooting:
                    isAiming = false;
                    isFiring = false;
                    if (firingCoroutine != null)
                    {
                        StopCoroutine(firingCoroutine);
                        firingCoroutine = null;
                    }
                    break;
                case SniperState.Relocating:
                    hasGoodPosition = false;
                    isAiming = false;
                    navAgent.isStopped = false;
                    repositionTimer = 0f;
                    break;
                case SniperState.Reloading:
                    isAiming = false;
                    isFiring = false;
                    navAgent.isStopped = false;
                    isReloadMoving = false;
                    reloadMoveTimer = 0f;
                    if (firingCoroutine != null)
                    {
                        StopCoroutine(firingCoroutine);
                        firingCoroutine = null;
                    }
                    break;
                case SniperState.Hunting:
                    isAiming = false;
                    navAgent.isStopped = false;
                    break;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        // Sniper range
        if (showSniperRange)
        {
            Gizmos.color = sniperRangeColor;
            Gizmos.DrawWireSphere(transform.position, sniperRange);

            // Preferred sniper distance
            Gizmos.color = new Color(sniperRangeColor.r, sniperRangeColor.g, sniperRangeColor.b, 0.1f);
            Gizmos.DrawWireSphere(transform.position, preferredSniperDistance);
        }

        // GPS tracking range
        if (maxTrackingRange > 0)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, maxTrackingRange);
        }

        if (Application.isPlaying)
        {
            // GPS connection line
            if (showGPSConnection && hasGPSLock && player != null)
            {
                Gizmos.color = gpsLineColor;
                Gizmos.DrawLine(transform.position + Vector3.up * 2f, playerGPSPosition + Vector3.up * 2f);

                // Draw predicted position
                if (predictPlayerMovement)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(predictedPlayerPosition, 0.5f);
                    Gizmos.DrawLine(playerGPSPosition, predictedPlayerPosition);
                }

                // Show distance
                float gpsDistance = Vector3.Distance(transform.position, playerGPSPosition);
                Vector3 midPoint = (transform.position + playerGPSPosition) * 0.5f + Vector3.up * 4f;

#if UNITY_EDITOR
                UnityEditor.Handles.Label(midPoint, $"Range: {gpsDistance:F1}m\nState: {currentState}");
#endif
            }

            // Show trajectory path
            if (showTrajectoryPath && hasValidTrajectory && trajectoryPoints.Count > 1)
            {
                Gizmos.color = trajectoryColor;
                for (int i = 0; i < trajectoryPoints.Count - 1; i++)
                {
                    Gizmos.DrawLine(trajectoryPoints[i], trajectoryPoints[i + 1]);
                }

                // Draw trajectory end point
                if (trajectoryPoints.Count > 0)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(trajectoryPoints[trajectoryPoints.Count - 1], 0.3f);
                }
            }

            // Show current target
            if (hasGPSLock)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(playerGPSPosition, 1f);

                // Show firing direction from all fire points
                if (currentState == SniperState.Aiming || currentState == SniperState.Shooting)
                {
                    if (firePoints != null && firePoints.Length > 0)
                    {
                        for (int i = 0; i < firePoints.Length; i++)
                        {
                            if (firePoints[i] != null)
                            {
                                Gizmos.color = i == 0 ? Color.red : Color.yellow; // Primary fire point in red
                                Gizmos.DrawLine(firePoints[i].position, predictedPlayerPosition);

                                // Draw fire point indicator
                                Gizmos.DrawWireSphere(firePoints[i].position, 0.2f);

#if UNITY_EDITOR
                            UnityEditor.Handles.Label(firePoints[i].position + Vector3.up * 0.5f, $"FP{i + 1}");
#endif
                            }
                        }
                    }
                }

                // Show burst fire indicator
                if (isFiring && useBurstFire)
                {
                    Gizmos.color = Color.red;
                    float burstIndicatorSize = 0.5f + (currentBurstCount % bulletsPerBurst) * 0.3f;
                    Gizmos.DrawWireSphere(transform.position + Vector3.up * 4f, burstIndicatorSize);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(transform.position + Vector3.up * 4.5f, 
                    $"BURST: {currentBurstCount % bulletsPerBurst + 1}/{bulletsPerBurst}");
#endif
                }
            }

            // Show best sniper position when evaluating
            if (bestSniperPosition != Vector3.zero && currentState == SniperState.Positioning)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(bestSniperPosition, 1f);
                Gizmos.DrawLine(transform.position, bestSniperPosition);

#if UNITY_EDITOR
                UnityEditor.Handles.Label(bestSniperPosition + Vector3.up * 2f, $"Score: {bestPositionScore:F1}");
#endif
            }

            // Show evaluated positions
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            foreach (Vector3 pos in evaluatedPositions)
            {
                Gizmos.DrawWireSphere(pos, 0.5f);
            }

            // State-specific visualizations
            switch (currentState)
            {
                case SniperState.Aiming:
                    // Show aiming progress
                    Gizmos.color = Color.yellow;
                    float aimProgress = aimTimer / aimingTime;
                    Gizmos.DrawWireSphere(transform.position + Vector3.up * 3f, aimProgress * 2f);
                    break;

                case SniperState.Reloading:
                    // Show reload progress
                    Gizmos.color = Color.blue;
                    float reloadProgress = 1f - (reloadTimer / reloadTime);
                    Gizmos.DrawWireSphere(transform.position + Vector3.up * 2.5f, reloadProgress * 1.5f);
                    break;
            }

            // Show NavMesh path
            if (navAgent != null && navAgent.hasPath && navAgent.path.corners.Length > 1)
            {
                Gizmos.color = Color.magenta;
                Vector3[] pathCorners = navAgent.path.corners;
                for (int i = 0; i < pathCorners.Length - 1; i++)
                {
                    Gizmos.DrawLine(pathCorners[i], pathCorners[i + 1]);
                }
            }

            // Show line of sight check
            if (requireLineOfSightToShoot && hasGPSLock)
            {
                bool hasLOS = CanSeePlayer();
                Gizmos.color = hasLOS ? Color.green : Color.red;
                Vector3 eyePos = transform.position + Vector3.up * 1.5f;
                Gizmos.DrawLine(eyePos, playerGPSPosition + Vector3.up * 1f);
            }
        }

        // Show positioning search area
        Gizmos.color = new Color(0f, 1f, 0f, 0.1f);
        if (hasGPSLock)
        {
            Gizmos.DrawWireSphere(playerGPSPosition, repositionRadius);
        }
    }

    public string GetStateInfo()
    {
        string gpsInfo = hasGPSLock ?
            $"\nGPS Lock: YES ({distanceToPlayer:F1}m)" :
            "\nGPS Lock: NO";

        string trackingRange = maxTrackingRange > 0 ?
            $"\nGPS Range: {maxTrackingRange}m" :
            "\nGPS Range: Unlimited";

        string trajectoryInfo = hasValidTrajectory ?
            $"\nTrajectory: Valid ({calculatedSpeed:F1} m/s)" :
            "\nTrajectory: Invalid";

        string predictionInfo = predictPlayerMovement ?
            $"\nPrediction: {predictionTime}s ahead" :
            "\nPrediction: Disabled";

        string positionInfo = hasGoodPosition ?
            $"\nPosition: Good (Score: {bestPositionScore:F1})" :
            "\nPosition: Searching";

        string aimingInfo = isAiming ?
            $"\nAiming: {aimTimer:F1}s / {aimingTime:F1}s" :
            "\nAiming: No";

        string burstInfo = useBurstFire ?
            $"\nBurst Mode: {bulletsPerBurst} bullets" :
            "\nBurst Mode: Disabled";

        string firingInfo = isFiring ?
            $"\nFiring: YES ({currentBurstCount % bulletsPerBurst + 1}/{bulletsPerBurst})" :
            "\nFiring: NO";

        string firePointInfo = firePoints != null ?
            $"\nFire Points: {firePoints.Length}" :
            "\nFire Points: 0";

        return $"State: {currentState} (Previous: {previousState})" +
               $"\nAmmo: {currentAmmo}/{maxAmmo}" +
               $"\nReloading: {isReloading}" +
               $"\nReload Timer: {(isReloading ? reloadTimer.ToString("F1") : "N/A")}" +
               gpsInfo + trackingRange + trajectoryInfo + predictionInfo +
               positionInfo + aimingInfo + burstInfo + firingInfo + firePointInfo +
               $"\nReposition Timer: {repositionTimer:F1}s / {repositionTime:F1}s" +
               $"\nTime Since State Change: {(Time.time - stateChangeTime):F1}s";
    }
}