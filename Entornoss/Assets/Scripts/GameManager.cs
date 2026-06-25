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
    public int GetKeys()
    {
        if (LocalPlayerController == null) return 0;

        PlayerGameState state = GetStateForPlayer(LocalPlayerController.EntityId);
        return state?.Keys ?? 0;
    }

    public int GetDiamonds()
    {
        if (LocalPlayerController == null) return 0;

        PlayerGameState state = GetStateForPlayer(LocalPlayerController.EntityId);
        return state?.Diamonds ?? 0;
    }

    public bool TryAddKey(string playerEntityId, string keyEntityId)
    {
        PlayerGameState state = GetStateForPlayer(playerEntityId);
        if (state == null) return false;

        state.AddKey();
        return true;
    }

    public bool TryAddDiamond(string playerEntityId, string diamondEntityId)
    {
        PlayerGameState state = GetStateForPlayer(playerEntityId);
        if (state == null) return false;

        state.AddDiamond();
        return true;
    }

    public bool TryOpenDoor(string playerEntityId, string doorEntityId)
    {
        PlayerGameState state = GetStateForPlayer(playerEntityId);
        if (state == null) return false;

        return state.UseKey();
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

        // 🌟 CARGA DE ESCENA SEGURA EN RED
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
        // 🌟 CARGA DE ESCENA SEGURA EN RED
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
        // 🌟 CARGA DE ESCENA SEGURA EN RED
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