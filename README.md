using System;

class Program
{
    static void Main()
    {

        Console.WriteLine("Enter a number:");
        int number = int.Parse(Console.ReadLine());

        if (number > 7)
        {
            Console.WriteLine("Hello");
        }

           else
        {
            Console.WriteLine("Invalid number input.");
        }


        Console.WriteLine("Enter a name:");
        string name = Console.ReadLine();
        if (name == "John")
        {
            Console.WriteLine("Hello, John");
        }
        else
        {
            Console.WriteLine("There is no such name");
        }


        Console.WriteLine("Enter array elements separated by spaces:");
        string[] inputArray = Console.ReadLine().Split(' ');
        int[] array = Array.ConvertAll(inputArray, int.Parse);

        Console.WriteLine("Elements that are multiples of 3:");
        foreach (int element in array)
        {
            if (element % 3 == 0)
            {
                Console.WriteLine(element);
            }
        }
    }
}




## Necropolis Casino (web game)

A dark-fantasy betting card game in a single HTML file: [`necropolis-casino/index.html`](necropolis-casino/index.html).

**Play:** open `necropolis-casino/index.html` in any modern browser. It is a single self-contained file: the card pictures and styles are built into it, so it works on its own and offline (online it also loads the Cinzel font from Google Fonts).

**Rules:** place one bet before round 1 (it stays locked for the whole game), then both armies draw five cards each from the crypt every round, dealt in matched pairs so both sides receive very similar points: one side (picked at random) draws a random card, and the other side gets a random card worth within 10 points of it (a card with no matching pile and no free slot is discarded and replaced, so each army always gains five). Each card shows only the points it gives; its colour shows the creature's level (a colour key sits under the rules). Matching cards join into one pile with an x2, x3… badge, and their points add up. Each round you get one spell: *Animate Dead* doubles one of your cards' points and upgrades it to its stronger form, *Implosion* destroys an enemy card (enemy cards stay face down until you resolve the round). The AI casts its own spell when the round resolves, after yours. 85% of the time it plays smart: it destroys your best card if that is worth more than doubling its own best card, otherwise it doubles its own. The other 15% it casts at random (a coin flip between Animate Dead and Implosion, random target). Its Animate Dead upgrades its card the same way. Tip: the AI hunts your biggest card, so destroying its biggest card is usually the safe play. After three rounds, if your army is stronger you get your bet back plus winnings of **bet × (your points − AI points) ÷ 100**, rounded down to whole gold; a draw returns your bet and a loss loses it. Your gold balance is saved in the browser.

Example: bet 100, you finish with 340 points against the AI's 300 → your 100 back plus 100 × 40 ÷ 100 = 40 winnings, 140 gold in total.

Measured RTP with these rules (400,000 simulated games per player type, cross-checked against the real game code). The 85% smart chance is tuned so even the strongest strategy found stays just under the 96–97% casino range:

| Player | Wins | RTP |
|---|---|---|
| Strongest strategy found (knows every AI card) | 64% | 95.8% |
| Skilled (uses only what the game shows) | 61% | 91.1% |
| Always destroys the AI's biggest card | 53% | 80.3% |
| Casts spells at random | 6.5% | 9.7% |
| Always doubles own best card | 1.7% | 3.8% |

A card spawns a group of creatures, so its points are spawn x power:

| Level | Colour | Creature | Spawns | Power each | Card gives | Cards in deck |
|---|---|---|---|---|---|---|
| 1 | Gray | Skeleton | 20 | 1 | 20 pts | 20 |
| 2 | Green | Zombie | 12 | 2 | 24 pts | 12 |
| 3 | Blue | Wight | 9 | 3 | 27 pts | 9 |
| 4 | Purple | Vampire | 8 | 4 | 32 pts | 8 |
| 5 | Orange | Lich | 6 | 7 | 42 pts | 6 |
| 6 | Red | Dark Knight | 3 | 17 | 51 pts | 3 |
| 7 | Gold | Bone Dragon | 1 | 60 | 60 pts | 1 |

**Card art:** every creature is drawn in one shared pixel-art style (same palette, outline and crypt alcove) so the whole army reads as one Necropolis. A unit shows its weaker base form (`necropolis-casino/img/<unit>.png`); the stronger form (`<unit>-empowered.png`, in a green spectral alcove) appears only after Animate Dead is cast on that stack.

The art is built from the original images in `necropolis-casino/art-source/` in two steps:
1. `cut_figures.py` cuts each creature out of its source image into `art-source/cutouts/` (needs `pip install "rembg[cpu]"`). The cutouts can be touched up by hand.
2. `make_card_art.py` turns the cutouts into the pixel-art cards in `img/` (needs Pillow and numpy).
3. `necropolis-casino/build.py` embeds the pictures from `img/` into `index.html` and precompiles its Tailwind styles (needs Node.js; `--art-only` skips the styles). Run it after changing the art or the Tailwind classes in the page.
