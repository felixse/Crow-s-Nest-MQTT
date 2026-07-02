namespace CrowsNestMqtt.MockBrokerTests;

using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Thread-safe in-memory <see cref="TextWriter"/> that captures every line the
/// broker writes via its injected log sink. Tests use this to observe both the
/// timestamped <c>[mock-eg-broker]</c> lines and the bare
/// <c>MOCK_EG_LISTENING_ON=</c> stdout line without polluting real stdout.
/// </summary>
internal sealed class CapturingLogWriter : TextWriter
{
    private readonly StringBuilder _buffer = new();
    private readonly List<string> _lines = new();
    private readonly object _sync = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_sync)
            {
                return _lines.ToArray();
            }
        }
    }

    public override void Write(char value)
    {
        lock (_sync)
        {
            if (value == '\n')
            {
                _lines.Add(_buffer.ToString().TrimEnd('\r'));
                _buffer.Clear();
            }
            else
            {
                _buffer.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }
        lock (_sync)
        {
            foreach (var ch in value)
            {
                if (ch == '\n')
                {
                    _lines.Add(_buffer.ToString().TrimEnd('\r'));
                    _buffer.Clear();
                }
                else
                {
                    _buffer.Append(ch);
                }
            }
        }
    }

    public override void WriteLine()
    {
        lock (_sync)
        {
            _lines.Add(_buffer.ToString());
            _buffer.Clear();
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            if (value is not null)
            {
                foreach (var ch in value)
                {
                    if (ch == '\n')
                    {
                        _lines.Add(_buffer.ToString().TrimEnd('\r'));
                        _buffer.Clear();
                    }
                    else
                    {
                        _buffer.Append(ch);
                    }
                }
            }
            _lines.Add(_buffer.ToString());
            _buffer.Clear();
        }
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for a captured line matching
    /// <paramref name="predicate"/>. Returns <c>true</c> if a match was seen
    /// within the deadline.
    /// </summary>
    public async Task<bool> WaitForLineAsync(Func<string, bool> predicate, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] snapshot;
            lock (_sync)
            {
                snapshot = _lines.ToArray();
            }
            if (snapshot.Any(predicate))
            {
                return true;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    public string RenderedLog
    {
        get
        {
            lock (_sync)
            {
                var sb = new StringBuilder();
                foreach (var line in _lines)
                {
                    sb.AppendLine(line);
                }
                if (_buffer.Length > 0)
                {
                    sb.AppendLine(_buffer.ToString());
                }
                return sb.ToString();
            }
        }
    }
}
