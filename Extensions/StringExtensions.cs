using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static class StringExtensions
{
    public static string Hash(this string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash); // uppercase hex string
    }

    public static string ToSafeFileName(this string url)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var regex = new Regex($"[{Regex.Escape(invalid)}]+");
        return regex.Replace(url, "_");
    }
}
