using System.Windows.Input;
using System.Windows.Threading;
using Quadtab.Controls;

namespace Quadtab.Behaviors;

/// <summary>
/// Liga o campo de texto formatado à barra flutuante (<see cref="FormatBar"/>): ela aparece em cima da palavra ou
/// frase que o usuário seleciona (soltando o mouse, ou com Shift+setas e Ctrl+A) e some ao desmarcar ou sair da edição.
/// Entrar na edição seleciona tudo sozinho e não abre a barra.
/// </summary>
public static class RichEdit
{
    public static void Attach(RichEditBox box)
    {
        box.SelectionChanged += (_, _) =>
        {
            if (FormatBar.Instance is not { } bar || !bar.IsShownFor(box)) return;
            if (!box.HasSelection) bar.HideFor(box);
            else bar.Follow();
        };
        box.PreviewMouseLeftButtonUp += (_, _) => box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Show(box));
        box.PreviewKeyUp += (_, e) =>
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || e.Key is Key.LeftShift or Key.RightShift;
            bool all = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.A;
            if (shift || all) Show(box);
        };
        box.IsVisibleChanged += (_, _) => { if (!box.IsVisible) FormatBar.Instance?.HideFor(box); };
        box.LostKeyboardFocus += (_, _) => FormatBar.Instance?.HideFor(box);
    }

    /// <summary>Mostra a barra em cima da seleção atual (se houver) ou a esconde.</summary>
    public static void Show(RichEditBox box)
    {
        var bar = FormatBar.Instance;
        if (bar is null) return;
        if (!box.IsVisible || !box.IsKeyboardFocused || !box.HasSelection) { bar.HideFor(box); return; }
        if (Mouse.LeftButton == MouseButtonState.Pressed) return; // ainda arrastando a seleção
        bar.ShowFor(box);
    }
}
