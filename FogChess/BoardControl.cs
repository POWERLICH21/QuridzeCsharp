using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using FogChess.Chess;

namespace FogChess;

/// <summary>
/// Draws the board from the point of view of the player whose turn it is and handles their clicks.
/// Between turns a cover screen hides the board so the two players can pass the computer to each other.
/// </summary>
[DesignerCategory("Code")]
public sealed class BoardControl : Control
{
    private enum Phase
    {
        Handoff,           // Board hidden, waiting for the next player to be ready.
        Playing,           // The current player is choosing a move.
        ChoosingPromotion, // A pawn is moving to the last rank; pick the new piece.
        TurnDone,          // The move was made; the mover looks at the result.
        GameOver,          // The whole board is revealed.
    }

    private static readonly Color BackgroundColor = Color.FromArgb(32, 34, 40);
    private static readonly Color LightSquare = Color.FromArgb(240, 217, 181);
    private static readonly Color DarkSquare = Color.FromArgb(181, 136, 99);
    private static readonly Color FogLight = Color.FromArgb(70, 74, 84);
    private static readonly Color FogDark = Color.FromArgb(56, 60, 69);
    private static readonly Color SelectedTint = Color.FromArgb(130, 246, 246, 105);
    private static readonly Color LastMoveTint = Color.FromArgb(90, 205, 210, 106);
    private static readonly Color CoordinateColor = Color.FromArgb(160, 164, 172);

    private static readonly PieceType[] PromotionChoices =
        [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];

    private readonly PieceRenderer _pieces = new();
    private Game _game = new();
    private Phase _phase;
    private PieceColor _viewer;
    private Square? _selected;
    private IReadOnlyList<Move> _selectedMoves = [];
    private Move? _pendingPromotion;

    public BoardControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = BackgroundColor;
        NewGame();
    }

    public event EventHandler? StatusChanged;

    public string StatusText { get; private set; } = "";

    public bool IsGameInProgress => _game.MoveCount > 0 && !_game.IsOver;

    public void NewGame()
    {
        _game = new Game();
        ShowHandoff();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pieces.Dispose();
        base.Dispose(disposing);
    }

    // ---------- Turn flow ----------

    private void ShowHandoff()
    {
        _phase = Phase.Handoff;
        ClearSelection();
        var next = _game.SideToMove;
        SetStatus($"{next} to move. {next.Opponent()}, please look away. {next}: click the board or press Space when ready.");
    }

    private void StartTurn()
    {
        _phase = Phase.Playing;
        _viewer = _game.SideToMove;
        string intro = _game.LastMove is { } last
            ? last.DescribeForOpponent(_game.GetVisibility(_viewer))
            : "You can only see one square around each of your pieces.";
        SetStatus($"{intro} {_viewer}, your move.");
    }

    private void Play(Move move)
    {
        var result = _game.MakeMove(move);
        ClearSelection();
        if (_game.IsOver)
        {
            _phase = Phase.GameOver;
            SetStatus($"{result.DescribeForMover()} {_game.EndReason} (Game > New Game to play again.)");
        }
        else
        {
            _phase = Phase.TurnDone;
            SetStatus($"{result.DescribeForMover()} Click the board or press Space to end your turn.");
        }
    }

    private void ClearSelection()
    {
        _selected = null;
        _selectedMoves = [];
        _pendingPromotion = null;
        Invalidate();
    }

    private void SetStatus(string text)
    {
        StatusText = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    // ---------- Input ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Right)
        {
            if (_phase == Phase.Playing)
                ClearSelection();
            return;
        }
        if (e.Button != MouseButtons.Left)
            return;

        switch (_phase)
        {
            case Phase.Handoff:
                StartTurn();
                break;
            case Phase.TurnDone:
                ShowHandoff();
                break;
            case Phase.ChoosingPromotion:
                ClickPromotion(e.Location);
                break;
            case Phase.Playing:
                ClickBoard(e.Location);
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool confirm = e.KeyCode is Keys.Space or Keys.Enter;
        if (confirm && _phase == Phase.Handoff)
            StartTurn();
        else if (confirm && _phase == Phase.TurnDone)
            ShowHandoff();
        else if (e.KeyCode == Keys.Escape && _phase == Phase.ChoosingPromotion)
            CancelPromotion();
        else if (e.KeyCode == Keys.Escape && _phase == Phase.Playing)
            ClearSelection();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool clickable = _phase != Phase.Playing || SquareAt(e.Location) is { } square &&
            (_game.PieceAt(square)?.Color == _viewer || _selectedMoves.Any(m => m.To == square));
        Cursor = clickable && _phase != Phase.GameOver ? Cursors.Hand : Cursors.Default;
    }

    private void ClickBoard(Point location)
    {
        if (SquareAt(location) is not { } square)
        {
            ClearSelection();
            return;
        }

        if (_selectedMoves.FirstOrDefault(m => m.To == square) is { } move)
        {
            if (_game.IsPromotion(move))
            {
                _pendingPromotion = move;
                _phase = Phase.ChoosingPromotion;
                Invalidate();
            }
            else
            {
                Play(move);
            }
            return;
        }

        // Your own pieces are always visible, so checking the real board here gives nothing away.
        if (_game.PieceAt(square)?.Color == _viewer && square != _selected)
        {
            _selected = square;
            _selectedMoves = _game.GetMoves(square);
            Invalidate();
        }
        else
        {
            ClearSelection();
        }
    }

    private void ClickPromotion(Point location)
    {
        var rects = PromotionRects();
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i].Contains(location) && _pendingPromotion is { } move)
            {
                Play(move with { Promotion = PromotionChoices[i] });
                return;
            }
        }
        CancelPromotion();
    }

    private void CancelPromotion()
    {
        _pendingPromotion = null;
        _phase = Phase.Playing;
        Invalidate();
    }

    // ---------- Layout ----------

    private (float Left, float Top, float Square) BoardLayout()
    {
        float side = Math.Min(ClientSize.Width, ClientSize.Height);
        float gutter = Math.Max(18f, side * 0.045f);
        float square = Math.Max(4f, (side - 2 * gutter) / 8f);
        return ((ClientSize.Width - square * 8) / 2f, (ClientSize.Height - square * 8) / 2f, square);
    }

    // The player whose view is shown always has their own pieces at the bottom.
    private RectangleF SquareRect(Square square)
    {
        var (left, top, size) = BoardLayout();
        int column = _viewer == PieceColor.White ? square.File : 7 - square.File;
        int row = _viewer == PieceColor.White ? 7 - square.Rank : square.Rank;
        return new RectangleF(left + column * size, top + row * size, size, size);
    }

    private Square? SquareAt(Point point)
    {
        var (left, top, size) = BoardLayout();
        int column = (int)Math.Floor((point.X - left) / size);
        int row = (int)Math.Floor((point.Y - top) / size);
        if (column is < 0 or > 7 || row is < 0 or > 7)
            return null;
        return _viewer == PieceColor.White ? new Square(column, 7 - row) : new Square(7 - column, row);
    }

    private RectangleF[] PromotionRects()
    {
        var (left, top, size) = BoardLayout();
        float box = size * 1.3f;
        float gap = size * 0.2f;
        float width = box * 4 + gap * 3;
        float x = left + (size * 8 - width) / 2;
        float y = top + size * 4 - box / 2 + size * 0.3f;
        return Enumerable.Range(0, 4).Select(i => new RectangleF(x + i * (box + gap), y, box, box)).ToArray();
    }

    // ---------- Painting ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackgroundColor);
        if (ClientSize.Width < 50 || ClientSize.Height < 50)
            return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (_phase == Phase.Handoff)
        {
            DrawHandoff(g);
            return;
        }

        DrawBoard(g, _phase == Phase.GameOver ? null : _game.GetVisibility(_viewer));

        if (_phase == Phase.ChoosingPromotion)
            DrawPromotionPicker(g);
        else if (_phase == Phase.TurnDone)
            DrawBanner(g, "Click to end your turn", atTop: true);
        else if (_phase == Phase.GameOver)
            DrawBanner(g, _game.EndReason ?? "Game over", atTop: false);
    }

    /// <param name="visible">Squares the viewer can see, or null to show everything.</param>
    private void DrawBoard(Graphics g, bool[,]? visible)
    {
        var lastMove = _game.LastMove is { Blocked: false } last ? new[] { last.From, last.To } : [];

        foreach (var square in Game.AllSquares())
        {
            var rect = SquareRect(square);
            bool light = (square.File + square.Rank) % 2 == 1;
            bool seen = visible is null || visible[square.File, square.Rank];

            using (var brush = new SolidBrush(seen ? (light ? LightSquare : DarkSquare) : (light ? FogLight : FogDark)))
                g.FillRectangle(brush, rect);
            if (!seen)
                continue;

            if (lastMove.Contains(square))
                FillTint(g, rect, LastMoveTint);
            if (square == _selected)
                FillTint(g, rect, SelectedTint);
            if (_game.PieceAt(square) is { } piece)
                _pieces.Draw(g, piece, rect);
        }

        foreach (var move in _selectedMoves)
            DrawMoveHint(g, move.To, visible);

        DrawCoordinates(g);
    }

    private void DrawMoveHint(Graphics g, Square target, bool[,]? visible)
    {
        var rect = SquareRect(target);
        bool seen = visible is null || visible[target.File, target.Rank];
        if (seen && _game.PieceAt(target) is not null)
        {
            // Ring around a piece you can capture.
            float inset = rect.Width * 0.06f;
            using var pen = new Pen(Color.FromArgb(110, 20, 20, 20), rect.Width * 0.08f);
            g.DrawEllipse(pen, rect.X + inset, rect.Y + inset, rect.Width - 2 * inset, rect.Height - 2 * inset);
        }
        else
        {
            // Dot on an empty square, or on a fog square where you can't know what is waiting.
            float radius = rect.Width * 0.15f;
            using var brush = new SolidBrush(seen ? Color.FromArgb(90, 20, 20, 20) : Color.FromArgb(120, 230, 230, 230));
            g.FillEllipse(brush, rect.X + rect.Width / 2 - radius, rect.Y + rect.Height / 2 - radius, radius * 2, radius * 2);
        }
    }

    private void DrawCoordinates(Graphics g)
    {
        var (left, top, size) = BoardLayout();
        using var font = new Font("Segoe UI", Math.Max(9f, size * 0.2f), GraphicsUnit.Pixel);
        float gutter = Math.Min(left, top);
        for (int i = 0; i < 8; i++)
        {
            char file = (char)(_viewer == PieceColor.White ? 'a' + i : 'h' - i);
            int rank = _viewer == PieceColor.White ? 8 - i : i + 1;
            DrawCentered(g, file.ToString(), font, CoordinateColor,
                new RectangleF(left + i * size, top + size * 8, size, gutter));
            DrawCentered(g, rank.ToString(), font, CoordinateColor,
                new RectangleF(left - gutter, top + i * size, gutter, size));
        }
    }

    private void DrawHandoff(Graphics g)
    {
        var next = _game.SideToMove;
        float unit = Math.Min(ClientSize.Width, ClientSize.Height) / 10f;
        float centerX = ClientSize.Width / 2f;
        float centerY = ClientSize.Height / 2f;

        _pieces.Draw(g, new Piece(PieceType.King, next), new RectangleF(centerX - unit * 1.4f, centerY - unit * 3.4f, unit * 2.8f, unit * 2.8f));

        using var title = new Font("Segoe UI", unit * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var body = new Font("Segoe UI", unit * 0.28f, GraphicsUnit.Pixel);
        DrawCentered(g, $"{next} to move", title, Color.White, new RectangleF(0, centerY - unit * 0.4f, ClientSize.Width, unit));
        DrawCentered(g, $"{next.Opponent()}, please look away.\nClick or press Space when {next} is ready.", body,
            Color.FromArgb(190, 194, 202), new RectangleF(0, centerY + unit * 0.7f, ClientSize.Width, unit * 1.4f));
    }

    private void DrawPromotionPicker(Graphics g)
    {
        var (left, top, size) = BoardLayout();
        using (var dim = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
            g.FillRectangle(dim, left, top, size * 8, size * 8);

        var rects = PromotionRects();
        using var font = new Font("Segoe UI", size * 0.32f, FontStyle.Bold, GraphicsUnit.Pixel);
        DrawCentered(g, "Promote your pawn to:", font, Color.White,
            new RectangleF(left, rects[0].Top - size * 0.8f, size * 8, size * 0.6f));

        using var boxBrush = new SolidBrush(LightSquare);
        for (int i = 0; i < rects.Length; i++)
        {
            using (var box = RoundedRect(rects[i], size * 0.12f))
                g.FillPath(boxBrush, box);
            _pieces.Draw(g, new Piece(PromotionChoices[i], _viewer), rects[i]);
        }
    }

    // atTop puts the banner over the opponent's side of the board, which is mostly fog anyway.
    private void DrawBanner(Graphics g, string text, bool atTop)
    {
        var (left, top, size) = BoardLayout();
        using var font = new Font("Segoe UI", size * 0.28f, FontStyle.Bold, GraphicsUnit.Pixel);
        var textSize = g.MeasureString(text, font);
        float width = Math.Min(textSize.Width + size * 0.8f, size * 8);
        float height = textSize.Height + size * 0.3f;
        float y = atTop ? top + size * 0.25f : top + (size * 8 - height) / 2;
        var rect = new RectangleF(left + (size * 8 - width) / 2, y, width, height);

        using (var path = RoundedRect(rect, size * 0.15f))
        using (var brush = new SolidBrush(Color.FromArgb(215, 20, 22, 28)))
            g.FillPath(brush, path);
        DrawCentered(g, text, font, Color.White, rect);
    }

    private static void FillTint(Graphics g, RectangleF rect, Color tint)
    {
        using var brush = new SolidBrush(tint);
        g.FillRectangle(brush, rect);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, RectangleF rect)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, rect, format);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
