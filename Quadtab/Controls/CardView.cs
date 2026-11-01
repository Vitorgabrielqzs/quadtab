using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Quadtab.Behaviors;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>
/// Cartão no estilo do Canvas do Obsidian: retângulo reto com borda de 2 px na cor escolhida (cinza
/// quando "branco"), fundo do tema levemente tingido, título opcional em negrito e o texto embaixo.
/// Usado pelas caixas de texto.
/// </summary>
public sealed class CardView : ItemViewBase
{
    const double Radius = 10;

    readonly Border _tint = new() { CornerRadius = new CornerRadius(Radius), IsHitTestVisible = false };
    readonly Border _frame = new() { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(2), Padding = new Thickness(16, 12, 16, 14) };
    readonly Grid _titleRow = new() { Margin = new Thickness(0, 0, 0, 6) };
    readonly TextBlock _titleText, _bodyText;
    readonly TextBox _titleEdit, _bodyEdit;

    public CardView()
    {
        var back = new Border { CornerRadius = new CornerRadius(Radius) };
        back.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        Children.Add(back);
        Children.Add(_tint);

        var stack = new StackPanel();
        stack.SetBinding(MarginProperty, "AttachMargin");
        _titleRow.Children.Add(_titleText = Text("Title", "title", 17, FontWeights.Bold, "IsEditingTitle", hiddenWhen: true));
        _titleRow.Children.Add(_titleEdit = Editor("Title", "line", 17, FontWeights.Bold));
        stack.Children.Add(_titleRow);
        var body = new Grid();
        body.Children.Add(_bodyText = Text("Text", "text", 14.5, FontWeights.Normal, "IsEditingText", hiddenWhen: true));
        body.Children.Add(_bodyEdit = Editor("Text", "multi", 14.5, FontWeights.Normal));
        stack.Children.Add(body);
        _frame.Child = stack;
        Children.Add(_frame);

        SetBinding(WidthProperty, "ItemWidth");
        SetBinding(MinHeightProperty, "ItemMinHeight");
        Background = Brushes.Transparent;
    }

    static TextBlock Text(string path, string field, double size, FontWeight weight, string editing, bool hiddenWhen)
    {
        var t = new TextBlock { FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5, LineStackingStrategy = LineStackingStrategy.MaxHeight };
        t.FontFamily = (FontFamily)Application.Current.FindResource("Inter");
        t.SetResourceReference(TextBlock.ForegroundProperty, "CardTextBrush");
        if (path == "Text") t.SetBinding(TextBlock.TextProperty, new Binding(path) { Converter = (IValueConverter)Application.Current.FindResource("NoteText") });
        else t.SetBinding(TextBlock.TextProperty, path);
        t.SetBinding(VisibilityProperty, new Binding(editing) { Converter = (IValueConverter)Application.Current.FindResource(hiddenWhen ? "Hidden" : "Visible") });
        UI.SetField(t, field);
        return t;
    }

    static TextBox Editor(string path, string mode, double size, FontWeight weight)
    {
        var e = new TextBox { Style = (Style)Application.Current.FindResource("EditBox"), FontSize = size, FontWeight = weight, Background = Brushes.Transparent };
        e.FontFamily = (FontFamily)Application.Current.FindResource("Inter");
        e.SetResourceReference(TextBox.ForegroundProperty, "CardTextBrush");
        if (mode == "line") e.TextWrapping = TextWrapping.NoWrap;
        if (path == "Text") EditBox.SetLists(e, true);
        EditBox.SetMode(e, mode);
        var text = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
        if (path == "Text") text.Converter = (IValueConverter)Application.Current.FindResource("NoteText");
        e.SetBinding(TextBox.TextProperty, text);
        e.SetBinding(VisibilityProperty, new Binding(path == "Title" ? "IsEditingTitle" : "IsEditingText") { Converter = (IValueConverter)Application.Current.FindResource("Visible") });
        return e;
    }

    protected override void Rebuild()
    {
        ApplyColor();
        ApplyTitle();
    }

    protected override void OnItem(string p)
    {
        if (p == nameof(BoardItemViewModel.ColorKey)) ApplyColor();
        else if (p is nameof(BoardItemViewModel.Title) or nameof(BoardItemViewModel.Editing)) ApplyTitle();
    }

    /// <summary>Borda na cor do item (tinta forte) e um leve tom da mesma cor no fundo.</summary>
    void ApplyColor()
    {
        if (Item is null) return;
        var c = Item.Color;
        foreach (var part in new FrameworkElement[] { _titleText, _titleEdit, _bodyText, _bodyEdit })
        {
            var dp = part is TextBox ? TextBox.ForegroundProperty : TextBlock.ForegroundProperty;
            if (c.IsDark) part.SetValue(dp, Brushes.White);
            else if (c.IsStrong) part.SetValue(dp, Brushes.Black);
            else part.SetResourceReference(dp, "CardTextBrush");
        }
        if (c.IsStrong)
        {
            // Cor forte: o cartão inteiro na cor, sem o tom claro por cima.
            _frame.BorderBrush = c.PaperBrush;
            _tint.Background = c.PaperBrush;
        }
        else if (c.Key == "branco")
        {
            _frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            _tint.Background = null;
        }
        else
        {
            _frame.BorderBrush = ShadowCache.BrushOf(c.Ink);
            _tint.Background = ShadowCache.BrushOf(ColorMath.WithAlpha(c.Ink, 0.10));
        }
    }

    /// <summary>O título só aparece quando existe (ou está sendo escrito).</summary>
    void ApplyTitle()
    {
        if (Item is null) return;
        _titleRow.Visibility = Item.IsEditingTitle || !string.IsNullOrWhiteSpace(Item.Title) ? Visibility.Visible : Visibility.Collapsed;
    }
}
