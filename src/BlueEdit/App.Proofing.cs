using System.Text.Json.Nodes;
using Tui;

sealed partial class App
{
    // "auto": the system's languages, on Windows those added there that it can spell-check, elsewhere the locale's
    // (falls back to the list in the settings)
    internal static string[] SpellLanguagesFor(Settings s) =>
        s.SpellAuto && Spell.SystemLanguages() is { Count: > 0 } system ? system.ToArray() : s.SpellLanguages.ToArray();

    ISpellProvider CreateSpell(Settings s, string[] langs) =>
        !OperatingSystem.IsWindows() || s.SpellProvider == "hunspell" ? new HunspellSpell(userWords, langs)
        : s.SpellProvider == "office" && OfficeSpell.Installed ? new OfficeSpell(userWords, langs) : new Spell(userWords, langs);

    void ReportGrammarFailures()
    {
        if (grammar == null) return;
        var news = grammar.All.Where(c => !c.Available && c.Status is { Length: > 0 } && grammarFailuresShown.Add(c.Name + c.Status))
            .Select(c => Tf("Grammar ({0}): {1}", c.Name, c.Status)).ToList();
        // a paragraph a checker could not check (the others go on)
        foreach (var c in grammar.All)
            if (c.LastFailure is { } f && grammarFailuresShown.Add(c.Name + f)) news.Add(Tf("Grammar ({0}): {1}", c.Name, f));
        if (news.Count > 0) Note(string.Join("; ", news));
    }

    // F7: a misspelled word at the caret first, otherwise a grammar finding there.
    void SpellFix()
    {
        var word = editor.WordAtCursor();
        bool mixed = word is { } wm && Lookalike.IsMixed(wm.Word);
        // a command in a formula ("\frac") is not a word: Hunspell called "frac" a typo and F7 hid the formula's error
        bool inFormula = editor.MathIn(editor.Row).Any(m => editor.Col >= m.Start && editor.Col <= m.Start + m.Length);
        bool typo = word is { } ww && !inFormula && (mixed || spell.Available && spell.IsCorrect(ww.Word) == false);
        if (!typo && editor.DiagnosticsAtCaret() is { Count: > 0 } ds) { QuickFix(ds); return; }
        if (!typo && editor.GrammarIssuesAtCaret() is { } g) { GrammarFix(g.Issues, g.First, g.Last); return; }
        if (mixed && word is { } mw)
        {
            // typed partly in the other layout: offer the word in one alphabet first
            var fixedWord = Lookalike.Fix(mw.Word);
            var options = (fixedWord != null ? new[] { fixedWord } : Array.Empty<string>()).Concat(spell.Available ? spell.Suggest(mw.Word) : new()).Distinct().ToList();
            if (Dialogs.Fix(app, Tf("Spelling: \"{0}\"", mw.Word), options, T("Latin and Cyrillic letters mixed: typed partly in the other layout."), [], "") is int k)
                editor.ReplaceWord(mw.Start, mw.Length, options[k]);
            return;
        }
        if (!spell.Available) { Note(spell.Error is { } e ? Tf("Spell check unavailable: {0}", e.Trim()) : T("Spell check unavailable")); return; }
        if (word is not { } w) { Note(T("No word here")); return; }
        if (!Scripts.Readable(spell.Languages, w.Word))
        {
            Note(Tf("\"{0}\" is not checked: no spelling language is written in its letters (now {1}; Tools > Spelling languages)", w.Word, string.Join(", ", spell.Languages)));
            return;
        }
        if (spell.IsCorrect(w.Word) is null) { Note(Tf("Still checking \"{0}\" ({1}), try again in a moment", w.Word, spell.Name)); return; }
        if (spell.IsCorrect(w.Word) == true) { Note(Tf("\"{0}\" is correct", w.Word)); return; }
        if (spell.Name != "Windows") { Note(Tf("Asking {0}...", spell.Name)); app.Render(); }
        var sugg = spell.Suggest(w.Word);
        // as the grammar window: the suggestions as buttons, then the dictionary and Ignore, the speller at the right
        var pick = Dialogs.Fix(app, Tf("Spelling: \"{0}\"", w.Word), sugg, "",
            [T("Add to dictionary"), T("Ignore")], Tf("{0} spelling", spell.Name));
        if (pick is not int i) return;
        if (i < sugg.Count) editor.ReplaceWord(w.Start, w.Length, sugg[i]);
        else if (i == sugg.Count) spell.AddToDictionary(w.Word);
        else spell.Ignore(w.Word);
    }

    // Shift+F7: the next spelling or grammar problem, selected so F7 fixes it.
    void NextProblem(bool backward)
    {
        if (!editor.SpellOn && !editor.GrammarOn && lsp == null && !editor.LatexCheckOn) { Note(T("Spelling and grammar checking are off (F8, Tools)")); return; }
        var from = Here();
        Note(editor.GoToNextProblem(backward) ?? (editor.GrammarOn ? T("No problems found (grammar may still be checking)") : T("No spelling problems found")));
        jumps.Record(from, Here(), "problem");
    }

    // F7 on a grammar finding: the fixes all checkers suggest there (each applies to its own stretch of text) and
    // their explanations, each once.
    void GrammarFix(IReadOnlyList<GrammarIssue> issues, int first, int last)
    {
        var options = new List<(GrammarIssue Issue, string Fix)>();
        foreach (var iss in issues)
            foreach (var r in iss.Replacements)
                if (!options.Any(o => o.Fix == r && o.Issue.Offset == iss.Offset && o.Issue.Length == iss.Length)) options.Add((iss, r));
        // the window (docs/tech/editing.md): the words in question in the title, the fixes as buttons, the explanation,
        // then Ignore / Never show this with the checkers at the right, in grey — information, not a choice
        string Was(GrammarIssue iss) { var w = editor.GrammarText(iss, first).Replace('\n', ' '); return w.Length > 30 ? w[..29] + "…" : w; }
        bool Same(GrammarIssue iss) => iss.Offset == issues[0].Offset && iss.Length == issues[0].Length;
        // a fix of another stretch of the text says what it changes: "checks → check"
        var fixes = options.Select(o => (Same(o.Issue) ? "" : Was(o.Issue) + " → ") + (o.Fix.Length == 0 ? T("(remove)") : o.Fix)).ToList();
        // each explanation once; another checker's signed with its name
        var explained = new List<string>();
        foreach (var iss in issues)
        {
            var line = iss == issues[0] || iss.Source.Length == 0 ? iss.Message : iss.Source + ": " + iss.Message;
            if (iss.Message.Length > 0 && !explained.Contains(line) && !explained.Contains(iss.Message)) explained.Add(line);
        }
        // LanguageTool's rule id after its name: what "Never show this" (or the settings) switches off
        var who = string.Join(" · ", issues.Select(x => x.Source == "LanguageTool" && x.Rule.Length > 0 ? x.Source + " · " + x.Rule : x.Source)
            .Where(x => x.Length > 0).Distinct());
        var lt = issues.FirstOrDefault(x => x.Source == "LanguageTool");
        Note(issues[0].Message + (lt != null && lt.Category.Length > 0 ? $"   [{lt.Rule} · {lt.Category}]" : ""));
        var title = Tf("Grammar: \"{0}\"", Was(issues[0]));
        if (Dialogs.Fix(app, title, fixes, string.Join("\n", explained), [T("Ignore"), T("Never show this")], who) is not int i) return;
        if (i < options.Count) editor.FixGrammar(options[i].Issue, first, options[i].Fix);
        else if (i == options.Count) editor.IgnoreHere(issues, first, last);
        else
        {
            var rules = issues.Select(x => x.Rule).Where(r => r.Length > 0).Distinct().ToList();
            Settings.SetValue(Settings.FilePath, "ignoredRules", Settings.JsonList(settings.IgnoredRules.Union(rules)));
            ReloadSettings();
            Note(Tf("Never shown again: {0} (Tools > Ignored rules brings it back)", string.Join(", ", rules.Select(r => r.Length > 40 ? r[..39] + "…" : r))));
        }
    }

    // Tools > Ignored rules: the findings F7 > Never show this put aside; untick one to see it again.
    void IgnoredRulesDialog()
    {
        var rules = settings.IgnoredRules;
        if (rules.Count == 0) { Note(T("No ignored rules (F7 > Never show this adds one)")); return; }
        var d = app.RunModal(new CheckListDialog(T("Ignored rules: untick to show them again"), rules.ToList(),
            Enumerable.Repeat(true, rules.Count).ToArray(), hint: T("Space on/off   Enter OK   Esc cancel")));
        if (d.Checked is not { } c) return;
        var kept = rules.Where((_, i) => c[i]).ToList();
        if (kept.Count == rules.Count) return;
        Settings.SetValue(Settings.FilePath, "ignoredRules", Settings.JsonList(kept));
        ReloadSettings();
        Note(Tf("Ignored rules: {0} shown again, {1} still ignored", rules.Count - kept.Count, kept.Count));
    }

    // Ctrl+F7: the language model suggests a better wording of the paragraph at the caret; the user ticks what to take.
    void StyleSuggest()
    {
        if (editor.ReadOnlyView) { Note(T("Read-only")); return; }
        if (editor.ParagraphAt(editor.Row) is not { } p) { Note(T("Style: put the caret in a paragraph of text")); return; }
        if (SharedLlm(settings) is not { } server)
        {
            Note(Tf("Style needs a language model in {0}: Tools > Set up grammar checking > A language model here", LlmHome(settings)));
            return;
        }
        var text = editor.ParagraphSource(p.First, p.Last);
        if (text.Length > StyleAdvisor.MaxParagraph) { Note(Tf("Style: the paragraph is too long (over {0} characters)", StyleAdvisor.MaxParagraph)); return; }
        Note(server.Running ? Tf("Style: asking {0}...", server.ModelName) : Tf("Style: starting {0} (a few seconds)...", server.ModelName));
        app.Render();
        List<StyleChange> changes;
        try { changes = StyleAdvisor.Suggest(server, text, editor.LanguageOf(editor.ParagraphText(p.First, p.Last, MathView.Spaces)), settings.StyleFocus.ToArray()); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            Note(Tf("Style: {0}", ex.Message));
            return;
        }
        var math = editor.ParagraphMath(p.First, p.Last);
        changes = changes.Where(c => !Prose.Touches(math, c.Offset, c.Length)).ToList();     // formulas are never reworded
        if (changes.Count == 0) { Note(Tf("Style ({0}): nothing to improve", server.ModelName)); return; }
        static string Clip(string s, int n) { s = s.Replace('\n', ' '); return s.Length > n ? s[..(n - 1)] + "…" : s; }
        var items = changes.Select(c => $"{Clip(text.Substring(c.Offset, c.Length), 30)} → {Clip(c.Replacement, 30)}" + (c.Reason.Length > 0 ? "   — " + c.Reason : "")).ToList();
        // the selected suggestion in full under the list: was, becomes, why
        string Details(int i) => $"{text.Substring(changes[i].Offset, changes[i].Length)}\n→ {changes[i].Replacement}" + (changes[i].Reason.Length > 0 ? $"\n({changes[i].Reason})" : "");
        var d = app.RunModal(new CheckListDialog(Tn(changes.Count, "Style ({1}): {0} suggestion", "Style ({1}): {0} suggestions", server.ModelName), items,
            Enumerable.Repeat(true, changes.Count).ToArray(), hint: T("Space on/off   Enter apply the ticked   Esc cancel"), details: Details));
        if (d.Checked is not { } c) return;
        var picked = changes.Where((_, i) => c[i]).ToList();
        if (picked.Count == 0) return;
        editor.ReplaceParagraph(p.First, p.Last, StyleAdvisor.Apply(text, picked), picked.Min(x => x.Offset));
        Note(Tf("Style: {0} of {1} applied (Ctrl+Z undoes)", picked.Count, changes.Count));
    }

    // ---------- formula pictures (FormulaImages.cs) ----------
    // The terminal is asked once whether it shows Sixel and how big a cell is, when the setting is on and the module is
    // there (after the start: an answer typed into a terminal not yet in raw mode would show up as text)
    void AskTerminalForGraphics()
    {
        if (!hostStarted || graphicsAsked || !(FormulaImages.Wanted || FormulaImages.ImagesWanted) || FormulaImages.ModuleDir == null) return;
        graphicsAsked = true;
        host.QueryGraphics();
    }

    // View > Formula pictures: kept in the settings
    void ToggleFormulaImages()
    {
        Settings.SetValue(Settings.FilePath, "formulaImages", settings.FormulaImages ? "false" : "true");
        ReloadSettings();
        Note(!settings.FormulaImages ? T("Formula pictures off: formulas as rows of text")
            : FormulaImages.ModuleDir == null ? T("Formula pictures need the mathimages module (docs/formula-pictures.md)")
            : FormulaImages.LoadError is { } error ? Tf("Formula pictures: {0}", error)
            : FormulaImages.Active ? T("Formula pictures on")
            : graphicsAsked && host.Sixel ? T("Formula pictures: this terminal did not report its cell size in pixels")
            : T("Formula pictures on where the terminal shows Sixel graphics (Windows Terminal 1.22 or later)"));
    }

    void ToggleSpell()
    {
        // kept in the settings, as Alt+G keeps line numbers: the Tools menu's √ and Tools > Settings never disagree
        Settings.SetValue(Settings.FilePath, "spellCheck", editor.SpellOn ? "false" : "true");
        ReloadSettings();
        Note(editor.SpellOn ? T("Spell check on") : T("Spell check off"));
    }
}
