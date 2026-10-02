using System.Diagnostics;

namespace FogChess.Chess;

/// <summary>
/// Chess with fog of war.
/// <list type="bullet">
/// <item>A player only sees the squares within one step (including diagonals) of their own pieces.</item>
/// <item>There is no check or checkmate: you win by capturing the enemy king.</item>
/// <item>The moves a player is offered are based on what they can see, then played out on the real board.
/// A rook, bishop or queen sliding through the fog stops at the first hidden enemy piece and captures it.
/// A pawn's double step stops after one square if a hidden piece stands on the second square.</item>
/// </list>
/// </summary>
public sealed class Game
{
    public const int Size = 8;

    private static readonly (int Files, int Ranks)[] KnightJumps =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int Files, int Ranks)[] Straight = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int Files, int Ranks)[] Diagonal = [(1, 1), (1, -1), (-1, 1), (-1, -1)];
    private static readonly (int Files, int Ranks)[] AllDirections = [.. Straight, .. Diagonal];

    private static readonly PieceType[] BackRank =
    [
        PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen,
        PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook,
    ];

    private readonly Piece?[,] _board = new Piece?[Size, Size];

    // [color, side]: side 0 is kingside, 1 is queenside.
    private readonly bool[,] _castlingRights = { { true, true }, { true, true } };

    public Game()
    {
        for (int file = 0; file < Size; file++)
        {
            _board[file, 0] = new Piece(BackRank[file], PieceColor.White);
            _board[file, 1] = new Piece(PieceType.Pawn, PieceColor.White);
            _board[file, 6] = new Piece(PieceType.Pawn, PieceColor.Black);
            _board[file, 7] = new Piece(BackRank[file], PieceColor.Black);
        }
    }

    public PieceColor SideToMove { get; private set; } = PieceColor.White;

    /// <summary>Number of moves played by both sides together.</summary>
    public int MoveCount { get; private set; }

    /// <summary>The square a pawn skipped over with a double step on the previous move.</summary>
    public Square? EnPassantSquare { get; private set; }

    public MoveResult? LastMove { get; private set; }
    public PieceColor? Winner { get; private set; }
    public bool IsDraw { get; private set; }
    public bool IsOver => Winner is not null || IsDraw;
    public string? EndReason { get; private set; }

    public static IEnumerable<Square> AllSquares()
    {
        for (int rank = 0; rank < Size; rank++)
            for (int file = 0; file < Size; file++)
                yield return new Square(file, rank);
    }

    public Piece? PieceAt(Square square) => _board[square.File, square.Rank];

    /// <summary>Which squares <paramref name="viewer"/> can see: every square next to or under one of their pieces.</summary>
    public bool[,] GetVisibility(PieceColor viewer)
    {
        var visible = new bool[Size, Size];
        foreach (var square in AllSquares())
        {
            if (PieceAt(square)?.Color != viewer)
                continue;
            for (int files = -1; files <= 1; files++)
            {
                for (int ranks = -1; ranks <= 1; ranks++)
                {
                    var seen = square.Offset(files, ranks);
                    if (seen.IsOnBoard)
                        visible[seen.File, seen.Rank] = true;
                }
            }
        }
        return visible;
    }

    /// <summary>The moves the side to move can try with the piece on <paramref name="from"/>, judged by what they can see.</summary>
    public IReadOnlyList<Move> GetMoves(Square from)
    {
        var moves = new List<Move>();
        if (!IsOver && PieceAt(from) is { } piece && piece.Color == SideToMove)
            AddMoves(from, piece, GetVisibility(SideToMove), moves);
        return moves;
    }

    public bool IsPromotion(Move move) =>
        PieceAt(move.From) is { Type: PieceType.Pawn } && move.To.Rank is 0 or Size - 1;

    public MoveResult MakeMove(Move move)
    {
        if (IsOver)
            throw new InvalidOperationException("The game is over.");
        if (move.Promotion is PieceType.Pawn or PieceType.King)
            throw new ArgumentException($"Cannot promote to a {move.Promotion.Value.Name()}.", nameof(move));
        if (!GetMoves(move.From).Any(m => m.To == move.To))
            throw new InvalidOperationException($"{move.From}-{move.To} is not a legal move.");

        var piece = PieceAt(move.From)!.Value;
        var mover = piece.Color;
        var visibleBefore = GetVisibility(mover);
        var enPassantSquare = EnPassantSquare;
        EnPassantSquare = null;

        var from = move.From;
        var to = move.To;
        Square? capturedOn = null;
        bool interrupted = false, blocked = false, castled = false, enPassant = false;

        if (piece.Type == PieceType.King && Math.Abs(to.File - from.File) == 2)
        {
            bool kingSide = to.File > from.File;
            int rookFile = kingSide ? Size - 1 : 0;
            int first = Math.Min(from.File, rookFile) + 1;
            int last = Math.Max(from.File, rookFile) - 1;
            blocked = Enumerable.Range(first, last - first + 1).Any(file => _board[file, from.Rank] is not null);
            if (blocked)
            {
                to = from;
            }
            else
            {
                castled = true;
                MovePiece(new Square(rookFile, from.Rank), new Square(kingSide ? 5 : 3, from.Rank));
            }
        }
        else if (piece.Type is PieceType.Bishop or PieceType.Rook or PieceType.Queen)
        {
            int files = Math.Sign(to.File - from.File);
            int ranks = Math.Sign(to.Rank - from.Rank);
            for (var square = from.Offset(files, ranks); square != to; square = square.Offset(files, ranks))
            {
                if (PieceAt(square) is { } hidden)
                {
                    // Own pieces are always visible, so anything we bump into here is an enemy.
                    Debug.Assert(hidden.Color != mover);
                    to = square;
                    interrupted = true;
                    break;
                }
            }
        }
        else if (piece.Type == PieceType.Pawn && Math.Abs(to.Rank - from.Rank) == 2)
        {
            var skipped = new Square(from.File, (from.Rank + to.Rank) / 2);
            if (PieceAt(to) is not null)
            {
                to = skipped;
                interrupted = true;
            }
            else
            {
                EnPassantSquare = skipped;
            }
        }
        else if (piece.Type == PieceType.Pawn && from.File != to.File && to == enPassantSquare)
        {
            enPassant = true;
            capturedOn = new Square(to.File, from.Rank);
        }

        if (!enPassant && to != from && PieceAt(to) is not null)
            capturedOn = to;

        Piece? captured = null;
        if (capturedOn is { } square1)
        {
            captured = PieceAt(square1);
            _board[square1.File, square1.Rank] = null;
        }

        PieceType? promotedTo = null;
        if (to != from)
        {
            MovePiece(from, to);
            if (piece.Type == PieceType.Pawn && to.Rank is 0 or Size - 1)
            {
                promotedTo = move.Promotion ?? PieceType.Queen;
                _board[to.File, to.Rank] = new Piece(promotedTo.Value, mover);
            }
            ClearCastlingRights(from);
            ClearCastlingRights(to);
        }

        MoveCount++;
        SideToMove = mover.Opponent();
        LastMove = new MoveResult
        {
            Mover = mover,
            Piece = piece.Type,
            From = from,
            RequestedTo = move.To,
            To = to,
            Captured = captured,
            CapturedOn = capturedOn,
            CaptureWasHidden = capturedOn is { } square2 && !visibleBefore[square2.File, square2.Rank],
            Interrupted = interrupted,
            Blocked = blocked,
            Castled = castled,
            EnPassant = enPassant,
            PromotedTo = promotedTo,
        };

        if (captured is { Type: PieceType.King })
        {
            Winner = mover;
            EndReason = $"{mover} wins by capturing the {mover.Opponent().ToString().ToLowerInvariant()} king!";
        }
        else if (AllSquares().All(s => PieceAt(s) is null or { Type: PieceType.King }))
        {
            IsDraw = true;
            EndReason = "Draw: only the two kings are left.";
        }
        else if (!HasAnyMove(SideToMove))
        {
            IsDraw = true;
            EndReason = $"Draw: {SideToMove} has no moves.";
        }

        return LastMove;
    }

    private bool HasAnyMove(PieceColor color)
    {
        var visible = GetVisibility(color);
        var moves = new List<Move>();
        foreach (var square in AllSquares())
        {
            if (PieceAt(square) is { } piece && piece.Color == color)
                AddMoves(square, piece, visible, moves);
            if (moves.Count > 0)
                return true;
        }
        return false;
    }

    private void MovePiece(Square from, Square to)
    {
        _board[to.File, to.Rank] = _board[from.File, from.Rank];
        _board[from.File, from.Rank] = null;
    }

    /// <summary>Anything moving from or to a king or rook home square ends that castling option.</summary>
    private void ClearCastlingRights(Square square)
    {
        foreach (var color in new[] { PieceColor.White, PieceColor.Black })
        {
            if (square.Rank != HomeRank(color))
                continue;
            if (square.File is 4 or 7)
                _castlingRights[(int)color, 0] = false;
            if (square.File is 4 or 0)
                _castlingRights[(int)color, 1] = false;
        }
    }

    private static int HomeRank(PieceColor color) => color == PieceColor.White ? 0 : Size - 1;

    /// <summary>What the moving player thinks is on a square: squares in the fog look empty.</summary>
    private Piece? SeenAt(Square square, bool[,] visible) =>
        visible[square.File, square.Rank] ? PieceAt(square) : null;

    private void AddMoves(Square from, Piece piece, bool[,] visible, List<Move> moves)
    {
        switch (piece.Type)
        {
            case PieceType.Pawn:
                AddPawnMoves(from, piece.Color, visible, moves);
                break;
            case PieceType.Knight:
                AddSteps(from, piece.Color, KnightJumps, visible, moves);
                break;
            case PieceType.Bishop:
                AddSlides(from, piece.Color, Diagonal, visible, moves);
                break;
            case PieceType.Rook:
                AddSlides(from, piece.Color, Straight, visible, moves);
                break;
            case PieceType.Queen:
                AddSlides(from, piece.Color, AllDirections, visible, moves);
                break;
            case PieceType.King:
                AddSteps(from, piece.Color, AllDirections, visible, moves);
                AddCastling(from, piece.Color, visible, moves);
                break;
        }
    }

    private void AddSteps(Square from, PieceColor color, (int Files, int Ranks)[] steps, bool[,] visible, List<Move> moves)
    {
        foreach (var (files, ranks) in steps)
        {
            var to = from.Offset(files, ranks);
            if (to.IsOnBoard && SeenAt(to, visible)?.Color != color)
                moves.Add(new Move(from, to));
        }
    }

    private void AddSlides(Square from, PieceColor color, (int Files, int Ranks)[] directions, bool[,] visible, List<Move> moves)
    {
        foreach (var (files, ranks) in directions)
        {
            for (var to = from.Offset(files, ranks); to.IsOnBoard; to = to.Offset(files, ranks))
            {
                var seen = SeenAt(to, visible);
                if (seen?.Color == color)
                    break;
                moves.Add(new Move(from, to));
                if (seen is not null)
                    break;
            }
        }
    }

    private void AddPawnMoves(Square from, PieceColor color, bool[,] visible, List<Move> moves)
    {
        int forward = color == PieceColor.White ? 1 : -1;
        int startRank = color == PieceColor.White ? 1 : Size - 2;

        var one = from.Offset(0, forward);
        if (one.IsOnBoard && SeenAt(one, visible) is null)
        {
            moves.Add(new Move(from, one));
            var two = one.Offset(0, forward);
            if (from.Rank == startRank && SeenAt(two, visible) is null)
                moves.Add(new Move(from, two));
        }

        foreach (int side in new[] { -1, 1 })
        {
            var target = from.Offset(side, forward);
            if (!target.IsOnBoard)
                continue;
            var seen = SeenAt(target, visible);
            bool capture = seen is { } enemy && enemy.Color != color;
            if (capture || (seen is null && target == EnPassantSquare))
                moves.Add(new Move(from, target));
        }
    }

    // No check in this variant, so castling only needs the king and rook unmoved and the squares between them looking empty.
    private void AddCastling(Square from, PieceColor color, bool[,] visible, List<Move> moves)
    {
        int rank = HomeRank(color);
        if (from != new Square(4, rank))
            return;

        bool CanCastle(int side, int rookFile, int firstFile, int lastFile)
        {
            if (!_castlingRights[(int)color, side] || PieceAt(new Square(rookFile, rank)) != new Piece(PieceType.Rook, color))
                return false;
            for (int file = firstFile; file <= lastFile; file++)
            {
                if (SeenAt(new Square(file, rank), visible) is not null)
                    return false;
            }
            return true;
        }

        if (CanCastle(0, Size - 1, 5, 6))
            moves.Add(new Move(from, new Square(6, rank)));
        if (CanCastle(1, 0, 1, 3))
            moves.Add(new Move(from, new Square(2, rank)));
    }
}
