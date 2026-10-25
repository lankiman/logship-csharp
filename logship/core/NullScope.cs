namespace Logship.Core;

/// <summary>A scope that does nothing. Logship does not record logging scopes (yet).</summary>
internal sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();

    private NullScope()
    {
    }

    public void Dispose()
    {
    }
}
