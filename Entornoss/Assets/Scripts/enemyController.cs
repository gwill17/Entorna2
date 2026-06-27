using UnityEngine;
using Unity.Netcode;
using System.Collections;

public abstract class EnemyController : CharController
{
    protected int damageToPlayer;
    protected GameObject[] dropPrefabs;

    /// <summary>
    /// Inicializa la configuración base del enemigo heredada del controlador de personaje.
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
    }

    /// <summary>
    /// Gestiona la interacción continua con el jugador para atacar o recibir daño.
    /// </summary>
    protected virtual void OnCollisionStay2D(Collision2D collision)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (!collision.gameObject.CompareTag("Player")) return;

        PlayerController player = collision.gameObject.GetComponent<PlayerController>();
        if (player == null) return;

        if (player.IsAttacking)
        {
            TakeDamage(player.DamageToEnemy, (transform.position - player.transform.position).normalized);
            checkDeath();
        }
        else
        {
            Vector2 knockbackDir = (player.transform.position - transform.position).normalized;
            player.TakeDamage(damageToPlayer, knockbackDir);
        }
    }

    /// <summary>
    /// Carga estadísticas de combate y referencias de drops desde EnemyStats.
    /// </summary>
    protected override void LoadStats()
    {
        base.LoadStats();

        EnemyStats enemyStats = stats as EnemyStats;

        if (enemyStats != null)
        {
            moveSpeed *= enemyStats.speedPenalty;
            damageToPlayer = enemyStats.attackDamage;
            dropPrefabs = enemyStats.dropPrefabs;
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] No tiene EnemyStats asignado. Usando valores por defecto.");
            damageToPlayer = 1;
            moveSpeed *= 0.75f;
        }
    }

    /// <summary>
    /// Marca y procesa la muerte del enemigo cuando su vida llega a cero.
    /// </summary>
    public override void Die()
    {
        // Interceptamos la llamada de Die() de la clase base para redirigirla al flujo controlado multijugador
        checkDeath();
    }

    /// <summary>
    /// Verifica si el enemigo debe morir y programa su destrucción en escena atribuyendo la baja al jugador correcto.
    /// </summary>
    protected virtual void checkDeath()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // 1. Buscamos al jugador más cercano en escena para asignarle la baja personal
        PlayerController[] jugadores = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        PlayerController atacante = null;
        float distanciaMinima = float.MaxValue;

        foreach (var p in jugadores)
        {
            float dist = Vector3.Distance(transform.position, p.transform.position);
            if (dist < distanciaMinima)
            {
                distanciaMinima = dist;
                atacante = p;
            }
        }

        // 2. Registramos la baja síncrona en el Servidor usando la ID de red del atacante encontrado
        if (atacante != null && GameManager.Instance != null)
        {
            GameManager.Instance.AddEnemyKillServer(atacante.OwnerClientId);
        }

        // 3. Spawneamos los drops de forma segura (una sola vez)
        spawnDrops();

        // 4. Despawn oficial del objeto de red
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Genera los drops del enemigo usando la configuración activa del mapa.
    /// </summary>
    protected virtual void spawnDrops()
    {
        if (dropPrefabs == null || dropPrefabs.Length == 0)
        {
            Debug.LogWarning($"[{gameObject.name}] No tiene dropPrefabs configurados.");
            return;
        }

        EnemyDropConfig dropCfg = getDropConfig();

        if (dropCfg == null)
        {
            Debug.LogWarning($"[{gameObject.name}] No hay MapConfig activo. No se spawnean drops.");
            return;
        }

        int dropCount = Random.Range(dropCfg.minDiamondDrops, dropCfg.maxDiamondDrops + 1);
        if (dropCount <= 0) return;

        float angleStep = 360f / dropCount;
        float startAngle = Random.Range(0f, 360f);

        for (int i = 0; i < dropCount; i++)
        {
            GameObject dropPrefab = dropPrefabs[0];

            if (dropPrefabs.Length > 1 && i == 0 && Random.value < dropCfg.keyDropChance)
                dropPrefab = dropPrefabs[1];

            if (dropPrefab != null)
            {
                float angle = startAngle + i * angleStep;
                Vector3 dropPosition = transform.position +
                    new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad), 0f) * 0.5f;

                GameObject drop = Instantiate(dropPrefab, dropPosition, Quaternion.identity);

                NetworkObject netObj = drop.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn();
                }
                else
                {
                    Debug.LogError($"{drop.name} no tiene NetworkObject");
                }
                UniqueEntity uniqueEntity = drop.GetComponent<UniqueEntity>();
                if (uniqueEntity != null) uniqueEntity.RegenerateIdOnSpawn();
            }
        }
    }

    /// <summary>
    /// Obtiene la configuración de drops del mapa según el tipo de enemigo.
    /// </summary>
    protected virtual EnemyDropConfig getDropConfig()
    {
        MapConfig mapCfg = GameManager.Instance?.SelectedMapConfig;
        if (mapCfg == null) return null;

        if (stats is ChaseEnemyStats)
            return mapCfg.dragonDropConfig;

        if (stats is LemniscateEnemyStats)
            return mapCfg.goatDropConfig;

        return null;
    }

    private IEnumerator despawnAfterDeath()
    {
        yield return new WaitForSeconds(1.2f);

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            yield break;

        NetworkObject netObj = GetComponent<NetworkObject>();

        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
    }
}