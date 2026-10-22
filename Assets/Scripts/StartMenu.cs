using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class StartMenu : MonoBehaviour
{
    public Button startButton;

    void Start()
    {
        Debug.Log("StartMenu Start");
        startButton.onClick.AddListener(OnClickStartButton);
    }

    void OnClickStartButton()
    {
        Debug.Log("click button");
    }
}
