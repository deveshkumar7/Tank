using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using TMPro;
using UnityEngine.UI;

public class AiAggressive : MonoBehaviour
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

    [Header("GPS AI Settings")]
    public float gpsUpdateInterval = 0.5f; // How often to update player position
    public float maxTrackingRange = 100.0f; // Maximum range to track player (0 = unlimited)
    public bool requireLineOfSightToAttack = true; // Still need LOS to attack, but not to track
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

    [Header("Firing Restrictions")]
    public bool requireDirectAiming = true;
    public float firingAngle = 15.0f;
    public bool showFiringCone = true;

    [Header("Flee Settings")]
    public float fleeDistance = 15.0f;
    public float fleeTime = 5.0f;
    public LayerMask obstacleLayerMask = -1;
    public float coverSearchRadius = 20.0f;
    public int coverSearchPoints = 16;
    public float minCoverDistance = 3.0f;
    public float edgeAvoidanceRadius = 2.0f;
    public float minFleeTime = 1.0f;

    [Header("GPS Visualization")]
    public bool showGPSConnection = true; // Show line to player even through walls
    public Color gpsLineColor = new Color(1f, 0.5f, 0f);


    public bool showPlayerDirection = true; // Show arrow pointing to player

    // FSM States
    public enum AIState
    {
        Patrol,
        Hunt, // New state: knows where player is but can't see them
        Chase, // Can see player and is chasing
        Attack,
        Flee
    }

    // Private variables
    private AIState currentState;
    private AIState previousState;
    private NavMeshAgent navAgent;
    private Transform player;
    private Vector3 patrolCenter;
    private Vector3 targetPatrolPoint;
    private float lastFireTime;
    private float patrolWaitTimer;
    private float lastKnownPlayerTime;
    private Vector3 lastKnownPlayerPosition;
    private float stateChangeTime;

    // GPS tracking variables
    private Vector3 playerGPSPosition;
    private float lastGPSUpdate;
    private bool hasGPSLock;
    private float distanceToPlayer;

    // Combat variables
    private int currentAmmo;
    private bool isReloading;
    private float reloadTimer;

    // Flee variables
    private float fleeTimer;
    private Vector3 fleeTarget;
    private bool hasFoundCover;
    private List<Vector3> potentialCoverPoints;
    private bool hasReachedFleeTarget;

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
    public Image healthBarFill;
    public Image reloadBarFill;

    void Start()
    {
        currentHealth = maxHealth;
        UpdateHealthUI();

        navAgent = GetComponent<NavMeshAgent>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        patrolCenter = transform.position;
        currentState = AIState.Patrol;
        previousState = AIState.Patrol;
        stateChangeTime = Time.time;
        lastPosition = transform.position;
        lastRotationY = transform.eulerAngles.y;
        currentAmmo = maxAmmo;
        isReloading = false;
        hasReachedFleeTarget = false;
        potentialCoverPoints = new List<Vector3>();

        // Initialize GPS system
        hasGPSLock = false;
        lastGPSUpdate = 0f;

        UpdateAmmoReloadUI();

        // Configure NavMeshAgent
        navAgent.speed = moveSpeed;
        navAgent.angularSpeed = rotationSpeed;
        navAgent.acceleration = 8.0f;
        navAgent.stoppingDistance = 0.5f;

        SetNewPatrolPoint();

        Debug.Log("GPS Tank AI initialized - Can track player anywhere on NavMesh!");
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
        ScoreManager scoreManager = FindObjectOfType<ScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.AddScore(10);
            Debug.Log("GPS Tank destroyed! Added 10 points to score.");
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
    }

    void Update()
    {
        UpdateGPSTracking();
        UpdateReload();
        UpdateFSM();
        UpdateTurningBehavior();
        CalculateRealisticMovement();
        RotateWheelsRealistically();
    }

    void UpdateGPSTracking()
    {
        if (player == null)
        {
            hasGPSLock = false;
            return;
        }

        // Update GPS position at specified intervals
        if (Time.time - lastGPSUpdate >= gpsUpdateInterval)
        {
            playerGPSPosition = player.position;
            distanceToPlayer = Vector3.Distance(transform.position, playerGPSPosition);

            // Check if player is within tracking range (if limited)
            if (maxTrackingRange > 0 && distanceToPlayer > maxTrackingRange)
            {
                hasGPSLock = false;
                Debug.Log($"Player out of GPS range: {distanceToPlayer:F1}m (Max: {maxTrackingRange}m)");
            }
            else
            {
                hasGPSLock = true;
                lastKnownPlayerPosition = playerGPSPosition;
                lastKnownPlayerTime = Time.time;
                //Debug.Log($"GPS Lock: Player at {distanceToPlayer:F1}m");
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
                Debug.Log("GPS Tank reloaded!");
            }
        }

        UpdateAmmoReloadUI();
    }

    void UpdateFSM()
    {
        float timeSinceStateChange = Time.time - stateChangeTime;

        switch (currentState)
        {
            case AIState.Patrol:
                PatrolBehavior();
                break;
            case AIState.Hunt:
                HuntBehavior();
                break;
            case AIState.Chase:
                ChaseBehavior();
                break;
            case AIState.Attack:
                AttackBehavior();
                break;
            case AIState.Flee:
                FleeBehavior(timeSinceStateChange);
                break;
        }
    }

    void PatrolBehavior()
    {
        // Priority 1: Check if out of ammo and need to flee
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        // Priority 2: Check GPS tracking for player
        if (hasGPSLock)
        {
            if (CanSeePlayer())
            {
                // Can see player directly - go to chase
                ChangeState(AIState.Chase);
                return;
            }
            else
            {
                // Know where player is but can't see them - start hunting
                ChangeState(AIState.Hunt);
                return;
            }
        }

        // Normal patrol behavior
        float distanceToPatrol = Vector3.Distance(transform.position, targetPatrolPoint);

        if (distanceToPatrol > 1.0f)
        {
            if (navAgent.isStopped)
            {
                navAgent.isStopped = false;
            }
            navAgent.SetDestination(targetPatrolPoint);
        }
        else
        {
            navAgent.isStopped = true;
            patrolWaitTimer += Time.deltaTime;

            if (patrolWaitTimer >= patrolWaitTime)
            {
                SetNewPatrolPoint();
                patrolWaitTimer = 0f;
            }
        }
    }

    void HuntBehavior()
    {
        // Priority 1: Check if out of ammo and need to flee
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        // Priority 2: Check if lost GPS lock
        if (!hasGPSLock)
        {
            Debug.Log("Lost GPS lock on player, returning to patrol");
            ChangeState(AIState.Patrol);
            return;
        }

        // Priority 3: Check if can now see player
        if (CanSeePlayer())
        {
            ChangeState(AIState.Chase);
            return;
        }

        // Hunt behavior: move towards last known GPS position
        float distanceToGPSTarget = Vector3.Distance(transform.position, playerGPSPosition);

        if (distanceToGPSTarget <= attackRange)
        {
            // Close enough to attack range, try to get line of sight
            if (requireLineOfSightToAttack)
            {
                // Move around to try to get line of sight
                FindBetterPositionForLineOfSight();
            }
            else
            {
                // Can attack without line of sight (GPS guided)
                ChangeState(AIState.Attack);
                return;
            }
        }
        else
        {
            // Move towards player's GPS position
            if (navAgent.isStopped)
            {
                navAgent.isStopped = false;
            }

            NavMeshHit hit;
            if (NavMesh.SamplePosition(playerGPSPosition, out hit, 5f, NavMesh.AllAreas))
            {
                navAgent.SetDestination(hit.position);
                Debug.Log($"Hunting player via GPS - Distance: {distanceToGPSTarget:F1}m");
            }
        }
    }

    void ChaseBehavior()
    {
        // Priority 1: Check if out of ammo and need to flee
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        // Priority 2: Check if lost sight but still have GPS
        if (!CanSeePlayer())
        {
            if (hasGPSLock)
            {
                ChangeState(AIState.Hunt);
                return;
            }
            else
            {
                ChangeState(AIState.Patrol);
                return;
            }
        }

        // Update positions
        lastKnownPlayerPosition = player.position;
        lastKnownPlayerTime = Time.time;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // Priority 3: Check if close enough to attack
        if (distanceToPlayer <= attackRange)
        {
            ChangeState(AIState.Attack);
            return;
        }

        // Chase the player directly
        if (navAgent.isStopped)
        {
            navAgent.isStopped = false;
        }
        navAgent.SetDestination(player.position);
    }

    void AttackBehavior()
    {
        float distanceToTarget = hasGPSLock ? distanceToPlayer : Vector3.Distance(transform.position, player.position);

        // Priority 1: Check if out of ammo and need to flee
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        // Priority 2: Check attack conditions based on settings
        bool canAttack = false;
        Vector3 targetPosition = Vector3.zero;

        if (requireLineOfSightToAttack)
        {
            // Need line of sight to attack
            if (CanSeePlayer())
            {
                canAttack = true;
                targetPosition = player.position;
            }
            else if (hasGPSLock)
            {
                // Lost line of sight, go back to hunting
                ChangeState(AIState.Hunt);
                return;
            }
            else
            {
                ChangeState(AIState.Patrol);
                return;
            }
        }
        else
        {
            // Can attack using GPS guidance
            if (hasGPSLock)
            {
                canAttack = true;
                targetPosition = playerGPSPosition;
            }
            else if (CanSeePlayer())
            {
                canAttack = true;
                targetPosition = player.position;
            }
            else
            {
                ChangeState(AIState.Patrol);
                return;
            }
        }

        // Priority 3: Check if target is too far
        if (distanceToTarget > attackRange * 1.2f)
        {
            if (CanSeePlayer())
            {
                ChangeState(AIState.Chase);
            }
            else if (hasGPSLock)
            {
                ChangeState(AIState.Hunt);
            }
            else
            {
                ChangeState(AIState.Patrol);
            }
            return;
        }

        // Stop and attack
        navAgent.isStopped = true;
        RotateTowardsTarget(targetPosition);

        // Fire if conditions are met
        if (canAttack && CanFireAtTarget(targetPosition))
        {
            FireAtTarget(targetPosition);
            lastFireTime = Time.time;
        }
    }

    void FleeBehavior(float timeSinceStateChange)
    {
        fleeTimer += Time.deltaTime;

        bool canExitFlee = timeSinceStateChange >= minFleeTime;

        // Find cover if needed
        if (!hasFoundCover || (hasReachedFleeTarget && Time.time - lastCoverSearchTime > coverSearchCooldown))
        {
            Vector3 bestCoverPoint = FindBestCoverPoint();
            if (bestCoverPoint != Vector3.zero)
            {
                fleeTarget = bestCoverPoint;
                hasFoundCover = true;
                hasReachedFleeTarget = false;
                lastCoverSearchTime = Time.time;
                Debug.Log("GPS Tank found cover!");
            }
            else
            {
                FleeDirectlyFromPlayer();
                hasFoundCover = false;
                hasReachedFleeTarget = false;
            }
        }

        // Move to flee target
        float distanceToFleeTarget = Vector3.Distance(transform.position, fleeTarget);

        if (distanceToFleeTarget > 1.5f)
        {
            if (navAgent.isStopped)
            {
                navAgent.isStopped = false;
            }
            navAgent.SetDestination(fleeTarget);
            hasReachedFleeTarget = false;
        }
        else
        {
            if (!hasReachedFleeTarget)
            {
                navAgent.isStopped = true;
                hasReachedFleeTarget = true;
                Debug.Log("GPS Tank reached flee target");
            }

            // Face away from player GPS position
            if (hasGPSLock)
            {
                Vector3 hideDirection = (transform.position - playerGPSPosition).normalized;
                Vector3 lookDirection = transform.position + hideDirection;
                RotateTowardsTarget(lookDirection);
            }
        }

        // Only exit flee state if reload is complete AND minimum flee time has passed
        if (!isReloading && currentAmmo > 0 && canExitFlee)
        {
            Debug.Log("GPS Tank finished reloading and is ready to fight!");
            fleeTimer = 0f;
            hasFoundCover = false;
            hasReachedFleeTarget = false;

            // Decide next state based on GPS and line of sight
            if (hasGPSLock)
            {
                if (CanSeePlayer())
                {
                    float dist = Vector3.Distance(transform.position, player.position);
                    if (dist <= attackRange)
                    {
                        ChangeState(AIState.Attack);
                    }
                    else
                    {
                        ChangeState(AIState.Chase);
                    }
                }
                else
                {
                    ChangeState(AIState.Hunt);
                }
            }
            else
            {
                ChangeState(AIState.Patrol);
            }
        }
    }

    void FindBetterPositionForLineOfSight()
    {
        // Try to find a position where we can get line of sight to the player
        Vector3 playerPos = playerGPSPosition;
        Vector3 currentPos = transform.position;

        // Try positions around the player at attack range
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (attackRange * 0.8f);
            Vector3 testPosition = playerPos + offset;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(testPosition, out hit, 5f, NavMesh.AllAreas))
            {
                // Check if this position would give line of sight
                Vector3 directionToPlayer = (playerPos - hit.position).normalized;
                RaycastHit rayHit;

                if (!Physics.Raycast(hit.position + Vector3.up * 0.5f, directionToPlayer, out rayHit,
                    Vector3.Distance(hit.position, playerPos)))
                {
                    // No obstacles - this position should give line of sight
                    navAgent.SetDestination(hit.position);
                    Debug.Log("Moving to get line of sight to GPS target");
                    return;
                }
            }
        }

        // If no good position found, just move closer to GPS position
        navAgent.SetDestination(playerGPSPosition);
    }

    void FleeDirectlyFromPlayer()
    {
        Vector3 threatPosition = hasGPSLock ? playerGPSPosition :
                                (player != null ? player.position : lastKnownPlayerPosition);

        Vector3 fleeDirection = (transform.position - threatPosition).normalized;
        Vector3 fleePosition = transform.position + fleeDirection * fleeDistance;

        Vector3 safeFleePosition = FindSafeNavMeshPosition(fleePosition, fleeDistance);

        if (safeFleePosition != Vector3.zero)
        {
            fleeTarget = safeFleePosition;
            Debug.Log("GPS Tank fleeing from GPS position");
        }
        else
        {
            // Try multiple directions
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f * Mathf.Deg2Rad;
                Vector3 searchDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 testPosition = transform.position + searchDirection * fleeDistance;

                Vector3 testSafePosition = FindSafeNavMeshPosition(testPosition, fleeDistance * 0.5f);
                if (testSafePosition != Vector3.zero)
                {
                    fleeTarget = testSafePosition;
                    Debug.Log($"GPS Tank found alternative flee direction at angle {i * 30f}");
                    break;
                }
            }
        }
    }

    Vector3 FindBestCoverPoint()
    {
        Vector3 threatPosition = hasGPSLock ? playerGPSPosition :
                                (player != null ? player.position : lastKnownPlayerPosition);

        Vector3 bestCoverPoint = Vector3.zero;
        float bestScore = -1f;

        for (int i = 0; i < coverSearchPoints; i++)
        {
            float angle = i * (360f / coverSearchPoints) * Mathf.Deg2Rad;
            Vector3 searchDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 searchPoint = transform.position + searchDirection * coverSearchRadius;

            Vector3 validCoverPoint = FindSafeNavMeshPosition(searchPoint, coverSearchRadius);

            if (validCoverPoint == Vector3.zero)
                continue;

            if (Vector3.Distance(transform.position, validCoverPoint) < minCoverDistance)
                continue;

            float coverScore = EvaluateCoverPoint(validCoverPoint, threatPosition);

            if (coverScore > bestScore)
            {
                bestScore = coverScore;
                bestCoverPoint = validCoverPoint;
            }
        }

        if (bestScore < 0.1f)
        {
            return FindCoverBehindObstacles(threatPosition);
        }

        return bestCoverPoint;
    }

    Vector3 FindCoverBehindObstacles(Vector3 threatPosition)
    {
        Vector3 threatDirection = (threatPosition - transform.position).normalized;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 sideDirection = new Vector3(
                threatDirection.z * Mathf.Cos(angle) - threatDirection.x * Mathf.Sin(angle),
                0,
                threatDirection.x * Mathf.Cos(angle) + threatDirection.z * Mathf.Sin(angle)
            );

            RaycastHit hit;
            Vector3 rayStart = transform.position + Vector3.up * 0.5f;

            if (Physics.Raycast(rayStart, sideDirection, out hit, coverSearchRadius, obstacleLayerMask))
            {
                Vector3 behindObstacle = hit.point + hit.normal * 2f;
                Vector3 safeCoverPoint = FindSafeNavMeshPosition(behindObstacle, 3f);

                if (safeCoverPoint != Vector3.zero)
                {
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

        if (!NavMesh.SamplePosition(targetPosition, out navHit, searchRadius, NavMesh.AllAreas))
        {
            return Vector3.zero;
        }

        Vector3 candidatePosition = navHit.position;

        if (!IsPositionAwayFromEdges(candidatePosition))
        {
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
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 checkPoint = position + direction * edgeAvoidanceRadius;

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(checkPoint, out hit, 1f, NavMesh.AllAreas))
            {
                return false;
            }
        }
        return true;
    }

    Vector3 FindPositionAwayFromEdges(Vector3 originalPosition, float searchRadius)
    {
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

        if (HasLineOfSightToPosition(coverPoint, threatPosition))
        {
            return 0f;
        }
        score += 50f;

        float distanceToThreat = Vector3.Distance(coverPoint, threatPosition);
        score += Mathf.Clamp01(distanceToThreat / coverSearchRadius) * 20f;

        float distanceFromSelf = Vector3.Distance(coverPoint, transform.position);
        score += Mathf.Clamp01(1f - (distanceFromSelf / coverSearchRadius)) * 10f;

        RaycastHit hit;
        Vector3 directionToThreat = (threatPosition - coverPoint).normalized;
        if (Physics.Raycast(coverPoint + Vector3.up * 0.5f, directionToThreat, out hit,
            Vector3.Distance(coverPoint, threatPosition), obstacleLayerMask))
        {
            score += 15f;
        }

        return score;
    }

    bool HasLineOfSightToPosition(Vector3 fromPosition, Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - fromPosition).normalized;
        RaycastHit hit;

        if (Physics.Raycast(fromPosition + Vector3.up * 0.5f, directionToTarget, out hit,
            Vector3.Distance(fromPosition, targetPosition)))
        {
            if (player != null && targetPosition == player.position)
            {
                return hit.collider.CompareTag("Player");
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        // Line of sight check
        Vector3 rayDirection = (player.position - transform.position).normalized;
        RaycastHit hit;

        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, rayDirection, out hit,
            Vector3.Distance(transform.position, player.position)))
        {
            return hit.collider.CompareTag("Player");
        }

        return true; // No obstacles = can see
    }

    bool CanFireAtTarget(Vector3 targetPosition)
    {
        // Basic firing conditions
        if (isReloading || currentAmmo <= 0 || Time.time - lastFireTime < timeBetweenBullets)
        {
            return false;
        }

        // If direct aiming is not required, fire as before
        if (!requireDirectAiming)
        {
            return true;
        }

        // Check if target is within firing angle
        return IsTargetInFiringCone(targetPosition);
    }

    bool IsTargetInFiringCone(Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - transform.position).normalized;
        Vector3 tankForward = transform.forward;

        float angleToTarget = Vector3.Angle(tankForward, directionToTarget);
        bool withinAngle = angleToTarget <= firingAngle;

        if (withinAngle)
        {
            Debug.Log($"Target in firing cone! Angle: {angleToTarget:F1}° (Max: {firingAngle}°)");
        }

        return withinAngle;
    }

    void FireAtTarget(Vector3 targetPosition)
    {
        if (bulletPrefab != null && firePoint != null && currentAmmo > 0)
        {
            // Calculate direction to target
            Vector3 fireDirection = (targetPosition - firePoint.position).normalized;
            Quaternion fireRotation = Quaternion.LookRotation(fireDirection);

            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, fireRotation);
            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null)
            {
                bulletRb.velocity = fireDirection * bulletSpeed;
            }

            currentAmmo--;
            UpdateAmmoReloadUI();

            string targetType = (targetPosition == player.position) ? "Player (LOS)" : "GPS Position";
            Debug.Log($"GPS Tank fired at {targetType}! Ammo remaining: {currentAmmo}");

            Destroy(bullet, 5.0f);
        }
    }

    void StartReload()
    {
        if (!isReloading)
        {
            isReloading = true;
            reloadTimer = reloadTime;
            Debug.Log("GPS Tank started reloading...");
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
            case AIState.Patrol:
                navAgent.speed = moveSpeed;
                break;
            case AIState.Hunt:
                navAgent.speed = chaseSpeed;
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
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            float rotationStep = rotationSpeed * Time.deltaTime / 100f;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationStep);
        }
    }

    void SetNewPatrolPoint()
    {
        Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
        Vector3 randomPoint = patrolCenter + new Vector3(randomCircle.x, 0, randomCircle.y);

        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomPoint, out hit, patrolRadius, NavMesh.AllAreas))
        {
            targetPatrolPoint = hit.position;
        }
        else
        {
            targetPatrolPoint = patrolCenter;
        }
    }

    void ChangeState(AIState newState)
    {
        if (currentState != newState)
        {
            previousState = currentState;
            currentState = newState;
            stateChangeTime = Time.time;

            Debug.Log($"GPS Tank state changed from {previousState} to {newState}");

            switch (newState)
            {
                case AIState.Patrol:
                    patrolWaitTimer = 0f;
                    navAgent.speed = moveSpeed;
                    if (Vector3.Distance(targetPatrolPoint, transform.position) < 1f)
                    {
                        SetNewPatrolPoint();
                    }
                    break;
                case AIState.Hunt:
                    navAgent.speed = chaseSpeed;
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
                    hasReachedFleeTarget = false;
                    navAgent.speed = fleeSpeed;
                    break;
            }

            if (newState != AIState.Attack)
            {
                navAgent.isStopped = false;
            }
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

    void OnDrawGizmosSelected()
    {
        // Detection/GPS range
        if (maxTrackingRange > 0)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.3f); // Cyan for GPS range
            Gizmos.DrawWireSphere(transform.position, maxTrackingRange);
        }

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Patrol radius
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(patrolCenter, patrolRadius);

        if (Application.isPlaying)
        {
            // Current patrol target
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(targetPatrolPoint, 0.5f);

            // GPS connection line
            if (showGPSConnection && hasGPSLock && player != null)
            {
                Gizmos.color = gpsLineColor;
                Gizmos.DrawLine(transform.position + Vector3.up * 2f, playerGPSPosition + Vector3.up * 2f);

                // Draw GPS target position
                Gizmos.color = new Color(gpsLineColor.r, gpsLineColor.g, gpsLineColor.b, 0.7f);
                Gizmos.DrawWireSphere(playerGPSPosition, 1f);

                // Show distance text
                float gpsDistance = Vector3.Distance(transform.position, playerGPSPosition);
                Vector3 midPoint = (transform.position + playerGPSPosition) * 0.5f + Vector3.up * 3f;

                // Show GPS status
                //string gpsStatus = hasGPSLock ? $"GPS: {gpsDistance:F1}m" : "GPS: NO LOCK";
                //UnityEditor.Handles.Label(midPoint, gpsStatus);
            }

            // Show player direction arrow
            if (showPlayerDirection && hasGPSLock && player != null)
            {
                Vector3 directionToPlayer = (playerGPSPosition - transform.position).normalized;
                Vector3 arrowStart = transform.position + Vector3.up * 1.5f;
                Vector3 arrowEnd = arrowStart + directionToPlayer * 3f;

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(arrowStart, arrowEnd);

                // Arrow head
                Vector3 arrowLeft = Quaternion.AngleAxis(30, Vector3.up) * (-directionToPlayer) * 0.5f;
                Vector3 arrowRight = Quaternion.AngleAxis(-30, Vector3.up) * (-directionToPlayer) * 0.5f;
                Gizmos.DrawLine(arrowEnd, arrowEnd + arrowLeft);
                Gizmos.DrawLine(arrowEnd, arrowEnd + arrowRight);
            }

            // Show firing cone when in attack mode
            if (showFiringCone && requireDirectAiming && currentState == AIState.Attack)
            {
                DrawFiringCone();
            }

            // State-specific visualizations
            if (currentState == AIState.Hunt)
            {
                // Show hunt path to GPS position
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawLine(transform.position, playerGPSPosition);
                Gizmos.DrawWireSphere(playerGPSPosition, 0.5f);
            }

            // Flee state visualization
            if (currentState == AIState.Flee)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(fleeTarget, 0.8f);
                Gizmos.DrawLine(transform.position, fleeTarget);

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, coverSearchRadius);

                // Show line of sight to GPS position
                if (hasGPSLock)
                {
                    bool hasLOS = !HasLineOfSightToPosition(transform.position, playerGPSPosition);
                    Gizmos.color = hasLOS ? Color.green : Color.red;
                    Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, playerGPSPosition);
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

    void DrawFiringCone()
    {
        Vector3 tankPosition = transform.position + Vector3.up * 0.5f;
        Vector3 tankForward = transform.forward;

        // Determine target position for cone calculation
        Vector3 targetPos = Vector3.zero;
        bool hasTarget = false;

        if (requireLineOfSightToAttack && CanSeePlayer())
        {
            targetPos = player.position;
            hasTarget = true;
        }
        else if (!requireLineOfSightToAttack && hasGPSLock)
        {
            targetPos = playerGPSPosition;
            hasTarget = true;
        }

        bool targetInCone = hasTarget && IsTargetInFiringCone(targetPos);

        // Set color based on whether target is in cone
        Gizmos.color = targetInCone ? new Color(1f, 0f, 0f, 0.3f) : new Color(1f, 1f, 0f, 0.2f);

        // Draw firing cone
        float coneDistance = attackRange * 1.5f;

        Vector3 leftEdge = Quaternion.AngleAxis(-firingAngle, Vector3.up) * tankForward;
        Vector3 rightEdge = Quaternion.AngleAxis(firingAngle, Vector3.up) * tankForward;

        Gizmos.DrawLine(tankPosition, tankPosition + leftEdge * coneDistance);
        Gizmos.DrawLine(tankPosition, tankPosition + rightEdge * coneDistance);

        // Draw arc
        int segments = 10;
        Vector3 prevPoint = tankPosition + leftEdge * coneDistance;

        for (int i = 1; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-firingAngle, firingAngle, (float)i / segments);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * tankForward;
            Vector3 newPoint = tankPosition + direction * coneDistance;
            Gizmos.DrawLine(prevPoint, newPoint);
            prevPoint = newPoint;
        }

        // Draw line to target
        if (hasTarget)
        {
            Vector3 directionToTarget = (targetPos - transform.position).normalized;
            float angleToTarget = Vector3.Angle(tankForward, directionToTarget);

            if (targetInCone && CanFireAtTarget(targetPos))
            {
                Gizmos.color = Color.red; // Can fire
            }
            else if (targetInCone)
            {
                Gizmos.color = Color.yellow; // In cone but can't fire
            }
            else
            {
                Gizmos.color = Color.white; // Out of cone
            }

            Gizmos.DrawLine(tankPosition, targetPos);
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

        string firingInfo = requireDirectAiming ?
            $"\nFiring Mode: Direct Aiming ({firingAngle}°)" :
            "\nFiring Mode: Any Direction";

        string losRequirement = requireLineOfSightToAttack ?
            "\nLOS Required: YES" :
            "\nLOS Required: NO (GPS Guided)";

        return $"State: {currentState} (Previous: {previousState})\nAmmo: {currentAmmo}/{maxAmmo}\nReloading: {isReloading}\nReload Timer: {(isReloading ? reloadTimer.ToString("F1") : "N/A")}\nSharp Turning: {isSharpTurning}\nCurrent Speed: {navAgent.speed:F1}{gpsInfo}{trackingRange}{firingInfo}{losRequirement}\nHas Cover: {hasFoundCover}\nReached Flee Target: {hasReachedFleeTarget}\nTime Since State Change: {(Time.time - stateChangeTime):F1}s";
    }
}