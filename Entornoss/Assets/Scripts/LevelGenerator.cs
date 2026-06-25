using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Netcode;
using UnityEngine.SceneManagement;

[System.Serializable]
public class WeightedTilemapFiller
{
    public TilemapFiller tilemapFiller;
    [Range(0, 100)] public float weight = 1f;
}

[System.Serializable]
public class RingSettings
{
    [Header("Configuración visual del anillo (prefabs)")]
    public string name = "Anillo";
    public WeightedTile[] weightedTiles;
    public GameObject wallPrefab;
    public GameObject cornerPrefab;
    public GameObject openDoor;
    public GameObject closedDoor;
    public GameObject decorativeElement;

    [Range(0f, 1f)]
    [Tooltip("Porcentaje de tiles del anillo que tendrán elemento decorativo (0 = ninguno, 1 = todos)")]
    public float decorativeElementPercentage = 0.05f;
}

public class LevelGenerator : NetworkBehaviour
{
    [SerializeField] private TilemapFiller tilemapFiller;

    [Header("Configuración por defecto (fallback sin selección de menú)")]
    [SerializeField] private MapConfig defaultMapConfig;

    [Header("Sala del tesoro (prefabs)")]
    [SerializeField] private GameObject treasurePrefab;
    [SerializeField] private WeightedTile[] treasureRoomTiles;
    [SerializeField] private GameObject treasureRoomWallPrefab;
    [SerializeField] private GameObject treasureRoomCornerPrefab;
    public GameObject treasureRoomOpenDoor;
    public GameObject treasureRoomClosedDoor;

    [Header("Prefabs de spawners por tipo de enemigo")]
    [SerializeField] private GameObject dragonSpawnerPrefab;
    [SerializeField] private GameObject goatSpawnerPrefab;

    [Header("Anillos del castillo (solo prefabs, en orden fijo)")]
    [Tooltip("Orden: 0=Decorada, 1=Baldosas, 2=Madera, 3=Baldosas rotas, 4=Patio, 5=Bosque exterior")]
    [SerializeField] private RingSettings[] castleRings;

    [SerializeField] private GameObject networkPlayerPrefab;

    private Tilemap tilemap;
    private bool hasPendingSpawn = false;
    private Vector3 pendingSpawnPos;

    private MapConfig activeConfig => GameManager.Instance?.SelectedMapConfig ?? defaultMapConfig;

    /// <summary>
    /// Inicializa referencias de escena locales.
    /// </summary>
    private void Awake()
    {
        tilemap = FindFirstObjectByType<Tilemap>();
    }

    /// <summary>
    /// Libera la clase base al destruirse.
    /// </summary>
    public override void OnDestroy()
    {
        base.OnDestroy();
    }

    /// <summary>
    /// 🌟 SOLUCIÓN AL CLON: Se ejecuta una única vez de forma segura cuando el objeto despierta en la red.
    /// </summary>
    /// 
    private bool levelGenerated = false;

    private void Start()
    {
        if (levelGenerated) return;

        //Debug.Log("[LevelGenerator] Generando mapa local");
        generateLevel();
        levelGenerated = true;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            //Debug.Log("[LevelGenerator] Servidor spawneando jugadores");
            SpawnPlayersProcedural();
        }
    }
    public override void OnNetworkSpawn()
    {
        Debug.Log($"[LevelGenerator] OnNetworkSpawn. IsServer={IsServer}, IsClient={IsClient}");
        /*
        base.OnNetworkSpawn();
        

        // 🛡️ Regla de oro: Solo el Servidor/Host coordina la creación del mapa y los personajes
        if (!IsServer) return;

        Debug.Log("[LevelGenerator] OnNetworkSpawn detectado en Servidor. Generando nivel único y seguro...");

        // 1. Generamos el nivel completo en orden
        generateLevel();

        // 2. Calculamos las posiciones finales y spawneamos a los jugadores conectados
        SpawnPlayersProcedural();*/
    }

    private void SpawnPlayersProcedural()
    {
        if (!IsServer) return;

        GameObject playerPrefab = networkPlayerPrefab;

        if (playerPrefab == null)
        {
            //Debug.LogError("[LevelGenerator] ¡No has asignado el prefab en la casilla 'Network Player Prefab'!");
            return;
        }

        if (!tryCalculateSpawnPos(out Vector3 spawnPosition))
        {
            //Debug.LogWarning("[LevelGenerator] No se pudo calcular la posición exterior. Usando posición por defecto.");
            spawnPosition = new Vector3(5f, 5f, -0.1f);
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            // Instanciamos el clon físico en el servidor
            GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);

            NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                // Le asignamos el control de red al cliente correspondiente de forma limpia
                netObj.SpawnAsPlayerObject(client.ClientId);
                Debug.Log($"[LevelGenerator] Player spawneado para ClientId={client.ClientId}, Owner={netObj.OwnerClientId}");
            }
        }
    }

    /// <summary>
    /// Genera la sala del tesoro y los anillos del mapa.
    /// </summary>
    private void generateLevel()
    {
        generateRings();
        generateTreasureRoom();
    }

    /// <summary>
    /// Construye la sala del tesoro e instancia el cofre en el centro.
    /// </summary>
    private void generateTreasureRoom()
    {
        if (tilemapFiller == null)
        {
            Debug.LogWarning("[LevelGenerator] No se ha asignado el TilemapFiller.");
            return;
        }

        int roomSize = activeConfig != null ? activeConfig.treasureRoomSize : 7;

        tilemapFiller.BuildSquareRoom(
            tilemap,
            roomSize,
            treasureRoomTiles,
            null,
            treasureRoomWallPrefab,
            treasureRoomCornerPrefab,
            treasureRoomOpenDoor,
            treasureRoomClosedDoor
        );

        if (treasurePrefab != null)
        {
            Vector3 center = new Vector3(0f, 0f, -0.1f);
            GameObject chest = Instantiate(treasurePrefab, center, Quaternion.identity);

            UniqueEntity uniqueEntity = chest.GetComponent<UniqueEntity>();
            if (uniqueEntity != null)
            {
                uniqueEntity.RegenerateIdOnSpawn();
            }
        }
        else
        {
            Debug.Log("[LevelGenerator] No hay treasurePrefab asignado, se genera la sala vacía visualmente.");
        }
    }

    /// <summary>
    /// Construye los anillos activos según la configuración de mapa seleccionada.
    /// </summary>
    private void generateRings()
    {
        if (castleRings == null || castleRings.Length == 0 || tilemapFiller == null) return;

        MapConfig cfg = activeConfig;
        int roomSize = cfg != null ? cfg.treasureRoomSize : 7;
        Vector2Int innerSize = new Vector2Int(roomSize, roomSize);

        for (int i = 0; i < castleRings.Length; i++)
        {
            RingSettings ring = castleRings[i];
            if (ring == null) continue;

            bool isLastRing = i == castleRings.Length - 1;
            bool isEnabled = isLastRing || isRingEnabled(cfg, i);

            if (!isEnabled)
            {
                Debug.Log($"[LevelGenerator] Anillo '{ring.name}' desactivado por MapConfig");
                continue;
            }

            int ringWidth = getRingWidth(cfg, i);
            GameObject[] spawners = buildSpawnersArray(cfg, i);
            float decorativePercentage = getDecorativePercentage(cfg, i, ring);

            tilemapFiller.BuildRectangularRingRoom(
                tilemap,
                innerSize,
                ringWidth,
                ring.weightedTiles,
                spawners,
                ring.wallPrefab,
                ring.cornerPrefab,
                isLastRing ? null : ring.openDoor,
                isLastRing ? null : ring.closedDoor,
                ring.decorativeElement,
                decorativePercentage
            );

            innerSize = new Vector2Int(
                innerSize.x + 2 * ringWidth,
                innerSize.y + 2 * ringWidth
            );
        }
    }

    private bool isRingEnabled(MapConfig cfg, int index)
    {
        if (cfg == null) return true;

        return index switch
        {
            0 => cfg.decoratedRoom.enabled,
            1 => cfg.tileRoom.enabled,
            2 => cfg.woodRoom.enabled,
            3 => cfg.brokenTileRoom.enabled,
            4 => cfg.castleYard.enabled,
            _ => true
        };
    }

    private int getRingWidth(MapConfig cfg, int index)
    {
        if (cfg == null) return 8;

        return index switch
        {
            0 => cfg.decoratedRoom.ringWidth,
            1 => cfg.tileRoom.ringWidth,
            2 => cfg.woodRoom.ringWidth,
            3 => cfg.brokenTileRoom.ringWidth,
            4 => cfg.castleYard.ringWidth,
            5 => cfg.outerForest.ringWidth,
            _ => 8
        };
    }

    private float getDecorativePercentage(MapConfig cfg, int index, RingSettings ring)
    {
        if (cfg == null) return ring.decorativeElementPercentage;

        return index switch
        {
            0 => cfg.decoratedRoom.decorativePercentage,
            1 => cfg.tileRoom.decorativePercentage,
            2 => cfg.woodRoom.decorativePercentage,
            3 => cfg.brokenTileRoom.decorativePercentage,
            4 => cfg.castleYard.decorativePercentage,
            5 => cfg.outerForest.decorativePercentage,
            _ => ring.decorativeElementPercentage
        };
    }

    private GameObject[] buildSpawnersArray(MapConfig cfg, int index)
    {
        Debug.Log($"[LevelGenerator] Ring {index}");
        if (cfg == null) return null;

        int dragons = 0;
        int goats = 0;

        switch (index)
        {
            case 0: dragons = cfg.decoratedRoom.dragonSpawnerCount; goats = cfg.decoratedRoom.goatSpawnerCount; break;
            case 1: dragons = cfg.tileRoom.dragonSpawnerCount; goats = cfg.tileRoom.goatSpawnerCount; break;
            case 2: dragons = cfg.woodRoom.dragonSpawnerCount; goats = cfg.woodRoom.goatSpawnerCount; break;
            case 3: dragons = cfg.brokenTileRoom.dragonSpawnerCount; goats = cfg.brokenTileRoom.goatSpawnerCount; break;
            case 4: dragons = cfg.castleYard.dragonSpawnerCount; goats = cfg.castleYard.goatSpawnerCount; break;
            case 5: dragons = cfg.outerForest.dragonSpawnerCount; goats = cfg.outerForest.goatSpawnerCount; break;
        }

        int total = dragons + goats;
        Debug.Log($"[LevelGenerator] Dragons={dragons} Goats={goats}");
        if (total == 0) return null;

        GameObject[] spawners = new GameObject[total];
        int idx = 0;

        for (int d = 0; d < dragons; d++)
            spawners[idx++] = dragonSpawnerPrefab;

        for (int g = 0; g < goats; g++)
            spawners[idx++] = goatSpawnerPrefab;

        return spawners;
    }

    private void preparePlayerSpawn()
    {
        if (!tryCalculateSpawnPos(out Vector3 spawnPos))
        {
            Debug.LogWarning("[LevelGenerator] No se pudo calcular posición de spawn del player.");
            return;
        }

        pendingSpawnPos = spawnPos;
        hasPendingSpawn = true;

        PlayerController existingPlayer = GameManager.Instance?.LocalPlayerController;
        if (existingPlayer != null)
        {
            applySpawnAndCharacter(existingPlayer, pendingSpawnPos);
            hasPendingSpawn = false;
        }
    }

    private void onLocalPlayerRegistered(PlayerController player)
    {
        if (player == null || !hasPendingSpawn) return;

        applySpawnAndCharacter(player, pendingSpawnPos);
        hasPendingSpawn = false;
    }

    private void applySpawnAndCharacter(PlayerController player, Vector3 spawnPos)
    {
        player.gameObject.SetActive(true);
        player.transform.position = spawnPos;
        applySelectedCharacter(player);
    }

    private bool tryCalculateSpawnPos(out Vector3 spawnPos)
    {
        spawnPos = Vector3.zero;

        if (castleRings == null || castleRings.Length == 0)
            return false;

        MapConfig cfg = activeConfig;
        int roomSize = cfg != null ? cfg.treasureRoomSize : 7;
        int forestIndex = castleRings.Length - 1;

        System.Collections.Generic.List<int> activeInnerRingIndices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < forestIndex; i++)
        {
            if (castleRings[i] == null) continue;
            if (!isRingEnabled(cfg, i)) continue;
            activeInnerRingIndices.Add(i);
        }

        if (activeInnerRingIndices.Count == 0)
        {
            int forestWidth = getRingWidth(cfg, forestIndex);
            if (forestWidth <= 0) forestWidth = 4;

            Vector2Int outerSizeFallback = new Vector2Int(
                roomSize + 2 * forestWidth,
                roomSize + 2 * forestWidth
            );

            int xMinFallback = Mathf.FloorToInt(-outerSizeFallback.x / 2f);
            int yMinFallback = Mathf.FloorToInt(-outerSizeFallback.y / 2f);
            int marginFallback = Mathf.Clamp(2, 1, Mathf.Max(1, forestWidth - 1));

            spawnPos = new Vector3(
                xMinFallback + marginFallback + 0.5f,
                yMinFallback + marginFallback + 0.5f,
                -0.1f
            );

            return true;
        }

        int penultimateIndex = activeInnerRingIndices[activeInnerRingIndices.Count - 1];
        int penultimateWidth = getRingWidth(cfg, penultimateIndex);

        Vector2Int sizeBeforePenultimate = new Vector2Int(roomSize, roomSize);
        for (int k = 0; k < activeInnerRingIndices.Count - 1; k++)
        {
            int idx = activeInnerRingIndices[k];
            int w = getRingWidth(cfg, idx);

            sizeBeforePenultimate = new Vector2Int(
                sizeBeforePenultimate.x + 2 * w,
                sizeBeforePenultimate.y + 2 * w
            );
        }

        Vector2Int penultimateOuterSize = new Vector2Int(
            sizeBeforePenultimate.x + 2 * penultimateWidth,
            sizeBeforePenultimate.y + 2 * penultimateWidth
        );

        int xMin = Mathf.FloorToInt(-penultimateOuterSize.x / 2f);
        int yMin = Mathf.FloorToInt(-penultimateOuterSize.y / 2f);
        int margin = Mathf.Clamp(2, 1, Mathf.Max(1, penultimateWidth - 1));

        spawnPos = new Vector3(
            xMin + margin + 0.5f,
            yMin + margin + 0.5f,
            -0.1f
        );

        return true;
    }

    private void applySelectedCharacter(PlayerController player)
    {
        if (GameManager.Instance == null) return;
        if (!player.IsOwner) return;

        if (GameManager.Instance.SelectedCharacterStats == null) return;

        PlayerStats selectedStats = GameManager.Instance.SelectedCharacterStats;
        player.ApplyCharacterStats(selectedStats);

        if (selectedStats.animatorController != null)
        {
            Animator animator = player.GetComponent<Animator>();
            if (animator != null)
            {
                animator.runtimeAnimatorController = selectedStats.animatorController;
                //Debug.Log($"[LevelGenerator] Animator cambiado a: {selectedStats.animatorController.name}");
            }
        }

        //Debug.Log($"[LevelGenerator] Personaje aplicado: {selectedStats.characterName}");
    }
}