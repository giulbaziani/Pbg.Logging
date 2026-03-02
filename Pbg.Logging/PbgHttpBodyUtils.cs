using System.Text;

namespace Pbg.Logging;

internal static class PbgHttpBodyUtils
{
    public static int GetMaxCaptureBytes(int maxChars)
    {
        return Math.Max(64, maxChars * 4);
    }

    public static bool IsTextLikeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var value = contentType.ToLowerInvariant();

        return value.StartsWith("text/")
               || value.Contains("json")
               || value.Contains("xml")
               || value.Contains("html")
               || value.Contains("x-www-form-urlencoded");
    }

    public static Encoding GetEncodingFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return Encoding.UTF8;
        }

        var parts = contentType.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!part.StartsWith("charset=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var charset = part["charset=".Length..].Trim().Trim('"');

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        return Encoding.UTF8;
    }

    public static string BytesToBodyString(byte[] bytes, string? contentType, int maxChars)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (IsTextLikeContentType(contentType))
        {
            var encoding = GetEncodingFromContentType(contentType);
            var text = encoding.GetString(bytes);
            return TrimToMaxLength(text, maxChars);
        }

        var base64 = Convert.ToBase64String(bytes);
        base64 = TrimToMaxLength(base64, maxChars);
        return $"[binary;base64]{base64}";
    }

    public static string TrimToMaxLength(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars];
    }
}
