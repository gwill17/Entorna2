using UnityEngine;
using UnityEngine.SceneManagement;

public class CharSelectionMenuButtonsHandler : MonoBehaviour
{
    [Header("Character Stats Assets")]
    [SerializeField] private PlayerStats greenCharacterStats;
    [SerializeField] private PlayerStats purpleCharacterStats;
    [SerializeField] private PlayerStats redCharacterStats;
    [SerializeField] private PlayerStats yellowCharacterStats;

    /// <summary>
    /// Vuelve al menú principal desde la pantalla de selección de personaje.
    /// </summary>
    public void OnBackButtonClicked()
    {
        SceneManager.LoadScene(SceneNames.MainMenu);
    }

    /// <summary>
    /// Selecciona el personaje verde e informa al Lobby del índice correspondiente.
    /// </summary>
    public void OnGreenButtonClicked()
    {
        GameManager.Instance.SelectCharacter(greenCharacterStats, 0);

        LobbyManager lobby = FindFirstObjectByType<LobbyManager>();

        if (lobby != null)
            lobby.SetMyCharacter(0);
    }
    /// <summary>
    /// Selecciona el personaje morado e informa al Lobby del índice correspondiente.
    /// </summary>
    public void OnPurpleButtonClicked()
    {
        GameManager.Instance.SelectCharacter(purpleCharacterStats, 1);

        LobbyManager lobby = FindFirstObjectByType<LobbyManager>();

        if (lobby != null)
            lobby.SetMyCharacter(1);
    }
    /// <summary>
    /// Selecciona el personaje rojo e informa al Lobby del índice correspondiente.
    /// </summary>
    public void OnRedButtonClicked()
    {
        GameManager.Instance.SelectCharacter(redCharacterStats, 2);

        LobbyManager lobby = FindFirstObjectByType<LobbyManager>();

        if (lobby != null)
            lobby.SetMyCharacter(2);
    }
    /// <summary>
    /// Selecciona el personaje amarillo e informa al Lobby del índice correspondiente.
    /// </summary>
    public void OnYellowButtonClicked()
    {
        GameManager.Instance.SelectCharacter(yellowCharacterStats, 3);

        LobbyManager lobby = FindFirstObjectByType<LobbyManager>();

        if (lobby != null)
            lobby.SetMyCharacter(3);
    }
    /// <summary>
    /// Procesa la selección de personaje y la sincroniza 
    /// a través del componente de red 'LobbySelectionNetwork'.
    /// </summary>
    private void SelectCharacter(PlayerStats stats, int index)
    {
        GameManager.Instance.SelectCharacter(stats, index);

        if (LobbySelectionNetwork.Instance == null)
        {
            Debug.LogError("[CharSelection] No existe LobbySelectionNetwork en la escena.");
            return;
        }

        if (!LobbySelectionNetwork.Instance.IsSpawned)
        {
            Debug.LogError("[CharSelection] LobbySelectionNetwork no está spawneado como NetworkObject.");
            return;
        }

        LobbySelectionNetwork.Instance.SetMyCharacter(index);
    }
    /// <summary>
    /// Valida la selección del personaje y delega el inicio de partida en GameManager.
    /// </summary>
    private void selectCharacterAndStartGame(PlayerStats characterStats)
    {
        if (characterStats == null)
        {
            Debug.LogError("[CharSelection] No se ha asignado PlayerStats para este personaje");
            return;
        }

        GameManager.Instance?.StartGame(characterStats);
    }
}
