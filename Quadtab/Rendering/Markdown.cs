using System.Text.RegularExpressions;

namespace Quadtab.Rendering;

/// <summary>Leituras simples de Markdown: títulos, links [[ ]], tags e prévia em texto puro.</summary>
public static partial class Markdown
{
    [GeneratedRegex(@"\[\[([^\[\]|#]+)(?:[|#][^\[\]]*)?\]\]")] public static partial Regex WikiLink();
    [GeneratedRegex(@"(?<![\w#])#([\p{L}\p{N}_\-/]+)")] public static partial Regex Tag();
    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")] public static partial Regex Heading();

    /// <summary>Tira o "# Título" do começo, se for igual ao nome da página.</summary>
    public static string StripTitle(string md, string title)
    {
        var lines = md.Replace("\r", "").Split('\n').ToList();
        if (lines.Count > 0 && Heading().Match(lines[0]) is { Success: true } m && m.Groups[1].Length == 1)
        {
            lines.RemoveAt(0);
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        }
        return string.Join("\n", lines).TrimEnd();
    }

    /// <summary>Texto curto para a folha de caderno no quadro (sem marcações).</summary>
    public static string ToPlainPreview(string md, int maxLines)
    {
        var outLines = new List<string>();
        bool code = false;
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            var l = raw.TrimEnd();
            if (l.StartsWith("```")) { code = !code; continue; }
            if (l.Trim() is "---" or "***") continue;
            if (!code)
            {
                var h = Heading().Match(l);
                if (h.Success) l = h.Groups[2].Value;
                l = l.Replace("- [ ] ", "○ ").Replace("- [x] ", "● ").Replace("- [X] ", "● ");
                if (l.StartsWith("- ") || l.StartsWith("* ")) l = "• " + l[2..];
                if (l.StartsWith("> ")) l = l[2..];
                l = l.Replace("**", "").Replace("__", "").Replace("~~", "").Replace("==", "");
                l = WikiLink().Replace(l, m => m.Groups[1].Value);
                l = Regex.Replace(l, @"!\[\[[^\]]*\]\]|!\[[^\]]*\]\([^)]*\)", "🖼");
            }
            if (l.Length == 0 && outLines.Count == 0) continue;
            outLines.Add(l);
            if (outLines.Count >= maxLines) break;
        }
        return string.Join("\n", outLines).TrimEnd();
    }

    public static IEnumerable<(int Level, string Text)> Headings(string md)
    {
        bool code = false;
        foreach (var raw in md.Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("```")) { code = !code; continue; }
            if (code) continue;
            var m = Heading().Match(raw);
            if (m.Success && m.Groups[1].Length <= 3) yield return (m.Groups[1].Length, m.Groups[2].Value.Trim());
        }
    }

    public static IEnumerable<string> Links(string md) =>
        WikiLink().Matches(md).Select(m => m.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> Tags(string md) =>
        Tag().Matches(md).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase);
}
