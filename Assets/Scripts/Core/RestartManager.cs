using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class RestartManager : MonoBehaviour
{
    [SerializeField] private float delay = 2f;

    private void Start()
    {
        BossController boss = FindFirstObjectByType<BossController>();
        if (boss != null)
            boss.OnDied += OnBossDied;
    }

    private void OnBossDied()
    {
        StartCoroutine(RestartAfterDelay());
    }

    private IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
