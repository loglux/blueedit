using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Tui;

/// <summary>
/// Display formulas as typeset pictures (#49, the second step; docs/formula-pictures.md): an optional module draws
/// them, the screen sends them to the terminal as Sixel graphics (<see cref="Screen.Picture"/>). Used when all of
/// these hold: the setting is on, the module is installed (a mathimages folder with BlueEdit.MathImages.dll: next to
/// the editor, in %LOCALAPPDATA%\BlueEdit, or where BLUEEDIT_MATHIMAGES points; "off" there turns it off), and the
/// terminal answered that it shows Sixel and how many pixels a cell is. Otherwise formulas are rows of text, as before.
/// Typing never waits: a formula not drawn yet is shown as text, and the layout is redone when its picture arrives.
/// </summary>
static class FormulaImages
{
    public delegate (byte[]? Mask, int Width, int Height, string? Error) RenderFn(string latex, float fontSize);

    /// <summary>Draws formulas: the module's, once loaded; tests put their own here.</summary>
    internal static RenderFn? Renderer;

    /// <summary>The setting ("formulaImages").</summary>
    public static bool Wanted { get; set; } = true;

    /// <summary>The terminal shows Sixel and reported this cell size (0 × 0: not, or no answer yet).</summary>
    public static (int W, int H) Cell { get; private set; }

    /// <summary>Why the module could not be used (shown once), or null.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>Called on the drawing thread when pictures arrived or were dropped (once for a burst of them): the
    /// editor lays its lines out again.</summary>
    public static Action? Changed;

    /// <summary>Pictures are drawn and used now.</summary>
    public static bool Active => Wanted && Cell.W > 0 && (Renderer != null || ModuleDir != null) && LoadError == null;

    // ---- the module ----
    static readonly Lazy<string?> Dir = new(FindModule);
    public static string? ModuleDir => Dir.Value;
    const string ModuleFile = "BlueEdit.MathImages.dll";

    static string? FindModule()
    {
        var env = Environment.GetEnvironmentVariable("BLUEEDIT_MATHIMAGES");
        if (env is "off" or "0") return null;
        string[] places = env is { Length: > 0 } ? [env] :
        [
            Path.Combine(AppContext.BaseDirectory, "mathimages"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "BlueEdit", "mathimages"),
        ];
        return places.FirstOrDefault(d => File.Exists(Path.Combine(d, ModuleFile)));
    }

    /// <summary>Loads the module into a context of its own (its CSharpMath and Skia beside the editor's runtime).</summary>
    static RenderFn? Load(string dir)
    {
        try
        {
            var context = new ModuleContext(dir);
            var type = context.LoadFromAssemblyPath(Path.Combine(dir, ModuleFile)).GetType("BlueEdit.MathImages.Renderer");
            if (type?.GetField("Api")?.GetValue(null) is not 1)
            {
                LoadError = "the mathimages module is of another version: install the one from this editor's release";
                return null;
            }
            return type.GetMethod("Render")!.CreateDelegate<RenderFn>();
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or TypeLoadException or ArgumentException or InvalidOperationException or MissingMethodException)
        {
            LoadError = "the mathimages module could not be loaded: " + ex.Message;
            return null;
        }
    }

    sealed class ModuleContext(string dir) : AssemblyLoadContext("mathimages")
    {
        // the module's own assemblies from its folder; the runtime's (System.*) from the editor
        protected override Assembly? Load(AssemblyName name)
        {
            var path = Path.Combine(dir, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string name)
        {
            foreach (var file in new[] { name, name + ".dll", name + ".so", "lib" + name + ".so", name + ".dylib", "lib" + name + ".dylib" })
            {
                var path = Path.Combine(dir, file);
                if (File.Exists(path)) return LoadUnmanagedDllFromPath(path);
            }
            return IntPtr.Zero;
        }
    }

    // ---- pictures ----
    // A formula's picture for the current cell size; null: it could not be drawn (then text). Kept per formula text.
    // Drawn ones wait in Done until the editor takes them (Taken) and lays its lines out again: the layout and the
    // drawing of one frame must see the same pictures.
    static readonly ConcurrentDictionary<string, CellPicture?> Done = new();
    static readonly Dictionary<string, CellPicture?> Shown = new();          // the main thread's
    // Waiting formulas, the last asked for first: the layout asks for every formula of the file in order, then the
    // drawing asks again for those on the screen, which puts them at the front.
    static readonly LinkedList<string> Queue = new();
    static readonly Dictionary<string, LinkedListNode<string>> Queued = new();
    static readonly object Lock = new();
    static Thread? _worker;
    static int _generation, _changePending;
    static string? _drawing;

    /// <summary>The terminal's answer: Sixel or not, and the cell size. A new size drops the pictures drawn for the old.</summary>
    public static void SetTerminal(bool sixel, (int W, int H)? cell)
    {
        var now = sixel && cell is { W: > 0, H: > 0 } c ? c : (0, 0);
        if (now == Cell) return;
        lock (Lock)
        {
            Cell = now;
            _generation++;
            Done.Clear(); Queue.Clear(); Queued.Clear();
        }
        Shown.Clear();
        Changed?.Invoke();
    }

    /// <summary>The picture of a display formula, or null when it is not ready (it is asked for, and
    /// <see cref="Changed"/> follows) or cannot be drawn.</summary>
    public static CellPicture? Get(string tex)
    {
        if (!Active) return null;
        if (Shown.TryGetValue(tex, out var pic)) return pic;
        lock (Lock)
        {
            if (tex == _drawing || Done.ContainsKey(tex)) return null;
            if (Queued.TryGetValue(tex, out var node)) Queue.Remove(node);
            Queued[tex] = Queue.AddFirst(tex);
            if (_worker == null) { _worker = new Thread(Work) { IsBackground = true, Name = "formula-images" }; _worker.Start(); }
            Monitor.Pulse(Lock);
        }
        return null;
    }

    static void Work()
    {
        while (true)
        {
            string tex; int generation; (int W, int H) cell;
            lock (Lock)
            {
                while (Queue.Count == 0) Monitor.Wait(Lock);
                tex = Queue.First!.Value;
                Queue.RemoveFirst();
                Queued.Remove(tex);
                _drawing = tex;
                generation = _generation; cell = Cell;
            }
            if (Renderer == null && LoadError == null && ModuleDir is { } dir) Renderer = Load(dir);
            var render = Renderer;
            if (render == null)                                          // LoadError says why: text from now on
            {
                lock (Lock) _drawing = null;
                Changed?.Invoke();
                continue;
            }
            var pic = Draw(render, tex, cell);
            lock (Lock)
            {
                _drawing = null;
                if (generation != _generation) continue;                 // the cell size changed meanwhile
                Done[tex] = pic;
            }
            // one relayout for a burst: the next is asked for only after the editor took this one
            if (Interlocked.Exchange(ref _changePending, 1) == 0) Changed?.Invoke();
        }
    }

    /// <summary>The editor takes the pictures drawn so far (on the main thread, before laying out again); the next
    /// one drawn calls <see cref="Changed"/> again.</summary>
    public static void Taken()
    {
        Interlocked.Exchange(ref _changePending, 0);
        if (Shown.Count > 4000) Shown.Clear();
        foreach (var tex in Done.Keys)
            if (Done.TryRemove(tex, out var pic)) Shown[tex] = pic;
    }

    /// <summary>A font a little smaller than a row: a fraction then takes two or three rows, as its text picture does.</summary>
    internal static float FontSize((int W, int H) cell) => cell.H * 0.75f;

    internal static CellPicture? Draw(RenderFn render, string tex, (int W, int H) cell)
    {
        (byte[]? Mask, int Width, int Height, string? Error) r;
        try { r = render(tex, FontSize(cell)); }
#pragma warning disable CA1031 // anything the module throws on a formula: that formula stays text
        catch (Exception) { return null; }
#pragma warning restore CA1031
        if (r.Mask is not { } mask) return null;
        // centred in whole cells
        int cols = (r.Width + cell.W - 1) / cell.W, rows = (r.Height + cell.H - 1) / cell.H;
        int pw = cols * cell.W, ox = (pw - r.Width) / 2, oy = (rows * cell.H - r.Height) / 2;
        var full = new byte[pw * rows * cell.H];
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++) full[(oy + y) * pw + ox + x] = Bolder[mask[y * r.Width + x]];
        return new CellPicture(full, cols, rows, cell.W, cell.H);
    }

    // Edges a little darker than their coverage: Windows Terminal scales Sixel pixels up to the font's (10 × 20 to
    // 14 × 28 in the window measured), and thin strokes came out pale next to the text
    static readonly byte[] Bolder = Enumerable.Range(0, 256).Select(a => (byte)Math.Round(255 * Math.Pow(a / 255.0, 0.6))).ToArray();

    /// <summary>For tests: nothing waits to be drawn.</summary>
    internal static bool Idle { get { lock (Lock) return Queue.Count == 0 && _drawing == null; } }

    /// <summary>For tests: forget every picture.</summary>
    internal static void Reset()
    {
        lock (Lock) { Done.Clear(); Queue.Clear(); Queued.Clear(); _generation++; }
        Shown.Clear();
        Interlocked.Exchange(ref _changePending, 0);
    }
}
