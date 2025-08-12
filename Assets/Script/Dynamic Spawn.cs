using UnityEngine;
using System.Collections;

public class DynamicSpawner : MonoBehaviour
{
    [Header("Common Spawn Settings")]
    public Transform spawnCenter;
    public float spawnRadius = 30f;
    public float spawnHeight = 1.5f;

    [Header("Enemy Prefabs")]
    public GameObject patrolGuardPrefab;
    public GameObject sniperPrefab;
    public GameObject multiShotSniperPrefab;
    public GameObject bossPrefab;
    public GameObject aggressivePrefab;

    [Header("Spawn Intervals (seconds)")]
    public float patrolGuardSpawnRate = 5f;
    public float sniperSpawnRate = 10f;
    public float multiShotSniperSpawnRate = 15f;
    public float bossSpawnRate = 30f;
    public float aggressiveSpawnRate = 8f;

    void Start()
    {
        StartCoroutine(SpawnEnemyLoop(patrolGuardPrefab, patrolGuardSpawnRate));
        StartCoroutine(SpawnEnemyLoop(sniperPrefab, sniperSpawnRate));
        StartCoroutine(SpawnEnemyLoop(multiShotSniperPrefab, multiShotSniperSpawnRate));
        StartCoroutine(SpawnEnemyLoop(bossPrefab, bossSpawnRate));
        StartCoroutine(SpawnEnemyLoop(aggressivePrefab, aggressiveSpawnRate));
    }

    IEnumerator SpawnEnemyLoop(GameObject enemyPrefab, float spawnRate)
    {
        yield return new WaitForSeconds(2f); 
        while (true)
        {
            yield return new WaitForSeconds(spawnRate);
            Vector3 spawnPos = GetRandomPoint(spawnCenter.position, spawnRadius);
            spawnPos.y += spawnHeight;
            Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
        }
    }

    Vector3 GetRandomPoint(Vector3 center, float radius)
    {
        Vector2 circle = Random.insideUnitCircle * radius;
        return new Vector3(center.x + circle.x, center.y, center.z + circle.y);
    }
}
