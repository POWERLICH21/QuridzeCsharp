# How to run the Allsubs demo on your computer

## 1. Install .NET 10 (one time)

Download and install the **.NET 10 SDK**: https://dotnet.microsoft.com/download/dotnet/10.0

(.NET 11 should also work, but the site has only been tested on .NET 10.)

To check it worked, open a terminal (on Windows: Command Prompt) and run:

```bash
dotnet --version
```

It should print a number starting with `10.` or higher.

## 2. Get the code

Download this ZIP and unpack it:

https://github.com/POWERLICH21/QuridzeCsharp/archive/refs/heads/claude/upbeat-shannon-y4g5k7.zip

## 3. Start the site

- **Windows:** double-click `start.bat` in the unpacked folder.
- **Mac / Linux:** run `./start.sh` in a terminal from the unpacked folder.

The first start takes about a minute while it downloads its components.
When you see `Now listening on: http://localhost:5080`, the site is running.

## 4. Open it

Go to **http://localhost:5080** in your browser. From there you can:

- click **Explore with sample data** to see the site filled with example subscriptions, or
- click **Create account** to register and add your own.

## 5. Stop it

Close the terminal window, or press `Ctrl + C` in it.

## Good to know

- Your accounts and subscriptions are saved in the file `server/allsubs.db`. Delete that file to start from empty.
- The site only works on your own computer for now. Other people cannot open it until it is hosted on the internet.
- If the page says "Cannot reach the server", the site is not running: repeat step 3.
