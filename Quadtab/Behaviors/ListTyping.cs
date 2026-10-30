using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Quadtab.Behaviors;

/// <summary>Regras das listas nas notas, em texto puro: no arquivo .md a bolinha é sempre "- ".</summary>
public static partial class ListText
{
    public const char Bullet = '•';

    // Começo de linha: recuo + marca e espaço (com caixa de seleção opcional) ou número com ponto (e espaço ou fim da linha).
    [GeneratedRegex(@"^([ \t]*)(?:([-*•])( \[[ xX]\])? |(\d{1,9})\.(?: |$))")] private static partial Regex Marker();
    [GeneratedRegex(@"^([ \t]*)(\d{1,9})\.( |$)")] private static partial Regex Numbered();
    [GeneratedRegex(@"(?m)^([ \t]*)- (?!\[[ xX]\] )")] private static partial Regex MdBullet();
    [GeneratedRegex(@"(?m)^([ \t]*)• ")] private static partial Regex ShownBullet();

    public static string ToDisplay(string md) => MdBullet().Replace(md, "$1• ");
    public static string ToMarkdown(string shown) => ShownBullet().Replace(shown, "$1- ");

    static int LineStart(string text, int caret) => caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;

    static int LineEnd(string text, int caret)
    {
        int end = text.IndexOf('\n', caret);
        return end < 0 ? text.Length : end;
    }

    /// <summary>
    /// Shift+Enter numa linha de lista: abre o próximo item (e renumera os de baixo). Item vazio (Shift+Enter
    /// duas vezes seguidas): tira a marca e a linha fica normal. Fora de uma lista devolve null.
    /// </summary>
    public static (string Text, int Caret)? NextItem(string text, int caret, int selection)
    {
        text = text.Remove(caret, selection);
        int start = LineStart(text, caret);
        var line = text[start..LineEnd(text, caret)];
        var m = Marker().Match(line);
        if (!m.Success || caret < start + m.Length) return null;

        if (m.Value.EndsWith(' ') && line[m.Length..].Trim().Length == 0)
            return (text.Remove(start, line.Length), start);

        string indent = m.Groups[1].Value;
        string next;
        int number = 0;
        if (m.Groups[4].Success)
        {
            number = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) + 1;
            next = $"{indent}{number}. ";
        }
        else next = indent + m.Groups[2].Value + (m.Groups[3].Success ? " [ ]" : "") + " ";

        int newCaret = caret + 1 + next.Length;
        text = text.Insert(caret, "\n" + next);
        return (number > 0 ? Renumber(text, newCaret, indent, number + 1) : text, newCaret);
    }

    /// <summary>Corrige os números das linhas seguidas da mesma lista, abaixo da posição dada.</summary>
    static string Renumber(string text, int from, string indent, int number)
    {
        int end = LineEnd(text, from);
        while (end < text.Length)
        {
            int start = end + 1;
            end = LineEnd(text, start);
            var m = Numbered().Match(text[start..end]);
            if (!m.Success || m.Groups[1].Value != indent) break;
            string digits = number.ToString(CultureInfo.InvariantCulture);
            var group = m.Groups[2];
            text = text.Remove(start + group.Index, group.Length).Insert(start + group.Index, digits);
            end += digits.Length - group.Length;
            number++;
        }
        return text;
    }

    /// <summary>Backspace logo depois da marca apaga a marca inteira. Fora disso devolve null.</summary>
    public static (string Text, int Caret)? Backspace(string text, int caret)
    {
        int start = LineStart(text, caret);
        var m = Marker().Match(text[start..LineEnd(text, caret)]);
        if (!m.Success || caret != start + m.Length || !m.Value.EndsWith(' ')) return null;
        return (text.Remove(start, m.Length), start);
    }

    /// <summary>Acabou de digitar "- " no começo da linha? Devolve a posição do hífen para virar bolinha.</summary>
    public static int? TypedBullet(string text, int caret)
    {
        int start = LineStart(text, caret);
        if (caret - start < 2 || text[caret - 1] != ' ' || text[caret - 2] != '-') return null;
        return text[start..(caret - 2)].Trim(' ', '\t').Length == 0 ? caret - 2 : null;
    }
}

/// <summary>Liga as listas a um campo de texto. bullets = mostra "•" no lugar de "- " (edição direto na nota).</summary>
public static class ListTyping
{
    public static void Attach(TextBox box, bool bullets)
    {
        box.PreviewKeyDown += (_, e) =>
        {
            (string Text, int Caret)? result = e.Key switch
            {
                Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) => ListText.NextItem(box.Text, box.SelectionStart, box.SelectionLength),
                Key.Back when box.SelectionLength == 0 => ListText.Backspace(box.Text, box.CaretIndex),
                _ => null,
            };
            if (result is not { } r) return;
            box.Text = r.Text;
            box.CaretIndex = r.Caret;
            e.Handled = true;
        };
        if (!bullets) return;
        box.TextChanged += (_, e) =>
        {
            if (!box.IsKeyboardFocused || !e.Changes.Any(c => c.AddedLength > 0)) return;
            int caret = box.CaretIndex;
            if (ListText.TypedBullet(box.Text, caret) is not { } dash) return;
            box.Select(dash, 1);
            box.SelectedText = ListText.Bullet.ToString();
            box.CaretIndex = caret;
        };
    }
}

/// <summary>Texto da nota: "- " no arquivo, "• " na tela. Mesmo comprimento, então o cursor não pula.</summary>
public sealed class NoteTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? ListText.ToDisplay(s) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? ListText.ToMarkdown(s) : "";
}
