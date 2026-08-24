using SilverScreen.Core.Common;
using SilverScreen.Core.Player;
using SilverScreen.Core.Player.Comments;
using SilverScreen.Core.Browsing.Common;
using SilverScreen.Core.Browsing.Home;
using SilverScreen.Core.Browsing.Channel;
using SilverScreen.Core.Browsing.Search;
using SilverScreen.Core.Browsing.History;
using SilverScreen.Core.Queue;
using SilverScreen.Core.Account.Session;
using SilverScreen.Core.Account.Profile;
using SilverScreen.Core.Preferences;
using SilverScreen.Infrastructure.Common;
using SilverScreen.Infrastructure.YouTube;
using SilverScreen.Infrastructure.Player;
using SilverScreen.Infrastructure.Player.Comments;
using SilverScreen.Infrastructure.Browsing.Common;
using SilverScreen.Infrastructure.Browsing.Home;
using SilverScreen.Infrastructure.Browsing.Channel;
using SilverScreen.Infrastructure.Browsing.Search;
using SilverScreen.Infrastructure.Browsing.History;
using SilverScreen.Infrastructure.Queue;
using SilverScreen.Infrastructure.Account.Session;
using SilverScreen.Infrastructure.Account.Auth;
using SilverScreen.Infrastructure.Account.Profile;
using SilverScreen.Infrastructure.Preferences;
using SilverScreen.Shell;
using SilverScreen.Browsing.Components;
using SilverScreen.Browsing.Home;
using SilverScreen.Browsing.Channel;
using SilverScreen.Browsing.Search;
using SilverScreen.Browsing.History;
using SilverScreen.Player;
using SilverScreen.Player.Views;
using SilverScreen.Player.Controllers;
using SilverScreen.Player.Comments;
using SilverScreen.Queue;
using SilverScreen.Account.Profile;
using SilverScreen.Account.Auth;
using SilverScreen.Account.Session;
using SilverScreen.Preferences;

using Serilog;
using Serilog.Core;
using Serilog.Events;

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
