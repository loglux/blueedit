using System.Text;
using Tui;

var back = Style.Rgb(0xAAAAAA, 0x0000AA);
var bar = Style.Rgb(0x000000, 0x00AAAA);
var text = new StringBuilder("hello");
string last = "(none)";
int events = 0;

string? stampPath = Environment.GetEnvironmentVariable("PROBE_STAMPS");
void Stamp(char tag) { if (stampPath != null) File.AppendAllText(stampPath, $"{tag} {DateTime.UtcNow.Ticks}\n"); }

static string Describe(InputEvent e) => e switch
{
    KeyInput { Key: Key.Character } k => $"char '{k.Character}'{ModText(k.Mods)}",
    KeyInput k => $"key {k.Key}{ModText(k.Mods)}",
    MouseInput m => $"mouse {m.Kind} button={m.Button} at {m.X},{m.Y}{ModText(m.Mods)}",
    PasteInput p => $"paste {p.Text.Length} chars",
    ResizeInput r => $"resize {r.Width}x{r.Height}",
    _ => e.ToString() ?? "?",
};

static string ModText(Mods m) => m == Mods.None ? "" : " +" + m.ToString().Replace(", ", "+");

using var host = new ConsoleHost();
host.Start();

void Draw()
{
    var s = host.Screen;
    s.Clear(back);
    s.Fill(0, 0, s.Width, 1, ' ', bar);
    s.Text(1, 0, "File  Edit  tui-probe", bar);
    s.Text(0, 2, text.ToString(), back);
    s.Text(1, 4, "last: " + last, back);
    s.Text(1, 5, $"size: {s.Width}x{s.Height}  events: {events}", back);
    s.SetCursor(Math.Min(text.Length, s.Width - 1), 2, true);
    host.Present();
}

Draw();
while (true)
{
    if (!host.TryNext(out var ev, 1000) || ev == null) continue;
    Stamp('K');
    events++;
    last = Describe(ev);
    if (ev is KeyInput q && q.Is('q', Mods.Ctrl)) break;
    switch (ev)
    {
        case KeyInput { Key: Key.Character, Mods: Mods.None or Mods.Shift } k: text.Append(k.Character); break;
        case KeyInput { Key: Key.Backspace, Mods: Mods.None }: if (text.Length > 0) text.Length--; break;
        case PasteInput p: text.Append(p.Text.Replace('\n', ' ').Replace("\r", "")); break;
        case ResizeInput r: host.ApplyResize(r.Width, r.Height); break;
    }
    Draw();
    Stamp('D');
}
