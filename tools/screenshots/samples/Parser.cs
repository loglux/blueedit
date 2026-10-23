using System.Text;

namespace Demo;

/// <summary>Splits a command line into words, honouring quotes.</summary>
public static class Parser
{
    /* Quotes group words: "two words" stays one argument.
       A backslash escapes the next character. */
    public static List<string> Split(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length) { current.Append(line[++i]); continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ' ' && !quoted)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;   // e.g. Split("copy \"a b.txt\" dest") -> 3 items
    }
}
