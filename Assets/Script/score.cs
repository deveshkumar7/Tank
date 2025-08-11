using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text scoreText;          // UI for score only
    public TMP_Text combinedText;       // UI showing Score + HighScore on different lines

    private int score ;
    private int highScore = 0;

    // Property to auto-update UI when score changes
    public int Score
    {
        get { return score; }
        set
        {
            if (score != value)
            {
                score = value;
                UpdateScoreUI();
                UpdateCombinedUI();

                // Update high score if needed
                if (score > highScore)
                {
                    highScore = score;
                    UpdateCombinedUI();
                }
            }
        }
    }

    private void Start()
    {
        score = 0;
        highScore = PlayerPrefs.GetInt("HighScore", 0);

        UpdateScoreUI();
        UpdateCombinedUI();
    }

    private void OnApplicationQuit()
    {
        PlayerPrefs.SetInt("HighScore", highScore);
        PlayerPrefs.Save();
    }

    private void UpdateScoreUI()
    {
        if (scoreText != null)
            scoreText.text = "Score: " + score;
    }

    private void UpdateCombinedUI()
    {
        if (combinedText != null)
            combinedText.text = "Score: " + score + "\nHigh Score: " + highScore;
    }

    // Public function to add score
    public void AddScore(int amount)
    {
        Score += amount;
    }

    // Public function to reset score
    public void ResetScore()
    {
        Score = 0;
    }
}
