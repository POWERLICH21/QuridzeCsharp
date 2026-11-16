using System.Text.RegularExpressions;

namespace Subfuse.Server;

/// <summary>Input rules shared by every endpoint. The lists mirror the ones in wwwroot/app.js.</summary>
public static partial class Rules
{
    public const int MaxName = 100;
    public const int MaxCategory = 40;
    public const int MinPassword = 8;
    public const int MaxPassword = 200;
    public const decimal MaxAmount = 1_000_000_000m;

    public static readonly HashSet<string> Currencies = ["USD", "EUR", "GBP", "GEL", "JPY", "INR", "BRL", "CAD", "AUD", "TRY"];
    public static readonly HashSet<string> Cycles = ["weekly", "monthly", "quarterly", "yearly"];
    public static readonly HashSet<string> Themes = ["system", "light", "dark"];

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    public static bool IsEmail(string? s) => s is { Length: <= 254 } && EmailPattern().IsMatch(s);

    public static string NormalizeEmail(string s) => s.Trim().ToLowerInvariant();

    /// <summary>Null when the password is acceptable, otherwise the message to show.</summary>
    public static string? PasswordProblem(string? password) => password switch
    {
        null or { Length: < MinPassword } => $"Password must be at least {MinPassword} characters.",
        { Length: > MaxPassword } => "Password is too long.",
        _ => null,
    };

    /// <summary>A 400 response in the shape the front end reads: { "error": "..." }.</summary>
    public static IResult Bad(string message) => Results.BadRequest(new { error = message });
}
