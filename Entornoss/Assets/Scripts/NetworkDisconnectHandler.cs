using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class NetworkDisconnectHandler : MonoBehaviour
{
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

    private void HandleClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (ExpectingDeathDisconnect)
            {
                ExpectingDeathDisconnect = false; 
                return; 
            }

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