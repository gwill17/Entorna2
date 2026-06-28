using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    [SerializeField] private GameObject startButton;
    [SerializeField] private TextMeshProUGUI playersStatusText;

    private readonly Dictionary<ulong, int> selectedCharacters = new();

    private const string MsgSelectCharacter = "Lobby_SelectCharacter";
    private const string MsgLobbyState = "Lobby_State";

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

    private void Update()
    {
        UpdateLobbyText();
    }

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

    private void OnReceiveCharacterSelection(ulong senderClientId, FastBufferReader reader)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        reader.ReadValueSafe(out ulong clientId);
        reader.ReadValueSafe(out int characterIndex);

        selectedCharacters[senderClientId] = characterIndex;
        BroadcastLobbyState();
    }

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

    private void OnClientChanged(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
            selectedCharacters.Remove(clientId);

        BroadcastLobbyState();
    }

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

    public void OnBackButtonClicked()
    {
        if (NetworkManager.Singleton != null)
        {
            //Debug.Log("[Lobby] Cerrando conexión de red de forma limpia.");

            if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientChanged;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientChanged;
            }

            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(SceneNames.MainMenu);
    }
}