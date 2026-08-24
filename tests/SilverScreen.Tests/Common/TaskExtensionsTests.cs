using Serilog;
using Serilog.Core;
using Serilog.Events;
using SilverScreen.Infrastructure.Common;

namespace SilverScreen.Tests.Common;

public sealed class TaskExtensionsTests
{
    [Fact]
    public void FireAndForget_NullTask_DoesNotThrow()
    {
        Task? task = null;
        task.FireAndForget();
    }

    [Fact]
    public void FireAndForget_CompletedTask_DoesNotLog()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        Task.CompletedTask.FireAndForget(logger);

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task FireAndForget_FaultedTask_LogsError()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var tcs = new TaskCompletionSource();
        var expectedException = new InvalidOperationException("Test fault");

        tcs.Task.FireAndForget(logger, "Custom fault message");
        tcs.SetException(expectedException);

        // Allow async continuation to complete
        for (var i = 0; i < 50 && sink.Events.Count == 0; i++)
            await Task.Delay(10);

        Assert.Single(sink.Events);
        var logEvent = sink.Events[0];
        Assert.Equal(LogEventLevel.Error, logEvent.Level);
        Assert.Same(expectedException, logEvent.Exception);
        Assert.Contains("Custom fault message", logEvent.RenderMessage());
    }

    [Fact]
    public async Task FireAndForget_CanceledTask_DoesNotLogError()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var cts = new CancellationTokenSource();
        var task = Task.Run(async () => { await Task.Delay(10, cts.Token); }, cts.Token);

        task.FireAndForget(logger);
        await cts.CancelAsync();

        await Task.Delay(50, CancellationToken.None);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task FireAndForget_ValueTask_Faulted_LogsError()
    {
        var sink = new CapturingSink();
        var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var expectedException = new InvalidOperationException("ValueTask fault");

        ThrowingValueTask().FireAndForget(logger);

        for (var i = 0; i < 50 && sink.Events.Count == 0; i++)
            await Task.Delay(10, CancellationToken.None);

        Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Error, sink.Events[0].Level);
        Assert.Same(expectedException, sink.Events[0].Exception);
        return;

        async ValueTask ThrowingValueTask()
        {
            await Task.Yield();
            throw expectedException;
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events)
            {
                Events.Add(logEvent);
            }
        }
    }
}