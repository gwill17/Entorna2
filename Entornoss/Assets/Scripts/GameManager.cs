using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.Playables;

public static class SceneNames
{
    public const string MainMenu = "MainMenu";
    public const string CharSelection = "CharSelectionScene";
    public const string LobbyScene = "LobbyScene";
    public const string PlaygroundLevel = "PlaygroundLevel";
    public const string DeadScene = "DeadScene";
    public const string VictoryScene = "VictoryScene";
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public PlayerController LocalPlayerController { get; private set; }
    public Transform LocalPlayerTransform => LocalPlayerController != null ? LocalPlayerController.transform : null;
    public UniqueEntity LocalPlayerEntity { get; private set; }

    private NetworkVariable<int> enemiesKilledNet = new NetworkVariable<int>(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);

    public int EnemiesKilled => enemiesKilledNet.Value;
    public PlayerStats SelectedCharacterStats { get; set; }
    public MapConfig SelectedMapConfig { get; set; }
    public int SelectedCharacterIndex { get; set; } = -1;
    public MapConfig[] availableMaps;

    private NetworkVariable<int> selectedMapIndexNet = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private float delayBeforeScene = 0.5f;

    private Dictionary<ulong, PlayerGameState> playerStates = new();
    private bool isGameOver = false;

    private int localClientDiamonds;
    private int localClientKeys;
    private int localClientEnemies;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        isGameOver = false;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneUnloaded += onSceneUnloaded;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        enemiesKilledNet.OnValueChanged += OnEnemiesKilledChanged;
    }
    private PlayerGameState GetStateForPlayer(string playerEntityId)
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        foreach (PlayerController player in players)
        {
            if (player.EntityId == playerEntityId)
            {
                ulong clientId = player.OwnerClientId;

                if (!playerStates.ContainsKey(clientId))
                    playerStates[clientId] = new PlayerGameState(playerEntityId);

                return playerStates[clientId];
            }
        }

        return null;
    }
    public void SelectCharacter(PlayerStats stats, int index)
    {
        SelectedCharacterStats = stats;
        SelectedCharacterIndex = index;

        Debug.Log($"[GameManager] Personaje elegido: {stats.characterName} índice {index}");
    }
    public override void OnDestroy()
    {
        enemiesKilledNet.OnValueChanged -= OnEnemiesKilledChanged;

        base.OnDestroy();
        SceneManager.sceneUnloaded -= onSceneUnloaded;
    }

    private void OnEnable()
    {
        GameEvents.OnPlayerDied += onPlayerDeath;
    }

    private void OnDisable()
    {
        GameEvents.OnPlayerDied -= onPlayerDeath;
    }

    public void RegisterLocalPlayer(PlayerController player, UniqueEntity entity)
    {
        LocalPlayerController = player;
        LocalPlayerEntity = entity;
        SetPlayerData(entity);
        GameEvents.LocalPlayerRegistered(player);
    }

    public void SetPlayerData(UniqueEntity playerEntity)
    {
        if (playerEntity == null || string.IsNullOrEmpty(playerEntity.EntityId)) return;
    }

    public void ResetGameData()
    {
        playerStates.Clear();
        Time.timeScale = 1f; 

        if (IsServer)
            enemiesKilledNet.Value = 0;
    }

    public void AddEnemyKillServer(ulong playerClientId)
    {
        if (!IsServer) return;

        if (!playerStates.ContainsKey(playerClientId))
        {
            playerStates[playerClientId] = new PlayerGameState($"PLAYER_{playerClientId}");
        }

        PlayerGameState state = playerStates[playerClientId];
        state.EnemiesKilled++; 
        playerStates[playerClientId] = state;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { playerClientId } }
        };
        SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, state.EnemiesKilled, clientRpcParams);

        enemiesKilledNet.Value++;
    }

    private void OnEnemiesKilledChanged(int oldValue, int newValue)
    {
        GameEvents.EnemyKilled(newValue);
    }
    public int GetDiamonds()
    {
        if (LocalPlayerController == null) return 0;

        if (IsServer)
        {
            ulong clientId = LocalPlayerController.OwnerClientId;
            return playerStates.ContainsKey(clientId) ? playerStates[clientId].Diamonds : 0;
        }
        return localClientDiamonds;
    }

    public int GetKeys()
    {
        if (LocalPlayerController == null) return 0;

        if (IsServer)
        {
            ulong clientId = LocalPlayerController.OwnerClientId;
            return playerStates.ContainsKey(clientId) ? playerStates[clientId].Keys : 0;
        }
        return localClientKeys;
    }

    public int GetMyEnemiesKilled()
    {
        if (IsServer && LocalPlayerController != null)
        {
            ulong clientId = LocalPlayerController.OwnerClientId;
            return playerStates.ContainsKey(clientId) ? playerStates[clientId].EnemiesKilled : 0;
        }
        return localClientEnemies;
    }

    public bool TryAddKey(ulong playerClientId, string keyEntityId)
    {
        if (!IsServer) return false;

        if (!playerStates.ContainsKey(playerClientId))
        {
            playerStates[playerClientId] = new PlayerGameState($"PLAYER_{playerClientId}");
        }

        PlayerGameState state = playerStates[playerClientId];

        state.AddKey();

        playerStates[playerClientId] = state;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { playerClientId } }
        };

        SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, state.EnemiesKilled, clientRpcParams);
        return true;
    }

    public bool TryAddDiamond(ulong playerClientId, string diamondEntityId)
    {
        if (!IsServer) return false;

        if (!playerStates.ContainsKey(playerClientId))
        {
            playerStates[playerClientId] = new PlayerGameState($"PLAYER_{playerClientId}");
        }

        PlayerGameState state = playerStates[playerClientId];
        state.AddDiamond();
        playerStates[playerClientId] = state;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { playerClientId } }
        };

        SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, state.EnemiesKilled, clientRpcParams);
        return true;
    }

    [ClientRpc]
    private void SincronizarHUDLocalClientRpc(int totalDiamonds, int totalKeys, int totalEnemies, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[HUD RPC] Actualizando mi HUD: Diamantes {totalDiamonds}, Llaves {totalKeys}");

        localClientDiamonds = totalDiamonds;
        localClientKeys = totalKeys;
        localClientEnemies = totalEnemies;

        GameEvents.DiamondsChanged();
        GameEvents.KeysChanged();
        
    }

    private PlayerController FindPlayerByEntityId(string entityId)
    {
        foreach (var player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (player.EntityId == entityId) return player;
        }
        return null;
    }

    
    [ClientRpc]
    private void UpdateLocalPlayerHUDClientRpc(int currentDiamonds, int currentKeys, ClientRpcParams clientRpcParams = default)
    {
        
        if (GameManager.Instance.LocalPlayerController != null)
        {
            Debug.Log($"[HUD RPC] Actualizando HUD local. Diamantes: {currentDiamonds}, Llaves: {currentKeys}");

            GameEvents.DiamondsChanged();
            GameEvents.KeysChanged();
        }
    }

    public bool TryOpenDoor(ulong clientId, Vector3 doorPosition)
    {
        if (!NetworkManager.Singleton.IsServer) return false;

        if (playerStates.TryGetValue(clientId, out PlayerGameState state))
        {
            if (state.Keys > 0)
            {
                state.Keys--; 
                playerStates[clientId] = state; 

                ClientRpcParams clientRpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { clientId } }
                };
                SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, state.EnemiesKilled, clientRpcParams);

                AbrePuertaPorPosicionGlobalClientRpc(doorPosition);

                return true;
            }
        }
        return false;
    }

    [ClientRpc]
    private void AbrePuertaPorPosicionGlobalClientRpc(Vector3 doorPosition)
    {
        DoorController[] puertas = FindObjectsByType<DoorController>(FindObjectsSortMode.None);

        foreach (DoorController puerta in puertas)
        {
            if (Vector3.Distance(puerta.transform.position, doorPosition) < 0.2f)
            {
                puerta.OpenDoorLocal(); 
                Debug.Log($"[Netcode] Puerta abierta síncronamente en posición: {doorPosition}");
                break;
            }
        }
    }
    public bool TryTriggerVictory(string playerEntityId, string chestEntityId)
    {
        if (isGameOver) return false;

        if (!IsServer)
        {
            NotificarVictoriaAlServidorServerRpc();
            return true;
        }

        ProcesarVictoriaGlobal();
        return true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotificarVictoriaAlServidorServerRpc()
    {
        if (isGameOver) return;
        ProcesarVictoriaGlobal();
    }

    private void ProcesarVictoriaGlobal()
    {
        if (isGameOver) return;
        isGameOver = true; 

        CongelarEntidadesPartidaClientRpc();

        Debug.Log($"[F3.5] ¡Victoria alcanzada! Avisando a todos los clientes.");

        CancelInvoke(nameof(loadDeadScene));
        Invoke(nameof(loadVictoryScene), delayBeforeScene);
    }


    public void StartGame(PlayerStats selectedCharacter)
    {
        if (selectedCharacter == null)
        {
            Debug.LogError("[GameManager] StartGame llamado sin personaje seleccionado.");
            return;
        }

        Debug.Log($"[GameManager] Personaje seleccionado: {selectedCharacter.characterName}");
        SelectedCharacterStats = selectedCharacter;
        ResetGameData();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.PlaygroundLevel, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(SceneNames.PlaygroundLevel);
        }
    }

    public void StartGame(PlayerStats selectedCharacter, MapConfig selectedMap)
    {
        SelectedMapConfig = selectedMap;
        StartGame(selectedCharacter);
    }

    public void TriggerGameOver()
    {
        
        if (isGameOver) return;

        Debug.Log($"[GameManager] Game Over. Keys: {GetKeys()}, Diamonds: {GetDiamonds()}, Enemies: {EnemiesKilled}");
        ProcesarDerrotaGlobal();
    }
    

    private void onSceneUnloaded(Scene scene)
    {
        if (scene.name == SceneNames.PlaygroundLevel)
        {
            GameEvents.ClearSceneEvents();
        }
    }

    private void loadDeadScene()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.DeadScene, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(SceneNames.DeadScene);
        }
    }

    private void victoryAchieved()
    {
        Debug.Log($"[GameManager] Victoria. Keys: {GetKeys()}, Diamonds: {GetDiamonds()}, Enemies: {EnemiesKilled}");
        Invoke(nameof(loadVictoryScene), delayBeforeScene);
    }

    private void loadVictoryScene()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.VictoryScene, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(SceneNames.VictoryScene);
        }
    }

    private void onPlayerDeath() 
    {
        if (isGameOver) return;

        Debug.Log($"[GameManager] Jugador muerto. Keys: {GetKeys()}, Diamonds: {GetDiamonds()}, Enemies: {EnemiesKilled}");
        ProcesarDerrotaGlobal();
    }

    private void ProcesarDerrotaGlobal()
    {
        if (isGameOver) return;
        isGameOver = true; 

        CongelarEntidadesPartidaClientRpc();

        Debug.Log($"[GameManager] ¡Derrota alcanzada! Avisando a todos los clientes.");

        CancelInvoke(nameof(loadVictoryScene));
        Invoke(nameof(loadDeadScene), delayBeforeScene);
    }

    [ClientRpc]
    private void CongelarEntidadesPartidaClientRpc()
    {
        EnemyController[] enemigos = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        foreach (EnemyController enemigo in enemigos)
        {
            enemigo.enabled = false; 

            if (enemigo.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr)) sr.enabled = false;
            foreach (var childSR in enemigo.GetComponentsInChildren<SpriteRenderer>()) childSR.enabled = false;

            if (enemigo.TryGetComponent<Collider2D>(out Collider2D col)) col.enabled = false;
            if (enemigo.TryGetComponent<Rigidbody2D>(out Rigidbody2D rb))
            {
                rb.linearVelocity = Vector2.zero;
                rb.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        PlayerController[] jugadores = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (PlayerController jugador in jugadores)
        {
            if (jugador.TryGetComponent<Collider2D>(out Collider2D col)) col.enabled = false;
            if (jugador.TryGetComponent<Rigidbody2D>(out Rigidbody2D rb))
            {
                rb.linearVelocity = Vector2.zero;
                rb.bodyType = RigidbodyType2D.Kinematic;
            }

            if (jugador.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr)) sr.enabled = false;
            foreach (var childSR in jugador.GetComponentsInChildren<SpriteRenderer>()) childSR.enabled = false;
        }

        SpriteRenderer[] todosLosSprites = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        foreach (SpriteRenderer sprite in todosLosSprites)
        {
            if (sprite == null || sprite.gameObject == null) continue;

            string nombre = sprite.gameObject.name.ToLower();

            if (nombre.Contains("diamond") || nombre.Contains("key") || nombre.Contains("drop") || nombre.Contains("gem"))
            {
                sprite.enabled = false;
                if (sprite.TryGetComponent<Collider2D>(out Collider2D colItem))
                {
                    colItem.enabled = false;
                }
            }
        }
    }

    public void SetMapIndexByHost(int mapDropdownIndex)
    {
        if (availableMaps == null || availableMaps.Length <= mapDropdownIndex) return;

        SelectedMapConfig = availableMaps[mapDropdownIndex];

        if (IsServer)
        {
            selectedMapIndexNet.Value = mapDropdownIndex;
        }
    }

}