using System.Text.Json;
using Game.Core;
using Game.Server;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Game.Server.Tests;

public sealed class StructuredObservabilityTests
{
    [Fact]
    public void LogJson_TickEntry_ContainsRequiredFields()
    {
        string json = LogJson.TickEntry(7, 2, 11, 13, 1.25);

        Assert.Contains("\"tick\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sessionCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"messagesIn\"", json, StringComparison.Ordinal);
        Assert.Contains("\"messagesOut\"", json, StringComparison.Ordinal);
        Assert.Contains("\"simStepMs\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsAndStructuredLogs_DoNotChangeDeterministicChecksum()
    {
        string checksumWithoutObservability = RunChecksumForTicks(100, enableMetrics: false, enableStructuredLogs: false);
        string checksumWithObservability = RunChecksumForTicks(100, enableMetrics: true, enableStructuredLogs: true);

        Assert.Equal(checksumWithoutObservability, checksumWithObservability);
    }

    [Fact]
    public void StructuredLogs_500Ticks_AreMonotonicAndContainRequiredFields()
    {
        using CapturingLoggerProvider provider = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(provider);
        });

        ServerConfig config = ServerConfig.Default(seed: 777) with
        {
            EnableStructuredLogs = true,
            EnableMetrics = true
        };

        ServerHost host = new(config, loggerFactory);
        for (int i = 0; i < 500; i++)
        {
            host.StepOnce();
        }

        List<string> entries = provider.Entries
            .Where(e => e.Category == typeof(ServerHost).FullName)
            .Select(e => e.Message)
            .Where(m => m.StartsWith("{\"tick\"", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(entries);

        int previousTick = 0;
        foreach (string entry in entries)
        {
            using JsonDocument doc = JsonDocument.Parse(entry);
            JsonElement root = doc.RootElement;

            Assert.True(root.TryGetProperty("tick", out JsonElement tickElement));
            Assert.True(root.TryGetProperty("sessionCount", out _));
            Assert.True(root.TryGetProperty("messagesIn", out _));
            Assert.True(root.TryGetProperty("messagesOut", out _));
            Assert.True(root.TryGetProperty("simStepMs", out _));

            int tick = tickElement.GetInt32();
            Assert.True(tick > previousTick);
            previousTick = tick;
        }

        string checksumWithObs = StateChecksum.Compute(host.CurrentWorld);
        string checksumWithoutObs = RunChecksumForTicks(500, enableMetrics: false, enableStructuredLogs: false);
        Assert.Equal(checksumWithoutObs, checksumWithObs);
    }

    private static string RunChecksumForTicks(int ticks, bool enableMetrics, bool enableStructuredLogs)
    {
        ServerConfig cfg = ServerConfig.Default(seed: 777) with
        {
            EnableMetrics = enableMetrics,
            EnableStructuredLogs = enableStructuredLogs
        };

        ServerHost host = new(cfg);
        host.AdvanceTicks(ticks);
        return StateChecksum.Compute(host.CurrentWorld);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly List<LogEntry> _entries;

        public CapturingLogger(string categoryName, List<LogEntry> entries)
        {
            _categoryName = categoryName;
            _entries = entries;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NoopScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Add(new LogEntry(_categoryName, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(string Category, string Message);

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
