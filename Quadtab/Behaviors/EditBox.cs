using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Quadtab.ViewModels;

namespace Quadtab.Behaviors;

/// <summary>
/// Aqui usamos a classe EditBox (propriedade anexada) para controlar o campo de edição dos itens. Modos:
/// "note" e "multi" = Enter termina, Shift+Enter quebra a linha (post-it, corpo de nota, bloco e pautado);
/// "line" = Enter termina (títulos e texto solto). Esc sempre termina.
/// Tab: alterna entre título e texto (itens sem título: termina).
/// Ao aparecer, ganha foco com o texto selecionado; ao perder o foco, termina a edição.
/// </summary>
public static class EditBox
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(string), typeof(EditBox), new PropertyMetadata(null, OnChanged));

    public static string? GetMode(DependencyObject d) => (string?)d.GetValue(ModeProperty);
    public static void SetMode(DependencyObject d, string? v) => d.SetValue(ModeProperty, v);

    /// <summary>Listas com bolinha e numeradas (notas). No XAML vem antes de Mode: a lista responde ao Shift+Enter primeiro.</summary>
    public static readonly DependencyProperty ListsProperty = DependencyProperty.RegisterAttached(
        "Lists", typeof(bool), typeof(EditBox), new PropertyMetadata(false, (d, e) =>
        {
            // O campo formatado (RichEditBox) trata as listas sozinho.
            if (d is TextBox tb && e.NewValue is true) ListTyping.Attach(tb, bullets: true);
        }));

    public static bool GetLists(DependencyObject d) => (bool)d.GetValue(ListsProperty);
    public static void SetLists(DependencyObject d, bool v) => d.SetValue(ListsProperty, v);

    // Na primeira vez que o modo é definido: liga o foco automático, as teclas e o fim da edição ao perder o foco.
    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBoxBase tb || e.OldValue is not null) return;
        tb.AcceptsReturn = (string?)e.NewValue == "multi";
        tb.IsVisibleChanged += (_, _) =>
        {
            if (!tb.IsVisible) return;
            if (tb.DataContext is BoardItemViewModel { HasDarkBackground: var dark })
            {
                // Item de cor escura: o campo não clareia o fundo, para o texto branco continuar legível.
                if (dark)
                {
                    tb.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
                    tb.CaretBrush = Brushes.White;
                }
                else
                {
                    tb.ClearValue(Control.BackgroundProperty);
                    tb.ClearValue(TextBoxBase.CaretBrushProperty);
                }
            }
            tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
            });
        };
        tb.PreviewKeyDown += OnKey;
        tb.LostKeyboardFocus += (_, _) => { if (tb.IsVisible) App.ViewModel.EndEdit(); };
    }

    // Esc termina; Tab alterna título/texto; Enter termina; Shift+Enter quebra a linha.
    static void OnKey(object sender, KeyEventArgs e)
    {
        var tb = (TextBoxBase)sender;
        if (e.Key == Key.Escape)
        {
            App.ViewModel.EndEdit();
            e.Handled = true;
            return;
        }
        // Tab alterna entre título e texto (notas, blocos, pautados); item sem título: termina a edição.
        if (e.Key == Key.Tab)
        {
            var vm = App.ViewModel;
            if (vm.EditingItem is { } item && item.HasTitle)
                vm.BeginEdit(item, item.Editing == EditField.Title ? EditField.Text : EditField.Title);
            else vm.EndEdit();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;

        // Enter sai da edição em qualquer campo; Shift+Enter quebra a linha (menos nos de uma linha só).
        if (GetMode(tb) != "line" && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // O campo formatado já tratou o Shift+Enter (lista ou quebra de linha) antes de chegar aqui.
            if (tb is TextBox plain)
            {
                int i = plain.SelectionStart;
                plain.Text = plain.Text.Remove(i, plain.SelectionLength).Insert(i, "\n");
                plain.CaretIndex = i + 1;
            }
        }
        else App.ViewModel.EndEdit();
        e.Handled = true;
    }
}
