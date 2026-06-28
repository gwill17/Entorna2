using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverCanvasHandler : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI jewelsValueText;
    [SerializeField] private TextMeshProUGUI globalJewelsValueText;
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

        if (GameManager.Instance != null)
        {
            GameManager.Instance.ResetGameData();
        }

        if (Unity.Netcode.NetworkManager.Singleton != null)
        {
            Unity.Netcode.NetworkManager.Singleton.Shutdown();
        }

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

        if (globalJewelsValueText != null)
            globalJewelsValueText.text = GameManager.Instance.GetGlobalDiamonds().ToString();

        if (keysValueText != null)
            keysValueText.text = GameManager.Instance.GetKeys().ToString();

        if (enemiesKilledText != null)
            enemiesKilledText.text = GameManager.Instance.EnemiesKilled.ToString();
    }
}
