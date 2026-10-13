using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum BombMatchState : byte
{
    WaitingForPlayers,
    Countdown,
    InProgress,
    Ended
}

public enum BombMatchResult : byte
{
    None,
    Decoded,     // a player decoded the bomb and won
    Exploded,    // the timer ran out, nobody wins
    NoSurvivors  // every player died before anyone decoded it
}

/// <summary>
/// Free-for-all "Bomb Decode" victory mode.
/// Every player is an enemy of every other player. A stationary bomb counts down
/// from 45 seconds; the first player to finish decoding it wins. If the timer
/// reaches zero the bomb explodes and nobody wins.
///
/// Server-authoritative: only the server changes match state. Clients read the
/// NetworkVariables to drive the HUD.
///
/// Put one in the game scene on a GameObject with a NetworkObject.
/// </summary>
public class BombVictoryGameManager : NetworkBehaviour
{
    public const ulong NoPlayer = ulong.MaxValue;

    public static BombVictoryGameManager Instance { get; private set; }

    [Header("Match")]
    [Tooltip("Players needed before the match starts. Set to 1 or 2 while testing.")]
    [SerializeField] int requiredPlayers = 6;
    [Tooltip("Seconds of 'Get ready' countdown before the bomb timer starts.")]
    [SerializeField] float preMatchCountdown = 5f;
    [Tooltip("Seconds until the bomb explodes.")]
    [SerializeField] float bombTimerSeconds = 45f;

    [Header("References")]
    [SerializeField] DecodableBomb bomb;

    public NetworkVariable<BombMatchState> State = new(BombMatchState.WaitingForPlayers);
    public NetworkVariable<BombMatchResult> Result = new(BombMatchResult.None);
    public NetworkVariable<ulong> WinnerClientId = new(NoPlayer);
    // Connected player count, mirrored for clients (only the server can read the client list).
    public NetworkVariable<int> PlayerCount = new();

    // End times are stored as server time so clients can count down locally
    // without the server sending a new value every frame.
    public NetworkVariable<double> CountdownEndTime = new();
    public NetworkVariable<double> BombEndTime = new();
    // Bomb time left at the moment the match ended, so the HUD timer freezes.
    public NetworkVariable<float> TimeLeftAtEnd = new();

    /// <summary>Fired on every client (and host) when the match ends.</summary>
    public event Action<BombMatchResult, ulong> MatchEnded;

    readonly HashSet<ulong> alivePlayers = new();

    public int RequiredPlayers => requiredPlayers;
    public float BombTimerSeconds => bombTimerSeconds;
    public DecodableBomb Bomb => bomb;

    double ServerNow => NetworkManager.ServerTime.Time;

    public float CountdownRemaining =>
        State.Value == BombMatchState.Countdown ? Mathf.Max(0f, (float)(CountdownEndTime.Value - ServerNow)) : 0f;

    public float BombTimeRemaining => State.Value switch
    {
        BombMatchState.InProgress => Mathf.Max(0f, (float)(BombEndTime.Value - ServerNow)),
        BombMatchState.Ended => TimeLeftAtEnd.Value,
        _ => bombTimerSeconds
    };

    void Awake()
    {
        Instance = this;
        if (bomb == null) bomb = FindObjectOfType<DecodableBomb>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer) return;

        int playerCount = NetworkManager.ConnectedClientsIds.Count;
        if (PlayerCount.Value != playerCount) PlayerCount.Value = playerCount;

        switch (State.Value)
        {
            case BombMatchState.WaitingForPlayers:
                if (playerCount >= requiredPlayers)
                {
                    CountdownEndTime.Value = ServerNow + preMatchCountdown;
                    State.Value = BombMatchState.Countdown;
                }
                break;

            case BombMatchState.Countdown:
                if (playerCount < requiredPlayers)
                {
                    State.Value = BombMatchState.WaitingForPlayers;
                }
                else if (ServerNow >= CountdownEndTime.Value)
                {
                    BeginMatch();
                }
                break;

            case BombMatchState.InProgress:
                if (ServerNow >= BombEndTime.Value)
                {
                    EndMatch(BombMatchResult.Exploded, NoPlayer);
                }
                break;
        }
    }

    void BeginMatch()
    {
        alivePlayers.Clear();
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            alivePlayers.Add(clientId);
        }

        Result.Value = BombMatchResult.None;
        WinnerClientId.Value = NoPlayer;
        BombEndTime.Value = ServerNow + bombTimerSeconds;
        State.Value = BombMatchState.InProgress;
    }

    // ---------- Server API: call these from your own scripts ----------

    /// <summary>
    /// SERVER ONLY. Call this from your health/damage script when a player dies.
    /// Dead players cannot decode the bomb and do not respawn this round.
    /// </summary>
    public void ReportPlayerDeath(ulong clientId)
    {
        if (!IsServer) return;
        if (!alivePlayers.Remove(clientId)) return;

        if (bomb != null) bomb.ServerCancelDecodeBy(clientId);

        if (State.Value == BombMatchState.InProgress && alivePlayers.Count == 0)
        {
            EndMatch(BombMatchResult.NoSurvivors, NoPlayer);
        }
    }

    /// <summary>SERVER ONLY. True if this player is in the current round and still alive.</summary>
    public bool IsPlayerAlive(ulong clientId) => alivePlayers.Contains(clientId);

    public bool IsMatchInProgress => State.Value == BombMatchState.InProgress;

    /// <summary>SERVER ONLY. Called by the bomb when a player finishes decoding.</summary>
    public void ServerBombDecoded(ulong clientId)
    {
        if (!IsServer || State.Value != BombMatchState.InProgress) return;
        EndMatch(BombMatchResult.Decoded, clientId);
    }

    // ------------------------------------------------------------------

    void HandleClientDisconnected(ulong clientId)
    {
        ReportPlayerDeath(clientId);
    }

    void EndMatch(BombMatchResult result, ulong winner)
    {
        if (State.Value == BombMatchState.Ended) return;

        if (bomb != null) bomb.ServerCancelDecode();

        TimeLeftAtEnd.Value = Mathf.Max(0f, (float)(BombEndTime.Value - ServerNow));
        Result.Value = result;
        WinnerClientId.Value = winner;
        State.Value = BombMatchState.Ended;

        MatchEndedClientRpc(result, winner);
    }

    [ClientRpc]
    void MatchEndedClientRpc(BombMatchResult result, ulong winner)
    {
        if (result == BombMatchResult.Exploded && bomb != null)
        {
            bomb.PlayExplosion();
        }
        MatchEnded?.Invoke(result, winner);
    }

    /// <summary>Name shown in the HUD. Replace with your lobby's player names if you have them.</summary>
    public static string GetPlayerDisplayName(ulong clientId) => $"Player {clientId + 1}";
}
