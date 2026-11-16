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





---

# Recuro — subscription tracker

A website where a person signs in and tracks their subscriptions: what they pay per month and per year, and when each one renews.

- `server/` — the web app (ASP.NET Core 10, SQLite database). The pages themselves are in `server/wwwroot/`.
- `server.tests/` — automated tests for the server.
- `docs/screenshots/` — pictures of the design.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
cd server
dotnet run
```

Then open the address it prints (for example http://localhost:5000). The database file `allsubs.db` is created automatically on first start.

## Run the tests

```bash
dotnet test server.tests
```
