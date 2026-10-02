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

**Rules:** bet gold, then draw five undead units. Each round you get one spell: *Animate Dead* doubles one of your stacks, *Implosion* destroys an enemy stack (enemy cards stay face down until you resolve the round). The AI casts its own spell when the round resolves. After three rounds the stronger army wins double the pot. Your gold balance is saved in the browser.

**Card art:** every creature is drawn in one shared pixel-art style (same palette, outline and crypt alcove) so the whole army reads as one Necropolis. A unit shows its weaker base form (`necropolis-casino/img/<unit>.png`); the stronger form (`<unit>-empowered.png`, in a green spectral alcove) appears only after Animate Dead is cast on that stack.

The art is built from the original images in `necropolis-casino/art-source/` in two steps:
1. `cut_figures.py` cuts each creature out of its source image into `art-source/cutouts/` (needs `pip install "rembg[cpu]"`). The cutouts can be touched up by hand.
2. `make_card_art.py` turns the cutouts into the pixel-art cards in `img/` (needs Pillow and numpy).
3. `necropolis-casino/build.py` embeds the pictures from `img/` into `index.html` and precompiles its Tailwind styles (needs Node.js; `--art-only` skips the styles). Run it after changing the art or the Tailwind classes in the page.
