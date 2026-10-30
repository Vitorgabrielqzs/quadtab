using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Quadtab.Behaviors;

/// <summary>
/// Campo de edição dos itens. Modos:
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

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb || e.OldValue is not null) return;
        tb.AcceptsReturn = (string?)e.NewValue == "multi";
        tb.IsVisibleChanged += (_, _) =>
        {
            if (!tb.IsVisible) return;
            tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
            });
        };
        tb.PreviewKeyDown += OnKey;
        tb.LostKeyboardFocus += (_, _) =>
        {
            if (tb.IsVisible) App.ViewModel.EndEdit();
        };
    }

    static void OnKey(object sender, KeyEventArgs e)
    {
        var tb = (TextBox)sender;
        var mode = GetMode(tb);
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
                vm.BeginEdit(item, item.Editing == ViewModels.EditField.Title ? ViewModels.EditField.Text : ViewModels.EditField.Title);
            else vm.EndEdit();
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;

        // Enter sai da edição em qualquer campo; Shift+Enter quebra a linha (menos nos de uma linha só).
        if (mode != "line" && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            int i = tb.SelectionStart;
            tb.Text = tb.Text.Remove(i, tb.SelectionLength).Insert(i, "\n");
            tb.CaretIndex = i + 1;
        }
        else App.ViewModel.EndEdit();
        e.Handled = true;
    }
}
