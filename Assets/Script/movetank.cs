using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MoveTank : MonoBehaviour
{
    public GameObject player;

    [Header("Movement Settings")]
    public float moveSpeed = 5.0f;
    public float rotationSpeed = 120.0f;
    public GameObject[] leftWheels;
    public GameObject[] rightWheels;
    public float wheelRotationSpeed = 200.0f;

    [Header("Particle Settings")]
    public GameObject moveParticlePrefab; // particle prefab to spawn
    public Transform[] particlePoints;    // points where particles appear
    public float particleSpawnInterval = 0.2f; // how often to spawn while moving/rotating
    private float lastParticleTime;


    [Header("Shooting Settings")]
    public GameObject shootparticle;
    public GameObject bulletPrefab;
    public Transform firePoint;

    public float fireRate = 2.0f;
    public float bulletSpeed = 15.0f;
    public KeyCode fireKey = KeyCode.Space;

    // -------- New UI & Health Settings --------
    [Header("UI Settings")]
    public Image healthBarImage; 
    public TextMeshProUGUI healthText;       // TextMeshPro for percentage
    public GameObject mainCanvas;            // Main UI canvas
    public GameObject gameOverCanvas;        // Game Over UI canvas

    [Header("Health Settings")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("SHIELD Settings")]
    public float maxshield = 100f;
    private float currentshield;
    public Image shieldP;                     // UI fill for shield
    public float shieldRechargeRate = 15f;    // units per second recharge
    public float shieldUsageRate = 25f;       // units per second usage
    public KeyCode shieldKey = KeyCode.LeftShift;
    public ParticleSystem shieldParticles;    // particle system for active shield

    private bool shieldActive = false;
    private float shieldTimeRemaining = 0f;


    [Header("Trajectory Visualization")]
    public bool showTrajectory = true;
    public int trajectoryPoints = 30;
    public float trajectoryTimeStep = 0.1f;
    public GameObject trajectoryPointPrefab; // Optional: prefab for trajectory points
    public Color trajectoryColor = Color.red;
    public float trajectoryPointSize = 0.1f;

    // Private variables
    private Rigidbody rb;
    private float moveInput;
    private float rotationInput;
    private float lastFireTime;
    private LineRenderer trajectoryLine;
    private List<GameObject> trajectoryPoints_Objects = new List<GameObject>();

    void Start()
    {
        if (shieldParticles != null)
            shieldParticles.Stop();

        rb = GetComponent<Rigidbody>();
        SetupTrajectoryVisualization();

        // Initialize Health
        currentHealth = maxHealth;
        currentshield = 0f;
        UpdateShieldUI();
        UpdateHealthUI();

        // Make sure Game Over canvas is hidden at start
        if (gameOverCanvas != null) gameOverCanvas.SetActive(false);
    }
    void UpdateShieldUI()
    {
        if (shieldP != null)
            shieldP.fillAmount = currentshield / maxshield;
    }


    // -------- NEW: Take Damage Method --------
    public void TakeDamage(float damage)
    {
        // Ignore damage if shield is active
        if (shieldActive)
        {
            Debug.Log("Shield absorbed the damage!");
            return;
        }
        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();

        if (currentHealth <= 0)
            GameOver();
    }

    // -------- NEW: Update UI Method --------
    void UpdateHealthUI()
    {
        if (healthBarImage != null)
            healthBarImage.fillAmount = currentHealth / maxHealth;

        if (healthText != null)
            healthText.text = "HEALTH - " + Mathf.RoundToInt((currentHealth / maxHealth) * 100) + "%";
    }

    // -------- NEW: Game Over Method --------
    void GameOver()
    {
        if (mainCanvas != null) mainCanvas.SetActive(false);
        if (gameOverCanvas != null) gameOverCanvas.SetActive(true);
        player.SetActive(false); // Correct

        // Stop tank movement
        moveSpeed = 0;
        rotationSpeed = 0;
    }
    void SetupTrajectoryVisualization()
    {
        // Create LineRenderer for trajectory
        GameObject trajectoryObj = new GameObject("TrajectoryLine");
        trajectoryObj.transform.SetParent(transform);
        trajectoryLine = trajectoryObj.AddComponent<LineRenderer>();

        // Configure LineRenderer
        Material trajectoryMaterial = new Material(Shader.Find("Sprites/Default"));
        trajectoryMaterial.color = trajectoryColor;
        trajectoryLine.material = trajectoryMaterial;
        trajectoryLine.startWidth = 0.05f;
        trajectoryLine.endWidth = 0.05f;
        trajectoryLine.positionCount = trajectoryPoints;
        trajectoryLine.useWorldSpace = true;
    }

    void Update()
    {
        // Get input
        moveInput = Input.GetAxis("Vertical");
        rotationInput = Input.GetAxis("Horizontal");

        // Handle shooting
        HandleShooting();
        // Handle particles when moving or rotating
        HandleMovementParticles();
        // Update trajectory visualization
        if (showTrajectory)
        {
            UpdateTrajectoryVisualization();
        }

        // Rotate wheels
        RotateWheels(moveInput, rotationInput);


        // Handle shield activation
        // Recharge shield if not active
        if (!shieldActive && currentshield < maxshield)
        {
            currentshield += shieldRechargeRate * Time.deltaTime;
            currentshield = Mathf.Clamp(currentshield, 0, maxshield);
            UpdateShieldUI();
        }

        // Activate shield when key pressed & shield has charge
        if (Input.GetKeyDown(shieldKey) && currentshield == maxshield && !shieldActive)
        {
            ActivateShield();
        }

        // While shield is active
        if (shieldActive)
        {
            currentshield -= shieldUsageRate * Time.deltaTime;
            currentshield = Mathf.Clamp(currentshield, 0, maxshield);
            UpdateShieldUI();

            if (currentshield <= 0)
            {
                DeactivateShield();
            }
        }

    }

    void ActivateShield()
    {
        shieldActive = true;
        if(currentshield == 100f){
            if (shieldParticles != null) shieldParticles.Play();
        }
        
        Debug.Log("Shield Activated!");
    }

    void DeactivateShield()
    {
        shieldActive = false;
        if (shieldParticles != null) shieldParticles.Stop();
        Debug.Log("Shield Deactivated!");
    }


    void UpdateTrajectoryVisualization()
    {
        if (firePoint == null)
        {
            trajectoryLine.enabled = false;
            return;
        }

        trajectoryLine.enabled = true;
        Vector3[] points = CalculateTrajectoryPoints();
        trajectoryLine.positionCount = points.Length;
        trajectoryLine.SetPositions(points);
    }

    Vector3[] CalculateTrajectoryPoints()
    {
        List<Vector3> points = new List<Vector3>();

        Vector3 startPosition = firePoint.position;
        Vector3 startVelocity = firePoint.forward * bulletSpeed;

        for (int i = 0; i < trajectoryPoints; i++)
        {
            float time = i * trajectoryTimeStep;
            Vector3 point = startPosition + startVelocity * time;

            // Apply gravity (assuming standard gravity)
            point.y += 0.5f * Physics.gravity.y * time * time;

            points.Add(point);

            // Stop calculating if trajectory goes below ground or hits something
            if (point.y < 0)
            {
                break;
            }

            // Optional: Add collision detection for trajectory
            if (i > 0)
            {
                RaycastHit hit;
                Vector3 direction = point - points[i - 1];
                if (Physics.Raycast(points[i - 1], direction.normalized, out hit, direction.magnitude))
                {
                    points[i] = hit.point;
                    break;
                }
            }
        }

        return points.ToArray();
    }

    void FixedUpdate()
    {
        MoveTankObj(moveInput);
        RotateTank(rotationInput);
    }

    void HandleShooting()
    {
        // Check if fire key is pressed and enough time has passed since last shot
        if (Input.GetKey(fireKey) && Time.time - lastFireTime >= 1.0f / fireRate)
        {
            FireBullet();
            lastFireTime = Time.time;
        }
    }

    void HandleMovementParticles()
    {
        if (Mathf.Abs(moveInput) > 0.1f || Mathf.Abs(rotationInput) > 0.1f)
        {
            if (Time.time - lastParticleTime >= particleSpawnInterval)
            {
                foreach (Transform point in particlePoints)
                {
                    if (moveParticlePrefab != null && point != null)
                    {
                        GameObject p = Instantiate(moveParticlePrefab, point.position, point.rotation);
                        Destroy(p, 2f); // destroy after effect finishes
                    }
                }
                lastParticleTime = Time.time;
            }
        }
    }

    void FireBullet()
    {
        if (bulletPrefab != null && firePoint != null)
        {
            GameObject fireblast = Instantiate(shootparticle, firePoint.position, firePoint.rotation);

            // Create bullet at fire point
            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);

            // Add velocity to bullet
            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null)
            {
                bulletRb.velocity = firePoint.forward * bulletSpeed;
            }

            Destroy(fireblast, 1.0f);

            //// Destroy bullet after 5 seconds to prevent memory issues
            //Destroy(bullet, 5.0f);

            // Optional: Add muzzle flash effect here
            // Optional: Add shooting sound effect here
        }
        else
        {
            Debug.LogWarning("Bullet prefab or fire point not assigned!");
        }
    }

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

    // Optional: Show trajectory in Scene view for debugging
    void OnDrawGizmosSelected()
    {
        if (showTrajectory && firePoint != null)
        {
            Gizmos.color = trajectoryColor;
            Vector3[] points = CalculateTrajectoryPoints();

            for (int i = 0; i < points.Length - 1; i++)
            {
                Gizmos.DrawLine(points[i], points[i + 1]);
                Gizmos.DrawWireSphere(points[i], trajectoryPointSize);
            }
        }
    }
}