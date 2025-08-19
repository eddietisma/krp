using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Krp.Tool.TerminalUi.Logging;

/// <summary>
/// Simple in-memory logger that keeps a bounded list of recent log entries.
/// </summary>
public sealed class InMemoryLoggingProvider : ILoggerProvider
{
    private readonly List<LogEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly Lock _sync = new();

    public InMemoryLoggingProvider(int maxEntries = 1000)
    {
        _maxEntries = maxEntries;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new MemoryLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    public int CountLogs()
    {
        lock (_sync)
        {
            return _entries.Count;
        }
    }

    public IEnumerable<LogEntry> ReadLogs(int skip, int take)
    {
        lock (_sync)
        {
            var available = _entries.Count - skip;
            if (available <= 0)
            {
                return [];
            }

            var count = take < available ? take : available;
            return _entries.GetRange(skip, count);
        }
    }

    internal void Add(LogEntry entry)
    {
        lock (_sync)
        {
            _entries.Add(entry);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveAt(0);
            }
        }
    }

    public record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message, Exception Exception);

    private sealed class MemoryLogger : ILogger
    {
        private readonly string _category;
        private readonly InMemoryLoggingProvider _provider;

        public MemoryLogger(InMemoryLoggingProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter(state, exception);
            _provider.Add(new LogEntry(DateTime.UtcNow, logLevel, _category, message, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}