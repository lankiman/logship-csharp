using System.Text;

namespace Logship.Formatting;

/// <summary>
/// JSON writers reject text containing a lone UTF-16 surrogate (for example a string cut in the
/// middle of an emoji). A log line should still get through, so such characters are replaced
/// with U+FFFD before writing.
/// </summary>
internal static class TextSafety
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                return Replace(value);
            }

            if (char.IsLowSurrogate(c))
            {
                return Replace(value);
            }
        }

        return value;
    }

    private static string Replace(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(c).Append(value[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                builder.Append('\uFFFD');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
