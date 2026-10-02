# Fog of War Chess

Chess for two players on one screen, where you only see the squares next to your own pieces
(one step in every direction). Everything else is fog.

## Play in the browser

Open `Web/index.html` in any browser. It is a single file with no install or server needed.
The game saves itself in the browser, so a refresh doesn't lose it.

- **New game vs Claude**: you play White. When the page is opened as a Claude artifact, Claude plays Black
  and is only sent Black's view (the squares Black can see, Black's moves and what Black learned).
  Anywhere else, a simple built-in bot plays Black instead.
- **New game, two players**: two people share the screen and the board is covered between turns.

## Play the Windows version

Open `QuridzeC#.sln` in Visual Studio, right-click **FogChess** and choose **Set as Startup Project**,
then press F5. From a terminal: `dotnet run --project FogChess`.

## Rules

- Between turns the board is covered. Pass the screen and let the other player reveal it.
- You see the squares next to your pieces. Everything else is fog.
- There is no check or checkmate. Capturing the enemy king wins.
- You may move into the fog. A rook, bishop or queen that runs into a hidden enemy stops there and captures it.
- A pawn's two-square first move stops after one square if a hidden piece is in the way.
- Castling fails, and costs you the turn, if a hidden piece stands between king and rook.
- En passant and promotion work as usual.

## Code

- `Web/index.html`: the browser version (HTML, CSS and JavaScript in one file).
- `Chess/`: the rules for the Windows version (board, moves, fog). No UI code.
- `BoardControl.cs`, `MainForm.cs`, `PieceRenderer.cs`: the Windows Forms window and drawing.
