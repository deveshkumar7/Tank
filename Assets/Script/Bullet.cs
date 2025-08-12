using UnityEngine;

public class Bullet : MonoBehaviour
{
    
    public float lifeTime = 5f;
    public float damage = 20f;

    public LayerMask groundLayer; 
    public GameObject hitParticlePrefab;

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (hitParticlePrefab != null)
        {
            GameObject particle = Instantiate(hitParticlePrefab, transform.position, Quaternion.identity);
            Destroy(particle, 2f);
        }

        if (((1 << other.gameObject.layer) & groundLayer) != 0)
        {
            Destroy(gameObject);
            return;
        }

        if (other.CompareTag("Player"))
        {
            other.GetComponent<MoveTank>()?.TakeDamage(damage);
        }
        else if (other.CompareTag("Enemy"))
        {
            other.GetComponent<AITankFSM>()?.TakeDamage(damage);
        }
        else if (other.CompareTag("Enemy2"))
        {
            other.GetComponent<AiAggressive>()?.TakeDamage(damage);
        }
        else if (other.CompareTag("EnemyS"))
        {
            other.GetComponent<AiSniper>()?.TakeDamage(damage);
        }
        else if (other.CompareTag("EnemyM"))
        {
            other.GetComponent<AIMulti>()?.TakeDamage(damage);
        }

        Destroy(gameObject);
    }

}
