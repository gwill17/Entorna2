using Unity.Netcode;
using UnityEngine;

public class LobbySelectionNetwork : NetworkBehaviour
{
    public static LobbySelectionNetwork Instance;

    public NetworkList<int> SelectedCharacters;

    private void Awake()
    {
        Instance = this;
        SelectedCharacters = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SelectedCharacters.Clear();

            for (int i = 0; i < 4; i++)
                SelectedCharacters.Add(-1);
        }
    }

    public void SetMyCharacter(int characterIndex)
    {
        if (NetworkManager.Singleton == null) return;

        ulong clientId = NetworkManager.Singleton.LocalClientId;
        SetCharacterServerRpc(clientId, characterIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetCharacterServerRpc(ulong clientId, int characterIndex)
    {
        int slot = (int)clientId;

        if (slot < 0 || slot >= SelectedCharacters.Count) return;

        SelectedCharacters[slot] = characterIndex;
    }

    public string GetCharacterName(int index)
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
}