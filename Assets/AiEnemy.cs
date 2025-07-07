using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AITankFSM : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 3.0f;
    public float rotationSpeed = 100.0f;
    public GameObject[] leftWheels;
    public GameObject[] rightWheels;
    public float wheelRotationSpeed = 200.0f;

    [Header("AI Settings")]
    public float detectionRange = 10.0f;
    public float attackRange = 5.0f;
    public float patrolRadius = 8.0f;
    public float patrolWaitTime = 2.0f;
    public float chaseSpeed = 4.0f;
    public float turnSpeed = 2.0f;

    [Header("Combat Settings")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float fireRate = 1.0f;
    public float bulletSpeed = 10.0f;

    // FSM States
    public enum AIState
    {
        Patrol,
        Chase,
        Attack,
        SearchPlayer
    }

    // Private variables
    private AIState currentState;
    private Rigidbody rb;
    private Transform player;
    private Vector3 patrolCenter;
    private Vector3 targetPatrolPoint;
    private float lastFireTime;
    private float patrolWaitTimer;
    private float searchTimer;
    private float lastKnownPlayerTime;
    private Vector3 lastKnownPlayerPosition;

    // Movement inputs (similar to player script)
    private float moveInput;
    private float rotationInput;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        patrolCenter = transform.position;
        currentState = AIState.Patrol;
        SetNewPatrolPoint();
    }

    void Update()
    {
        // Update FSM
        UpdateFSM();

        // Rotate wheels based on current movement inputs
        RotateWheels(moveInput, rotationInput);
    }

    void FixedUpdate()
    {
        // Apply movement using the same system as player
        MoveTankObj(moveInput);
        RotateTank(rotationInput);
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
            case AIState.SearchPlayer:
                SearchBehavior();
                break;
        }
    }

    void PatrolBehavior()
    {
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
            MoveTowardsTarget(targetPatrolPoint);
        }
        else
        {
            // Reached patrol point, wait then set new one
            patrolWaitTimer += Time.deltaTime;
            moveInput = 0f;
            rotationInput = 0f;

            if (patrolWaitTimer >= patrolWaitTime)
            {
                SetNewPatrolPoint();
                patrolWaitTimer = 0f;
            }
        }
    }

    void ChaseBehavior()
    {
        if (!CanSeePlayer())
        {
            // Lost sight of player, start searching
            ChangeState(AIState.SearchPlayer);
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
        MoveTowardsTarget(player.position);
    }

    void AttackBehavior()
    {
        if (!CanSeePlayer())
        {
            ChangeState(AIState.SearchPlayer);
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer > attackRange)
        {
            ChangeState(AIState.Chase);
            return;
        }

        // Stop moving and focus on attacking
        moveInput = 0f;

        // Rotate to face player
        RotateTowardsTarget(player.position);

        // Fire at player
        if (Time.time - lastFireTime >= 1.0f / fireRate)
        {
            FireAtPlayer();
            lastFireTime = Time.time;
        }
    }

    void SearchBehavior()
    {
        searchTimer += Time.deltaTime;

        // Check if we can see the player again
        if (CanSeePlayer())
        {
            ChangeState(AIState.Chase);
            return;
        }

        // Move to last known position
        float distanceToLastKnown = Vector3.Distance(transform.position, lastKnownPlayerPosition);

        if (distanceToLastKnown > 1.0f)
        {
            MoveTowardsTarget(lastKnownPlayerPosition);
        }
        else
        {
            // Reached last known position, look around
            rotationInput = 0.5f;
            moveInput = 0f;
        }

        // Give up searching after some time
        if (searchTimer >= 5.0f)
        {
            searchTimer = 0f;
            ChangeState(AIState.Patrol);
        }
    }

    void MoveTowardsTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        float dot = Vector3.Dot(transform.forward, direction);

        // Calculate rotation needed
        Vector3 cross = Vector3.Cross(transform.forward, direction);
        float rotationDirection = cross.y > 0 ? 1 : -1;

        // Set movement inputs
        if (dot > 0.7f) // Moving roughly forward
        {
            moveInput = 1.0f;
            rotationInput = rotationDirection * (1.0f - dot) * turnSpeed;
        }
        else if (dot < -0.7f) // Need to turn around
        {
            moveInput = -0.3f; // Back up a bit
            rotationInput = rotationDirection * turnSpeed;
        }
        else // Need to turn
        {
            moveInput = 0.3f;
            rotationInput = rotationDirection * turnSpeed;
        }

        // Clamp inputs
        moveInput = Mathf.Clamp(moveInput, -1f, 1f);
        rotationInput = Mathf.Clamp(rotationInput, -1f, 1f);
    }

    void RotateTowardsTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        Vector3 cross = Vector3.Cross(transform.forward, direction);
        float rotationDirection = cross.y > 0 ? 1 : -1;

        float dot = Vector3.Dot(transform.forward, direction);
        if (dot < 0.95f) // Not facing target accurately enough
        {
            rotationInput = rotationDirection * 0.5f;
        }
        else
        {
            rotationInput = 0f;
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
        if (bulletPrefab != null && firePoint != null)
        {
            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null)
            {
                bulletRb.velocity = firePoint.forward * bulletSpeed;
            }

            // Destroy bullet after some time
            Destroy(bullet, 5.0f);
        }
    }

    void SetNewPatrolPoint()
    {
        // Generate random point within patrol radius
        Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
        targetPatrolPoint = patrolCenter + new Vector3(randomCircle.x, 0, randomCircle.y);
    }

    void ChangeState(AIState newState)
    {
        currentState = newState;

        // Reset state-specific timers
        switch (newState)
        {
            case AIState.SearchPlayer:
                searchTimer = 0f;
                break;
            case AIState.Patrol:
                patrolWaitTimer = 0f;
                break;
        }
    }

    // Movement methods (same as player script)
    void MoveTankObj(float input)
    {
        Vector3 moveDirection = transform.forward * input * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + moveDirection);
    }

    void RotateTank(float input)
    {
        float rotation = input * rotationSpeed * Time.fixedDeltaTime;
        Quaternion turnRotation = Quaternion.Euler(0.0f, rotation, 0.0f);
        rb.MoveRotation(rb.rotation * turnRotation);
    }

    void RotateWheels(float moveInput, float rotationInput)
    {
        float wheelRotation = moveInput * wheelRotationSpeed * Time.deltaTime;

        foreach (GameObject wheel in leftWheels)
        {
            if (wheel != null)
            {
                wheel.transform.Rotate(wheelRotation - rotationInput * wheelRotationSpeed * Time.deltaTime, 0.0f, 0.0f);
            }
        }

        foreach (GameObject wheel in rightWheels)
        {
            if (wheel != null)
            {
                wheel.transform.Rotate(wheelRotation + rotationInput * wheelRotationSpeed * Time.deltaTime, 0.0f, 0.0f);
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
        }
    }
}