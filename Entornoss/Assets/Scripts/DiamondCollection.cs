using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(UniqueEntity))]
public class DiamondCollection : NetworkBehaviour
{
    [SerializeField] private string playerTag = "Player";

    private UniqueEntity uniqueEntity;

    public string EntityId => uniqueEntity?.EntityId ?? "UNKNOWN";
    public EntityType EntityType => uniqueEntity?.Type ?? EntityType.Pickup_Diamond;

    /// <summary>
    /// Inicializa la referencia de entidad única y valida el tipo configurado.
    /// </summary>
    private void Awake()
    {
        uniqueEntity = GetComponent<UniqueEntity>();

        if (uniqueEntity != null && uniqueEntity.Type != EntityType.Pickup_Diamond)
        {
            Debug.LogWarning($"[DiamondCollection] {gameObject.name} tiene tipo {uniqueEntity.Type} en lugar de Pickup_Diamond");
        }
    }

    /// <summary>
    /// Detecta la colisión con el jugador e intenta recoger el diamante.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (!collision.CompareTag(playerTag)) return;

        PlayerController player = collision.GetComponent<PlayerController>();
        if (player == null) return;
        if (GameManager.Instance == null) return;

        if (GameManager.Instance.TryAddDiamond(player.OwnerClientId, EntityId))
        {
            //Debug.Log($"[{EntityType}:{EntityId}] recogido por Cliente ID: {player.OwnerClientId}");

            NetworkObject netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true); 
            }
        }
    }
}
