using System.Globalization;

namespace Logship.Options;

internal static class FileOptionsValidator
{
    public static List<string> Validate(FileSinkOptions o)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(o.Directory))
            errors.Add("Directory is required.");
        if (string.IsNullOrWhiteSpace(o.FileNamePattern) || !o.FileNamePattern.Contains("{date}"))
            errors.Add("FileNamePattern must contain {date}.");
        if (!IsValidDatePattern(o.DatePattern))
            errors.Add("DatePattern is not a valid date format for file names.");
        if (o.MaxFileSizeBytes < 1) errors.Add("MaxFileSizeBytes must be at least 1.");
        if (o.FlushInterval <= TimeSpan.Zero) errors.Add("FlushInterval must be greater than zero.");
        if (o.BufferCap < 1) errors.Add("BufferCap must be at least 1.");

        return errors;
    }

    private static bool IsValidDatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        try
        {
            var text = DateTime.UtcNow.ToString(pattern, CultureInfo.InvariantCulture);
            return text.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}