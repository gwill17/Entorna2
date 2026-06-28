using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.Playables;

/// <summary>
/// Contiene los nombres exactos de las escenas del proyecto para evitar errores de escritura.
/// </summary>
public static class SceneNames
{
    public const string MainMenu = "MainMenu";
    public const string CharSelection = "CharSelectionScene";
    public const string LobbyScene = "LobbyScene";
    public const string PlaygroundLevel = "PlaygroundLevel";
    public const string DeadScene = "DeadScene";
    public const string VictoryScene = "VictoryScene";
}

/// <summary>
/// Gestor principal del juego. Centraliza el estado global, el inventario de los jugadores, 
/// las transiciones de red y las condiciones de victoria o derrota.
/// </summary>
public class GameManager : NetworkBehaviour
{
    #region Variables y Propiedades
    public static GameManager Instance { get; private set; }

    [Header("Configuración General")]
    [SerializeField] private float delayBeforeScene = 0.5f;
    public MapConfig[] availableMaps;

    [Header("Referencias de Jugador Local")]
    public PlayerController LocalPlayerController { get; private set; }
    public Transform LocalPlayerTransform => LocalPlayerController != null ? LocalPlayerController.transform : null;
    public UniqueEntity LocalPlayerEntity { get; private set; }

    [Header("Estado del Lobby y Selección")]
    public PlayerStats SelectedCharacterStats { get; set; }
    public MapConfig SelectedMapConfig { get; set; }
    public int SelectedCharacterIndex { get; set; } = -1;

    // Variables de Red (Sincronizadas)
    private NetworkVariable<int> enemiesKilledNet = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> totalGlobalDiamondsNet = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> selectedMapIndexNet = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Variables de Estado Privadas
    private Dictionary<ulong, PlayerGameState> playerStates = new();
    private bool isGameOver = false;

    // Variables de HUD Local
    private int localClientDiamonds;
    private int localClientKeys;
    private int localClientEnemies;

    // Propiedades Públicas
    public int EnemiesKilled => enemiesKilledNet.Value;
    #endregion

    #region Ciclo de Vida (Unity y Netcode)
    /// <summary>
    /// Configura el patrón Singleton y evita que el GameManager se destruya entre cambios de escena.
    /// </summary>
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

        ResetGameData();
    }

    private void OnEnable()
    {
        GameEvents.OnPlayerDied += onPlayerDeath;
    }

    private void OnDisable()
    {
        GameEvents.OnPlayerDied -= onPlayerDeath;
    }

    public override void OnDestroy()
    {
        enemiesKilledNet.OnValueChanged -= OnEnemiesKilledChanged;
        base.OnDestroy();
        SceneManager.sceneUnloaded -= onSceneUnloaded;
    }

    /// <summary>
    /// Se ejecuta al instanciar el gestor en la red. Reinicia variables globales y suscribe eventos.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        totalGlobalDiamondsNet.Value = 0;
        enemiesKilledNet.OnValueChanged += OnEnemiesKilledChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        ResetGameData();
    }
    #endregion

    #region Gestión de Jugadores y Lobby
    /// <summary>
    /// Registra el avatar del jugador local una vez instanciado y notifica a otros sistemas.
    /// </summary>
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

    /// <summary>
    /// Guarda la selección del personaje (estadísticas e índice) de forma local antes de iniciar partida.
    /// </summary>
    public void SelectCharacter(PlayerStats stats, int index)
    {
        SelectedCharacterStats = stats;
        SelectedCharacterIndex = index;
    }

    /// <summary>
    /// Configura el mapa a jugar desde el Lobby (Autoridad exclusiva del Servidor).
    /// </summary>
    public void SetMapIndexByHost(int mapDropdownIndex)
    {
        if (availableMaps == null || availableMaps.Length <= mapDropdownIndex) return;

        SelectedMapConfig = availableMaps[mapDropdownIndex];

        if (IsServer)
        {
            selectedMapIndexNet.Value = mapDropdownIndex;
        }
    }
    #endregion

    #region Sistema de Inventario y Recursos
    /// <summary>
    /// Devuelve los diamantes del jugador local (leyendo el estado del servidor si es Host).
    /// </summary>
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

    /// <summary>
    /// Calcula y devuelve la suma total de diamantes recogidos por todos los jugadores.
    /// </summary>
    public int GetGlobalDiamonds()
    {
        if (IsServer)
        {
            int totalGlobal = 0;
            foreach (var state in playerStates.Values)
            {
                totalGlobal += state.Diamonds;
            }
            return totalGlobal;
        }
        return totalGlobalDiamondsNet.Value;
    }

    /// <summary>
    /// Devuelve las llaves del jugador local (leyendo el estado del servidor si es Host).
    /// </summary>
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

    /// <summary>
    /// Devuelve las bajas realizadas específicamente por el jugador local.
    /// </summary>
    public int GetMyEnemiesKilled()
    {
        if (IsServer && LocalPlayerController != null)
        {
            ulong clientId = LocalPlayerController.OwnerClientId;
            return playerStates.ContainsKey(clientId) ? playerStates[clientId].EnemiesKilled : 0;
        }
        return localClientEnemies;
    }

    /// <summary>
    /// Busca y recupera el estado almacenado (inventario) de un jugador a partir de su ID.
    /// </summary>
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
    #endregion

    #region Lógica de Interacción Autoritativa (Servidor)
    /// <summary>
    /// Incrementa de manera autoritativa (solo servidor) las estadísticas de bajas de un jugador.
    /// </summary>
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

    /// <summary>
    /// Intenta añadir una llave al inventario de un jugador concreto en el servidor de forma aislada.
    /// </summary>
    public bool TryAddKey(ulong playerClientId, string keyEntityId)
    {
        if (!IsServer) return false;

        if (!playerStates.ContainsKey(playerClientId))
            playerStates[playerClientId] = new PlayerGameState($"PLAYER_{playerClientId}");

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

    /// <summary>
    /// Intenta añadir un diamante al inventario de un jugador en el servidor e incrementa el contador global.
    /// </summary>
    public bool TryAddDiamond(ulong playerClientId, string diamondEntityId)
    {
        if (!IsServer) return false;

        if (!playerStates.ContainsKey(playerClientId))
            playerStates[playerClientId] = new PlayerGameState($"PLAYER_{playerClientId}");

        PlayerGameState state = playerStates[playerClientId];
        state.AddDiamond();
        playerStates[playerClientId] = state;

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { playerClientId } }
        };

        SincronizarHUDLocalClientRpc(state.Diamonds, state.Keys, state.EnemiesKilled, clientRpcParams);
        totalGlobalDiamondsNet.Value++;
        return true;
    }

    /// <summary>
    /// Valida si el jugador tiene llaves suficientes y abre la puerta si se cumple la condición (Autoritativo).
    /// </summary>
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

    /// <summary>
    /// Intenta desencadenar el final de partida por victoria. Solo permite su validación final en el servidor.
    /// </summary>
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
    #endregion

    #region Sincronización y RPCs
    /// <summary>
    /// Dispara un evento global en el cliente cuando el servidor actualiza el número total de bajas.
    /// </summary>
    private void OnEnemiesKilledChanged(int oldValue, int newValue)
    {
        GameEvents.EnemyKilled(newValue);
    }

    /// <summary>
    /// Actualiza exclusivamente el HUD del cliente específico que haya recibido una actualización de recursos.
    /// </summary>
    [ClientRpc]
    private void SincronizarHUDLocalClientRpc(int totalDiamonds, int totalKeys, int totalEnemies, ClientRpcParams clientRpcParams = default)
    {
        localClientDiamonds = totalDiamonds;
        localClientKeys = totalKeys;
        localClientEnemies = totalEnemies;

        GameEvents.DiamondsChanged();
        GameEvents.KeysChanged();
    }

    [ClientRpc]
    private void UpdateLocalPlayerHUDClientRpc(int currentDiamonds, int currentKeys, ClientRpcParams clientRpcParams = default)
    {
        if (GameManager.Instance.LocalPlayerController != null)
        {
            GameEvents.DiamondsChanged();
            GameEvents.KeysChanged();
        }
    }

    /// <summary>
    /// Ordena a todos los clientes abrir visualmente una puerta específica tras su validación.
    /// </summary>
    [ClientRpc]
    private void AbrePuertaPorPosicionGlobalClientRpc(Vector3 doorPosition)
    {
        DoorController[] puertas = FindObjectsByType<DoorController>(FindObjectsSortMode.None);
        foreach (DoorController puerta in puertas)
        {
            if (Vector3.Distance(puerta.transform.position, doorPosition) < 0.2f)
            {
                puerta.OpenDoorLocal();
                break;
            }
        }
    }

    /// <summary>
    /// Solicita al servidor desde un cliente que procese la victoria al interactuar con el cofre.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void NotificarVictoriaAlServidorServerRpc()
    {
        if (isGameOver) return;
        ProcesarVictoriaGlobal();
    }

    /// <summary>
    /// Ordena a todos los clientes que pongan sus contadores locales a cero.
    /// </summary>
    [ClientRpc]
    private void ResetearHUDClientesClientRpc()
    {
        localClientDiamonds = 0;
        localClientKeys = 0;
        localClientEnemies = 0;

        GameEvents.DiamondsChanged();
        GameEvents.KeysChanged();
        GameEvents.EnemyKilled(0);
    }
    #endregion

    #region Flujo de Partida y Transiciones de Escena
    /// <summary>
    /// Restablece todas las variables e inventarios del juego al estado inicial.
    /// </summary>
    public void ResetGameData()
    {
        playerStates.Clear();
        Time.timeScale = 1f;
        isGameOver = false;

        localClientDiamonds = 0;
        localClientKeys = 0;
        localClientEnemies = 0;

        LocalPlayerController = null;
        LocalPlayerEntity = null;

        if (IsServer)
        {
            totalGlobalDiamondsNet.Value = 0;
            enemiesKilledNet.Value = 0;
            ResetearHUDClientesClientRpc();
        }
        else
        {
            GameEvents.DiamondsChanged();
            GameEvents.KeysChanged();
        }
    }

    /// <summary>
    /// Inicia la partida cargando el nivel procedimental a través del gestor de red sincronizado.
    /// </summary>
    public void StartGame(PlayerStats selectedCharacter)
    {
        if (selectedCharacter == null)
        {
            Debug.LogError("[GameManager] StartGame llamado sin personaje seleccionado.");
            return;
        }

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
        ProcesarDerrotaGlobal();
    }

    /// <summary>
    /// Limpia los eventos estáticos al descargar el nivel principal para evitar memory leaks.
    /// </summary>
    private void onSceneUnloaded(Scene scene)
    {
        if (scene.name == SceneNames.PlaygroundLevel)
        {
            GameEvents.ClearSceneEvents();
        }
    }

    /// <summary>
    /// Ordena la transición sincronizada hacia la pantalla de derrota.
    /// </summary>
    private void loadDeadScene()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            LimpiarEnemigosDeRed();
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.DeadScene, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(SceneNames.DeadScene);
        }
    }

    private void victoryAchieved()
    {
        Invoke(nameof(loadVictoryScene), delayBeforeScene);
    }

    /// <summary>
    /// Ordena la transición sincronizada hacia la pantalla de estadísticas finales (Victoria).
    /// </summary>
    private void loadVictoryScene()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            LimpiarEnemigosDeRed();
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.VictoryScene, LoadSceneMode.Single);
        }
        else
        {
            SceneManager.LoadScene(SceneNames.VictoryScene);
        }
    }

    /// <summary>
    /// Congela el juego globalmente y programa la transición a la escena de victoria.
    /// </summary>
    private void ProcesarVictoriaGlobal()
    {
        if (isGameOver) return;
        isGameOver = true;

        CongelarEntidadesPartidaClientRpc();

        CancelInvoke(nameof(loadDeadScene));
        Invoke(nameof(loadVictoryScene), delayBeforeScene);
    }

    /// <summary>
    /// Congela el juego globalmente y programa la transición a la escena de Game Over.
    /// </summary>
    private void ProcesarDerrotaGlobal()
    {
        if (isGameOver) return;
        isGameOver = true;

        CongelarEntidadesPartidaClientRpc();

        CancelInvoke(nameof(loadVictoryScene));
        Invoke(nameof(loadDeadScene), delayBeforeScene);
    }

    /// <summary>
    /// Gestiona la muerte de un cliente. Si quedan vivos, oculta el cadáver; si no, lanza el Game Over.
    /// </summary>
    private void onPlayerDeath(ulong deadClientId)
    {
        if (isGameOver) return;
        if (!IsServer) return;

        PlayerController[] todosLosJugadores = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        int jugadoresVivos = 0;

        foreach (PlayerController j in todosLosJugadores)
        {
            if (j.OwnerClientId != deadClientId && j.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr) && sr.enabled)
            {
                jugadoresVivos++;
            }
        }

        if (jugadoresVivos > 0)
        {
            OcultarCuerpoMuertoClientRpc(deadClientId);
            return;
        }

        ProcesarDerrotaGlobal();
    }

    /// <summary>
    /// Elimina físicamente a los enemigos de la red multijugador para evitar errores en cambios de escena.
    /// </summary>
    private void LimpiarEnemigosDeRed()
    {
        if (!IsServer) return;

        EnemyController[] enemigosRestantes = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        foreach (EnemyController enemigo in enemigosRestantes)
        {
            if (enemigo != null && enemigo.gameObject != null)
            {
                if (enemigo.TryGetComponent<NetworkObject>(out NetworkObject netObj))
                {
                    if (netObj.IsSpawned)
                        netObj.Despawn(true);
                }
                else
                {
                    Destroy(enemigo.gameObject);
                }
            }
        }
    }

    /// <summary>
    /// Informa a todos los clientes que desactiven visualmente y físicamente a un jugador muerto específico.
    /// </summary>
    [ClientRpc]
    private void OcultarCuerpoMuertoClientRpc(ulong clientId)
    {
        PlayerController[] jugadores = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (PlayerController jugador in jugadores)
        {
            if (jugador.OwnerClientId == clientId)
            {
                if (jugador.TryGetComponent<Collider2D>(out Collider2D col)) col.enabled = false;
                if (jugador.TryGetComponent<Rigidbody2D>(out Rigidbody2D rb))
                {
                    rb.linearVelocity = Vector2.zero;
                    rb.bodyType = RigidbodyType2D.Kinematic;
                }

                if (jugador.TryGetComponent<SpriteRenderer>(out SpriteRenderer sr)) sr.enabled = false;
                foreach (var childSR in jugador.GetComponentsInChildren<SpriteRenderer>()) childSR.enabled = false;
                break;
            }
        }
    }

    /// <summary>
    /// Detiene a todos los jugadores y enemigos) y oculta recursos al terminar la partida.
    /// </summary>
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
    #endregion
}