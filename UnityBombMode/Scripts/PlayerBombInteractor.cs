using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Add to the player prefab (the one with the player's NetworkObject).
/// Reads the local player's input and asks the server to start/stop decoding.
/// The server makes the final decision, so a cheating client can't decode from far away.
/// </summary>
public class PlayerBombInteractor : NetworkBehaviour
{
    [SerializeField] KeyCode decodeKey = KeyCode.E;
    [Tooltip("Minimum seconds between start requests while the key is held.")]
    [SerializeField] float requestInterval = 0.25f;

    float nextRequestTime;

    /// <summary>
    /// True while this player is the one decoding. Your weapon script can check this
    /// to block shooting while decoding, e.g. if (interactor.IsDecoding) return;
    /// </summary>
    public bool IsDecoding
    {
        get
        {
            var bomb = DecodableBomb.Instance;
            return bomb != null && bomb.IsSpawned && bomb.DecoderClientId.Value == OwnerClientId;
        }
    }

    /// <summary>True when the local player is close enough to the bomb to decode it.</summary>
    public bool IsNearBomb
    {
        get
        {
            var bomb = DecodableBomb.Instance;
            return bomb != null && bomb.IsInRange(transform.position);
        }
    }

    public static PlayerBombInteractor Local { get; private set; }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) Local = this;
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        var bomb = DecodableBomb.Instance;
        var game = BombVictoryGameManager.Instance;
        if (bomb == null || !bomb.IsSpawned || game == null || !game.IsSpawned) return;

        bool holding = Input.GetKey(decodeKey);

        if (holding
            && game.IsMatchInProgress
            && !bomb.IsBeingDecoded
            && IsNearBomb
            && Time.time >= nextRequestTime)
        {
            bomb.RequestStartDecodeServerRpc();
            nextRequestTime = Time.time + requestInterval;
        }

        // Also covers a start request that the server accepted after the key was released.
        if (!holding && IsDecoding && Time.time >= nextRequestTime)
        {
            bomb.RequestStopDecodeServerRpc();
            nextRequestTime = Time.time + requestInterval;
        }
    }
}
