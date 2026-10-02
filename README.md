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

**Play:** open `necropolis-casino/index.html` in any modern browser (it loads Tailwind and the Cinzel font from their CDNs, so it needs an internet connection).

**Rules:** bet gold, then draw five undead units. Each round you get one spell: *Animate Dead* doubles one of your stacks, *Implosion* destroys an enemy stack (enemy cards stay face down until you resolve the round). The AI casts its own spell when the round resolves. After three rounds the stronger army wins double the pot. Your gold balance is saved in the browser.
