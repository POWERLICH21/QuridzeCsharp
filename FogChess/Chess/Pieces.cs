namespace FogChess.Chess;

public enum PieceColor
{
    White,
    Black,
}

public enum PieceType
{
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King,
}

public readonly record struct Piece(PieceType Type, PieceColor Color);

/// <summary>A square on the board. File 0..7 is a..h, Rank 0..7 is 1..8.</summary>
public readonly record struct Square(int File, int Rank)
{
    public bool IsOnBoard => File is >= 0 and < 8 && Rank is >= 0 and < 8;

    public Square Offset(int files, int ranks) => new(File + files, Rank + ranks);

    public override string ToString() => $"{(char)('a' + File)}{Rank + 1}";
}

public sealed record Move(Square From, Square To, PieceType? Promotion = null);

public static class ChessExtensions
{
    public static PieceColor Opponent(this PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;

    public static string Name(this PieceType type) => type.ToString().ToLowerInvariant();
}
