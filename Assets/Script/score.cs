using UnityEngine;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text scoreText;          
    public TMP_Text combinedText;       // for game over screen

    private int score ;
    private int highScore = 0;

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

                // check HS
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

    public void AddScore(int amount)
    {
        Score += amount;
    }

    public void ResetScore()
    {
        Score = 0;
    }
}
