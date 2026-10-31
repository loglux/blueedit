using System.Collections.Concurrent;
using Tui;
using Xunit;

/// <summary>The window's size is polled (docs/tech/tui.md): a change is reported however early it comes.</summary>
public sealed class ConsoleHostSizeTests
{
    sealed class SizedPlatform : ITerminalPlatform
    {
        readonly BlockingCollection<string> _input = new();

        public volatile int Width = 140;

        public int ReadInput(char[] buffer) { _input.Take(); return 0; }
        public Stream Output { get; } = new MemoryStream();
        public (int Width, int Height) Size => (Width, 25);
        public void EnterRawMode() { }
        public void RestoreMode() { }
        public void Dispose() { }
    }

    static ResizeInput? NextResize(ConsoleHost host, int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until)
            if (host.TryNext(out var ev, 50) && ev is ResizeInput r) return r;
        return null;
    }

    [Fact]
    public void A_size_change_between_creating_the_screen_and_starting_is_reported()
    {
        var platform = new SizedPlatform();
        using var host = new ConsoleHost(platform);
        Assert.Equal(140, host.Screen.Width);
        platform.Width = 70;                                     // a pane split while the editor starts up
        host.Start();
        Assert.Equal(70, NextResize(host, 2000)?.Width);
    }

    [Fact]
    public void A_change_after_starting_is_reported_and_no_change_is_not()
    {
        var platform = new SizedPlatform();
        using var host = new ConsoleHost(platform);
        host.Start();
        Assert.Null(NextResize(host, 300));
        platform.Width = 100;
        Assert.Equal(100, NextResize(host, 2000)?.Width);
    }
}
