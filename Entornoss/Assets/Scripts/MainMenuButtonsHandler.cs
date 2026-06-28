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

    /// <summary>
    /// Inicializa el menú desplegable de mapas al arrancar y bloquea la interacción 
    /// si el cliente no tiene permisos de servidor.
    /// </summary>
    private void Start()
    {
        initializeMapDropdown();

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer && NetworkManager.Singleton.IsClient)
        {
            if (mapsDropdown != null) mapsDropdown.interactable = false;
        }
    }
    /// <summary>
    /// Limpia los listeners de la interfaz para evitar fugas de memoria al destruir el objeto.
    /// </summary>
    private void OnDestroy()
    {
        if (mapsDropdown != null)
            mapsDropdown.onValueChanged.RemoveListener(onMapDropdownChanged);
    }
    /// <summary>
    /// Inicia la partida como Host, guarda la configuración del mapa y carga el Lobby.
    /// </summary>
    public void OnHostButtonClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[MainMenu] No hay NetworkManager en la escena.");
            return;
        }

        if (mapsDropdown != null)
        {
            GameManager.Instance.SetMapIndexByHost(mapsDropdown.value);
        }

        NetworkManager.Singleton.StartHost();
        Debug.Log("Host elegido");
        SceneManager.LoadScene(SceneNames.LobbyScene);
    }

    /// <summary>
    /// Inicia la partida como Cliente, conectándose al Host y cargando el Lobby.
    /// </summary>
    public void OnClientButtonClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[MainMenu] No hay NetworkManager en la escena.");
            return;
        }

        GameManager.Instance.SelectedMapConfig = null;

        NetworkManager.Singleton.StartClient();
        SceneManager.LoadScene(SceneNames.LobbyScene);
    }
    /// <summary>
    /// Navega a la escena de selección de personaje si hay mapa seleccionado iniciando el Host.
    /// </summary>
    public void OnButtonPlayClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[MainMenu] No hay NetworkManager en la escena.");
            return;
        }

        if (mapsDropdown != null)
        {
            GameManager.Instance.SetMapIndexByHost(mapsDropdown.value);
        }

        if (GameManager.Instance?.SelectedMapConfig == null)
        {
            Debug.LogWarning("[MainMenu] No hay mapa seleccionado.");
            return;
        }

        NetworkManager.Singleton.StartHost();
        Debug.Log("[MainMenu] Host de Netcode iniciado. Cambiando de escena de forma segura...");

        NetworkManager.Singleton.SceneManager.LoadScene(SceneNames.CharSelection, LoadSceneMode.Single);
    }

    /// <summary>
    /// Placeholder para la lógica del menú de opciones.
    /// </summary>
    public void OnOptionsButtonClicked()
    {
        Debug.Log("Options button pressed");
    }
    /// <summary>
    /// Cierra la aplicación de forma segura.
    /// </summary>
    public void OnExitButtonClicked()
    {
        Debug.Log("Exit button pressed");
#if UNITY_EDITOR        
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    /// <summary>
    /// Rellena el componente Dropdown con las configuraciones de mapa disponibles en el scriptable object.
    /// </summary>
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
    /// <summary>
    /// Actualiza la configuración global del mapa en el GameManager según el índice seleccionado.
    /// </summary>
    private void applySelectedMap(int index)
    {
        if (availableMaps == null || index < 0 || index >= availableMaps.Length) return;
        if (GameManager.Instance == null) return;

        GameManager.Instance.SelectedMapConfig = availableMaps[index];
        //Debug.Log($"[MainMenu] Mapa seleccionado: {availableMaps[index].mapName}");
    }
}