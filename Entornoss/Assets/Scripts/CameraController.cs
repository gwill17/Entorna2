using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10);
    private Transform target;

    private void OnEnable()
    {
        GameEvents.OnLocalPlayerRegistered += handlePlayerRegistered;
    }

    private void OnDisable()
    {
        GameEvents.OnLocalPlayerRegistered -= handlePlayerRegistered;
    }

    private void Start()
    {
        tryFindLocalPlayer();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            tryFindLocalPlayer();
            return;
        }

        transform.position = target.position + offset;
    }

    private void handlePlayerRegistered(PlayerController player)
    {
        if (player == null) return;

        target = player.transform;
        transform.position = target.position + offset;

        Debug.Log("[CameraController] Siguiendo jugador local: " + player.name);
    }

    private void tryFindLocalPlayer()
    {
        if (GameManager.Instance == null) return;
        if (GameManager.Instance.LocalPlayerTransform == null) return;

        target = GameManager.Instance.LocalPlayerTransform;
        transform.position = target.position + offset;

        Debug.Log("[CameraController] Jugador local encontrado por fallback.");
    }
}