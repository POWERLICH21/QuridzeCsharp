using System;
using System.Text;

// Joker Hunt: six face-down cards sit around the center of the table.
// One of them is the Joker, the rest are random playing cards hidden
// under the "fog of war". Find the Joker to survive the round and grow
// your multiplier. Survive all three rounds for the biggest payout.
class JokerHunt
{
    // ---- Game settings (tweak these to balance the casino) ----
    const int StartingTokens = 1000;
    const int MinBet = 10;
    const int CardCount = 6;
    const int Rounds = 3;
    const int FlipsPerRound = 3;

    // Total multiplier on the bet after winning each round.
    // With 3 flips out of 6 cards the chance to win a round is 50%,
    // so 1.9x per round keeps a small house edge (95% return).
    static readonly double[] RoundMultipliers = { 1.9, 3.6, 6.8 };

    static readonly string[] Ranks = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
    static readonly string[] Suits = { "♠", "♥", "♦", "♣" };

    readonly Random random = Random.Shared;
    int tokens = StartingTokens;

    // Face of each card, and whether the player has flipped it.
    readonly string[] faces = new string[CardCount];
    readonly bool[] flipped = new bool[CardCount];
    int jokerIndex;

    public void Run()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("=====================================");
        Console.WriteLine("         JOKER HUNT  -  Casino       ");
        Console.WriteLine("=====================================");
        Console.WriteLine($"Six hidden cards surround the table. One of them is the Joker.");
        Console.WriteLine($"Each round you get {FlipsPerRound} flips to find it.");
        Console.WriteLine($"Win a round and your multiplier grows. Lose and your bet is gone.");
        Console.WriteLine();

        while (tokens >= MinBet)
        {
            Console.WriteLine($"Your tokens: {tokens}");
            int? bet = AskBet();
            if (bet == null)
                break;

            PlayGame(bet.Value);
            Console.WriteLine();
        }

        if (tokens < MinBet)
            Console.WriteLine("You are out of tokens. The house wins this time!");
        Console.WriteLine($"You leave the table with {tokens} tokens. Goodbye!");
    }

    void PlayGame(int bet)
    {
        tokens -= bet;

        for (int round = 1; round <= Rounds; round++)
        {
            Console.WriteLine();
            Console.WriteLine($"----------- ROUND {round} of {Rounds} -----------");
            Console.WriteLine($"Win this round to reach x{RoundMultipliers[round - 1]} " +
                              $"({Payout(bet, round)} tokens).");

            DealCards();

            if (!PlayRound())
            {
                Console.WriteLine($"No Joker found. You lose your bet of {bet} tokens.");
                return;
            }

            int winnings = Payout(bet, round);
            Console.WriteLine($"You found the Joker! Current prize: {winnings} tokens (x{RoundMultipliers[round - 1]}).");

            if (round == Rounds)
            {
                tokens += winnings;
                Console.WriteLine($"JACKPOT! You cleared all {Rounds} rounds and win {winnings} tokens!");
                return;
            }

            int nextPrize = Payout(bet, round + 1);
            if (!AskYesNo($"Cash out {winnings} tokens now, or risk it for {nextPrize}? (c = cash out, r = risk): ", 'r', 'c'))
            {
                tokens += winnings;
                Console.WriteLine($"You cash out {winnings} tokens.");
                return;
            }
        }
    }

    // Lets the player flip cards until they find the Joker or run out of flips.
    bool PlayRound()
    {
        for (int flipsLeft = FlipsPerRound; flipsLeft > 0; flipsLeft--)
        {
            DrawTable();
            int choice = AskCard(flipsLeft);
            flipped[choice] = true;

            if (choice == jokerIndex)
            {
                DrawTable();
                return true;
            }

            Console.WriteLine($"Card {choice + 1} is {faces[choice]}. Not the Joker!");
        }

        // Out of flips: lift the fog and show where the Joker was.
        Array.Fill(flipped, true);
        DrawTable();
        Console.WriteLine($"The Joker was hiding under card {jokerIndex + 1}.");
        return false;
    }

    void DealCards()
    {
        jokerIndex = random.Next(CardCount);
        for (int i = 0; i < CardCount; i++)
        {
            flipped[i] = false;
            faces[i] = i == jokerIndex
                ? "JOKER"
                : Ranks[random.Next(Ranks.Length)] + Suits[random.Next(Suits.Length)];
        }
    }

    // Cards are laid out in a ring around the center of the table:
    //        1       2
    //    6    CENTER    3
    //        5       4
    void DrawTable()
    {
        Console.WriteLine();
        Console.WriteLine($"          (1)        (2)");
        Console.WriteLine($"        {Card(0)}    {Card(1)}");
        Console.WriteLine();
        Console.WriteLine($"   (6)                   (3)");
        Console.WriteLine($" {Card(5)}   ~ TABLE ~   {Card(2)}");
        Console.WriteLine();
        Console.WriteLine($"          (5)        (4)");
        Console.WriteLine($"        {Card(4)}    {Card(3)}");
        Console.WriteLine();
    }

    // Every card is drawn with the same width so the table stays aligned.
    string Card(int i)
    {
        string face = flipped[i] ? faces[i] : "???";
        return "[" + face.PadLeft((5 + face.Length) / 2).PadRight(5) + "]";
    }

    static int Payout(int bet, int round) => (int)(bet * RoundMultipliers[round - 1]);

    int? AskBet()
    {
        while (true)
        {
            Console.Write($"Place your bet ({MinBet}-{tokens}), or q to leave: ");
            string? input = Console.ReadLine();
            if (input == null || input.Trim().ToLower() == "q")
                return null;

            if (int.TryParse(input, out int bet) && bet >= MinBet && bet <= tokens)
                return bet;

            Console.WriteLine("That is not a valid bet.");
        }
    }

    int AskCard(int flipsLeft)
    {
        while (true)
        {
            Console.Write($"Flips left: {flipsLeft}. Which card do you flip (1-{CardCount})? ");
            string? input = Console.ReadLine();
            if (input == null)
                Environment.Exit(0);

            if (int.TryParse(input, out int number) && number >= 1 && number <= CardCount)
            {
                if (!flipped[number - 1])
                    return number - 1;
                Console.WriteLine("That card is already face up. Pick another one.");
            }
            else
            {
                Console.WriteLine($"Please enter a number from 1 to {CardCount}.");
            }
        }
    }

    // Returns true for the "yes" key and false for the "no" key.
    static bool AskYesNo(string question, char yes, char no)
    {
        while (true)
        {
            Console.Write(question);
            string? input = Console.ReadLine()?.Trim().ToLower();
            if (input == null)
                return false;
            if (input == yes.ToString())
                return true;
            if (input == no.ToString())
                return false;
            Console.WriteLine($"Please type {yes} or {no}.");
        }
    }
}
