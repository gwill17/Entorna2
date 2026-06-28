using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Controla la lógica del jugador en el entorno multijugador, incluyendo el movimiento, 
/// el sistema de combate, la sincronización de red y la gestión de estadísticas.
/// </summary>
public class PlayerController : CharController
{
    #region Variables y Propiedades
    [Header("Configuración de Combate")]
    protected int damageToEnemy;
    protected float attackCooldown;

    [Header("Configuración de Personajes")]
    [SerializeField] private PlayerStats[] availableCharacters;
    [SerializeField] private GameObject gameOverCanvasPrefab;

    private PlayerControls controls;

    // Variables de Red (Sincronización)
    private NetworkVariable<int> healthNet = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> isAttackingNet = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> selectedCharacterIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsAttacking => isAttackingNet.Value;
    public int DamageToEnemy => damageToEnemy;
    #endregion

    #region Ciclo de Vida (Unity y Netcode)
    /// <summary>
    /// Inicializa controles de entrada y registra el jugador local.
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
        controls = new PlayerControls();

        controls.Player.Move.performed += ctx => {
            if (IsOwner) movement = ctx.ReadValue<Vector2>();
        };
        controls.Player.Move.canceled += _ => {
            if (IsOwner) movement = Vector2.zero;
        };
        controls.Player.Attack.performed += onAttack;
    }

    /// <summary>
    /// Inicializa estado del jugador y notifica valores iniciales al HUD.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        healthNet.OnValueChanged += OnHealthNetChanged;

        if (IsServer) healthNet.Value = health;

        if (IsOwner) GameEvents.HealthChanged(healthNet.Value);

        selectedCharacterIndex.OnValueChanged += OnCharacterIndexChanged;

        // Si somos el dueño, notificamos al servidor nuestro personaje
        if (IsOwner && GameManager.Instance.SelectedCharacterIndex >= 0)
        {
            SetCharacterServerRpc(GameManager.Instance.SelectedCharacterIndex);
        }

        if (selectedCharacterIndex.Value >= 0) ApplyCharacterByIndex(selectedCharacterIndex.Value);

        base.OnNetworkSpawn();

        if (IsOwner)
        {
            UniqueEntity uniqueEntity = GetComponent<UniqueEntity>();
            if (GameManager.Instance != null) GameManager.Instance.RegisterLocalPlayer(this, uniqueEntity);

            GameEvents.HealthChanged(health);
            GameEvents.KeysChanged();
            GameEvents.DiamondsChanged();
        }
    }

    protected override void Start() => base.Start();

    /// <summary>
    /// Actualiza animación, orientación y estado de vida en cada frame.
    /// </summary>
    protected override void Update()
    {
        if (isDead) return;
        if (!IsOwner) return;

        animator.SetFloat("speed", movement.sqrMagnitude);

        if (movement.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(movement.y, movement.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
        }

        if (IsServer && selectedCharacterIndex.Value >= 0) checkDeath();
    }
    #endregion

    #region Input y Controles
    private void OnEnable()
    {
        if (controls == null) return;
        controls.Enable();
        controls.Player.Attack.performed += onAttack;
    }

    private void OnDisable()
    {
        if (controls == null) return;
        controls.Player.Attack.performed -= onAttack;
        controls.Disable();
    }
    #endregion

    #region Lógica de Combate y Muerte
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

    /// <summary>
    /// Finaliza el estado de ataque del jugador.
    /// </summary>
    private void endAttack()
    {
        if (!IsOwner) return;
        SetAttackingServerRpc(false);
    }

    /// <summary>
    /// Aplica daño al jugador y notifica el cambio de salud al HUD.
    /// </summary>
    public override void TakeDamage(int amount, Vector2 knockbackDir)
    {
        if (!IsServer) return;

        base.TakeDamage(amount, knockbackDir);
        healthNet.Value = health;

        if (health <= 0 && !isDead) Die();
    }

    /// <summary>
    /// Gestiona la muerte del jugador de manera autoritativa.
    /// </summary>
    public override void Die()
    {
        if (isDead) return;
        base.Die();

        if (IsServer && GameManager.Instance != null) GameEvents.PlayerDied(OwnerClientId);
    }

    /// <summary>
    /// Verifica si la salud ha llegado a cero y ejecuta la muerte.
    /// </summary>
    private void checkDeath()
    {
        if (health <= 0 && !isDead && selectedCharacterIndex.Value >= 0) Die();
    }
    #endregion

    #region Gestión de Estadísticas y Personajes
    /// <summary>
    /// Aplica un conjunto de estadísticas de personaje y recarga sus valores activos.
    /// </summary>
    public void ApplyCharacterStats(PlayerStats newStats)
    {
        if (newStats != null)
        {
            stats = newStats;
            LoadStats();
        }
    }

    /// <summary>
    /// Carga estadísticas del personaje seleccionado y aplica valores de combate y movimiento.
    /// </summary>
    protected override void LoadStats()
    {
        base.LoadStats();
        PlayerStats playerStats = stats as PlayerStats;

        if (playerStats != null)
        {
            moveSpeed *= playerStats.speedBonus;
            damageToEnemy = playerStats.attackDamage;
            attackCooldown = playerStats.attackCooldown;
        }
        else
        {
            damageToEnemy = 50;
            attackCooldown = 0.5f;
            moveSpeed *= 1.25f;
        }
    }

    private void OnCharacterIndexChanged(int oldValue, int newValue) => ApplyCharacterByIndex(newValue);

    private void ApplyCharacterByIndex(int index)
    {
        if (index < 0 || index >= availableCharacters.Length) return;
        ApplyCharacterStats(availableCharacters[index]);
    }
    #endregion

    #region Sincronización y RPCs
    /// <summary>
    /// Callback disparado cuando el valor de red 'healthNet' cambia. 
    /// Sincroniza la salud local y, si llega a cero, gestiona la muerte del propietario.
    /// </summary>
    private void OnHealthNetChanged(int oldValue, int newValue)
    {
        health = newValue;

        if (IsOwner)
        {
            GameEvents.HealthChanged(newValue);
            if (newValue <= 0) HandleLocalDeath();
        }
    }

    /// <summary>
    /// Ejecuta la lógica de final de partida local. Si es el servidor, desactiva los componentes del jugador.
    /// Si es un cliente, desconecta de la red y carga la escena de derrota.
    /// </summary>
    private void HandleLocalDeath()
    {
        if (IsServer)
        {
            if (controls != null) controls.Player.Disable();
            if (rb != null) rb.linearVelocity = Vector2.zero;
            if (characterCollider != null) characterCollider.enabled = false;

            foreach (var sr in GetComponentsInChildren<SpriteRenderer>()) sr.enabled = false;

            if (gameOverCanvasPrefab != null) Instantiate(gameOverCanvasPrefab);
        }
        else
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkDisconnectHandler.ExpectingDeathDisconnect = true;
                NetworkManager.Singleton.Shutdown();
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.DeadScene);
        }
    }

    /// <summary>
    /// Solicita al servidor que actualice el índice de personaje seleccionado para este jugador.
    /// </summary>
    [ServerRpc]
    private void SetCharacterServerRpc(int characterIndex)
    {
        if (characterIndex < 0 || characterIndex >= availableCharacters.Length) return;
        selectedCharacterIndex.Value = characterIndex;
    }

    /// <summary>
    /// Envía la solicitud de ataque al servidor para que este autorice la ejecución en todos los clientes.
    /// </summary>
    [ServerRpc]
    private void PlayAttackServerRpc() => PlayAttackClientRpc();

    /// <summary>
    /// Ejecuta la animación de ataque en todos los clientes conectados.
    /// </summary>
    [ClientRpc]
    private void PlayAttackClientRpc()
    {
        animator.ResetTrigger("Attack");
        animator.SetTrigger("Attack");
    }

    /// <summary>
    /// Sincroniza el estado de ataque (atacando/no atacando) mediante una NetworkVariable en el servidor.
    /// </summary>
    [ServerRpc]
    private void SetAttackingServerRpc(bool value) => isAttackingNet.Value = value;

    /// <summary>
    /// Permite a cualquier cliente solicitar al servidor la apertura de una puerta.
    /// El servidor validará si el jugador tiene llaves suficientes antes de ejecutar la acción.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SolicitarAperturaPuertaServerRpc(Vector3 doorPosition, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        GameManager.Instance.TryOpenDoor(clientId, doorPosition);
    }
    #endregion
}