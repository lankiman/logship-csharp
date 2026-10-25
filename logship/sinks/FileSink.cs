using System.Globalization;
using System.Text;
using Logship.Core;
using Logship.Formatting;
using Logship.Options;

namespace Logship.Sinks;

/// <summary>
/// Appends log lines to a file. A new file starts when the (UTC) date changes or the size limit would
/// be passed. The worker thread is the only thing that touches the file, so no locking is needed.
/// </summary>
internal sealed class FileSink : BatchingSink
{
    private const int MaxBatch = 512;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly FileSinkOptions _options;
    private readonly TimeProvider _time;
    private readonly string _directory;

    private FileStream? _stream;
    private StreamWriter? _writer;
    private string? _dateKey;
    private int _sequence;
    private long _size;
    private volatile string? _currentPath;

    public FileSink(FileSinkOptions options, TimeProvider? time = null)
        : base(options.BufferCap, Math.Min(MaxBatch, options.BufferCap), options.FlushInterval)
    {
        _options = options.Clone();
        _time = time ?? TimeProvider.System;
        _directory = Path.GetFullPath(_options.Directory);

        Start();
    }

    /// <summary>The file currently open for writing, or null when none is open.</summary>
    public string? CurrentFilePath => _currentPath;

    protected override async Task EmitBatchAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct)
    {
        try
        {
            var now = _time.GetUtcNow();

            foreach (var entry in batch)
            {
                var line = LineFormatter.Format(entry, _options.Format);
                var bytes = Utf8.GetByteCount(line) + 1; // + the newline

                EnsureWriter(now, bytes);

                await _writer!.WriteAsync(line).ConfigureAwait(false);
                await _writer.WriteAsync('\n').ConfigureAwait(false);
                _size += bytes;
            }

            // One flush per batch, not per line.
            await _writer!.FlushAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Start over with a fresh file handle on the next batch, then let the engine report it.
            CloseWriter();
            throw;
        }
    }

    protected override async ValueTask DisposeResourcesAsync()
    {
        var writer = _writer;
        _writer = null;
        _stream = null;
        _currentPath = null;

        if (writer is not null)
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EnsureWriter(DateTimeOffset now, int incomingBytes)
    {
        var dateKey = now.UtcDateTime.ToString(_options.DatePattern, CultureInfo.InvariantCulture);

        if (_writer is not null)
        {
            if (dateKey != _dateKey)
            {
                CloseWriter();
                _sequence = 0;
            }
            else if (_size > 0 && _size + incomingBytes > _options.MaxFileSizeBytes)
            {
                CloseWriter();
                _sequence++;
            }
        }

        if (_writer is null)
        {
            Open(dateKey);
        }
    }

    private void Open(string dateKey)
    {
        System.IO.Directory.CreateDirectory(_directory);

        // Skip over files that are already full, which also covers a restart in the middle of a day.
        var path = PathFor(dateKey, _sequence);
        while (File.Exists(path) && new FileInfo(path).Length >= _options.MaxFileSizeBytes)
        {
            _sequence++;
            path = PathFor(dateKey, _sequence);
        }

        // FileShare.Read lets you tail the file while it is being written.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
        _stream = stream;
        _writer = new StreamWriter(stream, Utf8, 4096);
        _size = stream.Length;
        _dateKey = dateKey;
        _currentPath = path;
    }

    private string PathFor(string dateKey, int sequence)
    {
        var name = _options.FileNamePattern.Replace("{date}", dateKey, StringComparison.Ordinal);
        if (sequence > 0)
        {
            var extension = Path.GetExtension(name);
            name = $"{name[..^extension.Length]}.{sequence}{extension}";
        }

        return Path.Combine(_directory, name);
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Closing a broken handle can fail; there is nothing more to do with it.
        }

        _writer = null;
        _stream = null;
        _currentPath = null;
        _size = 0;
    }
}
