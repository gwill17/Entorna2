using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class NetworkDisconnectHandler : MonoBehaviour
{
    /// <summary>
    /// Bandera global para evitar disparar la lógica de desconexión si esta fue provocada 
    /// intencionadamente por un evento de muerte local.
    /// </summary>
    public static bool ExpectingDeathDisconnect = false;

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
    }
    /// <summary>
    /// Procesa la desconexión. Si el cliente local es el que se desconecta y no fue por muerte,
    /// reinicia los datos del juego y redirige a la escena de derrota.
    /// </summary>
    private void HandleClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;

        // Verifica si el cliente afectado es el cliente local
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            // Si la desconexión es esperada por muerte, se reinicia la bandera y se sale
            if (ExpectingDeathDisconnect)
            {
                ExpectingDeathDisconnect = false; 
                return; 
            }
            // Si es un cliente y el servidor se cierra, el cliente regresa al menú o escena del final.
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.Log("[Cliente] El Host ha cerrado el servidor. Regresando automáticamente al menú principal...");

                if (GameManager.Instance != null)
                {
                    GameManager.Instance.ResetGameData();
                }

                SceneManager.LoadScene(SceneNames.DeadScene);
            }
        }
    }
}