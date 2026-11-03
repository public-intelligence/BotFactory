using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace BotFactory.Core.Entities;

/// <summary>
/// Generates the opaque, non-enumerable key used in a bot's public/embed URL
/// (<see cref="Bot.PublicId"/>). 128 bits of CSPRNG entropy encoded as 22
/// base62 characters — URL-safe without escaping and infeasible to guess.
/// </summary>
public static class PublicIdGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const int Length = 22;

    public static string NewId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        var value = new BigInteger(bytes, isUnsigned: true);

        var sb = new StringBuilder(Length);
        for (var i = 0; i < Length; i++)
        {
            value = BigInteger.DivRem(value, Alphabet.Length, out var remainder);
            sb.Append(Alphabet[(int)remainder]);
        }
        return sb.ToString();
    }
}
