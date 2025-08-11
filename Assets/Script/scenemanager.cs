using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    // Name of the scene you want to load
    [SerializeField] private string sceneName;

    // Load the specified scene
    public void StartGame()
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("Scene name is empty! Please assign a scene name in the inspector.");
        }
    }

    // Exit the game
    public void ExitGame()
    {
        Debug.Log("Exiting Game...");
        Application.Quit();

        // If running in the editor
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
