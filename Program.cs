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



