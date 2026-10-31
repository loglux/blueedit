using Pos = (int Line, int Col);

/// <summary>
/// Selecting by meaning (Alt+=, docs/tech/editing.md): the next larger piece around what is selected. Each kind of piece
/// offers its candidates and the smallest one that holds the selection and more wins, so no fixed order has to fit both
/// text and code.
/// </summary>
static class SelectionSteps
{
    const string Openers = "([{«“", Closers = ")]}»”";
    const int ScanLines = 1000;

    /// <param name="prose">Text: sentences, and formulas in $; an apostrophe is not a quote. Code: indented blocks.</param>
    /// <param name="headings">The file's headings, in order: its sections.</param>
    /// <param name="isCode">Whether a bracket here counts (not in a string or comment); null: all do.</param>
    public static (Pos Start, Pos End)? Next(IReadOnlyList<string> lines, Pos start, Pos end, bool prose,
        IReadOnlyList<(int Line, int Level)>? headings = null, Func<int, int, bool>? isCode = null)
    {
        var found = new List<(Pos Start, Pos End)>();
        if (start.Line == end.Line)
        {
            Word(lines[start.Line], start, end, found);
            Quotes(lines[start.Line], start.Line, prose, found);
            if (prose) Sentence(lines, start, found);
        }
        var solid = new List<(Pos Start, Pos End)>();
        Brackets(lines, start, end, isCode, solid);
        if (!prose)
        {
            var block = new List<(Pos Start, Pos End)>();
            IndentedBlock(lines, start, end, block);
            solid.AddRange(block.Where(b => !solid.Exists(s => Crosses(b, s))));
        }
        Lines(lines, start, end, found);
        Paragraph(lines, start, end, found);
        if (headings != null) Sections(lines, headings, start, found);
        found.Add(((0, 0), (lines.Count - 1, lines[^1].Length)));
        // a piece that cuts across brackets or a block would leave no way to them: from "d])" no bracket is around any more
        found.RemoveAll(c => solid.Exists(s => Crosses(c, s)));
        found.AddRange(solid);

        (Pos Start, Pos End)? best = null;
        long bestSize = long.MaxValue;
        foreach (var c in found)
        {
            if (Before(start, c.Start) || Before(c.End, end) || (c.Start == start && c.End == end) || c.Start == c.End) continue;
            long size = Size(lines, c.Start, c.End);
            if (size < bestSize) { best = c; bestSize = size; }
        }
        return best;
    }

    static bool Before(Pos a, Pos b) => a.Line < b.Line || (a.Line == b.Line && a.Col < b.Col);

    // they overlap and neither holds the other
    static bool Crosses((Pos Start, Pos End) a, (Pos Start, Pos End) b) =>
        Before(a.Start, b.End) && Before(b.Start, a.End)
        && !(!Before(a.Start, b.Start) && !Before(b.End, a.End))
        && !(!Before(b.Start, a.Start) && !Before(a.End, b.End));

    static long Size(IReadOnlyList<string> lines, Pos a, Pos b)
    {
        if (a.Line == b.Line) return b.Col - a.Col;
        long n = lines[a.Line].Length - a.Col + 1 + b.Col;
        for (int i = a.Line + 1; i < b.Line; i++) n += lines[i].Length + 1;
        return n;
    }

    static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    // the word at the caret, or just before it
    static void Word(string line, Pos start, Pos end, List<(Pos, Pos)> found)
    {
        int at = start.Col < line.Length && IsWord(line[start.Col]) ? start.Col
            : start == end && start.Col > 0 && IsWord(line[start.Col - 1]) ? start.Col - 1 : -1;
        if (at < 0) return;
        int a = at, b = at + 1;
        while (a > 0 && IsWord(line[a - 1])) a--;
        while (b < line.Length && IsWord(line[b])) b++;
        found.Add(((start.Line, a), (start.Line, b)));
    }

    // what is between two equal marks of a line, then with them; marks pair up from the line's start
    static void Quotes(string line, int li, bool prose, List<(Pos, Pos)> found)
    {
        foreach (char q in prose ? "\"`$" : "\"'`")
        {
            int open = -1;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] != q || (i > 0 && line[i - 1] == '\\')) continue;
                if (q == '$' && ((i > 0 && line[i - 1] == '$') || (i + 1 < line.Length && line[i + 1] == '$'))) continue;  // $$
                if (open < 0) { open = i; continue; }
                found.Add(((li, open + 1), (li, i)));
                found.Add(((li, open), (li, i + 1)));
                open = -1;
            }
        }
    }

    // the sentence, within its paragraph; a stop inside a formula ends nothing. A stop after an abbreviation does: telling
    // them apart would need a dictionary for each language
    static void Sentence(IReadOnlyList<string> lines, Pos at, List<(Pos, Pos)> found)
    {
        if (lines[at.Line].Trim().Length == 0) return;
        int first = at.Line, last = at.Line;
        while (first > 0 && lines[first - 1].Trim().Length > 0) first--;
        while (last + 1 < lines.Count && lines[last + 1].Trim().Length > 0) last++;
        Pos start = (first, lines[first].Length - lines[first].TrimStart().Length);
        for (int li = first; li <= last; li++)
        {
            var line = lines[li];
            bool inFormula = false;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '$') inFormula = !inFormula;
                if (inFormula || line[i] is not ('.' or '!' or '?' or '…')) continue;
                int e = i + 1;
                while (e < line.Length && line[e] is '.' or '!' or '?' or '"' or '»' or '”' or ')') e++;
                if (e < line.Length && line[e] != ' ') continue;     // 3.14, a.txt
                Pos end = (li, e);
                if (!Before(end, at)) { found.Add((start, end)); return; }
                int next = e;
                while (next < line.Length && line[next] == ' ') next++;
                start = next < line.Length ? (li, next) : li < last ? (li + 1, lines[li + 1].Length - lines[li + 1].TrimStart().Length) : (li, next);
                i = e - 1;
            }
        }
        if (!Before((last, lines[last].Length), at)) found.Add((start, (last, lines[last].Length)));
    }

    // the nearest brackets around the selection: inside them, then with them
    static void Brackets(IReadOnlyList<string> lines, Pos start, Pos end, Func<int, int, bool>? isCode, List<(Pos, Pos)> found)
    {
        var stack = new Stack<char>();
        Pos? open = null;
        int kind = -1;
        for (int li = start.Line, n = 0; li >= 0 && n <= ScanLines && open == null; li--, n++)
        {
            var line = lines[li];
            for (int i = li == start.Line ? start.Col - 1 : line.Length - 1; i >= 0; i--)
            {
                int o = Openers.IndexOf(line[i]), k = Closers.IndexOf(line[i]);
                if ((o < 0 && k < 0) || isCode?.Invoke(li, i) == false) continue;
                if (k >= 0) { stack.Push(line[i]); continue; }
                if (stack.Count > 0) { if (stack.Pop() != Closers[o]) return; continue; }
                open = (li, i); kind = o;
                break;
            }
        }
        if (open is not { } from) return;
        stack.Clear();
        for (int li = end.Line, n = 0; li < lines.Count && n <= ScanLines; li++, n++)
        {
            var line = lines[li];
            for (int i = li == end.Line ? end.Col : 0; i < line.Length; i++)
            {
                int o = Openers.IndexOf(line[i]), k = Closers.IndexOf(line[i]);
                if ((o < 0 && k < 0) || isCode?.Invoke(li, i) == false) continue;
                if (o >= 0) { stack.Push(line[i]); continue; }
                if (stack.Count > 0) { if (stack.Pop() != Openers[k]) return; continue; }
                if (k != kind) return;
                found.Add(((from.Line, from.Col + 1), (li, i)));
                found.Add((from, (li, i + 1)));
                return;
            }
        }
    }

    // the line without its indent, then whole; a selection over several lines, those lines whole
    static void Lines(IReadOnlyList<string> lines, Pos start, Pos end, List<(Pos, Pos)> found)
    {
        var first = lines[start.Line];
        if (start.Line == end.Line) found.Add(((start.Line, first.Length - first.TrimStart().Length), (start.Line, first.TrimEnd().Length)));
        found.Add(((start.Line, 0), (end.Line, lines[end.Line].Length)));
    }

    static int Indent(string line) => line.Length - line.TrimStart().Length;

    // the lines around at least as deep as these, blank ones between them included
    static void IndentedBlock(IReadOnlyList<string> lines, Pos start, Pos end, List<(Pos, Pos)> found)
    {
        int depth = int.MaxValue;
        for (int li = start.Line; li <= end.Line; li++)
            if (lines[li].Trim().Length > 0) depth = Math.Min(depth, Indent(lines[li]));
        if (depth == int.MaxValue || depth == 0) return;
        bool Inside(int li) => lines[li].Trim().Length == 0 || Indent(lines[li]) >= depth;
        int a = start.Line, b = end.Line;
        while (a > 0 && Inside(a - 1)) a--;
        while (b + 1 < lines.Count && Inside(b + 1)) b++;
        while (a < b && lines[a].Trim().Length == 0) a++;
        while (b > a && lines[b].Trim().Length == 0) b--;
        found.Add(((a, 0), (b, lines[b].Length)));
    }

    static void Paragraph(IReadOnlyList<string> lines, Pos start, Pos end, List<(Pos, Pos)> found)
    {
        int a = start.Line, b = end.Line;
        if (lines[a].Trim().Length == 0 || lines[b].Trim().Length == 0) return;
        while (a > 0 && lines[a - 1].Trim().Length > 0) a--;
        while (b + 1 < lines.Count && lines[b + 1].Trim().Length > 0) b++;
        found.Add(((a, 0), (b, lines[b].Length)));
    }

    // every section the caret's line is in: from its heading to the next of the same level or higher
    static void Sections(IReadOnlyList<string> lines, IReadOnlyList<(int Line, int Level)> headings, Pos at, List<(Pos, Pos)> found)
    {
        for (int h = 0; h < headings.Count && headings[h].Line <= at.Line; h++)
        {
            int stop = lines.Count;
            for (int k = h + 1; k < headings.Count; k++)
                if (headings[k].Level <= headings[h].Level) { stop = headings[k].Line; break; }
            if (stop <= at.Line) continue;
            int last = stop - 1;
            while (last > headings[h].Line && lines[last].Trim().Length == 0) last--;
            found.Add(((headings[h].Line, 0), (last, lines[last].Length)));
        }
    }
}
