using UnityEngine;
using Unity.Netcode;

public class NetworkAutoStart : MonoBehaviour
{
    private void Start()
    {
        // Verificamos si existe el NetworkManager en la escena
        if (NetworkManager.Singleton != null)
        {
            // Arranca automáticamente como Host (Servidor + Primer Jugador)
            NetworkManager.Singleton.StartHost();
            Debug.Log("[NetworkAutoStart] Iniciando partida automáticamente como HOST.");
        }
        else
        {
            Debug.LogError("[NetworkAutoStart] No se encontró el NetworkManager en la escena. Asegúrate de haberlo creado.");
        }
    }
}