using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using UnityEngine.UI;

public class AITankFSM : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 3.0f;
    public float rotationSpeed = 100.0f;
    public GameObject[] leftWheels;
    public GameObject[] rightWheels;
    public float wheelRotationSpeed = 200.0f;

    [Header("Turn Settings")]
    public float sharpTurnAngle = 40.0f;
    public float slowTurnSpeedMultiplier = 0.3f;

    [Header("AI Settings")]
    public float detectionRange = 10.0f;
    public float attackRange = 5.0f;
    public float patrolRadius = 8.0f;
    public float patrolWaitTime = 2.0f;
    public float chaseSpeed = 4.0f;
    public float fleeSpeed = 5.0f;
    public float turnSpeed = 2.0f;

    [Header("Combat Settings")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float timeBetweenBullets = 1.0f;
    public float bulletSpeed = 10.0f;
    public int maxAmmo = 5;
    public float reloadTime = 3.0f;

    [Header("Flee Settings")]
    public float fleeDistance = 15.0f;
    public float fleeTime = 5.0f;
    public LayerMask obstacleLayerMask = -1;
    public float coverSearchRadius = 20.0f;
    public int coverSearchPoints = 16;
    public float minCoverDistance = 3.0f;
    public float edgeAvoidanceRadius = 2.0f;

    // FSM States
    public enum AIState
    {
        Patrol,
        Chase,
        Attack,
        Flee
    }

    // Private variables
    private AIState currentState;
    private NavMeshAgent navAgent;
    private Transform player;
    private Vector3 patrolCenter;
    private Vector3 targetPatrolPoint;
    private float lastFireTime;
    private float patrolWaitTimer;
    private float lastKnownPlayerTime;
    private Vector3 lastKnownPlayerPosition;

    // Combat variables
    private int currentAmmo;
    private bool isReloading;
    private float reloadTimer;

    // Flee variables
    private float fleeTimer;
    private Vector3 fleeTarget;
    private bool hasFoundCover;
    private List<Vector3> potentialCoverPoints;

    // Cover system cache
    private float lastCoverSearchTime;
    private float coverSearchCooldown = 2.0f;

    // Movement tracking for realistic wheel rotation
    private Vector3 lastPosition;
    private float lastRotationY;
    private float currentForwardSpeed;
    private float currentTurnSpeed;
    private bool isSharpTurning;


    [Header("Health Settings")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("UI References")]
    public Image healthBarFill; // The health bar fill image
    public Image reloadBarFill; // The ammo/reload bar fill image


    void Start()
    {
        //health UI
        currentHealth = maxHealth;
        UpdateHealthUI();

        navAgent = GetComponent<NavMeshAgent>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        patrolCenter = transform.position;
        currentState = AIState.Patrol;
        lastPosition = transform.position;
        lastRotationY = transform.eulerAngles.y;
        currentAmmo = maxAmmo;
        isReloading = false;
        potentialCoverPoints = new List<Vector3>();

        // Initialize UI
        UpdateAmmoReloadUI();

        // Configure NavMeshAgent
        navAgent.speed = moveSpeed;
        navAgent.angularSpeed = rotationSpeed;
        navAgent.acceleration = 8.0f;
        navAgent.stoppingDistance = 0.5f;

        SetNewPatrolPoint();
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();

        if (currentHealth <= 0)
        {
            DestroyTank();
        }
    }

    void DestroyTank()
    {
        // Add score when tank is destroyed
        ScoreManager scoreManager = FindObjectOfType<ScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.AddScore(10);
            Debug.Log("Tank destroyed! Added 10 points to score.");
        }
        else
        {
            Debug.LogWarning("ScoreManager not found! Could not add score.");
        }

        // Destroy the tank GameObject
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
                // During reload, show reload progress (0 to 1)
                float reloadProgress = 1f - (reloadTimer / reloadTime);
                reloadBarFill.fillAmount = reloadProgress;
            }
            else
            {
                // When not reloading, show current ammo percentage
                float ammoPercentage = (float)currentAmmo / maxAmmo;
                reloadBarFill.fillAmount = ammoPercentage;
            }
        }
    }

    void Update()
    {
        // Update reload timer
        UpdateReload();

        // Update FSM
        UpdateFSM();

        // Check for sharp turning and adjust speed
        UpdateTurningBehavior();

        // Calculate realistic movement for wheel rotation
        CalculateRealisticMovement();

        // Rotate wheels based on calculated movement
        RotateWheelsRealistically();
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
                Debug.Log("Tank reloaded!");
            }
        }

        // Always update the UI (handles both ammo display and reload progress)
        UpdateAmmoReloadUI();
    }


    void UpdateFSM()
    {
        switch (currentState)
        {
            case AIState.Patrol:
                PatrolBehavior();
                break;
            case AIState.Chase:
                ChaseBehavior();
                break;
            case AIState.Attack:
                AttackBehavior();
                break;
            case AIState.Flee:
                FleeBehavior();
                break;
        }
    }

    void PatrolBehavior()
    {
        // Check if out of ammo and need to flee (priority check)
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        // Check for player detection
        if (CanSeePlayer())
        {
            lastKnownPlayerPosition = player.position;
            lastKnownPlayerTime = Time.time;
            ChangeState(AIState.Chase);
            return;
        }

        // Move towards patrol point
        float distanceToPatrol = Vector3.Distance(transform.position, targetPatrolPoint);

        if (distanceToPatrol > 1.0f)
        {
            navAgent.SetDestination(targetPatrolPoint);
            navAgent.isStopped = false;
        }
        else
        {
            // Reached patrol point, wait then set new one
            navAgent.isStopped = true;
            patrolWaitTimer += Time.deltaTime;

            if (patrolWaitTimer >= patrolWaitTime)
            {
                SetNewPatrolPoint();
                patrolWaitTimer = 0f;
            }
        }
    }

    void ChaseBehavior()
    {
        // Check if out of ammo and need to flee (priority check)
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        if (!CanSeePlayer())
        {
            // Lost sight of player, continue patrolling
            ChangeState(AIState.Patrol);
            return;
        }

        lastKnownPlayerPosition = player.position;
        lastKnownPlayerTime = Time.time;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= attackRange)
        {
            ChangeState(AIState.Attack);
            return;
        }

        // Chase the player
        navAgent.SetDestination(player.position);
        navAgent.isStopped = false;
    }

    void AttackBehavior()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // Check if out of ammo and need to flee (regardless of player visibility)
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        if (!CanSeePlayer())
        {
            ChangeState(AIState.Patrol);
            return;
        }

        if (distanceToPlayer > attackRange)
        {
            ChangeState(AIState.Chase);
            return;
        }

        // Stop moving and focus on attacking
        navAgent.isStopped = true;

        // Rotate to face player smoothly
        RotateTowardsTarget(player.position);

        // Fire at player if not reloading and has ammo
        if (!isReloading && currentAmmo > 0 && Time.time - lastFireTime >= timeBetweenBullets)
        {
            FireAtPlayer();
            lastFireTime = Time.time;
        }
    }

    void FleeBehavior()
    {
        fleeTimer += Time.deltaTime;

        // Try to find cover if we haven't found it yet or need to update
        if (!hasFoundCover || Time.time - lastCoverSearchTime > coverSearchCooldown)
        {
            Vector3 bestCoverPoint = FindBestCoverPoint();
            if (bestCoverPoint != Vector3.zero)
            {
                fleeTarget = bestCoverPoint;
                hasFoundCover = true;
                lastCoverSearchTime = Time.time;
                Debug.Log("Tank found cover behind obstacle!");
            }
            else
            {
                // Fallback: move away from player if no cover found
                FleeDirectlyFromPlayer();
            }
        }

        // Move to cover position
        if (Vector3.Distance(transform.position, fleeTarget) > 1.5f)
        {
            navAgent.SetDestination(fleeTarget);
            navAgent.isStopped = false;
        }
        else
        {
            // Reached cover, stop and hide
            navAgent.isStopped = true;

            // Face away from player while hiding (more realistic)
            if (player != null)
            {
                Vector3 hideDirection = (transform.position - player.position).normalized;
                Vector3 lookDirection = transform.position + hideDirection;
                RotateTowardsTarget(lookDirection);
            }
        }

        // Return to patrol after reload is complete
        if (!isReloading && currentAmmo > 0)
        {
            fleeTimer = 0f;
            hasFoundCover = false;
            ChangeState(AIState.Patrol);
        }
    }

    void FleeDirectlyFromPlayer()
    {
        if (player != null)
        {
            // Always use current player position
            Vector3 fleeDirection = (transform.position - player.position).normalized;
            Vector3 fleePosition = transform.position + fleeDirection * fleeDistance;

            // Find valid NavMesh position away from edges
            Vector3 safeFleePosition = FindSafeNavMeshPosition(fleePosition, fleeDistance);

            if (safeFleePosition != Vector3.zero)
            {
                fleeTarget = safeFleePosition;
            }
            else
            {
                // If can't find safe position, try multiple directions
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * 30f * Mathf.Deg2Rad;
                    Vector3 searchDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 testPosition = transform.position + searchDirection * fleeDistance;

                    Vector3 testSafePosition = FindSafeNavMeshPosition(testPosition, fleeDistance * 0.5f);
                    if (testSafePosition != Vector3.zero)
                    {
                        fleeTarget = testSafePosition;
                        break;
                    }
                }
            }
        }
    }

    Vector3 FindBestCoverPoint()
    {
        if (player == null) return Vector3.zero;

        Vector3 bestCoverPoint = Vector3.zero;
        float bestScore = -1f;

        // Always use current player position for fleeing
        Vector3 currentPlayerPosition = player.position;

        // Generate potential cover points in a circle around the tank
        for (int i = 0; i < coverSearchPoints; i++)
        {
            float angle = i * (360f / coverSearchPoints) * Mathf.Deg2Rad;
            Vector3 searchDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 searchPoint = transform.position + searchDirection * coverSearchRadius;

            // Find valid NavMesh position with edge avoidance
            NavMeshHit navHit;
            Vector3 validCoverPoint = FindSafeNavMeshPosition(searchPoint, coverSearchRadius);

            if (validCoverPoint == Vector3.zero)
                continue;

            // Skip if too close to current position
            if (Vector3.Distance(transform.position, validCoverPoint) < minCoverDistance)
                continue;

            // Check if this point provides cover from current player position
            float coverScore = EvaluateCoverPoint(validCoverPoint, currentPlayerPosition);

            if (coverScore > bestScore)
            {
                bestScore = coverScore;
                bestCoverPoint = validCoverPoint;
            }
        }

        // If no good cover found, try raycasting to find obstacles
        if (bestScore < 0.1f)
        {
            return FindCoverBehindObstacles(currentPlayerPosition);
        }

        return bestCoverPoint;
    }

    Vector3 FindCoverBehindObstacles(Vector3 threatPosition)
    {
        // Cast rays in multiple directions to find obstacles
        Vector3 threatDirection = (threatPosition - transform.position).normalized;

        for (int i = 0; i < 8; i++)
        {
            // Create perpendicular directions to threat
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 sideDirection = new Vector3(
                threatDirection.z * Mathf.Cos(angle) - threatDirection.x * Mathf.Sin(angle),
                0,
                threatDirection.x * Mathf.Cos(angle) + threatDirection.z * Mathf.Sin(angle)
            );

            RaycastHit hit;
            Vector3 rayStart = transform.position + Vector3.up * 0.5f;

            // Cast ray to find obstacle
            if (Physics.Raycast(rayStart, sideDirection, out hit, coverSearchRadius, obstacleLayerMask))
            {
                // Find position behind the obstacle
                Vector3 behindObstacle = hit.point + hit.normal * 2f;

                // Use safe NavMesh position finding
                Vector3 safeCoverPoint = FindSafeNavMeshPosition(behindObstacle, 3f);

                if (safeCoverPoint != Vector3.zero)
                {
                    // Verify this position provides cover
                    if (!HasLineOfSightToPosition(safeCoverPoint, threatPosition))
                    {
                        return safeCoverPoint;
                    }
                }
            }
        }

        return Vector3.zero;
    }

    Vector3 FindSafeNavMeshPosition(Vector3 targetPosition, float searchRadius)
    {
        NavMeshHit navHit;

        // First try to find any valid NavMesh position
        if (!NavMesh.SamplePosition(targetPosition, out navHit, searchRadius, NavMesh.AllAreas))
        {
            return Vector3.zero;
        }

        Vector3 candidatePosition = navHit.position;

        // Check if this position is too close to NavMesh edges
        if (!IsPositionAwayFromEdges(candidatePosition))
        {
            // Try to find a better position away from edges
            Vector3 betterPosition = FindPositionAwayFromEdges(candidatePosition, searchRadius);
            if (betterPosition != Vector3.zero)
            {
                return betterPosition;
            }
        }

        return candidatePosition;
    }

    bool IsPositionAwayFromEdges(Vector3 position)
    {
        // Check in 8 directions around the position
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 checkPoint = position + direction * edgeAvoidanceRadius;

            NavMeshHit hit;
            // If we can't find NavMesh nearby, we're too close to an edge
            if (!NavMesh.SamplePosition(checkPoint, out hit, 1f, NavMesh.AllAreas))
            {
                return false;
            }
        }
        return true;
    }

    Vector3 FindPositionAwayFromEdges(Vector3 originalPosition, float searchRadius)
    {
        // Try multiple positions in a spiral pattern to find one away from edges
        for (float radius = 2f; radius <= searchRadius; radius += 1f)
        {
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                Vector3 testDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 testPosition = originalPosition + testDirection * radius;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(testPosition, out hit, 2f, NavMesh.AllAreas))
                {
                    if (IsPositionAwayFromEdges(hit.position))
                    {
                        return hit.position;
                    }
                }
            }
        }
        return Vector3.zero;
    }

    float EvaluateCoverPoint(Vector3 coverPoint, Vector3 threatPosition)
    {
        float score = 0f;

        // 1. Check if threat can see this position (most important)
        if (HasLineOfSightToPosition(coverPoint, threatPosition))
        {
            return 0f; // Visible to threat = bad cover
        }
        score += 50f; // Hidden from threat = good

        // 2. Distance from threat (further is better for hiding)
        float distanceToThreat = Vector3.Distance(coverPoint, threatPosition);
        score += Mathf.Clamp01(distanceToThreat / coverSearchRadius) * 20f;

        // 3. Distance from current position (closer is better for quick escape)
        float distanceFromSelf = Vector3.Distance(coverPoint, transform.position);
        score += Mathf.Clamp01(1f - (distanceFromSelf / coverSearchRadius)) * 10f;

        // 4. Check if there's an obstacle between cover point and threat
        RaycastHit hit;
        Vector3 directionToThreat = (threatPosition - coverPoint).normalized;
        if (Physics.Raycast(coverPoint + Vector3.up * 0.5f, directionToThreat, out hit,
            Vector3.Distance(coverPoint, threatPosition), obstacleLayerMask))
        {
            score += 15f; // Obstacle blocking = extra good
        }

        return score;
    }

    bool HasLineOfSightToPlayer(Vector3 fromPosition)
    {
        if (player == null) return false;
        return HasLineOfSightToPosition(fromPosition, player.position);
    }

    bool HasLineOfSightToPosition(Vector3 fromPosition, Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - fromPosition).normalized;
        RaycastHit hit;

        // Cast ray from potential position to target
        if (Physics.Raycast(fromPosition + Vector3.up * 0.5f, directionToTarget, out hit,
            Vector3.Distance(fromPosition, targetPosition)))
        {
            // If ray hits player first, there's line of sight (only check if target is player)
            if (targetPosition == player.position)
            {
                return hit.collider.CompareTag("Player");
            }
            else
            {
                // For other positions, any obstacle blocks line of sight
                return false;
            }
        }

        return true; // No obstacles = line of sight exists
    }

    void StartReload()
    {
        isReloading = true;
        reloadTimer = reloadTime;
        Debug.Log("Tank is reloading...");
    }


    void UpdateTurningBehavior()
    {
        if (navAgent.hasPath && navAgent.path.corners.Length > 1)
        {
            // Calculate angle to next waypoint
            Vector3 directionToWaypoint = (navAgent.path.corners[1] - transform.position).normalized;
            Vector3 currentForward = transform.forward;

            float angleToTarget = Vector3.Angle(currentForward, directionToWaypoint);

            // Check if this is a sharp turn
            if (angleToTarget > sharpTurnAngle)
            {
                if (!isSharpTurning)
                {
                    isSharpTurning = true;
                    // Reduce speed for sharp turn
                    navAgent.speed = navAgent.speed * slowTurnSpeedMultiplier;
                }
            }
            else
            {
                if (isSharpTurning)
                {
                    isSharpTurning = false;
                    // Restore normal speed
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
        // Restore speed based on current state
        switch (currentState)
        {
            case AIState.Patrol:
                navAgent.speed = moveSpeed;
                break;
            case AIState.Chase:
                navAgent.speed = chaseSpeed;
                break;
            case AIState.Attack:
                navAgent.speed = moveSpeed;
                break;
            case AIState.Flee:
                navAgent.speed = fleeSpeed;
                break;
        }
    }

    void RotateTowardsTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0; // Keep rotation only on Y axis

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            float rotationStep = rotationSpeed * Time.deltaTime / 100f;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationStep);
        }
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > detectionRange) return false;

        // Raycast to check line of sight
        Vector3 rayDirection = (player.position - transform.position).normalized;
        RaycastHit hit;

        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, rayDirection, out hit, detectionRange))
        {
            return hit.collider.CompareTag("Player");
        }

        return false;
    }

    void FireAtPlayer()
    {
        if (bulletPrefab != null && firePoint != null && currentAmmo > 0)
        {
            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null)
            {
                bulletRb.velocity = firePoint.forward * bulletSpeed;
            }

            // Decrease ammo and update UI immediately
            currentAmmo--;
            UpdateAmmoReloadUI(); // Update the bar immediately when firing

            Debug.Log($"Tank fired! Ammo remaining: {currentAmmo}");

            // Destroy bullet after some time
            Destroy(bullet, 5.0f);
        }
    }

    void SetNewPatrolPoint()
    {
        // Generate random point within patrol radius
        Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
        Vector3 randomPoint = patrolCenter + new Vector3(randomCircle.x, 0, randomCircle.y);

        // Make sure the point is on the NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomPoint, out hit, patrolRadius, NavMesh.AllAreas))
        {
            targetPatrolPoint = hit.position;
        }
        else
        {
            targetPatrolPoint = patrolCenter; // Fallback to center if no valid point found
        }
    }

    void ChangeState(AIState newState)
    {
        currentState = newState;

        // Reset state-specific timers and agent settings
        switch (newState)
        {
            case AIState.Patrol:
                patrolWaitTimer = 0f;
                navAgent.speed = moveSpeed;
                break;
            case AIState.Chase:
                navAgent.speed = chaseSpeed;
                break;
            case AIState.Attack:
                navAgent.speed = moveSpeed;
                break;
            case AIState.Flee:
                fleeTimer = 0f;
                hasFoundCover = false;
                navAgent.speed = fleeSpeed;
                break;
        }
    }

    void CalculateRealisticMovement()
    {
        // Get current movement data
        Vector3 currentPosition = transform.position;
        float currentRotationY = transform.eulerAngles.y;

        // Calculate forward movement speed
        Vector3 positionDelta = currentPosition - lastPosition;
        Vector3 localPositionDelta = transform.InverseTransformDirection(positionDelta);
        currentForwardSpeed = localPositionDelta.z / Time.deltaTime;

        // Calculate rotation speed (turn rate)
        float rotationDelta = Mathf.DeltaAngle(lastRotationY, currentRotationY);
        currentTurnSpeed = rotationDelta / Time.deltaTime;

        // Smooth the values to avoid jittery movement
        currentForwardSpeed = Mathf.Lerp(currentForwardSpeed, navAgent.velocity.magnitude, Time.deltaTime * 5f);
        currentTurnSpeed = Mathf.Lerp(currentTurnSpeed, rotationDelta / Time.deltaTime, Time.deltaTime * 3f);

        // Store for next frame
        lastPosition = currentPosition;
        lastRotationY = currentRotationY;
    }

    void RotateWheelsRealistically()
    {
        // Calculate wheel rotation based on actual movement
        float wheelRotation = currentForwardSpeed * wheelRotationSpeed * Time.deltaTime;

        // Calculate differential rotation for turning
        // When turning, inner wheels slow down, outer wheels speed up
        float turnEffect = currentTurnSpeed * 0.01f; // Adjust this multiplier for more/less turn effect

        // Rotate left wheels
        foreach (GameObject wheel in leftWheels)
        {
            if (wheel != null)
            {
                float leftWheelSpeed = wheelRotation + (currentTurnSpeed > 0 ? -turnEffect : turnEffect);
                wheel.transform.Rotate(leftWheelSpeed, 0.0f, 0.0f);
            }
        }

        // Rotate right wheels
        foreach (GameObject wheel in rightWheels)
        {
            if (wheel != null)
            {
                float rightWheelSpeed = wheelRotation + (currentTurnSpeed > 0 ? turnEffect : -turnEffect);
                wheel.transform.Rotate(rightWheelSpeed, 0.0f, 0.0f);
            }
        }
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

        // Patrol radius
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(patrolCenter, patrolRadius);

        // Current target
        if (Application.isPlaying)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(targetPatrolPoint, 0.5f);

            // Show flee target if in flee state
            if (currentState == AIState.Flee)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f); // Orange color
                Gizmos.DrawWireSphere(fleeTarget, 0.8f);
                Gizmos.DrawLine(transform.position, fleeTarget);

                // Show cover search radius
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, coverSearchRadius);

                // Show edge avoidance radius around current position
                Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
                Gizmos.DrawWireSphere(transform.position, edgeAvoidanceRadius);

                // Show if tank has line of sight to player
                if (player != null)
                {
                    bool hasLOS = HasLineOfSightToPlayer(transform.position);
                    Gizmos.color = hasLOS ? Color.red : Color.green;
                    Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, player.position);
                }
            }

            // Show NavMesh path
            if (navAgent != null && navAgent.path != null)
            {
                Gizmos.color = Color.magenta;
                Vector3[] pathCorners = navAgent.path.corners;
                for (int i = 0; i < pathCorners.Length - 1; i++)
                {
                    Gizmos.DrawLine(pathCorners[i], pathCorners[i + 1]);
                }
            }
        }
    }

    // Public method to get current state info (for UI debugging)
    public string GetStateInfo()
    {
        return $"State: {currentState}\nAmmo: {currentAmmo}/{maxAmmo}\nReloading: {isReloading}\nReload Timer: {(isReloading ? reloadTimer.ToString("F1") : "N/A")}\nSharp Turning: {isSharpTurning}\nCurrent Speed: {navAgent.speed:F1}\nHas Cover: {hasFoundCover}";
    }
}