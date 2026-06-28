using UnityEngine;
using Unity.Netcode;

public class NetworkAutoStart : MonoBehaviour
{
    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
            Debug.Log("[NetworkAutoStart] Iniciando partida automáticamente como HOST.");
        }
        else
        {
            Debug.LogError("[NetworkAutoStart] No se encontró el NetworkManager en la escena. Asegúrate de haberlo creado.");
        }
    }
}