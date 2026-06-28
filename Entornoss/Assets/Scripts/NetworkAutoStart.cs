using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Gestiona el inicio automático de la instancia de red como Host al cargar la escena.
/// </summary>
public class NetworkAutoStart : MonoBehaviour
{
    /// <summary>
    /// Verifica la existencia del NetworkManager e inicia automáticamente la partida en modo Host.
    /// </summary>
    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {// Arranca automáticamente como Host (Servidor + Primer Jugador)
            NetworkManager.Singleton.StartHost();
            Debug.Log("[NetworkAutoStart] Iniciando partida automáticamente como HOST.");
        }
        else
        {
            Debug.LogError("[NetworkAutoStart] No se encontró el NetworkManager en la escena. Asegúrate de haberlo creado.");
        }
    }
}