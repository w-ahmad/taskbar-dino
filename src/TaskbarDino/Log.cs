using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace TaskbarDino;

/// <summary>
/// Minimal diagnostics. Off by default (zero cost: the interpolated-string handler skips formatting
/// entirely). Enabled with <c>--log</c> or <c>TASKBARDINO_LOG=1</c>, which appends to
/// <c>%LOCALAPPDATA%\TaskbarDino\taskbardino.log</c>; messages also go to the debugger when attached.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static bool Enabled { get; private set; } = Debugger.IsAttached;

    public static string Directory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarDino");

    public static void EnableFile()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = Path.Combine(Directory, "taskbardino.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            {
                File.Delete(path);
            }

            _writer = new StreamWriter(path, append: true) { AutoFlush = true };
            Enabled = true;
        }
        catch (Exception)
        {
            // Logging is best-effort.
        }
    }

    public static void Info(ref Handler message) => Write("INF", ref message);

    public static void Warn(ref Handler message) => Write("WRN", ref message);

    public static void Error(Exception ex, string context)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.AppendAllText(Path.Combine(Directory, "crash.log"), $"{DateTimeOffset.Now:O} {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }

        if (Enabled)
        {
            Emit($"ERR {DateTime.Now:HH:mm:ss.fff} {context}: {ex}");
        }
    }

    private static void Write(string level, ref Handler message)
    {
        if (!message.Enabled)
        {
            return;
        }

        Emit($"{level} {DateTime.Now:HH:mm:ss.fff} {message.ToStringAndClear()}");
    }

    private static void Emit(string line)
    {
        lock (Gate)
        {
            if (Debugger.IsAttached)
            {
                Debug.WriteLine(line);
            }

            _writer?.WriteLine(line);
        }
    }

    /// <summary>Formats only when logging is enabled, so disabled log calls allocate nothing.</summary>
    [InterpolatedStringHandler]
    public ref struct Handler
    {
        private readonly StringBuilder? _sb;

        public Handler(int literalLength, int formattedCount, out bool shouldAppend)
        {
            shouldAppend = Log.Enabled;
            _sb = shouldAppend ? new StringBuilder(literalLength + (formattedCount * 8)) : null;
        }

        public bool Enabled => _sb is not null;

        public void AppendLiteral(string s) => _sb!.Append(s);

        public void AppendFormatted<T>(T value) => _sb!.Append(value);

        public void AppendFormatted<T>(T value, string? format) => _sb!.Append(value is IFormattable f ? f.ToString(format, null) : value?.ToString());

        public string ToStringAndClear() => _sb!.ToString();
    }
}
