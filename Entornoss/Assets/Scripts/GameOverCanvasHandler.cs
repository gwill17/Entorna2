using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverCanvasHandler : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI jewelsValueText;
    [SerializeField] private TextMeshProUGUI keysValueText;
    [SerializeField] private TextMeshProUGUI enemiesKilledText;

    /// <summary>
    /// Inicializa la pantalla mostrando las estadísticas finales de la partida.
    /// </summary>
    private void Start()
    {
        displayGameStats();
    }

    /// <summary>
    /// Carga el menú principal al pulsar el botón de volver.
    /// </summary>
    public void OnBackButtonClicked()
    {
        Debug.Log("[UI Game Over] Limpiando datos de juego y regresando al menú principal.");

        // 1. Forzamos al GameManager a resetear todas sus variables locales e inventarios a 0
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ResetGameData();
        }

        // 2. Apagamos el sistema de red de Netcode de forma limpia para que la siguiente partida empiece desde cero
        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            Unity.Netcode.NetworkManager.Singleton.Shutdown();
        }

        // 3. Cambiamos a la escena del menú principal
        SceneManager.LoadScene(SceneNames.MainMenu);
    }

    /// <summary>
    /// Actualiza los textos del panel con diamantes, llaves y enemigos eliminados.
    /// </summary>
    private void displayGameStats()
    {
        if (GameManager.Instance == null) return;

        if (jewelsValueText != null)
            jewelsValueText.text = GameManager.Instance.GetDiamonds().ToString();

        if (keysValueText != null)
            keysValueText.text = GameManager.Instance.GetKeys().ToString();

        if (enemiesKilledText != null)
            enemiesKilledText.text = GameManager.Instance.EnemiesKilled.ToString();
    }
}
