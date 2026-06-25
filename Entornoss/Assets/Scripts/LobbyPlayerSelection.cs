using UnityEngine;
using Unity.Netcode;

public class LobbyPlayerSelection : NetworkBehaviour
{
    public NetworkVariable<int> SelectedCharacterIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public void SelectCharacter(int characterIndex)
    {
        if (!IsOwner) return;
        SelectCharacterServerRpc(characterIndex);
    }

    [ServerRpc]
    private void SelectCharacterServerRpc(int characterIndex)
    {
        if (characterIndex < 0 || characterIndex > 3) return;
        SelectedCharacterIndex.Value = characterIndex;
    }
}