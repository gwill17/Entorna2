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

    [SerializeField] private float delayBeforeScene = 0.5f;

    private Dictionary<ulong, PlayerGameState> playerStates = new();

    private int localClientDiamonds;
    private int localClientKeys;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        //playerState = new PlayerGameState("PLAYER_1");
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
        //playerState = new PlayerGameState(playerEntity.EntityId);
    }

    public void ResetGameData()
    {
        playerStates.Clear();

        if (IsServer)
            enemiesKilledNet.Value = 0;
    }

    public void AddEnemyKill()
    {
        enemiesKilledNet.Value++;
        GameEvents.EnemyKilled(enemiesKilledNet.Value);
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

    public bool TryAddKey(ulong playerClientId, string keyEntityId)
    {
        if (!IsServer) return false;

        // 1. Si no existe, creamos el estado inicial
        if (!playerStates.ContainsKey(playerClientId))
        {
            playerStates[playerClientId] = new PlayerGameState($"PLAYER_{playerClientId}");
        }

        // 2. EXTRAEMOS el struct (Copia temporal)
        PlayerGameState state = playerStates[playerClientId];

        // 3. Modificamos la variable
        state.AddKey();

        // 4. ¡CRUCIAL! Volvemos a guardar el struct modificado en el diccionario
        playerStates[playerClientId] = state;

        // Enviamos el RPC ÚNICAMENTE al cliente que lo recogió para actualizar su HUD
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { playerClientId } }
        };

        SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, clientRpcParams);
        return true;
    }

    // HAZ LO MISMO CON LOS DIAMANTES POR SI ACASO EN GameManager.cs
    public bool TryAddDiamond(ulong playerClientId, string diamondEntityId)
    {
        if (!IsServer) return false;

        if (!playerStates.ContainsKey(playerClientId))
        {
            playerStates[playerClientId] = new PlayerGameState($"PLAYER_{playerClientId}");
        }

        // Extraemos, modificamos y reasignamos el struct
        PlayerGameState state = playerStates[playerClientId];
        state.AddDiamond();
        playerStates[playerClientId] = state;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { playerClientId } }
        };

        SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, clientRpcParams);
        return true;
    }

    // Sincronizamos las variables del cliente que recibe el RPC antes de actualizar su HUD
    [ClientRpc]
    private void SincronizarHUDLocalClientRpc(int totalDiamonds, int totalKeys, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log($"[HUD RPC] Actualizando mi HUD: Diamantes {totalDiamonds}, Llaves {totalKeys}");

        localClientDiamonds = totalDiamonds;
        localClientKeys = totalKeys;

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

    public bool TryOpenDoor(ulong clientId, string doorEntityId)
    {
        if (!NetworkManager.Singleton.IsServer) return false;

        if (playerStates.TryGetValue(clientId, out PlayerGameState state))
        {
            if (state.Keys > 0)
            {
                state.Keys--;
                playerStates[clientId] = state;

                // 1. Sincronizamos el HUD del jugador que usó la llave
                ClientRpcParams clientRpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { clientId } }
                };
                SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, clientRpcParams);

                // 2. ¡IMPORTANTE! El Servidor le ordena a TODOS los clientes abrir la puerta físicamente
                NotificarAperturaPuertaAClientes(doorEntityId);

                return true;
            }
        }
        return false;
    }

    public void NotificarAperturaPuertaAClientes(string doorEntityId)
    {
        if (!IsServer) return;

        // Al ejecutarse desde el GameManager (que sí está spawneado), viaja seguro por la red
        AbrePuertaEnTodosLosClientesClientRpc(doorEntityId);
    }

    [ClientRpc]
    private void AbrePuertaEnTodosLosClientesClientRpc(string doorEntityId)
    {
        Debug.Log($"[ClientRpc] Orden recibida en cliente: Abriendo puerta con ID {doorEntityId}");

        // Buscamos todas las puertas en la escena local de ESTE cliente
        DoorController[] puertas = FindObjectsByType<DoorController>(FindObjectsSortMode.None);

        foreach (DoorController puerta in puertas)
        {
            if (puerta.EntityId == doorEntityId)
            {
                // Ejecuta la desactivación del collider y el cambio de sprite local
                puerta.OpenDoorLocal();
                Debug.Log($"[ClientRpc] Puerta {doorEntityId} abierta con éxito localmente.");
                break;
            }
        }
    }

    public bool TryTriggerVictory(string playerEntityId, string chestEntityId)
    {
        victoryAchieved();
        return true;
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

        // CARGA DE ESCENA SEGURA EN RED
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
        Debug.Log($"[GameManager] Game Over. Keys: {GetKeys()}, Diamonds: {GetDiamonds()}, Enemies: {EnemiesKilled}");
        Invoke(nameof(loadDeadScene), delayBeforeScene);
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
        //  CARGA DE ESCENA SEGURA EN RED
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
        //  CARGA DE ESCENA SEGURA EN RED
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
        Debug.Log($"[GameManager] Jugador muerto. Keys: {GetKeys()}, Diamonds: {GetDiamonds()}, Enemies: {EnemiesKilled}");
    }

    
}