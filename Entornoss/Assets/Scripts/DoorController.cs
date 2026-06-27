using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(UniqueEntity))]
public class DoorController : NetworkBehaviour
{
    [SerializeField] private Sprite openDoorSprite;

    // VARIABLE QUE FALTABA: Controla si la puerta ya fue procesada localmente
    private bool isOpen = false;

    private Collider2D triggerCollider;
    private Collider2D blockingCollider;
    private SpriteRenderer spriteRenderer;
    private UniqueEntity uniqueEntity;

    public string EntityId => uniqueEntity?.EntityId ?? "UNKNOWN";
    public EntityType EntityType => uniqueEntity?.Type ?? EntityType.Interactive_Door;

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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isOpen) return;

        if (!other.CompareTag("Player")) return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;

        if (!player.IsOwner) return;

        // Marcamos localmente como abierta para evitar doble envío por lag
        isOpen = true;

        // ¡Le pasamos la posición exacta en el mundo!
        player.SolicitarAperturaPuertaServerRpc(transform.position);
    }

    /// <summary>
    /// Abre la puerta visualmente y desactiva la colisión en este ordenador.
    /// </summary>
    public void OpenDoorLocal()
    {
        isOpen = true;

        if (openDoorSprite != null && spriteRenderer != null)
            spriteRenderer.sprite = openDoorSprite;

        if (blockingCollider != null)
            blockingCollider.enabled = false;

        if (triggerCollider != null)
            triggerCollider.enabled = false;
    }

    private void cacheColliders()
    {
        Collider2D[] colliders = GetComponents<Collider2D>();
        foreach (Collider2D col in colliders)
        {
            if (col.isTrigger) triggerCollider = col;
            else blockingCollider = col;
        }
    }
}