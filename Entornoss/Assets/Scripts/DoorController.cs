using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(UniqueEntity))]
public class DoorController : NetworkBehaviour
{
    [SerializeField] private Sprite openDoorSprite;

    private bool isOpen = false;
    private Collider2D triggerCollider;
    private Collider2D blockingCollider;
    private SpriteRenderer spriteRenderer;
    private UniqueEntity uniqueEntity;

    public string EntityId => uniqueEntity?.EntityId ?? "UNKNOWN";
    public EntityType EntityType => uniqueEntity?.Type ?? EntityType.Interactive_Door;

    /// <summary>
    /// Inicializa componentes de la puerta y valida la configuración de entidad.
    /// </summary>
    private void Awake()
    {
        uniqueEntity = GetComponent<UniqueEntity>();

        if (uniqueEntity != null && uniqueEntity.Type != EntityType.Interactive_Door)
        {
            Debug.LogWarning($"[DoorController] {gameObject.name} tiene tipo {uniqueEntity.Type} en lugar de Interactive_Door");
        }

        spriteRenderer = GetComponent<SpriteRenderer>();
        cacheColliders();
    }

    /// <summary>
    /// Gestiona la interacción de apertura cuando entra un jugador en el trigger.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // OJO: Quitamos el "if (!IsServer) return;" porque queremos que el CLIENTE 
        // que toca la puerta localmente también pueda iniciar el proceso.

        if (isOpen || !other.CompareTag("Player")) return;
        if (!other.TryGetComponent(out PlayerController player)) return;

        // Solo el dueño local de ese personaje debe activar la puerta
        if (!player.IsOwner) return;

        // El jugador le pide al servidor que intente abrir la puerta
        player.SolicitarAperturaPuertaServerRpc(EntityId);
    }

    /// <summary>
    /// Abre la puerta visualmente y desactiva la colisión bloqueante.
    /// </summary>
    /// 
    public void OpenDoorLocal()
    {
        isOpen = true;

        if (openDoorSprite != null && spriteRenderer != null)
            spriteRenderer.sprite = openDoorSprite;

        if (blockingCollider != null)
            blockingCollider.enabled = false;
    }
    public void OpenDoor(PlayerController player)
    {
        isOpen = true;

        Debug.Log($"[{EntityType}:{EntityId}] opened by [Player:{player.EntityId}]");

        if (openDoorSprite != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = openDoorSprite;
        }

        if (blockingCollider != null)
        {
            blockingCollider.enabled = false;
        }
    }

    /// <summary>
    /// Localiza y almacena los colliders de trigger y bloqueo de la puerta.
    /// </summary>
    private void cacheColliders()
    {
        Collider2D[] colliders = GetComponents<Collider2D>();
        foreach (Collider2D col in colliders)
        {
            if (col.isTrigger)
                triggerCollider = col;
            else
                blockingCollider = col;
        }
    }
}