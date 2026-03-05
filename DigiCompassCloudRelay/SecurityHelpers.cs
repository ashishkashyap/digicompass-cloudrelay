using System.Security.Cryptography;
using System.Text;

namespace DigiCompassCloudRelay;

public static class SecurityHelpers
{
    public static bool RelayKeyValid(string? provided)
    {
        if (string.IsNullOrWhiteSpace(provided)) return false;
        return TimingSafeEquals(provided.Trim(), RelayConfig.RelayApiKey);
    }

    public static string NewSixDigitCode()
    {
        var n = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return n.ToString("D6");
    }

    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static bool TimingSafeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}