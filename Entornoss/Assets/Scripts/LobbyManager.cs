using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    #region Variables y Constantes
    [Header("Referencias de Interfaz (UI)")]
    [SerializeField] private GameObject startButton;
    [SerializeField] private TextMeshProUGUI playersStatusText;

    // Estado local/global del Lobby
    private readonly Dictionary<ulong, int> selectedCharacters = new();

    // Constantes de Mensajería de Red
    private const string MsgSelectCharacter = "Lobby_SelectCharacter";
    private const string MsgLobbyState = "Lobby_State";
    #endregion

    #region Ciclo de Vida (Unity)
    /// <summary>
    /// Inicializa el estado del lobby, configura la visibilidad del botón de inicio 
    /// y registra los manejadores de mensajes personalizados de red.
    /// </summary>
    private void Start()
    {
        if (NetworkManager.Singleton == null) return;

        startButton.SetActive(NetworkManager.Singleton.IsServer);

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            MsgSelectCharacter,
            OnReceiveCharacterSelection
        );

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
            MsgLobbyState,
            OnReceiveLobbyState
        );

        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientChanged;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientChanged;
        }
    }

    /// <summary>
    /// Actualiza la interfaz de texto del lobby en cada fotograma.
    /// </summary>
    private void Update()
    {
        UpdateLobbyText();
    }

    /// <summary>
    /// Limpia los eventos y anula el registro de los manejadores de mensajes de red al destruir el objeto.
    /// </summary>
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.CustomMessagingManager != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MsgSelectCharacter);
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MsgLobbyState);
            }

            if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientChanged;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientChanged;
            }
        }
    }
    #endregion

    #region Eventos de Botones (UI Handlers)
    /// <summary>
    /// Inicia la transición sincronizada hacia el nivel de juego principal.
    /// Solo ejecutable por el Host.
    /// </summary>
    public void OnStartGameButtonClicked()
    {
        if (NetworkManager.Singleton == null) return;

        if (!NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[Lobby] Solo el Host puede iniciar la partida.");
            return;
        }

        NetworkManager.Singleton.SceneManager.LoadScene(
            SceneNames.PlaygroundLevel,
            LoadSceneMode.Single
        );
    }

    /// <summary>
    /// Desconecta limpiamente al jugador de la red (cerrando el Host o saliendo del servidor) 
    /// y lo devuelve a la escena del menú principal.
    /// </summary>
    public void OnBackButtonClicked()
    {
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientChanged;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientChanged;
            }

            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(SceneNames.MainMenu);
    }
    #endregion

    #region Actualización Visual del Lobby
    /// <summary>
    /// Actualiza el componente de texto de la interfaz con la lista de jugadores conectados, 
    /// sus roles de red (Host/Client) y los personajes que han elegido.
    /// </summary>
    private void UpdateLobbyText()
    {
        if (playersStatusText == null) return;
        if (NetworkManager.Singleton == null) return;

        string text = "Jugadores conectados:\n";

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            string role = client.ClientId == NetworkManager.ServerClientId ? "Host" : "Client";

            int index = selectedCharacters.ContainsKey(client.ClientId)
                ? selectedCharacters[client.ClientId]
                : -1;

            text += $"- {role} ID {client.ClientId} - {GetCharacterName(index)}\n";
        }

        playersStatusText.text = text;
    }

    /// <summary>
    /// Devuelve el nombre identificativo del personaje a partir de su índice numérico.
    /// </summary>
    private string GetCharacterName(int index)
    {
        return index switch
        {
            0 => "Green",
            1 => "Purple",
            2 => "Red",
            3 => "Yellow",
            _ => "Sin elegir"
        };
    }
    #endregion

    #region Mensajería de Red y Sincronización (Netcode)
    /// <summary>
    /// Registra la selección de personaje del jugador local. Si es cliente, envía un mensaje 
    /// al servidor. Si es servidor, actualiza y propaga el estado global.
    /// </summary>
    public void SetMyCharacter(int characterIndex)
    {
        if (NetworkManager.Singleton == null) return;

        ulong myId = NetworkManager.Singleton.LocalClientId;

        if (NetworkManager.Singleton.IsServer)
        {
            selectedCharacters[myId] = characterIndex;
            BroadcastLobbyState();
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe(myId);
        writer.WriteValueSafe(characterIndex);

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            MsgSelectCharacter,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced
        );
    }

    /// <summary>
    /// Recibe la selección de personaje de un cliente en el servidor y actualiza el estado global.
    /// </summary>
    private void OnReceiveCharacterSelection(ulong senderClientId, FastBufferReader reader)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        reader.ReadValueSafe(out ulong clientId);
        reader.ReadValueSafe(out int characterIndex);

        selectedCharacters[senderClientId] = characterIndex;
        BroadcastLobbyState();
    }

    /// <summary>
    /// Empaqueta y envía el diccionario actualizado de selecciones de personaje 
    /// a todos los clientes conectados.
    /// </summary>
    private void BroadcastLobbyState()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        using FastBufferWriter writer = new FastBufferWriter(256, Allocator.Temp);

        writer.WriteValueSafe(selectedCharacters.Count);

        foreach (var pair in selectedCharacters)
        {
            writer.WriteValueSafe(pair.Key);
            writer.WriteValueSafe(pair.Value);
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                MsgLobbyState,
                client.ClientId,
                writer,
                NetworkDelivery.ReliableSequenced
            );
        }
    }

    /// <summary>
    /// Recibe el estado global del lobby emitido por el servidor y actualiza el diccionario local del cliente.
    /// </summary>
    private void OnReceiveLobbyState(ulong senderClientId, FastBufferReader reader)
    {
        selectedCharacters.Clear();

        reader.ReadValueSafe(out int count);

        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out int characterIndex);

            selectedCharacters[clientId] = characterIndex;
        }
    }

    /// <summary>
    /// Gestiona las conexiones y desconexiones de clientes en el servidor, 
    /// limpiando el estado de los desconectados y propagando los cambios.
    /// </summary>
    private void OnClientChanged(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
            selectedCharacters.Remove(clientId);

        BroadcastLobbyState();
    }
    #endregion
}