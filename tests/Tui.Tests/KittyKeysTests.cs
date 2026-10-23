using System.Collections.Concurrent;
using System.Text;
using Tui;
using Xunit;

/// <summary>The kitty keyboard protocol (#184): asked for only from a terminal that answered its query.</summary>
public sealed class KittyKeysTests
{
    sealed class FakePlatform(bool hasKeyEvents) : ITerminalPlatform
    {
        readonly BlockingCollection<string> _input = new();
        readonly MemoryStream _output = new();

        public void Send(string text) => _input.Add(text);

        public string Written
        {
            get { lock (_output) return Encoding.UTF8.GetString(_output.ToArray()); }
        }

        public int ReadInput(char[] buffer)
        {
            var s = _input.Take();
            s.CopyTo(0, buffer, 0, s.Length);
            return s.Length;
        }

        public Stream Output => new LockedStream(_output);
        public (int Width, int Height) Size => (80, 25);
        public void EnterRawMode() { }
        public void RestoreMode() { }
        public bool HasKeyEvents => hasKeyEvents;
        public void Dispose() { }
    }

    sealed class LockedStream(MemoryStream inner) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count) { lock (inner) inner.Write(buffer, offset, count); }
        public override void Flush() { }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    const string Query = "\u001b[?u", Push = "\u001b[>5u", Pop = "\u001b[<u";

    static void WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until) Thread.Sleep(10);
        Assert.True(done());
    }

    [Fact]
    public void Pushed_after_the_terminal_answered_and_popped_when_off_and_at_the_end()
    {
        var platform = new FakePlatform(hasKeyEvents: false);
        var host = new ConsoleHost(platform);
        host.UseKeyEvents(true);
        host.Start();
        Assert.Contains(Query, platform.Written);
        Assert.DoesNotContain(Push, platform.Written);
        platform.Send("\u001b[?0u");
        WaitFor(() => platform.Written.Contains(Push, StringComparison.Ordinal));
        host.UseKeyEvents(false);
        Assert.EndsWith(Pop, platform.Written);
        host.UseKeyEvents(true);
        Assert.EndsWith(Push, platform.Written);
        host.Dispose();
        Assert.Contains(Pop, platform.Written[platform.Written.LastIndexOf(Push, StringComparison.Ordinal)..]);
    }

    [Fact]
    public void A_terminal_that_does_not_answer_gets_only_the_query()
    {
        var platform = new FakePlatform(hasKeyEvents: false);
        var host = new ConsoleHost(platform);
        host.UseKeyEvents(true);
        host.Start();
        host.Dispose();
        Assert.DoesNotContain(Push, platform.Written);
        Assert.DoesNotContain(Pop, platform.Written);
    }

    [Fact]
    public void Off_by_the_setting_or_with_the_consoles_own_key_events_nothing_is_asked()
    {
        foreach (var (hasKeyEvents, on) in new[] { (false, false), (true, true) })
        {
            var platform = new FakePlatform(hasKeyEvents);
            var host = new ConsoleHost(platform);
            host.UseKeyEvents(on);
            host.Start();
            host.Dispose();
            Assert.DoesNotContain(Query, platform.Written);
        }
    }
}
