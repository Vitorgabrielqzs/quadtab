using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Abrir rápido (Ctrl+O): digite parte do nome de um quadro ou página e aperte Enter.</summary>
public sealed class QuickOpen : Grid
{
    sealed record Entry(string Name, string Path, bool IsPage, string Folder, string? Snippet = null, int? ItemId = null);

    readonly TextBox _box = new() { FontSize = 18, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center };
    readonly StackPanel _list = new();
    List<Entry> _all = [];
    List<Entry> _shown = [];
    int _pick;

    public QuickOpen()
    {
        Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
        Visibility = Visibility.Collapsed;
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == this) Hide(); };

        var stack = new StackPanel();
        var search = new DockPanel { Margin = new Thickness(6, 2, 6, 8) };
        var icon = new Icon { Data = UiKit.G("IcSearch"), Size = 20, Margin = new Thickness(0, 0, 10, 0) };
        icon.SetResourceReference(Icon.ForegroundProperty, "Ink3Brush");
        search.Children.Add(icon);
        _box.SetResourceReference(TextBox.ForegroundProperty, "InkBrush");
        _box.SetResourceReference(TextBox.CaretBrushProperty, "AccentBrush");
        _box.TextChanged += (_, _) => { _pick = 0; Filter(); };
        _box.PreviewKeyDown += OnKey;
        search.Children.Add(_box);
        stack.Children.Add(search);
        var sep = UiKit.Separator();
        sep.Margin = new Thickness(-14, 0, -14, 6);
        stack.Children.Add(sep);
        stack.Children.Add(_list);
        var hint = UiKit.Label("Procura em quadros, notas e textos · Enter abre · ↑↓ escolhe · Esc fecha", 12);
        hint.Margin = new Thickness(8, 8, 0, 0);
        stack.Children.Add(hint);

        Glass = UiKit.Glass(stack, 26, new Thickness(14, 12, 14, 12));
        Glass.Width = 580;
        Glass.VerticalAlignment = VerticalAlignment.Top;
        Glass.HorizontalAlignment = HorizontalAlignment.Center;
        Glass.Margin = new Thickness(0, 110, 0, 0);
        Children.Add(Glass);
    }

    public GlassPanel Glass { get; }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show()
    {
        _all = StorageService.BoardFiles().Select(f => new Entry(Path.GetFileNameWithoutExtension(f), f, false, Folder(f, StorageService.BoardsDir)))
            .Concat(StorageService.PageFiles().Select(f => new Entry(Path.GetFileNameWithoutExtension(f), f, true, Folder(f, StorageService.PagesDir))))
            .ToList();
        // Os recentes vêm primeiro quando a busca está vazia.
        var recent = StorageService.State.Recentes;
        _all = _all.OrderBy(e => { int i = recent.IndexOf(StorageService.Rel(e.Path)); return i < 0 ? int.MaxValue : i; }).ThenBy(e => e.Name).ToList();
        _box.Text = "";
        _pick = 0;
        Filter();
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => _box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Hide() => Visibility = Visibility.Collapsed;

    /// <summary>Abre já com um texto digitado (conferência).</summary>
    internal void ShowWith(string text)
    {
        Show();
        _box.Text = text;
    }

    static string Folder(string file, string root)
    {
        var rel = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
        return rel == "." ? "" : rel;
    }

    void Filter()
    {
        var q = BoardItemViewModel.Normalize(_box.Text.Trim());
        _shown = _all.Where(e => q.Length == 0 || BoardItemViewModel.Normalize(e.Name).Contains(q))
            .OrderBy(e => q.Length > 0 && BoardItemViewModel.Normalize(e.Name).StartsWith(q, StringComparison.Ordinal) ? 0 : 1).Take(q.Length == 0 ? 9 : 5).ToList();
        if (q.Length >= 2)
        {
            // Textos: itens do quadro aberto e o conteúdo dos outros quadros e páginas.
            var vm = App.ViewModel;
            foreach (var it in vm.Items.Where(i => i.Matches(q)).Take(4))
                _shown.Add(new Entry(it.DisplayName, vm.FilePath, false, "", "Neste quadro · " + it.TypeLabel, it.Id));
            foreach (var r in App.Library.SearchEverywhere(_box.Text).Where(r => r.Snippet.Length > 0 && !_shown.Any(e => e.Path == r.Path && e.ItemId is null)).Take(5))
                _shown.Add(new Entry(r.Name, r.Path, r.IsPage, "", r.Snippet));
        }
        _list.Children.Clear();
        for (int i = 0; i < _shown.Count; i++)
        {
            var e = _shown[i];
            var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9), Cursor = Cursors.Hand };
            if (i == _pick) row.SetResourceReference(Border.BackgroundProperty, "Accent14Brush");
            else row.Background = Brushes.Transparent;
            var dock = new DockPanel();
            var kind = UiKit.Label(e.IsPage ? "Nota" : e.ItemId is not null ? "Item" : "Quadro", 12.5);
            DockPanel.SetDock(kind, Dock.Right);
            dock.Children.Add(kind);
            var ic = new Icon { Data = UiKit.G(e.IsPage ? "IcPage" : e.ItemId is not null ? "IcTarget" : "IcBoard"), Size = 17, Margin = new Thickness(0, 0, 10, 0) };
            ic.SetResourceReference(Icon.ForegroundProperty, "Ink2Brush");
            dock.Children.Add(ic);
            var text = new StackPanel();
            text.Children.Add(UiKit.Row(new TextBlock { Text = e.Name, FontSize = 15, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis },
                e.Folder.Length > 0 ? UiKit.Label("  " + e.Folder, 12.5) : null));
            if (e.Snippet is { Length: > 0 } sn)
            {
                var snip = UiKit.Label(sn, 12.5);
                snip.TextTrimming = TextTrimming.CharacterEllipsis;
                text.Children.Add(snip);
            }
            dock.Children.Add(text);
            row.Child = dock;
            row.MouseLeftButtonDown += (_, _) => Open(e);
            _list.Children.Add(row);
        }
        if (_shown.Count == 0 && _box.Text.Trim().Length > 0)
        {
            var create = UiKit.Label($"Criar a nota \"{_box.Text.Trim()}\" (Enter)", 14);
            create.Margin = new Thickness(12, 8, 0, 8);
            _list.Children.Add(create);
        }
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Hide(); e.Handled = true; break;
            case Key.Down when _shown.Count > 0: _pick = (_pick + 1) % _shown.Count; Filter(); e.Handled = true; break;
            case Key.Up when _shown.Count > 0: _pick = (_pick + _shown.Count - 1) % _shown.Count; Filter(); e.Handled = true; break;
            case Key.Enter:
                if (_shown.Count > 0) Open(_shown[_pick]);
                else if (_box.Text.Trim() is { Length: > 0 } name)
                {
                    Hide();
                    var page = StorageService.CreatePage(name, $"# {name}\n\n");
                    App.Library.Refresh();
                    App.OpenPage(page);
                }
                e.Handled = true;
                break;
        }
    }

    void Open(Entry e)
    {
        Hide();
        if (e.ItemId is { } id) App.FocusItem(e.Path, id);
        else if (e.IsPage) App.OpenPage(e.Path);
        else App.OpenBoard(e.Path);
    }
}
