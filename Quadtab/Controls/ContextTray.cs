using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>
/// Bandeja que abre junto ao cursor (botão direito no vazio). Tudo o que ela cria nasce no ponto clicado.
/// Tem duas páginas: a principal (elementos e ferramentas) e a biblioteca de formas.
/// </summary>
public sealed class ContextTray : ContentControl
{
    readonly ContentControl _page = new();
    Point _world;

    public ContextTray()
    {
        Glass = UiKit.Glass(_page, 28, new Thickness(14, 12, 14, 14));
        Glass.Width = 372;
        Content = Glass;
        Focusable = false;
    }

    public GlassPanel Glass { get; }

    /// <summary>Fechar (depois de criar algo ou ao clicar fora).</summary>
    public event Action? CloseRequested;

    public BoardView? Board { get; set; }

    MainViewModel Vm => App.ViewModel;

    public void Show(Point world)
    {
        _world = world;
        ShowMain();
    }

    void Done() => CloseRequested?.Invoke();

    /// <summary>Abre direto na biblioteca de formas (a forma escolhida nasce em "world").</summary>
    public void ShowShapesAt(Point world)
    {
        _world = world;
        ShowShapes();
    }

    // ---------- página principal ----------

    void ShowMain()
    {
        var root = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(4, 0, 0, 2) };
        var close = UiKit.IconButton("IcClose", "Fecha a bandeja (ou clique com o botão direito de novo)", Done, 30, 14);
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = "Adicionar aqui", FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(head);

        // Fundo do quadro (primeiro, como em todas as bandejas)
        root.Children.Add(UiKit.Section("Quadro"));
        root.Children.Add(ModelChips(Vm, ShowMain));

        // Elementos e ferramentas
        root.Children.Add(UiKit.Section("Elementos"));
        var grid = new UniformGrid { Columns = 4 };
        void Tile(string label, string icon, Action run, string tip)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("TrayCard"), Height = 66, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(4), ToolTip = tip };
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel();
            var ic = new Icon { Data = UiKit.G(icon), HorizontalAlignment = HorizontalAlignment.Center };
            ic.SetResourceReference(Icon.ForegroundProperty, "AccentBrush");
            stack.Children.Add(ic);
            stack.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            b.Content = stack;
            b.Click += (_, _) => run();
            grid.Children.Add(b);
        }
        Tile("Página", "IcPage", () => { Vm.Add(ItemKind.Page, "branco", _world.X, _world.Y); Done(); }, "Cola uma folha de caderno aqui");
        Tile("Texto", "IcText", () => { Vm.Add(ItemKind.Text, "branco", _world.X, _world.Y); Done(); }, "Cola um texto grande aqui");
        Tile("Bloco", "IcBlock", () => { Vm.Add(ItemKind.Block, "branco", _world.X, _world.Y); Done(); }, "Cola um bloco com título livre aqui");
        Tile("Caixa", "IcTextBox", () => { Vm.Add(ItemKind.TextBox, "branco", _world.X, _world.Y); Done(); }, "Cola uma caixa de texto aqui");
        Tile("Formas", "IcShapes", ShowShapes, "Mostra a biblioteca de formas");
        Tile("Linha", "IcLinePlain", () => { Done(); DrawSettings.ArrowEnd = false; Board!.Tool = BoardTool.Line; }, "Desenha uma linha (de um item a outro para ligar)");
        Tile("Seta", "IcLine", () => { Done(); DrawSettings.ArrowEnd = true; Board!.Tool = BoardTool.Line; }, "Desenha uma seta (de um item a outro para ligar)");
        Tile("Desenhar", "IcPen", () => { Done(); Board!.Tool = BoardTool.Pen; }, "Desenha à mão livre");
        Tile("Tabela", "IcTable", AddTable, "Cola uma tabela aqui");
        Tile("Imagem", "IcImage", () => PickFiles("Imagens e vídeos|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.tif;*.tiff;*.mp4;*.mov;*.wmv;*.webm;*.m4v|Todos os arquivos|*.*"), "Escolhe imagens ou vídeos do computador");
        Tile("Arquivo", "IcFile", () => PickFiles("Todos os arquivos|*.*|PDF|*.pdf"), "Escolhe um arquivo do computador (PDF mostra a 1ª página)");
        Tile("Link", "IcLink", ShowLink, "Cola um cartão de link com prévia");
        Tile("Colar", "IcDuplicate", () => { MediaService.Paste(Vm, _world); Done(); }, "Cola aqui o que foi copiado · Ctrl+V");
        root.Children.Add(grid);

        var foot = UiKit.Row(
            UiKit.Pill(Vm.TrayOpen ? "Fechar bandeja" : "Abrir bandeja", () => { Vm.TrayOpen = !Vm.TrayOpen; Done(); }, "IcTray"),
            UiKit.Pill(Vm.TitleHidden ? "Mostrar título" : "Ocultar título", () => { Vm.TitleHidden = !Vm.TitleHidden; Done(); }, Vm.TitleHidden ? "IcEye" : "IcEyeOff"));
        foot.Margin = new Thickness(-3, 4, 0, 0);
        root.Children.Add(foot);
        _page.Content = root;
    }

    /// <summary>Chips dos modelos do quadro (Liso, Cortiça, Branco, Lousa, Pontilhado). Usado pelas duas bandejas.</summary>
    public static WrapPanel ModelChips(MainViewModel vm, Action changed)
    {
        var row = new WrapPanel();
        foreach (var m in Palette.ModelOrder)
            row.Children.Add(UiKit.Chip(Palette.ModelName(m), vm.Model == m, () => { vm.Model = m; changed(); }));
        return row;
    }

    void AddTable()
    {
        var size = BoardItemViewModel.DefaultSize(ItemKind.Table);
        Vm.AddItem(new BoardItem
        {
            Tipo = "tabela", X = Math.Round(_world.X - size.Width / 2), Y = Math.Round(_world.Y - size.Height / 2), Fixacao = "nenhuma",
            Estilo = BoardItemViewModel.DefaultStyle(ItemKind.Table), Tabela = [["Coluna 1", "Coluna 2", "Coluna 3"], ["", "", ""], ["", "", ""]],
        });
        Done();
    }

    void PickFiles(string filter)
    {
        var world = _world;
        Done();
        var dlg = new OpenFileDialog { Filter = filter, Multiselect = true, Title = "Escolha os arquivos" };
        if (dlg.ShowDialog(Window.GetWindow(Board)) == true) MediaService.AddFiles(Vm, dlg.FileNames, world);
    }

    // ---------- link ----------

    void ShowLink()
    {
        var root = new StackPanel();
        root.Children.Add(Back("Link"));
        var box = new TextBox { Style = (Style)Application.Current.FindResource("EditBox"), FontSize = 15, Margin = new Thickness(2, 6, 2, 4), Padding = new Thickness(8, 6, 8, 6) };
        if (Clipboard.ContainsText() && MediaService.IsUrl(Clipboard.GetText(), out var u)) box.Text = u;
        var hint = UiKit.Label("Cole o endereço e aperte Enter", 12.5);
        hint.Margin = new Thickness(6, 0, 0, 0);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (MediaService.IsUrl(box.Text, out var url))
            {
                MediaService.AddLink(Vm, url, _world);
                Done();
            }
            else hint.Text = "Esse endereço não parece um link (https://…)";
            e.Handled = true;
        };
        root.Children.Add(box);
        root.Children.Add(hint);
        _page.Content = root;
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---------- formas ----------

    string? _category;
    string _query = "";

    void ShowShapes()
    {
        var root = new StackPanel();
        root.Children.Add(Back("Formas"));
        var search = new TextBox { Style = (Style)Application.Current.FindResource("EditBox"), FontSize = 14, Margin = new Thickness(2, 6, 2, 6), Padding = new Thickness(8, 5, 8, 5), Text = _query };
        root.Children.Add(search);
        var cats = new WrapPanel();
        cats.Children.Add(UiKit.Chip("Todas", _category is null, () => { _category = null; ShowShapes(); }));
        foreach (var c in ShapeLibrary.Categories)
            cats.Children.Add(UiKit.Chip(c, _category == c, () => { _category = c; ShowShapes(); }));
        root.Children.Add(cats);
        var list = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var scroll = new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Style = (Style)Application.Current.FindResource("OverlayScroll") };
        root.Children.Add(scroll);

        void Fill()
        {
            list.Children.Clear();
            foreach (var s in ShapeLibrary.Search(_query, _category))
            {
                var path = new System.Windows.Shapes.Path { Data = s.Geometry, Stretch = Stretch.Uniform, Width = 34, Height = 34, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
                path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent14Brush");
                path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
                var b = new Button { Style = (Style)Application.Current.FindResource("SoftButton"), Width = 54, Height = 54, Margin = new Thickness(0, 0, 4, 4), Content = path, ToolTip = $"Cola a forma \"{s.Name}\" aqui" };
                Behaviors.UI.SetRadius(b, new CornerRadius(14));
                b.SetResourceReference(Behaviors.UI.HoverBackgroundProperty, "FillBrush");
                var key = s.Key;
                b.Click += (_, _) => AddShape(key);
                list.Children.Add(b);
            }
            if (list.Children.Count == 0) list.Children.Add(UiKit.Label("Nenhuma forma com esse nome", 13));
        }
        search.TextChanged += (_, _) => { _query = search.Text; Fill(); };
        Fill();
        _page.Content = root;
        Dispatcher.BeginInvoke(() => search.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    void AddShape(string key)
    {
        var size = BoardItemViewModel.DefaultSize(ItemKind.Shape);
        Vm.AddItem(new BoardItem
        {
            Tipo = "forma", Forma = key, Texto = "", Fixacao = "nenhuma",
            X = Math.Round(_world.X - size.Width / 2), Y = Math.Round(_world.Y - size.Height / 2),
            Estilo = BoardItemViewModel.DefaultStyle(ItemKind.Shape),
        });
        Done();
    }

    FrameworkElement Back(string title)
    {
        var dock = new DockPanel();
        var back = UiKit.IconButton("IcChevronLeft", "Volta para a bandeja", ShowMain, 30, 15);
        DockPanel.SetDock(back, Dock.Left);
        dock.Children.Add(back);
        var close = UiKit.IconButton("IcClose", "Fecha a bandeja", Done, 30, 14);
        DockPanel.SetDock(close, Dock.Right);
        dock.Children.Add(close);
        dock.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        return dock;
    }
}
