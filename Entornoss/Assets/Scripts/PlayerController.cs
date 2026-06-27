using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Collections;

public class PlayerController : CharController
{
    protected int damageToEnemy;
    protected float attackCooldown;

    private PlayerControls controls;

    private NetworkVariable<int> healthNet = new NetworkVariable<int>(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
    private NetworkVariable<bool> isAttackingNet = new NetworkVariable<bool>(
    false,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);

    public bool IsAttacking => isAttackingNet.Value;
    public int DamageToEnemy => damageToEnemy;

    [SerializeField] private PlayerStats[] availableCharacters;

    private NetworkVariable<int> selectedCharacterIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server

    );

    /// <summary>
    /// Inicializa controles de entrada y registra el jugador local en el gestor global.
    /// </summary>
    protected override void Awake()
    {
        // Se asume que CharController ya implementa la inicialización base.
        base.Awake();
        controls = new PlayerControls();

        // Vinculamos las acciones del nuevo Input System
        controls.Player.Move.performed += ctx => {
            if (IsOwner) movement = ctx.ReadValue<Vector2>();
        };
        controls.Player.Move.canceled += _ => {
            if (IsOwner) movement = Vector2.zero;
        };
        controls.Player.Attack.performed += onAttack;

        // Ocultar hasta que LevelGenerator lo reposicione
        //gameObject.SetActive(false);
    }

    /// <summary>
    /// Inicializa estado del jugador y notifica los valores iniciales al HUD.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        healthNet.OnValueChanged += OnHealthNetChanged;

        if (IsServer)
        {
            healthNet.Value = health;
        }
        if (IsOwner)
        {
            GameEvents.HealthChanged(healthNet.Value);
        }
        //Debug.Log($"[PlayerController] Spawn. IsOwner={IsOwner}, OwnerClientId={OwnerClientId}, LocalClientId={NetworkManager.Singleton.LocalClientId}");
        selectedCharacterIndex.OnValueChanged += OnCharacterIndexChanged;

        if (IsOwner && GameManager.Instance.SelectedCharacterIndex >= 0)
        {
            SetCharacterServerRpc(GameManager.Instance.SelectedCharacterIndex);
        }

        if (selectedCharacterIndex.Value >= 0)
        {
            ApplyCharacterByIndex(selectedCharacterIndex.Value);
        }
        base.OnNetworkSpawn();

        // Únicamente el cliente dueño de este personaje debe inicializar el HUD y registrarse
        if (IsOwner)
        {
            UniqueEntity uniqueEntity = GetComponent<UniqueEntity>();

//Debug.Log("SOY EL OWNER: " + OwnerClientId);

            if (GameManager.Instance != null)
            {
                GameManager.Instance.RegisterLocalPlayer(this, uniqueEntity);
                //Debug.Log("[PlayerController] Jugador local registrado: " + gameObject.name);
                //Debug.Log("LOCAL PLAYER REGISTRADO");

            }

            GameEvents.HealthChanged(health);
            GameEvents.KeysChanged();
            GameEvents.DiamondsChanged();
        }

        //IsAttacking = false;

    }
    private void OnHealthNetChanged(int oldValue, int newValue)
    {
        health = newValue;

        Debug.Log($"[HEALTH] {newValue}");

        if (IsOwner)
        {
            GameEvents.HealthChanged(newValue);
        }
    }
    [ServerRpc]
    private void SetCharacterServerRpc(int characterIndex)
    {
        if (characterIndex < 0 || characterIndex >= availableCharacters.Length)
            return;

        selectedCharacterIndex.Value = characterIndex;
    }

    private void OnCharacterIndexChanged(int oldValue, int newValue)
    {
        ApplyCharacterByIndex(newValue);
    }

    private void ApplyCharacterByIndex(int index)
    {
        if (index < 0 || index >= availableCharacters.Length)
            return;

        ApplyCharacterStats(availableCharacters[index]);

        //Debug.Log($"[PlayerController] Aplicado personaje de índice {index} en {gameObject.name}");
    }
    /// <summary>
    /// Actualiza animación, orientación y estado de vida en cada frame.
    /// </summary>
    protected override void Update()
    {
        if (isDead) return;
        if (IsOwner && movement != Vector2.zero)
        {
            //Debug.Log($"MOVIMIENTO: {movement}");
        }
        // Solo el dueño del objeto calcula su movimiento, rotación y comprueba su muerte de forma activa
        if (!IsOwner) return;

        animator.SetFloat("speed", movement.sqrMagnitude);

        if (movement.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(movement.y, movement.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        }
        if (IsServer && selectedCharacterIndex.Value >= 0)
        {
            checkDeath();
        }
    }

    /// <summary>
    /// Activa el mapa de controles y suscribe la acción de ataque.
    /// </summary>
    private void OnEnable()
    {
        if (controls == null) return;

        controls.Enable();

        // Asignamos la acción de ataque controlando que solo responda el dueño local
        controls.Player.Attack.performed += onAttack; ;
    }

    /// <summary>
    /// Desuscribe las acciones de entrada de forma segura.
    /// </summary>
    private void OnDisable()
    {
        if (controls == null) return;

        controls.Player.Attack.performed -= onAttack;
        controls.Disable();
    }

    protected override void Start()
    {
        base.Start();
    }

    /// <summary>
    /// Gestiona la muerte del jugador de manera autoritativa.
    /// </summary>
    public override void Die()
    {
        if (isDead) return;

        base.Die();

        if (IsServer)
        {
            ShowDeadSceneClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { OwnerClientId }
                }
            });
        }
    }
    /// <summary>
    /// Aplica daño al jugador y notifica el cambio de salud al HUD.
    /// </summary>
    [ClientRpc]
    private void ShowDeadSceneClientRpc(ClientRpcParams clientRpcParams = default)
    {
        GameEvents.PlayerDied();

        UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.DeadScene);
    }
    public override void TakeDamage(int amount, Vector2 knockbackDir)
    {
        if (!IsServer) return;

        base.TakeDamage(amount, knockbackDir);

        healthNet.Value = health;

        if (health <= 0 && !isDead)
        {
            Die();
        }
    }

    /// <summary>
    /// Aplica un conjunto de estadísticas de personaje y recarga sus valores activos.
    /// </summary>
    public void ApplyCharacterStats(PlayerStats newStats)
    {
        // Casting del campo heredado
        if (newStats != null)
        {
            stats = newStats;
            // Forzamos la recarga limpia para evitar mutaciones exponenciales de moveSpeed
            LoadStats();
        }
    }

    /// <summary>
    /// Carga estadísticas del personaje seleccionado y aplica valores de combate y movimiento.
    /// </summary>
    protected override void LoadStats()
    {
        

        base.LoadStats();

        //  Haz casting del campo heredado
        PlayerStats playerStats = stats as PlayerStats;

        if (playerStats != null)
        {
            // Aplica el bonus de velocidad del jugador
            moveSpeed *= playerStats.speedBonus;
            
            // Carga stats específicas del jugador
            damageToEnemy = playerStats.attackDamage;
            attackCooldown = playerStats.attackCooldown;
        }
        else
        {
            // Valores por defecto si no hay PlayerStats
            //Debug.LogWarning($"[{gameObject.name}] No tiene PlayerStats asignado. Usando valores por defecto.");
            damageToEnemy = 50;
            attackCooldown = 0.5f;
            moveSpeed *= 1.25f; // Bonus por defecto
        }
    }

    /// <summary>
    /// Verifica si la salud ha llegado a cero y ejecuta la muerte una sola vez.
    /// </summary>
    private void checkDeath()
    {
        if (health <= 0 && !isDead && selectedCharacterIndex.Value >= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Inicia la animación de ataque y programa su final según el cooldown.
    /// </summary>
    private void onAttack(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        PlayAttackServerRpc();
        SetAttackingServerRpc(true);

        Invoke(nameof(endAttack), attackCooldown);
    }
    [ServerRpc]
    private void PlayAttackServerRpc()
    {
        PlayAttackClientRpc();
    }

    [ClientRpc]
    private void PlayAttackClientRpc()
    {
        animator.ResetTrigger("Attack");
        animator.SetTrigger("Attack");
    }


    /// <summary>
    /// Finaliza el estado de ataque del jugador.
    /// </summary>
    private void endAttack()
    {
        if (!IsOwner) return;

        SetAttackingServerRpc(false);
    }
    [ServerRpc]
    private void SetAttackingServerRpc(bool value)
    {
        isAttackingNet.Value = value;
    }
    [ServerRpc]
    public void SolicitarAperturaPuertaServerRpc(Vector3 doorPosition, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // Le pasamos la posición al GameManager
        if (GameManager.Instance.TryOpenDoor(clientId, doorPosition))
        {
            Debug.Log($"[Server] Servidor autorizó la puerta en la posición {doorPosition}");
        }
        else
        {
            Debug.LogWarning($"[Server] Cliente {clientId} intentó abrir la puerta pero no tiene llaves.");
        }
    }
}
