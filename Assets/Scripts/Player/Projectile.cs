using UnityEngine;

public class Projectile : MonoBehaviour
{
    [SerializeField] private float speed;
    private float direction;
    private bool hit;
    private float lifetime;

    private Animator anim;
    private BoxCollider2D boxCollider;
    private Transform parentHolder; // To remember the holder

    private void Awake()
    {
        anim = GetComponent<Animator>();
        boxCollider = GetComponent<BoxCollider2D>();
        parentHolder = transform.parent; // Remember who spawned us
    }

    private void Update()
    {
        if (hit) return;

        // --- FIX: RESTORED DIRECTION MATH ---
        // We MUST multiply by direction. 
        // Why? Because changing Scale flips the picture, but NOT the movement axis.
        float movementSpeed = speed * Time.deltaTime * direction;
        
        transform.Translate(movementSpeed, 0, 0);

        lifetime += Time.deltaTime;
        if (lifetime > 5) Deactivate();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        hit = true;
        boxCollider.enabled = false;
        anim.SetTrigger("explode");

        if (collision.tag == "Enemy")
             collision.GetComponent<Health>()?.TakeDamage(1);
    }

    public void SetDirection(float _direction)
    {
        lifetime = 0;
        direction = _direction;
        gameObject.SetActive(true);
        hit = false;
        boxCollider.enabled = true;

        // --- FIX: DETACH ---
        // Detach from player so turning around doesn't flip the fireball mid-air
        transform.SetParent(null);

        // Flip the Sprite
        float localScaleX = transform.localScale.x;
        if (Mathf.Sign(localScaleX) != _direction)
            localScaleX = -localScaleX;

        transform.localScale = new Vector3(localScaleX, transform.localScale.y, transform.localScale.z);
    }

    private void Deactivate()
    {
        gameObject.SetActive(false);
        // Re-attach to player so we carry the ammo to the next room
        if (parentHolder != null)
            transform.SetParent(parentHolder);
    }
}