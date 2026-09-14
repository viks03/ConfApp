// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// An environment whose content root is a folder under the scratch area, so that
/// <c>DatabaseLocation.ResolveBackupFolder</c> and <c>HealthCheckService</c> look
/// at a folder the test made rather than at <c>backups/</c> in the repository.
/// </summary>
internal sealed class ScratchEnvironment : IWebHostEnvironment
{
    public ScratchEnvironment(string root)
    {
        ContentRootPath = root;
        WebRootPath     = Path.Combine(root, "wwwroot");

        Directory.CreateDirectory(ContentRootPath);
        Directory.CreateDirectory(WebRootPath);

        ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
        WebRootFileProvider     = new PhysicalFileProvider(WebRootPath);
    }

    public string        ApplicationName         { get; set; } = "ConferenceApp";
    public string        EnvironmentName         { get; set; } = "Development";
    public string        ContentRootPath         { get; set; }
    public string        WebRootPath             { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
    public IFileProvider WebRootFileProvider     { get; set; }
}

/// <summary>
/// A logger that keeps the lines. The background services have no other output:
/// "the backup is skipped because the database file is missing" exists only as a
/// line in the log, and that is exactly why it has to be read as a result.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _lines = new();
    private readonly object _gate = new();

    public IReadOnlyList<string> Lines { get { lock (_gate) return _lines.ToArray(); } }

    public string Text => string.Join(Environment.NewLine, Lines);

    public bool Has(LogLevel level, string contains) =>
        Lines.Any(l => l.StartsWith(level + ":", StringComparison.Ordinal)
                    && l.Contains(contains, StringComparison.Ordinal));

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                            Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var line = $"{logLevel}: {formatter(state, exception)}";
        if (exception != null) line += " | " + exception.GetType().Name + ": " + exception.Message;

        lock (_gate) _lines.Add(line);
    }
}
