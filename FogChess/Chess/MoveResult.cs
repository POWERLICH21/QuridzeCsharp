using System.Text;

namespace FogChess.Chess;

/// <summary>What actually happened when a move was played on the real (hidden) board.</summary>
public sealed class MoveResult
{
    public required PieceColor Mover { get; init; }
    public required PieceType Piece { get; init; }
    public required Square From { get; init; }

    /// <summary>The square the player clicked.</summary>
    public required Square RequestedTo { get; init; }

    /// <summary>Where the piece really ended up. Equal to <see cref="From"/> when the move was blocked.</summary>
    public required Square To { get; init; }

    public Piece? Captured { get; init; }
    public Square? CapturedOn { get; init; }

    /// <summary>The captured piece was standing in the fog, so the mover did not know it was there.</summary>
    public bool CaptureWasHidden { get; init; }

    /// <summary>The piece was stopped short of <see cref="RequestedTo"/> by a piece hidden in the fog.</summary>
    public bool Interrupted { get; init; }

    /// <summary>Castling failed because a hidden piece stood in the way; nothing moved.</summary>
    public bool Blocked { get; init; }

    public bool Castled { get; init; }
    public bool EnPassant { get; init; }
    public PieceType? PromotedTo { get; init; }

    /// <summary>Message for the player who made the move.</summary>
    public string DescribeForMover()
    {
        if (Blocked)
            return "Castling failed: an unseen piece is standing between your king and rook. You lose this turn.";

        string name = Piece.Name();
        var text = new StringBuilder();
        if (Castled)
            text.Append(To.File == 6 ? "You castled kingside." : "You castled queenside.");
        else if (Interrupted && Captured is { } hit)
            text.Append($"Your {name} ran into a hidden {hit.Type.Name()} on {To} and captured it!");
        else if (Interrupted)
            text.Append($"Your {name} was blocked by an unseen piece and stopped on {To}.");
        else if (Captured is { } surprise && CaptureWasHidden)
            text.Append($"Your {name} moved to {To} and captured a hidden {surprise.Type.Name()}!");
        else if (Captured is { } taken)
            text.Append($"Your {name} captured the {taken.Type.Name()} on {CapturedOn}{(EnPassant ? " en passant" : "")}.");
        else
            text.Append($"Your {name} moved {From} → {To}.");

        if (PromotedTo is { } promotion)
            text.Append($" It was promoted to a {promotion.Name()}!");
        return text.ToString();
    }

    /// <summary>
    /// Message for the other player when their turn starts. Only mentions what they can see:
    /// <paramref name="visible"/> is their current visibility.
    /// </summary>
    public string DescribeForOpponent(bool[,] visible)
    {
        string who = Mover.ToString();
        string text;
        if (!Blocked && visible[To.File, To.Rank])
            text = Castled ? $"{who} castled." : $"{who}'s {Piece.Name()} moved to {To}.";
        else if (!Blocked && visible[From.File, From.Rank])
            text = $"{who}'s {Piece.Name()} left {From} and disappeared into the fog.";
        else
            text = $"{who} made a move you could not see.";

        if (Captured is { } lost)
            text += $" Your {lost.Type.Name()} on {CapturedOn} was captured!";
        return text;
    }
}
