using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode; 

#if UNITY_EDITOR
using UnityEditor;
#endif

public class MainMenuButtonsHandler : MonoBehaviour
{
    [Header("Map Configs disponibles")]
    [SerializeField] private MapConfig[] availableMaps;

    [Header("UI")]
    [SerializeField] private TMP_Dropdown mapsDropdown;

    private void Start()
    {
        initializeMapDropdown();
    }

    private void OnDestroy()
    {
        if (mapsDropdown != null)
            mapsDropdown.onValueChanged.RemoveListener(onMapDropdownChanged);
    }
    public void OnHostButtonClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[MainMenu] No hay NetworkManager en la escena.");
            return;
        }

        NetworkManager.Singleton.StartHost();
        Debug.Log("Host elegido");
        SceneManager.LoadScene(SceneNames.LobbyScene);
    }

    public void OnClientButtonClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[MainMenu] No hay NetworkManager en la escena.");
            return;
        }

        NetworkManager.Singleton.StartClient();
        SceneManager.LoadScene(SceneNames.LobbyScene);
    }
    /// <summary>
    /// Navega a la escena de selección de personaje si hay mapa seleccionado iniciando el Host.
    /// </summary>
    public void OnButtonPlayClicked()
    {
        if (GameManager.Instance?.SelectedMapConfig == null)
        {
            Debug.LogWarning("[MainMenu] No hay mapa seleccionado.");
            return;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
            Debug.Log("[MainMenu] Host de Netcode iniciado. Cambiando de escena de forma segura...");

            // 🌟 CAMBIO CRÍTICO: Usamos el SceneManager de Netcode en vez del normal de Unity
            NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.CharSelection, LoadSceneMode.Single);
        }
        else
        {
            // Por si acaso el NetworkManager no está, dejamos el método antiguo como salvavidas
            SceneManager.LoadScene(SceneNames.CharSelection);
        }
    }

    public void OnOptionsButtonClicked()
    {
        Debug.Log("Options button pressed");
    }

    public void OnExitButtonClicked()
    {
        Debug.Log("Exit button pressed");
#if UNITY_EDITOR        
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void initializeMapDropdown()
    {
        if (mapsDropdown == null || availableMaps == null || availableMaps.Length == 0)
        {
            Debug.LogWarning("[MainMenu] Dropdown de mapas no configurado.");
            return;
        }

        mapsDropdown.ClearOptions();

        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (MapConfig map in availableMaps)
        {
            options.Add(new TMP_Dropdown.OptionData(map != null ? map.mapName : "Sin nombre"));
        }

        mapsDropdown.AddOptions(options);
        mapsDropdown.value = 0;
        mapsDropdown.RefreshShownValue();
        mapsDropdown.onValueChanged.AddListener(onMapDropdownChanged);

        applySelectedMap(0);
    }

    private void onMapDropdownChanged(int index)
    {
        applySelectedMap(index);
    }

    private void applySelectedMap(int index)
    {
        if (availableMaps == null || index < 0 || index >= availableMaps.Length) return;
        if (GameManager.Instance == null) return;

        GameManager.Instance.SelectedMapConfig = availableMaps[index];
        //Debug.Log($"[MainMenu] Mapa seleccionado: {availableMaps[index].mapName}");
    }
}