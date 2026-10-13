using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple HUD for the bomb mode. Put it on your Canvas and drag in the UI elements.
/// Every field is optional; leave one empty to hide that part of the HUD.
/// </summary>
public class BombModeHUD : MonoBehaviour
{
    [Header("Always visible")]
    [SerializeField] TMP_Text timerText;      // "0:45"
    [SerializeField] TMP_Text statusText;     // "Waiting for players 3/6", "Player 2 is decoding!"

    [Header("Decoding")]
    [SerializeField] TMP_Text promptText;     // "Hold E to decode"
    [SerializeField] GameObject progressRoot; // parent of the progress bar
    [SerializeField] Image progressFill;      // Image Type = Filled

    [Header("End screen")]
    [SerializeField] GameObject endPanel;
    [SerializeField] TMP_Text endTitleText;   // "VICTORY" / "DEFEAT"
    [SerializeField] TMP_Text endDetailText;  // "Player 4 decoded the bomb"

    [SerializeField] string decodeKeyLabel = "E";
    [SerializeField] Color urgentTimerColor = Color.red;
    [SerializeField] float urgentTimerSeconds = 10f;

    Color normalTimerColor = Color.white;

    void Start()
    {
        if (timerText != null) normalTimerColor = timerText.color;
        if (endPanel != null) endPanel.SetActive(false);
    }

    void Update()
    {
        var game = BombVictoryGameManager.Instance;
        var bomb = DecodableBomb.Instance;
        if (game == null || !game.IsSpawned) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;

        UpdateTimer(game);
        UpdateStatus(game, bomb, localId);
        UpdateDecodeUI(game, bomb, localId);
        UpdateEndScreen(game, localId);
    }

    void UpdateTimer(BombVictoryGameManager game)
    {
        if (timerText == null) return;

        float seconds = game.BombTimeRemaining;
        timerText.text = $"{(int)seconds / 60}:{(int)seconds % 60:00}";
        timerText.color = game.IsMatchInProgress && seconds <= urgentTimerSeconds ? urgentTimerColor : normalTimerColor;
    }

    void UpdateStatus(BombVictoryGameManager game, DecodableBomb bomb, ulong localId)
    {
        if (statusText == null) return;

        switch (game.State.Value)
        {
            case BombMatchState.WaitingForPlayers:
                statusText.text = $"Waiting for players {game.PlayerCount.Value}/{game.RequiredPlayers}";
                break;

            case BombMatchState.Countdown:
                statusText.text = $"Get ready! {Mathf.CeilToInt(game.CountdownRemaining)}";
                break;

            case BombMatchState.InProgress:
                if (bomb != null && bomb.IsBeingDecoded)
                {
                    ulong decoder = bomb.DecoderClientId.Value;
                    statusText.text = decoder == localId
                        ? "You are decoding the bomb!"
                        : $"{BombVictoryGameManager.GetPlayerDisplayName(decoder)} is decoding - stop them!";
                }
                else
                {
                    statusText.text = "Decode the bomb before it explodes!";
                }
                break;

            default:
                statusText.text = "";
                break;
        }
    }

    void UpdateDecodeUI(BombVictoryGameManager game, DecodableBomb bomb, ulong localId)
    {
        bool inProgress = game.IsMatchInProgress && bomb != null && bomb.IsSpawned;
        var local = PlayerBombInteractor.Local;

        bool showProgress = inProgress && bomb.IsBeingDecoded;
        if (progressRoot != null) progressRoot.SetActive(showProgress);
        if (progressFill != null && showProgress) progressFill.fillAmount = bomb.DecodeProgress;

        if (promptText != null)
        {
            bool nearBomb = inProgress && local != null && local.IsNearBomb;
            bool someoneElseDecoding = inProgress && bomb.IsBeingDecoded && bomb.DecoderClientId.Value != localId;

            promptText.gameObject.SetActive(nearBomb);
            promptText.text = someoneElseDecoding
                ? "Bomb is locked - another player is decoding"
                : $"Hold [{decodeKeyLabel}] to decode";
        }
    }

    void UpdateEndScreen(BombVictoryGameManager game, ulong localId)
    {
        bool ended = game.State.Value == BombMatchState.Ended;
        if (endPanel != null) endPanel.SetActive(ended);
        if (!ended) return;

        ulong winner = game.WinnerClientId.Value;
        bool localWon = game.Result.Value == BombMatchResult.Decoded && winner == localId;

        if (endTitleText != null) endTitleText.text = localWon ? "VICTORY" : "DEFEAT";

        if (endDetailText != null)
        {
            endDetailText.text = game.Result.Value switch
            {
                BombMatchResult.Decoded => localWon
                    ? "You decoded the bomb!"
                    : $"{BombVictoryGameManager.GetPlayerDisplayName(winner)} decoded the bomb",
                BombMatchResult.Exploded => "The bomb exploded. Nobody survived.",
                BombMatchResult.NoSurvivors => "Everyone was eliminated.",
                _ => ""
            };
        }
    }
}
