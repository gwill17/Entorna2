using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(UniqueEntity))]
public class EnemySpawner : NetworkBehaviour
{
    [Header("Configuración del Spawner")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private int totalEnemies = 5;
    [SerializeField] private float spawnInterval = 2f;

    [Header("Opciones de área de spawn")]
    [SerializeField] private bool spawnInArea = false;
    [SerializeField] private float spawnRadius = 3f;

    private int spawnedCount = 0;
    private float timer = 0f;

    /// <summary>
    /// Inicializa el temporizador de aparición de enemigos.
    /// </summary>
    private void Start()
    {
        timer = spawnInterval;
        //Debug.Log($"[EnemySpawner] Start en {gameObject.name}. IsServer={NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer}. EnemyPrefab={enemyPrefab}");

    }

    /// <summary>
    /// Controla el intervalo de aparición y limita el número total de enemigos.
    /// </summary>
    private void Update()
    {
        //Debug.Log($"Spawner Update: {gameObject.name}");

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (enemyPrefab == null)
        {
            Debug.LogError("[EnemySpawner] enemyPrefab NULL en " + gameObject.name);
            return;
        }

        if (spawnedCount >= totalEnemies)
        {
            //Debug.LogWarning("[EnemySpawner] Límite alcanzado en " + gameObject.name + " totalEnemies=" + totalEnemies);
            return;
        }

        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            //Debug.Log("[EnemySpawner] Intentando spawnear " + enemyPrefab.name);
            spawnEnemy();
            spawnedCount++;
            timer = spawnInterval;
        }
    }

    /// <summary>
    /// Instancia un enemigo en la posición del spawner o dentro del radio configurado.
    /// </summary>
    private void spawnEnemy()
    {
        //Debug.Log($"[EnemySpawner] Intentando spawnear {enemyPrefab.name}");
        Vector3 spawnPos = transform.position;

        if (spawnInArea)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(0f, spawnRadius);
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
            spawnPos += new Vector3(offset.x, offset.y, 0f);
        }

        GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);

        NetworkObject netObj = enemy.GetComponent<NetworkObject>();

        if (netObj != null)
        {
            netObj.Spawn();
            //Debug.Log($"[EnemySpawner] Enemigo spawneado en red: {enemy.name}");
        }
        else
        {
            Debug.LogError($"{enemy.name} no tiene NetworkObject");
        }

        UniqueEntity uniqueEntity = enemy.GetComponent<UniqueEntity>();
        if (uniqueEntity != null)
            uniqueEntity.RegenerateIdOnSpawn();
    }
}
