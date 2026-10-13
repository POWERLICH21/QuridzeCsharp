using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Stationary bomb that players decode by holding the interact key next to it.
/// Only one player can decode at a time; the others have to kill that player
/// to stop them. Progress resets if the decoder lets go, walks away, or dies.
///
/// Place it in the game scene on a GameObject with a NetworkObject.
/// Do NOT add a NetworkTransform: the bomb never moves.
/// </summary>
[DisallowMultipleComponent]
public class DecodableBomb : NetworkBehaviour
{
    public static DecodableBomb Instance { get; private set; }

    [Header("Decoding")]
    [Tooltip("Seconds a player has to hold the interact key to decode the bomb.")]
    [SerializeField] float decodeDuration = 6f;
    [Tooltip("How close (meters) a player must be to start and keep decoding.")]
    [SerializeField] float interactRange = 2.5f;

    [Header("Effects (optional)")]
    [SerializeField] GameObject explosionPrefab;
    [SerializeField] AudioSource decodeLoopAudio;

    /// <summary>Client id of the player currently decoding, or NoPlayer.</summary>
    public NetworkVariable<ulong> DecoderClientId = new(BombVictoryGameManager.NoPlayer);
    /// <summary>Server time when the current decode started.</summary>
    public NetworkVariable<double> DecodeStartTime = new();

    public float DecodeDuration => decodeDuration;
    public float InteractRange => interactRange;
    public bool IsBeingDecoded => DecoderClientId.Value != BombVictoryGameManager.NoPlayer;

    /// <summary>0..1 decode progress of the current decoder (0 if nobody is decoding).</summary>
    public float DecodeProgress
    {
        get
        {
            if (!IsSpawned || !IsBeingDecoded) return 0f;
            double elapsed = NetworkManager.ServerTime.Time - DecodeStartTime.Value;
            return Mathf.Clamp01((float)(elapsed / decodeDuration));
        }
    }

    void Awake()
    {
        Instance = this;

        // Keep the bomb stationary even if a Rigidbody was added to it.
        if (TryGetComponent(out Rigidbody body))
        {
            body.isKinematic = true;
        }
    }

    public override void OnNetworkSpawn()
    {
        DecoderClientId.OnValueChanged += HandleDecoderChanged;
    }

    public override void OnNetworkDespawn()
    {
        DecoderClientId.OnValueChanged -= HandleDecoderChanged;
        if (Instance == this) Instance = null;
    }

    public bool IsInRange(Vector3 position) =>
        (position - transform.position).sqrMagnitude <= interactRange * interactRange;

    void Update()
    {
        if (!IsSpawned || !IsServer || !IsBeingDecoded) return;

        ulong decoder = DecoderClientId.Value;
        if (!IsValidDecoder(decoder))
        {
            ServerCancelDecode();
            return;
        }

        if (NetworkManager.ServerTime.Time - DecodeStartTime.Value >= decodeDuration)
        {
            BombVictoryGameManager.Instance.ServerBombDecoded(decoder);
        }
    }

    // ---------- Requests from players ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestStartDecodeServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        if (IsBeingDecoded) return; // someone else got there first
        if (!IsValidDecoder(sender)) return;

        DecodeStartTime.Value = NetworkManager.ServerTime.Time;
        DecoderClientId.Value = sender;
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestStopDecodeServerRpc(ServerRpcParams rpcParams = default)
    {
        ServerCancelDecodeBy(rpcParams.Receive.SenderClientId);
    }

    // ---------- Server API ----------

    /// <summary>
    /// SERVER ONLY. Stops the decode if this player is the one decoding.
    /// Call it from your damage script if you want getting shot to interrupt decoding.
    /// </summary>
    public void ServerCancelDecodeBy(ulong clientId)
    {
        if (IsServer && DecoderClientId.Value == clientId) ServerCancelDecode();
    }

    /// <summary>SERVER ONLY. Stops whoever is decoding.</summary>
    public void ServerCancelDecode()
    {
        if (!IsServer) return;
        DecoderClientId.Value = BombVictoryGameManager.NoPlayer;
    }

    bool IsValidDecoder(ulong clientId)
    {
        var game = BombVictoryGameManager.Instance;
        if (game == null || !game.IsMatchInProgress || !game.IsPlayerAlive(clientId)) return false;

        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client)) return false;
        var playerObject = client.PlayerObject;
        return playerObject != null && IsInRange(playerObject.transform.position);
    }

    // ---------- Effects (run on every client) ----------

    void HandleDecoderChanged(ulong previous, ulong current)
    {
        if (decodeLoopAudio == null) return;
        if (current != BombVictoryGameManager.NoPlayer) decodeLoopAudio.Play();
        else decodeLoopAudio.Stop();
    }

    /// <summary>Called on every client by the game manager when the timer runs out.</summary>
    public void PlayExplosion()
    {
        if (decodeLoopAudio != null) decodeLoopAudio.Stop();
        if (explosionPrefab != null) Instantiate(explosionPrefab, transform.position, Quaternion.identity);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
