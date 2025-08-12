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

    // FSM States
    public enum AIState
    {
        Patrol,
        Chase,
        Attack,
        Flee
    }


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

    // AMMO
    private int currentAmmo;
    private bool isReloading;
    private float reloadTimer;

    // Flee
    private float fleeTimer;
    private Vector3 fleeTarget;
    private bool hasFoundCover;
    private List<Vector3> potentialCoverPoints;
    private bool hasReachedFleeTarget;
    private float lastCoverSearchTime;
    private float coverSearchCooldown = 2.0f;

    // WHEEL
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
        UpdateReload();
        UpdateFSM();
        UpdateTurningBehavior();
        CalculateRealisticMovement();
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

        UpdateAmmoReloadUI();
    }

    void UpdateFSM()
    {
        // Store time since last state change
        float timeSinceStateChange = Time.time - stateChangeTime;

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

        // Priority 2: Check for player detection
        if (CanSeePlayer())
        {
            lastKnownPlayerPosition = player.position;
            lastKnownPlayerTime = Time.time;
            ChangeState(AIState.Chase);
            return;
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

    void ChaseBehavior()
    {
        // Priority 1: Check if out of ammo and need to flee
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        // Priority 2: Check if still can see player
        if (!CanSeePlayer())
        {
            // Lost sight, return to patrol
            ChangeState(AIState.Patrol);
            return;
        }

        // Update last known player position
        lastKnownPlayerPosition = player.position;
        lastKnownPlayerTime = Time.time;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // Priority 3: Check if close enough to attack
        if (distanceToPlayer <= attackRange)
        {
            ChangeState(AIState.Attack);
            return;
        }

        // Chase the player
        if (navAgent.isStopped)
        {
            navAgent.isStopped = false;
        }
        navAgent.SetDestination(player.position);
    }

    void AttackBehavior()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // Priority 1: Check if out of ammo and need to flee
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
            ChangeState(AIState.Flee);
            return;
        }

        // Priority 2: Check if can still see player
        if (!CanSeePlayer())
        {
            ChangeState(AIState.Patrol);
            return;
        }

        // Priority 3: Check if player is too far
        if (distanceToPlayer > attackRange * 1.2f) // Add hysteresis
        {
            ChangeState(AIState.Chase);
            return;
        }

        // Stop and attack
        navAgent.isStopped = true;
        RotateTowardsTarget(player.position);

        // Fire if conditions are met (including angle check if enabled)
        if (CanFireAtPlayer())
        {
            FireAtPlayer();
            lastFireTime = Time.time;
        }
    }

    void FleeBehavior(float timeSinceStateChange)
    {
        fleeTimer += Time.deltaTime;

        // Ensure minimum flee time to avoid rapid state switching
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
                Debug.Log("Tank found cover behind obstacle!");
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
            // Reached flee target
            if (!hasReachedFleeTarget)
            {
                navAgent.isStopped = true;
                hasReachedFleeTarget = true;
                Debug.Log("Tank reached flee target");
            }

            // Face away from player while hiding
            if (player != null)
            {
                Vector3 hideDirection = (transform.position - player.position).normalized;
                Vector3 lookDirection = transform.position + hideDirection;
                RotateTowardsTarget(lookDirection);
            }
        }

        // Only exit flee state if reload is complete AND minimum flee time has passed
        if (!isReloading && currentAmmo > 0 && canExitFlee)
        {
            Debug.Log("Tank finished reloading and is ready to fight!");
            fleeTimer = 0f;
            hasFoundCover = false;
            hasReachedFleeTarget = false;

            // Decide next state based on player visibility
            if (CanSeePlayer())
            {
                float distanceToPlayer = Vector3.Distance(transform.position, player.position);
                if (distanceToPlayer <= attackRange)
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
                ChangeState(AIState.Patrol);
            }
        }
    }

    void FleeDirectlyFromPlayer()
    {
        if (player != null)
        {
            Vector3 fleeDirection = (transform.position - player.position).normalized;
            Vector3 fleePosition = transform.position + fleeDirection * fleeDistance;

            Vector3 safeFleePosition = FindSafeNavMeshPosition(fleePosition, fleeDistance);

            if (safeFleePosition != Vector3.zero)
            {
                fleeTarget = safeFleePosition;
                Debug.Log("Tank fleeing directly from player");
            }
            else
            {
                // Try multiple directions if direct flee fails
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * 30f * Mathf.Deg2Rad;
                    Vector3 searchDirection = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 testPosition = transform.position + searchDirection * fleeDistance;

                    Vector3 testSafePosition = FindSafeNavMeshPosition(testPosition, fleeDistance * 0.5f);
                    if (testSafePosition != Vector3.zero)
                    {
                        fleeTarget = testSafePosition;
                        Debug.Log($"Tank found alternative flee direction at angle {i * 30f}");
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

        Vector3 currentPlayerPosition = player.position;

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

            float coverScore = EvaluateCoverPoint(validCoverPoint, currentPlayerPosition);

            if (coverScore > bestScore)
            {
                bestScore = coverScore;
                bestCoverPoint = validCoverPoint;
            }
        }

        if (bestScore < 0.1f)
        {
            return FindCoverBehindObstacles(currentPlayerPosition);
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

    bool HasLineOfSightToPlayer(Vector3 fromPosition)
    {
        if (player == null) return false;
        return HasLineOfSightToPosition(fromPosition, player.position);
    }

    bool HasLineOfSightToPosition(Vector3 fromPosition, Vector3 targetPosition)
    {
        Vector3 directionToTarget = (targetPosition - fromPosition).normalized;
        RaycastHit hit;

        if (Physics.Raycast(fromPosition + Vector3.up * 0.5f, directionToTarget, out hit,
            Vector3.Distance(fromPosition, targetPosition)))
        {
            if (targetPosition == player.position)
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

    void StartReload()
    {
        if (!isReloading) // Prevent multiple reload calls
        {
            isReloading = true;
            reloadTimer = reloadTime;
            Debug.Log("Tank started reloading...");
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

    bool CanSeePlayer()
    {
        if (player == null) return false;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > detectionRange) return false;

        Vector3 rayDirection = (player.position - transform.position).normalized;
        RaycastHit hit;

        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, rayDirection, out hit, detectionRange))
        {
            return hit.collider.CompareTag("Player");
        }

        return false;
    }

    bool CanFireAtPlayer()
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

        // Check if player is within firing angle
        return IsPlayerInFiringCone();
    }

    bool IsPlayerInFiringCone()
    {
        if (player == null) return false;

        Vector3 directionToPlayer = (player.position - transform.position).normalized;
        Vector3 tankForward = transform.forward;

        // Calculate angle between tank's forward direction and direction to player
        float angleToPlayer = Vector3.Angle(tankForward, directionToPlayer);

        // Check if player is within the firing cone
        bool withinAngle = angleToPlayer <= firingAngle;

        if (withinAngle)
        {
            Debug.Log($"Player in firing cone! Angle: {angleToPlayer:F1}° (Max: {firingAngle}°)");
        }
        else
        {
            Debug.Log($"Player outside firing cone. Angle: {angleToPlayer:F1}° (Max: {firingAngle}°) - Still aiming...");
        }

        return withinAngle;
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

            currentAmmo--;
            UpdateAmmoReloadUI();

            Debug.Log($"Tank fired! Ammo remaining: {currentAmmo}");

            Destroy(bullet, 5.0f);
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

            Debug.Log($"Tank state changed from {previousState} to {newState}");

            // Reset state-specific variables
            switch (newState)
            {
                case AIState.Patrol:
                    patrolWaitTimer = 0f;
                    navAgent.speed = moveSpeed;
                    // Ensure we have a valid patrol point
                    if (Vector3.Distance(targetPatrolPoint, transform.position) < 1f)
                    {
                        SetNewPatrolPoint();
                    }
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

            // Ensure NavMeshAgent is not stopped when changing to movement states
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
        // Detection range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

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

            // Show firing cone when in attack mode
            if (showFiringCone && requireDirectAiming && currentState == AIState.Attack)
            {
                DrawFiringCone();
            }

            // Flee state visualization
            if (currentState == AIState.Flee)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(fleeTarget, 0.8f);
                Gizmos.DrawLine(transform.position, fleeTarget);

                Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, coverSearchRadius);

                Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
                Gizmos.DrawWireSphere(transform.position, edgeAvoidanceRadius);

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

    void DrawFiringCone()
    {
        if (player == null) return;

        Vector3 tankPosition = transform.position + Vector3.up * 0.5f;
        Vector3 tankForward = transform.forward;

        // Check if player is in firing cone
        bool playerInCone = IsPlayerInFiringCone();

        // Set color based on whether player is in cone
        Gizmos.color = playerInCone ? new Color(1f, 0f, 0f, 0.3f) : new Color(1f, 1f, 0f, 0.2f);

        // Draw firing cone
        float coneDistance = attackRange * 1.5f;

        // Calculate cone edges
        Vector3 leftEdge = Quaternion.AngleAxis(-firingAngle, Vector3.up) * tankForward;
        Vector3 rightEdge = Quaternion.AngleAxis(firingAngle, Vector3.up) * tankForward;

        // Draw cone lines
        Gizmos.DrawLine(tankPosition, tankPosition + leftEdge * coneDistance);
        Gizmos.DrawLine(tankPosition, tankPosition + rightEdge * coneDistance);

        // Draw arc at the end of cone
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

        // Draw line to player if visible
        if (player != null)
        {
            Vector3 directionToPlayer = (player.position - transform.position).normalized;
            float angleToPlayer = Vector3.Angle(tankForward, directionToPlayer);

            // Different color for line to player based on firing capability
            if (playerInCone && CanFireAtPlayer())
            {
                Gizmos.color = Color.red; // Can fire
            }
            else if (playerInCone)
            {
                Gizmos.color = Color.yellow; // In cone but can't fire (cooldown, etc.)
            }
            else
            {
                Gizmos.color = Color.white; // Out of cone
            }

            Gizmos.DrawLine(tankPosition, player.position);
        }
    }

    public string GetStateInfo()
    {
        string firingInfo = requireDirectAiming ?
            $"\nFiring Mode: Direct Aiming ({firingAngle}°)\nCan Fire: {CanFireAtPlayer()}" :
            "\nFiring Mode: Any Direction\nCan Fire: " + (!isReloading && currentAmmo > 0 && Time.time - lastFireTime >= timeBetweenBullets);

        return $"State: {currentState} (Previous: {previousState})\nAmmo: {currentAmmo}/{maxAmmo}\nReloading: {isReloading}\nReload Timer: {(isReloading ? reloadTimer.ToString("F1") : "N/A")}\nSharp Turning: {isSharpTurning}\nCurrent Speed: {navAgent.speed:F1}\nHas Cover: {hasFoundCover}\nReached Flee Target: {hasReachedFleeTarget}\nTime Since State Change: {(Time.time - stateChangeTime):F1}s{firingInfo}";
    }
}