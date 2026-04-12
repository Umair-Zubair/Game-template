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
    private Rigidbody2D rb;

    private int playerLayerMask;
    private int groundLayerMask;
    private Vector2 hitDetectionSize;

    private void Awake()
    {
        SetupComponents();
    }

    private void SetupComponents()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        spriteRenderer.sortingOrder = 10;

        anim = GetComponent<Animator>();

        bool colliderCreated = false;
        boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null)
        {
            boxCollider = gameObject.AddComponent<BoxCollider2D>();
            colliderCreated = true;
        }
        boxCollider.isTrigger = true;
        if (colliderCreated)
            boxCollider.size = new Vector2(0.3f, 0.3f);

        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        playerLayerMask = LayerMask.GetMask("Player");
        groundLayerMask = LayerMask.GetMask("Ground");

        float scale = Mathf.Abs(transform.localScale.x);
        float colliderWidth = boxCollider.size.x * scale;
        float visualHeight = spriteRenderer.bounds.size.y;
        hitDetectionSize = new Vector2(colliderWidth, Mathf.Max(visualHeight, boxCollider.size.y * scale));
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

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        if (boxCollider != null)
            boxCollider.enabled = true;

        if (spriteRenderer != null)
            spriteRenderer.sprite = projectileSprite != null ? projectileSprite : CreatePlaceholderSprite();

        gameObject.SetActive(true);
    }

    private void Update()
    {
        lifeTimer += Time.deltaTime;

        switch (currentState)
        {
            case ProjectileState.Forming:
                stateTimer += Time.deltaTime;
                if (stateTimer >= formDuration)
                {
                    currentState = ProjectileState.Falling;
                    if (rb != null)
                        rb.linearVelocity = Vector2.down * fallSpeed;
                }
                break;

            case ProjectileState.Falling:
                if (lifeTimer > lifetime)
                {
                    Destroy(gameObject);
                    return;
                }
                if (!hasHit)
                    CheckForHit();
                break;

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

    private void CheckForHit()
    {
        Vector2 center = transform.position;

        Collider2D playerHit = Physics2D.OverlapBox(center, hitDetectionSize, 0f, playerLayerMask);
        if (playerHit != null)
        {
            Health playerHealth = playerHit.GetComponent<Health>();
            if (playerHealth != null)
                playerHealth.TakeDamage(damage);
            hasHit = true;
            PlayExplosion();
            return;
        }

        Collider2D groundHit = Physics2D.OverlapBox(center, hitDetectionSize, 0f, groundLayerMask);
        if (groundHit != null)
        {
            hasHit = true;
            PlayExplosion();
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (hasHit) return;

        if (collision.CompareTag("Player"))
        {
            Health playerHealth = collision.GetComponent<Health>();
            if (playerHealth != null)
                playerHealth.TakeDamage(damage);
            hasHit = true;
            PlayExplosion();
            return;
        }

        if (collision.CompareTag("Ground") || collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            hasHit = true;
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

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        if (boxCollider != null)
            boxCollider.enabled = false;

        if (anim != null)
        {
            anim.SetTrigger("explode");
        }
        else
        {
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
