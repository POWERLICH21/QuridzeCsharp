using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Subfuse.Server.Security;

/// <summary>Time-based one-time passwords (RFC 6238) as used by authenticator apps: SHA-1, 6 digits, 30-second steps.</summary>
public static class Totp
{
    private const int Digits = 6;
    private const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.Slice(offset, 4)) & 0x7FFFFFFF;
        return (binary % 1_000_000).ToString().PadLeft(Digits, '0');
    }

    /// <summary>
    /// Checks a code against the current step and one step either side (to allow for clock drift).
    /// Returns the matching step, or null. Steps at or before <paramref name="lastUsedStep"/> are refused.
    /// </summary>
    public static long? Verify(byte[] secret, string code, DateTimeOffset now, long lastUsedStep)
    {
        if (code.Length != Digits) return null;
        var given = Encoding.ASCII.GetBytes(code);
        var current = StepAt(now);
        long? match = null;
        for (var step = current - 1; step <= current + 1; step++)
        {
            var expected = Encoding.ASCII.GetBytes(Code(secret, step));
            if (CryptographicOperations.FixedTimeEquals(expected, given) && step > lastUsedStep) match = step;
        }
        return match;
    }

    /// <summary>The link an authenticator app reads from the QR code.</summary>
    public static string Uri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}" +
        $"?secret={Base32(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    public static string Base32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }
}
