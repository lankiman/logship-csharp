namespace Logship.Options;

/// <summary>Works out which sink settings apply to a logger category.</summary>
internal static class TargetResolver
{
    public static (LokiSinkOptions? Loki, FileSinkOptions? File) Resolve(string category, LogshipOptions options)
    {
        var target = FindTarget(category, options.Targets);
        if (target is null)
        {
            return (options.Loki, options.File);
        }

        var inherit = target.InheritDefaults;
        return (
            target.Loki ?? (inherit ? options.Loki : null),
            target.File ?? (inherit ? options.File : null));
    }

    private static LogshipTargetOptions? FindTarget(string category, Dictionary<string, LogshipTargetOptions>? targets)
    {
        if (targets is null)
        {
            return null;
        }

        LogshipTargetOptions? best = null;
        var bestLength = -1;

        foreach (var (name, target) in targets)
        {
            if (string.IsNullOrWhiteSpace(name) || target is null || name.Length <= bestLength)
            {
                continue;
            }

            if (Matches(category, name))
            {
                best = target;
                bestLength = name.Length;
            }
        }

        return best;
    }

    // "Payments" matches "Payments" and "Payments.Service", but not "PaymentsX".
    private static bool Matches(string category, string name)
        => category.Equals(name, StringComparison.OrdinalIgnoreCase)
           || (category.Length > name.Length
               && category.StartsWith(name, StringComparison.OrdinalIgnoreCase)
               && category[name.Length] == '.');
}
