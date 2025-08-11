using UnityEngine;

public class Bullet : MonoBehaviour
{
    
    public float lifeTime = 5f;
    public float damage = 20f;

    public LayerMask groundLayer; // Assign "Ground" layer in Inspector
    public GameObject hitParticlePrefab;

    void Start()
    {

        // Auto destroy after lifetime
        Destroy(gameObject, lifeTime);
    }

    void OnTriggerEnter(Collider other)
    {
        // Spawn hit particle
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

        Destroy(gameObject);
    }

}
