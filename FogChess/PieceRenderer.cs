using System.Drawing.Drawing2D;
using FogChess.Chess;

namespace FogChess;

/// <summary>
/// Draws chess pieces from the chess symbols in the Segoe UI Symbol font (♚♛♜♝♞♟).
/// The same filled symbol is used for both sides, and the fill color tells them apart.
/// </summary>
public sealed class PieceRenderer : IDisposable
{
    private readonly Dictionary<PieceType, GraphicsPath> _shapes = [];

    public PieceRenderer()
    {
        using var font = FindFont();
        foreach (var type in Enum.GetValues<PieceType>())
        {
            var path = new GraphicsPath();
            path.AddString(Symbol(type), font, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
            _shapes[type] = path;
        }

        // Scale every shape so the king is 1 unit tall, centered horizontally, standing on y = 0.
        float kingHeight = _shapes[PieceType.King].GetBounds().Height;
        if (kingHeight <= 0)
            kingHeight = 1;
        foreach (var path in _shapes.Values)
        {
            var bounds = path.GetBounds();
            using var matrix = new Matrix();
            matrix.Scale(1 / kingHeight, 1 / kingHeight);
            matrix.Translate(-(bounds.X + bounds.Width / 2), -bounds.Bottom);
            path.Transform(matrix);
        }
    }

    public void Draw(Graphics g, Piece piece, RectangleF square)
    {
        using var path = (GraphicsPath)_shapes[piece.Type].Clone();
        float size = square.Height * 0.8f;
        using (var matrix = new Matrix())
        {
            matrix.Translate(square.X + square.Width / 2, square.Y + square.Height * 0.9f);
            matrix.Scale(size, size);
            path.Transform(matrix);
        }

        bool white = piece.Color == PieceColor.White;
        using var fill = new SolidBrush(white ? Color.FromArgb(250, 250, 250) : Color.FromArgb(28, 28, 28));
        using var outline = new Pen(white ? Color.FromArgb(28, 28, 28) : Color.FromArgb(205, 205, 205), Math.Max(1f, square.Height / 40f))
        {
            LineJoin = LineJoin.Round,
        };
        g.FillPath(fill, path);
        g.DrawPath(outline, path);
    }

    public void Dispose()
    {
        foreach (var path in _shapes.Values)
            path.Dispose();
        _shapes.Clear();
    }

    private static string Symbol(PieceType type) => type switch
    {
        PieceType.King => "♚",
        PieceType.Queen => "♛",
        PieceType.Rook => "♜",
        PieceType.Bishop => "♝",
        PieceType.Knight => "♞",
        _ => "♟",
    };

    private static FontFamily FindFont()
    {
        foreach (string name in new[] { "Segoe UI Symbol", "DejaVu Sans" })
        {
            try
            {
                return new FontFamily(name);
            }
            catch (ArgumentException)
            {
                // Font not installed, try the next one.
            }
        }
        return FontFamily.GenericSansSerif;
    }
}
