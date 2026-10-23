using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// One language server process spoken to over stdin/stdout with JSON-RPC 2.0 framed by "Content-Length" headers
/// (the Language Server Protocol's base protocol). Requests get a task with the result; notifications from the
/// server go to <see cref="Notification"/>; requests from the server (configuration, progress, registration) are
/// answered with an empty result so the server never waits on us. Started on its own threads; never blocks the
/// caller except for <see cref="Request"/>'s task when awaited.
/// </summary>
sealed class LspClient : IDisposable
{
    readonly Process _process;
    readonly IntPtr _job;
    readonly Stream _in;
    readonly object _writeLock = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    int _nextId;
    volatile bool _closed;

    /// <summary>A notification from the server: method and params (on the reader thread).</summary>
    public event Action<string, JsonNode?>? Notification;
    /// <summary>The answer to the server's workspace/configuration for a section (null: the whole configuration), on
    /// the reader thread; without it every item is answered with null, the server's defaults.</summary>
    public Func<string?, JsonNode?>? Configuration { get; set; }

    /// <summary>The server's workspace/applyEdit (after a command it ran): the WorkspaceEdit; true when it is taken.</summary>
    public Func<JsonNode?, bool>? ApplyEdit { get; set; }
    /// <summary>The server's process ended (or its output closed).</summary>
    public event Action? Exited;
    public bool Running => !_closed && !_process.HasExited;
    /// <summary>Lines the server wrote to stderr (the last ones), for "why did it fail".</summary>
    public string LastError { get; private set; } = "";

    public LspClient(string command, IReadOnlyList<string> args, string workingDirectory)
    {
        var psi = StartInfo(command, args);
        psi.WorkingDirectory = workingDirectory;
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        _process = Process.Start(psi) ?? throw new InvalidOperationException(command + " did not start");
        _job = JobObject.KillOnClose(_process);
        _in = _process.StandardInput.BaseStream;
        _process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) LastError = d; };
        _process.BeginErrorReadLine();
        new Thread(ReadLoop) { IsBackground = true, Name = "LSP " + Path.GetFileName(command) }.Start();
    }

    /// <summary>How to start <paramref name="command"/> on Windows: a name is looked up on PATH with PATHEXT, and
    /// npm's .cmd shims (pyright-langserver.cmd) run through cmd.exe.</summary>
    internal static ProcessStartInfo StartInfo(string command, IReadOnlyList<string> args)
    {
        var exe = Resolve(command) ?? throw new FileNotFoundException($"{command} not found on PATH");
        ProcessStartInfo psi;
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // cmd /s /c drops the first and the last quote of what follows, so the whole line is quoted once more:
            // without that a shim under "C:\Program Files\nodejs" was cut at the space (measured with npm.cmd)
            psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                "/d /s /c \"" + string.Join(" ", args.Prepend(exe).Select(CmdQuote)) + "\"");
        }
        else
        {
            psi = new ProcessStartInfo(exe);
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        CodeTools.AddToEnvironment(psi);
        return psi;
    }

    // one argument for cmd.exe: quoted when it has a space or a character cmd would act on
    internal static string CmdQuote(string a) =>
        a.Length > 0 && a.IndexOfAny([' ', '\t', '&', '|', '<', '>', '^', '(', ')', '"']) < 0 ? a : "\"" + a.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>The full path of a command: as given when it is a path, otherwise found on PATH (with PATHEXT
    /// extensions, %USERPROFILE%\.dotnet\tools where dotnet global tools go, pip's per-user Scripts folders, and the
    /// editor's own tools folder, CodeTools); null when not found.</summary>
    public static string? Resolve(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;
        // elsewhere the name itself, and only an executable file. WSL's PATH also lists Windows folders (/mnt/c/...):
        // npm's shims there look executable but need Windows' node, so those folders are skipped
        if (!OperatingSystem.IsWindows())
        {
            bool wsl = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") != null;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // ~/.local/bin too: tools/lsp-setup/install.sh puts pyright there (npm -g would need root), and a shell
            // started before that folder existed does not have it on PATH yet
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                         .Append(Path.Combine(home, ".local", "bin")).Append(Path.Combine(home, ".dotnet", "tools")).Concat(CodeTools.BinDirs))
            {
                if (wsl && d.StartsWith("/mnt/", StringComparison.Ordinal)) continue;
                var p = Path.Combine(d, command);
                if (File.Exists(p) && (File.GetUnixFileMode(p) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0) return p;
            }
            return null;
        }
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        // also where `pip install --user` puts programs (black.exe): %APPDATA%\Python\Python3xx\Scripts, which pip
        // leaves off PATH and only warns about
        var pipUser = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Python");
        IEnumerable<string> pipScripts = [];
        try { if (Directory.Exists(pipUser)) pipScripts = Directory.GetDirectories(pipUser, "Python3*").OrderDescending().Select(d => Path.Combine(d, "Scripts")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"))
            .Concat(pipScripts)
            .Concat(CodeTools.BinDirs);                                     // what Tools > Code tools installed
        foreach (var d in dirs)
        {
            foreach (var e in exts)
            {
                var p = Path.Combine(d.Trim('"'), command + e);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    /// <summary>Sends a request; the task completes with the result (or faults with the server's error).</summary>
    public Task<JsonNode?> Request(string method, JsonNode? parameters)
    {
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        if (!Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }))
        {
            _pending.TryRemove(id, out _);
            tcs.TrySetException(new InvalidOperationException("the language server is not running"));
        }
        return tcs.Task;
    }

    public void Notify(string method, JsonNode? parameters) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

    bool Send(JsonObject message)
    {
        if (_closed) return false;
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        try
        {
            lock (_writeLock)
            {
                _in.Write(header);
                _in.Write(body);
                _in.Flush();
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Close(); return false; }
    }

    void ReadLoop()
    {
        var output = _process.StandardOutput.BaseStream;
        try
        {
            while (!_closed)
            {
                int length = ReadHeaders(output);
                if (length < 0) break;
                var body = new byte[length];
                int got = 0;
                while (got < length)
                {
                    int n = output.Read(body, got, length - got);
                    if (n <= 0) { Close(); return; }
                    got += n;
                }
                Dispatch(JsonNode.Parse(body));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or JsonException or InvalidOperationException) { }
        Close();
    }

    // "Content-Length: N\r\n" and maybe other headers, then an empty line; -1 at the end of the stream
    static int ReadHeaders(Stream s)
    {
        int length = -1;
        var line = new StringBuilder();
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) return -1;
            if (b == '\n')
            {
                var l = line.ToString().TrimEnd('\r');
                line.Clear();
                if (l.Length == 0) { if (length >= 0) return length; continue; }
                int colon = l.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0 && l[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    length = int.Parse(l[(colon + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
            else line.Append((char)b);
        }
    }

    void Dispatch(JsonNode? msg)
    {
        if (msg is not JsonObject o) return;
        var method = (string?)o["method"];
        var id = o["id"];
        if (method == null && id != null)                                    // a response to one of our requests
        {
            if (_pending.TryRemove((int)id, out var tcs))
            {
                if (o["error"] is { } err) tcs.TrySetException(new InvalidOperationException((string?)err["message"] ?? "language server error"));
                else tcs.TrySetResult(o["result"]?.DeepClone());
            }
            return;
        }
        if (method != null && id != null)                                    // a request from the server: answer it
        {
            JsonNode? result = method switch
            {
                "workspace/configuration" when o["params"]?["items"] is JsonArray items =>
                    new JsonArray(items.Select(i => Configuration?.Invoke((string?)i?["section"])).ToArray()),
                "workspace/applyEdit" => new JsonObject { ["applied"] = ApplyEdit?.Invoke(o["params"]?["edit"]?.DeepClone()) ?? false },
                _ => null,
            };
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result });
            return;
        }
        if (method != null) Notification?.Invoke(method, o["params"]);
    }

    void Close()
    {
        if (_closed) return;
        _closed = true;
        foreach (var p in _pending.Values) p.TrySetException(new InvalidOperationException("the language server stopped"));
        _pending.Clear();
        Exited?.Invoke();
    }

    public void Dispose()
    {
        if (!_closed)
        {
            try { Request("shutdown", null).Wait(1000); } catch (AggregateException) { }
            Notify("exit", null);
        }
        Close();
        try { if (!_process.HasExited && !_process.WaitForExit(500)) _process.Kill(true); } catch (InvalidOperationException) { }
        if (_job != IntPtr.Zero) JobObject.Close(_job);
        _process.Dispose();
    }
}
