using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    static string NameOf(string? path) => path == null ? T("NONAME") : System.IO.Path.GetFileName(path);

    bool IsModified(int i) => i == cur ? editor.Modified : docs[i].Modified;

    string? PathOf(int i) => i == cur ? editor.Path : docs[i].Path;

    bool Pristine() => editor.Path == null && !editor.Modified && editor.Lines.Count == 1 && editor.Lines[0] == "";

    void Sync() => docs[cur] = editor.CaptureDoc();

    void Switch(int i)
    {
        if (i < 0 || i >= docs.Count || i == cur) return;
        Sync(); cur = i; editor.ApplyDoc(docs[cur]);
    }

    void NextDoc() => Switch((cur + 1) % docs.Count);

    void PrevDoc() => Switch((cur + docs.Count - 1) % docs.Count);

    void NewDoc()
    {
        Sync(); editor.NewFile(); docs.Add(editor.CaptureDoc()); cur = docs.Count - 1;
    }

    void OpenPath(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        Sync();
        int existing = docs.FindIndex(d => d.Path != null && string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) { Switch(existing); return; }
        if (Pristine()) { editor.Load(path); Sync(); return; }       // reuse the empty startup buffer
        editor.Load(path); docs.Add(editor.CaptureDoc()); cur = docs.Count - 1;
    }

    List<Recovery.Item> RecoveryItems()
    {
        var items = new List<Recovery.Item>();
        for (int i = 0; i < docs.Count; i++)
        {
            if (!IsModified(i)) continue;
            bool active = i == cur;
            var lines = active ? editor.Lines : docs[i].Lines;
            var enc = active ? editor.Encoding : docs[i].Enc;
            bool crLf = active ? editor.CrLf : docs[i].Eol == "\r\n";
            items.Add(new(PathOf(i), EditorView.TextOf(lines), FileEncodings.ShortName(enc), crLf));
        }
        return items;
    }

    void StoreRecovery(bool now = false)
    {
        if (settings.AutoRecoverSeconds == 0) { recovery.Store(Array.Empty<Recovery.Item>()); return; }
        if (!now && Environment.TickCount64 < recoveryDue) return;
        recoveryDue = Environment.TickCount64 + settings.AutoRecoverSeconds * 1000L;
        recovery.Store(RecoveryItems());
    }

    void OfferRecovery()
    {
        var left = Recovery.Left(recovery.Dir);
        foreach (var s in left.Where(s => s.Items.Count == 0)) Recovery.Remove(s);
        left = left.Where(s => s.Items.Count > 0).ToList();
        if (left.Count == 0) return;
        var items = left.SelectMany(s => s.Items).ToList();
        var when = left[^1].Written.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var names = string.Join("\n", items.Take(8).Select(i => "  " + NameOf(i.Path))) + (items.Count > 8 ? "\n  " + Tf("and {0} more", items.Count - 8) : "");
        var answer = Dialogs.Ask(app, T("Recover"),
            Tf("BlueEdit ended without saving (last copy {0}).\nUnsaved text of:\n{1}", when, names), new[] { Ui.Tc("recover", "Recover"), Ui.Tc("recover", "Discard"), T("Later") }, 2);
        if (answer == 2) return;
        if (answer == 0)
            foreach (var item in items)
            {
                if (item.Path != null) OpenPath(item.Path);
                else if (!Pristine()) NewDoc();
                editor.Recover(item.Path == null ? null : System.IO.Path.GetFullPath(item.Path), item.Text,
                    FileEncodings.FromShortName(item.Encoding) ?? new System.Text.UTF8Encoding(false), item.CrLf);
                Sync();
            }
        foreach (var s in left) Recovery.Remove(s);
        if (answer == 0) { Note(Tn(items.Count, "Recovered {0} document: F2 saves", "Recovered {0} documents: F2 saves")); StoreRecovery(true); }
    }

    // ---------- commands ----------
    void Save()
    {
        if (editor.ReadOnlyFile) { Note(T("Read-only (-r)")); return; }
        if (editor.Path == null) { SaveAs(); return; }
        if (editor.DiskDiffers &&
            Dialogs.Ask(app, T("Changed on disk"), Tf("{0} was changed by another program\nsince it was opened here. Overwrite it?", NameOf(editor.Path)), new[] { T("Overwrite"), T("Cancel") }, 1) != 0)
            return;
        if (!Write(editor.Save)) return;
        if (IsSettingsFile(editor.Path) && !editor.Modified) ReloadSettings();
    }

    // A file that cannot be written (locked by another program, no permission, a full disk) is said in a dialog and the
    // document stays unsaved, so closing still asks. Before, the exception ended the editor with every unsaved document.
    bool Write(Action save, string? path = null)
    {
        try { save(); StoreRecovery(true); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Ask(app, T("Not saved"), Tf("{0} could not be written:\n{1}", NameOf(path ?? editor.Path), ex.Message), new[] { T("OK") }, 0);
            return false;
        }
    }

    // Another program (git, a build, a coding assistant) changed the open file. Unchanged buffers follow it silently;
    // with unsaved edits the user decides. Either way Ctrl+Z gets the previous text back.
    void CheckDisk()
    {
        var name = NameOf(editor.Path);
        switch (editor.CheckDisk())
        {
            case EditorView.DiskChange.Changed when !editor.Modified:
                editor.ReloadFromDisk();
                Note(T("Reloaded: changed by another program (Ctrl+Z: previous text)"));
                if (IsSettingsFile(editor.Path)) ReloadSettings();
                break;
            case EditorView.DiskChange.Changed:
                var answer = Dialogs.Ask(app, T("Changed on disk"),
                    Tf("{0} was changed by another program,\nand it has unsaved edits here.", name), new[] { T("Reload (Ctrl+Z undoes)"), T("Keep mine") }, 1);
                if (answer == 0) { editor.ReloadFromDisk(); Note(T("Reloaded (Ctrl+Z: your edits)")); }
                else { editor.AcceptDisk(); Note(T("Kept your version; F2 overwrites the file")); }
                break;
            case EditorView.DiskChange.Deleted:
                editor.AcceptDisk();
                Note(Tf("{0} was deleted by another program; F2 saves it again", name));
                break;
        }
    }

    // For programs that open an editor and wait for it (git commit and others): save everything, then exit.
    void SaveAllAndQuit()
    {
        Sync();
        for (int i = 0; i < docs.Count; i++)
        {
            if (!IsModified(i)) continue;
            Switch(i);
            Save();
            if (editor.Modified) return;                              // cancelled or read-only: stay
        }
        app.RequestStop();
    }

    void SaveAs()
    {
        var p = Dialogs.SaveFile(app, editor.Path);
        if (p != null) Write(() => editor.SaveAs(p), p);
    }

    // true if it is OK to discard the active document (saved, discarded, or unmodified)
    bool CanClose()
    {
        if (!editor.Modified) return true;
        switch (Dialogs.ConfirmUnsaved(app, NameOf(editor.Path)))
        {
            case Dialogs.Choice.Save: Save(); return !editor.Modified;
            case Dialogs.Choice.Discard: return true;
            default: return false;
        }
    }

    void CloseDoc()
    {
        if (!CanClose()) return;
        if (editor.Path != null) lsp?.Close(editor.Path);
        docs.RemoveAt(cur);
        if (docs.Count == 0) { editor.NewFile(); docs.Add(editor.CaptureDoc()); cur = 0; }
        else { cur = Math.Min(cur, docs.Count - 1); editor.ApplyDoc(docs[cur]); }
    }

    // Esc with nothing to cancel: close this document (asking about unsaved changes); with the last one, leave.
    void EscapeClose()
    {
        if (docs.Count > 1) CloseDoc(); else Quit();
    }

    void Quit()
    {
        Sync();
        for (int i = 0; i < docs.Count; i++)
        {
            if (!IsModified(i)) continue;
            Switch(i);
            if (!CanClose()) return;      // Cancel (or a failed save) keeps the editor open on that document
        }
        app.RequestStop();
    }

    void Open()
    {
        var p = Dialogs.OpenFile(app, editor.Path == null ? null : System.IO.Path.GetDirectoryName(editor.Path));
        if (p != null) OpenPath(p);
    }

    int? PickEncoding(string title)
    {
        var items = FileEncodings.All.Select(c => c.Label).ToList();
        int now = FileEncodings.IndexOf(editor.Encoding);
        return Dialogs.Pick(app, title, items, Math.Max(0, now), current: now);
    }

    // The file opened as the wrong code page (mojibake): read the same bytes again in another one.
    void ReopenWithEncoding()
    {
        if (editor.Path == null || !File.Exists(editor.Path)) { Note(T("Not saved yet: nothing to reopen")); return; }
        if (editor.Modified)
        {
            var answer = Dialogs.Ask(app, T("Reopen"), Tf("Reopening drops the unsaved changes\nin {0}.", NameOf(editor.Path)), new[] { T("Reopen"), T("Cancel") }, 1);
            if (answer != 0) return;
        }
        if (PickEncoding(T("Reopen with encoding")) is not int i) return;
        int bad = editor.Reload(FileEncodings.All[i].Create());
        Note(bad == 0 ? Tf("Reopened as {0}", editor.EncodingName) : Tn(bad, "Reopened as {1}: {0} byte did not decode (shown as �)", "Reopened as {1}: {0} bytes did not decode (shown as �)", editor.EncodingName));
    }

    // The text is right; the file should be written in another encoding from now on.
    void ConvertEncoding()
    {
        if (PickEncoding(T("Save in encoding")) is not int i) return;
        var enc = FileEncodings.All[i].Create();
        var lost = editor.Unencodable(enc);
        if (lost.Count > 0)
        {
            var (r, c) = lost[0];
            var more = lost.Count >= 1000 ? "1000+" : lost.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var answer = Dialogs.Ask(app, T("Convert"),
                Tf("{0} characters cannot be stored in {1}.\nThey would be saved as '?'. First one: Ln {2}, Col {3}.", more, FileEncodings.ShortName(enc), r + 1, c + 1),
                new[] { T("Convert anyway"), T("Show first"), T("Cancel") }, 2);
            if (answer == 1) { editor.GoToLine(r + 1); editor.Col = c; return; }
            if (answer != 0) return;
        }
        editor.ConvertTo(enc);
        Note(Tf("Will save as {0} (F2)", editor.EncodingName));
    }

    void ToggleLineEndings()
    {
        editor.SetLineEndings(!editor.CrLf);
        Note(editor.CrLf ? T("Will save with CRLF (Windows) line endings (F2)") : T("Will save with LF (Unix) line endings (F2)"));
    }

    // File > Save all: every modified document with a name (a new one asks for its name).
    void SaveAll()
    {
        Sync();
        int back = cur, saved = 0;
        for (int i = 0; i < docs.Count; i++)
        {
            if (!IsModified(i)) continue;
            Switch(i);
            Save();
            if (!editor.Modified) saved++;
        }
        Switch(back);
        Note(saved == 0 ? T("Nothing to save") : Tn(saved, "Saved {0} file", "Saved {0} files"));
    }
}
