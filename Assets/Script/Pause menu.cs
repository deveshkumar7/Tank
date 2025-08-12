using UnityEngine;

public class PauseManager : MonoBehaviour
{
    [Header("Canvas to Hide on Pause")]
    public GameObject[] hideOnPause;

    [Header("Canvas to Show on Pause")]
    public GameObject[] showOnPause;

    [Header("Key Bindings")]
    public KeyCode pauseKey = KeyCode.Escape;

    private bool isPaused = false;

    void Update()
    {
        if (Input.GetKeyDown(pauseKey))
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        if (isPaused)
            ResumeGame();
        else
            PauseGame();
    }

    public void PauseGame()
    {
        Time.timeScale = 0f; // Freeze game
        isPaused = true;

        foreach (GameObject obj in hideOnPause)
            if (obj != null) obj.SetActive(false);

        foreach (GameObject obj in showOnPause)
            if (obj != null) obj.SetActive(true);

        Debug.Log("Game Paused");
    }

    public void ResumeGame()
    {
        Time.timeScale = 1f; // Unfreeze game
        isPaused = false;

        foreach (GameObject obj in hideOnPause)
            if (obj != null) obj.SetActive(true);

        foreach (GameObject obj in showOnPause)
            if (obj != null) obj.SetActive(false);

        Debug.Log("Game Resumed");
    }
}
