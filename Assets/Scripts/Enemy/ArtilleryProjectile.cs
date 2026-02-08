using UnityEngine;

/// <summary>
/// Artillery projectile that falls from above and damages the player on contact.
/// Creates a simple box visual as placeholder until proper sprites are added.
/// </summary>
public class ArtilleryProjectile : MonoBehaviour
{
    [Header("Sprite")]
    [Tooltip("Drag your artillery sprite here. Leave empty to use placeholder box.")]
    [SerializeField] private Sprite artillerySprite;
    [Tooltip("Drag your warning indicator sprite here. Leave empty to use placeholder box.")]
    [SerializeField] private Sprite warningSprite;

    [Header("Settings")]
    [SerializeField] private float fallSpeed = 15f;
    [SerializeField] private float damage = 1f;  // 1 heart of damage
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private Color spriteColor = Color.white;

    [Header("Warning Indicator")]
    [SerializeField] private bool showWarning = true;
    [SerializeField] private float warningDuration = 0.5f;
    [SerializeField] private Color warningColor = new Color(1f, 0f, 0f, 0.3f);

    private float lifeTimer;
    private bool hasHit;
    private bool isFalling;
    private float warningTimer;
    private Vector3 targetPosition;

    private SpriteRenderer spriteRenderer;
    private BoxCollider2D boxCollider;
    private SpriteRenderer warningIndicator;
    private GameObject warningObject;

    private void Awake()
    {
        SetupComponents();
    }

    /// <summary>
    /// Sets up the visual components
    /// </summary>
    private void SetupComponents()
    {
        // Create or get SpriteRenderer for the projectile
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        // Use custom sprite if provided, otherwise create placeholder box
        spriteRenderer.sprite = artillerySprite != null ? artillerySprite : CreateBoxSprite();
        spriteRenderer.color = spriteColor;
        spriteRenderer.sortingOrder = 10;

        // Setup collider
        boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null)
        {
            boxCollider = gameObject.AddComponent<BoxCollider2D>();
        }
        boxCollider.isTrigger = true;
        boxCollider.size = new Vector2(1f, 1f);

        // Setup Rigidbody2D for physics
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }
        rb.gravityScale = 0; // We control movement manually
        rb.isKinematic = true;

        // Set layer for proper collision
        gameObject.layer = LayerMask.NameToLayer("EnemyProjectile");
        if (gameObject.layer == -1)
        {
            gameObject.layer = LayerMask.NameToLayer("Default");
        }
    }

    /// <summary>
    /// Creates a simple white box sprite (placeholder)
    /// </summary>
    private Sprite CreateBoxSprite()
    {
        // Create a 32x32 white texture
        Texture2D texture = new Texture2D(32, 32);
        Color[] pixels = new Color[32 * 32];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.white;
        }

        texture.SetPixels(pixels);
        texture.Apply();

        // Create sprite from texture (32 pixels per unit for a 1x1 unit box)
        return Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
    }

    /// <summary>
    /// Initialize and activate the artillery projectile
    /// </summary>
    public void Initialize(Vector3 spawnPosition, Vector3 groundTarget)
    {
        transform.position = spawnPosition;
        targetPosition = groundTarget;
        lifeTimer = 0f;
        hasHit = false;
        warningTimer = 0f;
        isFalling = !showWarning;

        gameObject.SetActive(true);

        if (showWarning)
        {
            CreateWarningIndicator(groundTarget);
            // Hide projectile during warning
            if (spriteRenderer != null)
                spriteRenderer.enabled = false;
        }
    }

    /// <summary>
    /// Creates a warning indicator on the ground
    /// </summary>
    private void CreateWarningIndicator(Vector3 position)
    {
        if (warningObject == null)
        {
            warningObject = new GameObject("WarningIndicator");
            warningObject.transform.SetParent(transform);
            warningIndicator = warningObject.AddComponent<SpriteRenderer>();
            // Use custom warning sprite if provided, otherwise use placeholder
            warningIndicator.sprite = warningSprite != null ? warningSprite : CreateBoxSprite();
            warningIndicator.sortingOrder = 5;
        }

        warningObject.transform.position = position;
        warningIndicator.color = warningColor;
        warningObject.SetActive(true);
    }

    private void Update()
    {
        if (hasHit) return;

        lifeTimer += Time.deltaTime;

        // Warning phase
        if (showWarning && !isFalling)
        {
            warningTimer += Time.deltaTime;

            // Pulse the warning indicator
            if (warningIndicator != null)
            {
                float pulse = Mathf.PingPong(warningTimer * 4f, 1f);
                warningIndicator.color = new Color(warningColor.r, warningColor.g, warningColor.b, 0.2f + pulse * 0.5f);
            }

            if (warningTimer >= warningDuration)
            {
                isFalling = true;
                if (spriteRenderer != null)
                    spriteRenderer.enabled = true;
                if (warningObject != null)
                    warningObject.SetActive(false);
            }
            return;
        }

        // Falling phase
        transform.position += Vector3.down * fallSpeed * Time.deltaTime;

        // Lifetime check
        if (lifeTimer > lifetime)
        {
            Deactivate();
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (hasHit) return;

        // Damage player
        if (collision.CompareTag("Player"))
        {
            Health playerHealth = collision.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
                Debug.Log($"[ARTILLERY] Hit player for {damage} damage!");
            }
            hasHit = true;
            Deactivate();
            return;
        }

        // Hit ground or other solid objects
        if (collision.CompareTag("Ground") || collision.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            hasHit = true;
            OnHitGround();
        }
    }

    /// <summary>
    /// Called when artillery hits the ground
    /// </summary>
    private void OnHitGround()
    {
        // Could add explosion effect here later
        Debug.Log("[ARTILLERY] Hit ground!");
        Deactivate();
    }

    private void Deactivate()
    {
        hasHit = true;
        if (warningObject != null)
            warningObject.SetActive(false);
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        hasHit = true;
        isFalling = false;
        warningTimer = 0f;
    }
}
