// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;

namespace ConferenceApp.Services.Logging
{
    /// <summary>
    /// The file log settings, read from the <c>Logging:File</c> section.
    /// </summary>
    public sealed class FileLoggerOptions
    {
        /// <summary>Whether the file log runs at all. Off leaves only the
        /// console provider.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The folder for the log files. Empty means
        /// <c>&lt;content root&gt;/logs</c>. On a server whose application
        /// directory is read-only, a different path is set here.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>How many days of files are kept. Zero or less disables
        /// pruning entirely.</summary>
        public int RetainedDays { get; set; } = 30;

        /// <summary>
        /// Nothing below this level reaches the file. A second floor,
        /// independent of the rules in <c>Logging:File:LogLevel</c> that the
        /// logging framework applies per category (the provider's alias is
        /// "File").
        /// </summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    }

    /// <summary>
    /// [S-04 / E-02]: <c>Program.cs</c> registered no logging provider at all,
    /// so everything stayed in the default console. Started as a service that
    /// goes to journald, under <c>dotnet run</c> in tmux it is lost on restart,
    /// and the panel reads no log. Any "something failed in a background
    /// service" had nowhere it could be found the next day.
    /// <para>
    /// It is deliberately a hundred lines of local code rather than a package:
    /// all that is wanted is a text file rotated by day. Writing goes through
    /// one queue and one thread, so that neither a request nor a background
    /// service ever blocks on the log.
    /// </para>
    /// </summary>
    [ProviderAlias("File")]
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly FileLoggerOptions _options;
        private readonly string _directory;
        private readonly BlockingCollection<string> _lines = new(boundedCapacity: 4096);
        private readonly Thread _writer;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

        private DateOnly _currentDay = DateOnly.MinValue;
        private StreamWriter? _stream;
        private bool _disposed;

        public FileLoggerProvider(IOptions<FileLoggerOptions> options, string contentRootPath)
        {
            _options = options.Value;

            _directory = string.IsNullOrWhiteSpace(_options.Path)
                ? System.IO.Path.Combine(contentRootPath, "logs")
                : System.IO.Path.GetFullPath(_options.Path);

            Directory.CreateDirectory(_directory);

            _writer = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "file-logger"
            };
            _writer.Start();
        }

        public string Directory_ => _directory;

        public ILogger CreateLogger(string categoryName)
            => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

        internal bool IsEnabled(LogLevel level)
            => !_disposed && level != LogLevel.None && level >= _options.MinimumLevel;

        internal void Enqueue(string line)
        {
            // TryAdd, not Add: if the writer falls behind, a log line is lost,
            // but no request or background service blocks because of the log.
            _lines.TryAdd(line);
        }

        private void WriteLoop()
        {
            foreach (var line in _lines.GetConsumingEnumerable())
            {
                try
                {
                    var today = DateOnly.FromDateTime(DateTime.Now);

                    if (_stream is null || today != _currentDay)
                    {
                        _stream?.Dispose();
                        _currentDay = today;
                        _stream = new StreamWriter(
                            new FileStream(
                                System.IO.Path.Combine(_directory, $"conferenceapp-{today:yyyy-MM-dd}.log"),
                                FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                            // UTF8Encoding(false), not Encoding.UTF8: the latter
                            // writes a BOM at the start of each new daily file and
                            // the first line then stops matching under grep.
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                        { AutoFlush = true };

                        Prune();
                    }

                    _stream.WriteLine(line);
                }
                catch
                {
                    // A failure while writing the log must not bring the
                    // application down. Dropping the stream makes the next line
                    // reopen the file, which is the only recovery available here.
                    _stream = null;
                }
            }

            _stream?.Dispose();
            _stream = null;
        }

        private void Prune()
        {
            if (_options.RetainedDays <= 0) return;

            try
            {
                var cutoff = DateTime.Now.AddDays(-_options.RetainedDays);

                foreach (var file in System.IO.Directory.GetFiles(_directory, "conferenceapp-*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch
            {
                // An old log left behind is the smaller problem: pruning runs
                // on the writer thread and must not stop it.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lines.CompleteAdding();
            _writer.Join(TimeSpan.FromSeconds(5));
            _lines.Dispose();
        }

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _provider;
            private readonly string _category;

            public FileLogger(FileLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            // Scopes are not supported: the file format is one flat line per
            // entry and nothing in the application uses scoped logging.
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                                    Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message) && exception is null) return;

                var sb = new StringBuilder(256);
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(Short(logLevel)).Append("] ")
                  .Append(_category).Append(": ")
                  .Append(message);

                if (exception is not null)
                    sb.AppendLine().Append(exception);

                _provider.Enqueue(sb.ToString());
            }

            private static string Short(LogLevel level) => level switch
            {
                LogLevel.Trace       => "TRC",
                LogLevel.Debug       => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning     => "WRN",
                LogLevel.Error       => "ERR",
                LogLevel.Critical    => "CRT",
                _                    => "???"
            };
        }
    }
}
