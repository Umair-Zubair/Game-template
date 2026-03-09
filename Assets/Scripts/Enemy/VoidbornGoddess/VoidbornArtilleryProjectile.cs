using UnityEngine;

/// <summary>
/// Custom artillery projectile for the Voidborn Goddess boss.
/// Lifecycle:
///   STATE 1 — FORMING : Appears above the battlefield, stays briefly suspended while forming.
///   STATE 2 — FALLING : After a short delay, falls straight down toward the player's captured X position.
///   STATE 3 — IMPACT  : On collision with the player, deals damage and is destroyed.
///
/// Uses simple downward velocity (no physics). Consistent with the existing projectile style.
/// </summary>
public class VoidbornArtilleryProjectile : MonoBehaviour
{
    [Header("Projectile Settings")]
    [Tooltip("How long the projectile stays suspended while forming")]
    [SerializeField] private float formDuration = 0.8f;

    [Tooltip("Downward speed once the projectile starts falling")]
    [SerializeField] private float fallSpeed = 12f;

    [Tooltip("Maximum time alive before auto-destroy")]
    [SerializeField] private float lifetime = 6f;

    [Header("Visuals")]
    [Tooltip("Drag a custom sprite here. Leave empty for a placeholder box.")]
    [SerializeField] private Sprite projectileSprite;

    // --- Runtime state ---
    private enum ProjectileState { Forming, Falling, Exploding }
    private ProjectileState currentState;
    private float stateTimer;
    private float lifeTimer;
    private bool hasHit;
    private int damage = 2;

    private SpriteRenderer spriteRenderer;
    private Animator anim;
    private BoxCollider2D boxCollider;

    private void Awake()
    {
        SetupComponents();
    }

    /// <summary>
    /// Creates or retrieves the required components (SpriteRenderer, Collider, Rigidbody2D).
    /// Follows the same pattern as the existing ArtilleryProjectile.
    /// </summary>
    private void SetupComponents()
    {
        // SpriteRenderer
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        spriteRenderer.sortingOrder = 10;

        // Animator — used for spawn/idle/explosion animations
        anim = GetComponent<Animator>();

        // BoxCollider2D — trigger-based, same as ArtilleryProjectile
        boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null)
            boxCollider = gameObject.AddComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;
        boxCollider.size = new Vector2(1f, 1f);

        // Rigidbody2D — kinematic, we control movement manually
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.isKinematic = true;

        // Use the EnemyProjectile layer for proper collision filtering
        int layer = LayerMask.NameToLayer("EnemyProjectile");
        gameObject.layer = layer != -1 ? layer : LayerMask.NameToLayer("Default");
    }

    /// <summary>
    /// Initializes and activates the projectile at the given spawn position.
    /// Called by VoidbornGoddessController.SpawnArtilleryProjectile().
    /// </summary>
    /// <param name="spawnPosition">World position where the projectile appears.</param>
    /// <param name="damageAmount">Damage dealt on player contact.</param>
    public void Initialize(Vector3 spawnPosition, int damageAmount)
    {
        transform.position = spawnPosition;
        damage = damageAmount;
        currentState = ProjectileState.Forming;
        stateTimer = 0f;
        lifeTimer = 0f;
        hasHit = false;

        // Apply sprite (custom or placeholder)
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = projectileSprite != null ? projectileSprite : CreatePlaceholderSprite();
        }

        gameObject.SetActive(true);
    }

    private void Update()
    {
        switch (currentState)
        {
            // STATE 1 — FORMING: stays suspended briefly
            case ProjectileState.Forming:
                lifeTimer += Time.deltaTime;
                stateTimer += Time.deltaTime;

                if (stateTimer >= formDuration)
                {
                    currentState = ProjectileState.Falling;
                }
                break;

            // STATE 2 — FALLING: simple downward velocity
            case ProjectileState.Falling:
                lifeTimer += Time.deltaTime;
                if (lifeTimer > lifetime)
                {
                    Destroy(gameObject);
                    return;
                }
                transform.position += Vector3.down * fallSpeed * Time.deltaTime;
                break;

            // STATE 3 — EXPLODING: wait for the HandExplosion animation to finish, then destroy
            case ProjectileState.Exploding:
                if (anim != null)
                {
                    AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
                    if (info.IsName("HandExplosion") && info.normalizedTime >= 0.95f)
                    {
                        Destroy(gameObject);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// IMPACT: deals damage on player contact, plays explosion on any valid collision.
    /// After the explosion animation finishes, the projectile is destroyed.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (hasHit) return;

        // Player hit — deal damage then explode
        if (collision.CompareTag("Player"))
        {
            Health playerHealth = collision.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
                Debug.Log($"[VoidbornArtillery] Hit player for {damage} damage!");
            }
            hasHit = true;
            PlayExplosion();
            return;
        }

        // Ground hit — explode without damage
        if (collision.CompareTag("Ground") || collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            hasHit = true;
            Debug.Log("[VoidbornArtillery] Hit ground.");
            PlayExplosion();
        }
    }

    /// <summary>
    /// Stops movement, disables the collider, and triggers the "explode" animation.
    /// The projectile is destroyed once HandExplosion finishes (checked in Update).
    /// </summary>
    private void PlayExplosion()
    {
        currentState = ProjectileState.Exploding;

        // Disable collider so it can't hit anything else during the explosion
        if (boxCollider != null)
            boxCollider.enabled = false;

        // Trigger the explosion animation
        if (anim != null)
        {
            anim.SetTrigger("explode");
        }
        else
        {
            // No Animator — fall back to immediate destroy
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Creates a simple 32×32 white box sprite as a placeholder.
    /// Same approach used by the existing ArtilleryProjectile.
    /// </summary>
    private Sprite CreatePlaceholderSprite()
    {
        Texture2D tex = new Texture2D(32, 32);
        Color[] pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
    }
}
