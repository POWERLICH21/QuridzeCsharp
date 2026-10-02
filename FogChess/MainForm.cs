using System.ComponentModel;

namespace FogChess;

[DesignerCategory("Code")]
public sealed class MainForm : Form
{
    private const string Rules =
        "Fog of War Chess\n\n" +
        "• Two players share one computer. Between turns the board is hidden; pass the computer and click when ready.\n" +
        "• You only see the squares next to your own pieces (one square in every direction). Everything else is fog.\n" +
        "• There is no check or checkmate. You win by capturing the enemy king, so keep it safe!\n" +
        "• You may move into the fog. A rook, bishop or queen that runs into a hidden enemy piece stops there and captures it.\n" +
        "• A pawn's two-square first move stops after one square if a hidden piece is in the way.\n" +
        "• Castling fails (and you lose the turn) if a hidden piece stands between your king and rook.\n" +
        "• En passant and promotion work as in normal chess.\n\n" +
        "Controls: left-click a piece, then a dot to move. Right-click or Esc cancels. F2 starts a new game.";

    private readonly BoardControl _board = new() { Dock = DockStyle.Fill };

    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 72,
        Padding = new Padding(14, 8, 14, 8),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.FromArgb(24, 26, 31),
        ForeColor = Color.FromArgb(230, 232, 236),
        Font = new Font("Segoe UI", 11f),
        UseMnemonic = false,
    };

    public MainForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Fog of War Chess";
        ClientSize = new Size(640, 740);
        MinimumSize = new Size(420, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var menu = new MenuStrip();
        var gameMenu = new ToolStripMenuItem("&Game");
        gameMenu.DropDownItems.Add(new ToolStripMenuItem("&New Game", null, (_, _) => NewGame()) { ShortcutKeys = Keys.F2 });
        gameMenu.DropDownItems.Add(new ToolStripSeparator());
        gameMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()));
        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add(new ToolStripMenuItem("&How to Play", null, (_, _) => ShowRules()) { ShortcutKeys = Keys.F1 });
        menu.Items.AddRange([gameMenu, helpMenu]);

        // Docking is laid out from the last added control to the first, so the board fills what is left.
        Controls.Add(_board);
        Controls.Add(_status);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _board.StatusChanged += (_, _) => _status.Text = _board.StatusText;
        _status.Text = _board.StatusText;
        ActiveControl = _board;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Use most of the screen height, and roughly the same width so the board has room to be big.
        var area = Screen.FromControl(this).WorkingArea;
        int height = Math.Max(MinimumSize.Height, area.Height * 9 / 10);
        int width = Math.Max(MinimumSize.Width, Math.Min(area.Width * 9 / 10, height - _status.Height - MainMenuStrip!.Height));
        Bounds = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    private void NewGame()
    {
        if (_board.IsGameInProgress &&
            MessageBox.Show(this, "Abandon the current game and start a new one?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        _board.NewGame();
        _board.Focus();
    }

    private void ShowRules() =>
        MessageBox.Show(this, Rules, "How to Play", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
