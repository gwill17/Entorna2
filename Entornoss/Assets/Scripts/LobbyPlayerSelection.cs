using UnityEngine;
using Unity.Netcode;

public class LobbyPlayerSelection : NetworkBehaviour
{
    /// <summary>
    /// Índice del personaje seleccionado sincronizado en red. 
    /// Solo el servidor tiene permiso para modificar este valor.
    /// </summary>
    public NetworkVariable<int> SelectedCharacterIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    /// <summary>
    /// Selecciona un personaje el jugador.
    /// Solo se ejecuta si el cliente posee la autoridad sobre este objeto.
    /// </summary>
    public void SelectCharacter(int characterIndex)
    {
        if (!IsOwner) return;
        SelectCharacterServerRpc(characterIndex);
    }
    /// <summary>
    /// Solicitud enviada al servidor para actualizar el índice del personaje seleccionado.
    /// </summary>
    [ServerRpc]
    private void SelectCharacterServerRpc(int characterIndex)
    {
        if (characterIndex < 0 || characterIndex > 3) return;
        SelectedCharacterIndex.Value = characterIndex;
    }
}