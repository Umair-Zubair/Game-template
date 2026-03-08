using UnityEngine;
using UnityEngine.UI;

public class VolumeText : MonoBehaviour
{
    [SerializeField] private string volumeName;
    [SerializeField] private string textIntro; //Sound:  or Music:
    private Text txt;

    private void Awake()
    {
        txt = GetComponent<Text>();
    }
    private void Update()
    {
        UpdateVolume();
    }
    private void UpdateVolume()
    {
        float volumeValue = PlayerPrefs.GetFloat(volumeName, 1f) * 100;
        txt.text = textIntro + Mathf.RoundToInt(volumeValue).ToString();
    }
}