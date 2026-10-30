using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;

namespace Quadtab.ViewModels;

/// <summary>Um grupo da lista "No quadro" (Post-its, Páginas...), que abre e fecha.</summary>
public partial class OutlineGroup : ObservableObject
{
    public OutlineGroup(string key, string name)
    {
        Key = key;
        Name = name;
        IsOpen = StorageService.State.Abertos.Contains("grupo:" + key);
    }

    public string Key { get; }
    public string Name { get; }
    public ObservableCollection<BoardItemViewModel> Items { get; } = [];
    [ObservableProperty] public partial bool IsOpen { get; set; }
    partial void OnIsOpenChanged(bool value) => MainViewModel.SaveOpen("grupo:" + Key, value);
    public int Count => Items.Count;
    public void Changed() => OnPropertyChanged(nameof(Count));
}

/// <summary>Estado de um quadro aberto e da interface. As telas só leem daqui e chamam os métodos.</summary>
public partial class MainViewModel : ObservableObject
{
    static readonly Random Rng = new();
    public const string ClipFormat = "Quadtab.Itens";

    readonly DispatcherTimer _saveTimer;
    readonly DispatcherTimer _clockTimer;
    DateTime _lastEdit;
    int _seq;
    string _query = "";
    bool _restoring;

    // desfazer / refazer: fotos dos itens antes de cada mudança
    readonly List<string> _undo = [];
    readonly List<string> _redo = [];
    string? _lastSnapshot;
    DateTime _lastCheckpoint;

    static List<BoardItem>? _clipboard;
    static ItemStyle? _styleClip;
    static (string Color, Fastening Fastening, bool Lined)? _noteStyleClip;

    public MainViewModel(Board board, string path)
    {
        FilePath = path;
        BoardTitle = Path.GetFileNameWithoutExtension(path);
        // Quadros salvos antes dos modelos voltarem já estavam no visual liso: continuam liso.
        Model = board.Versao < 4 ? BoardModel.Liso : Palette.ParseModel(board.Modelo);
        Camera = board.Camera;
        Zoom = board.Camera?.Zoom ?? 1;
        TitleHidden = board.TituloOculto;
        ShowGrid = board.Grade;
        DiagramMode = board.Diagrama;
        SidebarOpen = StorageService.State.LateralAberta;
        NewStyle = Enum.TryParse<NoteStyle>(StorageService.State.EstiloPostit, out var ns) ? ns : NoteStyle.Pin;
        Search = "";
        BoardSearch = "";
        OutlineOpen = StorageService.State.Abertos.Contains("noquadro");
        foreach (var s in board.Cenas) Scenes.Add(s);

        foreach (var m in board.Itens)
        {
            MigrateNote(m);
            MigratePage(m);
            var vm = new BoardItemViewModel(m);
            vm.PropertyChanged += OnItemChanged;
            Items.Add(vm);
            _seq = Math.Max(_seq, m.Id);
        }
        _lastEdit = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
        foreach (var (k, n) in GroupNames) OutlineGroups.Add(new OutlineGroup(k, n));
        RefreshOutline();
        _lastSnapshot = Snapshot();

        // Salva ~1 s depois da última mudança.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            await StorageService.SaveBoardAsync(FilePath, ToBoard());
            IsSaving = false;
        };

        // Atualiza o "editado há ..." de tempos em tempos.
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => OnPropertyChanged(nameof(Subtitle));
        _clockTimer.Start();
    }

    static readonly (string Key, string Name)[] GroupNames =
    [
        ("postit", "Post-its"), ("pagina", "Notas"), ("bloco", "Blocos de vidro"), ("texto", "Textos"),
        ("forma", "Formas e linhas"), ("desenho", "Desenhos"), ("midia", "Imagens e arquivos"),
    ];

    static string GroupOf(BoardItemViewModel i) => i.Kind switch
    {
        ItemKind.PostIt => "postit",
        ItemKind.Page => "pagina",
        ItemKind.Block => "bloco",
        ItemKind.Text or ItemKind.TextBox => "texto",
        ItemKind.Shape or ItemKind.Line or ItemKind.Table => "forma",
        ItemKind.Drawing => "desenho",
        _ => "midia",
    };

    /// <summary>Blocos com o título padrão antigo ("Bloco de vidro") ficam com o título livre.</summary>
    static void MigrateNote(BoardItem m)
    {
        if (m.Tipo == "bloco" && m.Titulo == "Bloco de vidro") m.Titulo = "";
    }

    /// <summary>Páginas antigas (título + texto no quadro) viram arquivos .md no cofre.</summary>
    static void MigratePage(BoardItem m)
    {
        if (m.Tipo != "pagina") return;
        if (m.Arquivo is { } rel && File.Exists(StorageService.Full(rel)))
        {
            var text = StorageService.ReadPage(StorageService.Full(rel));
            m.Titulo = Path.GetFileNameWithoutExtension(rel);
            m.Texto = Markdown.StripTitle(text, m.Titulo);
            return;
        }
        var name = string.IsNullOrWhiteSpace(m.Titulo) ? "Nova nota" : m.Titulo;
        var existing = StorageService.FindPage(name);
        var path = existing ?? StorageService.CreatePage(name, $"# {name}\n\n{m.Texto}\n");
        m.Arquivo = StorageService.Rel(path);
        m.Titulo = Path.GetFileNameWithoutExtension(path);
        m.Texto = Markdown.StripTitle(StorageService.ReadPage(path), m.Titulo);
    }

    public ObservableCollection<BoardItemViewModel> Items { get; } = [];

    /// <summary>Lista "No quadro" em grupos que abrem e fecham.</summary>
    public ObservableCollection<OutlineGroup> OutlineGroups { get; } = [];

    public ObservableCollection<Scene> Scenes { get; } = [];

    public LibraryViewModel Library => App.Library;

    public string FilePath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string BoardTitle { get; set; }

    public NoteColor[] Colors => Palette.Colors;

    /// <summary>Última cor de post-it usada (fica salva em estado.json).</summary>
    public string LastColor
    {
        get => StorageService.State.CorPostit;
        set
        {
            if (value == StorageService.State.CorPostit) return;
            StorageService.State.CorPostit = value;
            StorageService.SaveState();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelName), nameof(ModelIndex), nameof(Subtitle), nameof(StatusText))]
    public partial BoardModel Model { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    public partial double Zoom { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleToggleLabel), nameof(TitleToggleVerb))]
    public partial bool TitleHidden { get; set; }

    [ObservableProperty] public partial bool SidebarOpen { get; set; }
    [ObservableProperty] public partial bool ShowGrid { get; set; }
    [ObservableProperty] public partial bool DiagramMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayToggleVerb))]
    public partial bool TrayOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsSaving { get; set; }

    [ObservableProperty] public partial string Search { get; set; }
    [ObservableProperty] public partial string BoardSearch { get; set; }
    [ObservableProperty] public partial BoardItemViewModel? Selected { get; set; }
    [ObservableProperty] public partial NoteStyle NewStyle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutlineArrowAngle))]
    public partial bool OutlineOpen { get; set; }
    partial void OnOutlineOpenChanged(bool value) => SaveOpen("noquadro", value);
    public double OutlineArrowAngle => OutlineOpen ? 90 : 0;

    public BoardItemViewModel? EditingItem { get; private set; }

    public int Count => Items.Count;
    public string CountLabel => Count == 1 ? "1 item" : $"{Count} itens";
    public string ModelName => Palette.ModelName(Model);
    public int ModelIndex => (int)Model;
    public string Subtitle => $"{CountLabel} · editado {EditedAgo()}";
    public string StatusText => $"{(IsSaving ? "Salvando…" : "Salvo")} · {CountLabel}";
    public string ZoomText => $"{Math.Round(Zoom * 100)}%";
    public string TitleToggleVerb => TitleHidden ? "Mostrar" : "Ocultar";
    public string TitleToggleLabel => TitleToggleVerb + " título";
    public string TrayToggleVerb => TrayOpen ? "Fechar" : "Abrir";
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public static void SaveOpen(string key, bool open)
    {
        var list = StorageService.State.Abertos;
        list.Remove(key);
        if (open) list.Add(key);
        StorageService.SaveState();
    }

    string EditedAgo()
    {
        var span = DateTime.Now - _lastEdit;
        if (span.TotalMinutes < 1) return "agora";
        if (span.TotalMinutes < 60) return $"há {(int)span.TotalMinutes} min";
        if (span.TotalHours < 24) return $"há {(int)span.TotalHours} h";
        return $"em {_lastEdit:dd/MM}";
    }

    // ---------- comandos simples ----------

    [RelayCommand] void ToggleSidebar() => SidebarOpen = !SidebarOpen;
    [RelayCommand] void ToggleTitle() => TitleHidden = !TitleHidden;
    [RelayCommand] void ToggleTray() => TrayOpen = !TrayOpen;
    [RelayCommand] void ToggleOutline() => OutlineOpen = !OutlineOpen;
    [RelayCommand] void SetModel(BoardModel model) => Model = model;
    [RelayCommand] void SetNewStyle(NoteStyle style) => NewStyle = style;

    partial void OnModelChanged(BoardModel value) { Checkpoint(); MarkDirty(); }
    partial void OnTitleHiddenChanged(bool value) => MarkDirty();
    partial void OnShowGridChanged(bool value) => MarkDirty(touchClock: false);
    partial void OnDiagramModeChanged(bool value) => MarkDirty(touchClock: false);
    partial void OnZoomChanged(double value) => MarkDirty(touchClock: false);
    partial void OnNewStyleChanged(NoteStyle value)
    {
        if (StorageService.State.EstiloPostit == value.ToString()) return;
        StorageService.State.EstiloPostit = value.ToString();
        StorageService.SaveState();
    }

    partial void OnSidebarOpenChanged(bool value)
    {
        StorageService.State.LateralAberta = value;
        StorageService.SaveState();
    }

    /// <summary>Renomeia o quadro (o arquivo também).</summary>
    public void Rename(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == BoardTitle) return;
        Flush();
        FilePath = StorageService.RenameBoard(FilePath, name);
        BoardTitle = Path.GetFileNameWithoutExtension(FilePath);
        MarkDirty();
    }

    // ---------- seleção ----------

    // Selected é o item principal; vários itens podem estar selecionados ao mesmo tempo (IsSelected).
    bool _keepOthers;

    partial void OnSelectedChanged(BoardItemViewModel? oldValue, BoardItemViewModel? newValue)
    {
        if (!_keepOthers)
            foreach (var i in Items)
                if (i.IsSelected && i != newValue) i.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(SelectionCount));
    }

    public IEnumerable<BoardItemViewModel> SelectedItems => Items.Where(i => i.IsSelected);
    public int SelectionCount => Items.Count(i => i.IsSelected);

    /// <summary>Seleciona vários de uma vez (caixa de seleção). add = soma à seleção atual.</summary>
    public void SelectMany(IReadOnlyCollection<BoardItemViewModel> items, bool add)
    {
        if (!add)
            foreach (var i in Items)
                if (i.IsSelected && !items.Contains(i)) i.IsSelected = false;
        foreach (var i in items) i.IsSelected = true;
        _keepOthers = true;
        Selected = items.LastOrDefault() ?? (add ? Selected : null);
        _keepOthers = false;
        OnPropertyChanged(nameof(SelectionCount));
    }

    /// <summary>Seleciona o item e, se ele estiver num grupo, o grupo inteiro.</summary>
    public void SelectWithGroup(BoardItemViewModel item, bool add = false)
    {
        if (item.Group is { } g) SelectMany(Items.Where(i => i.Group == g).ToList(), add);
        else if (add) SelectMany([item], true);
        else Selected = item;
    }

    /// <summary>Shift + clique: põe ou tira um item (ou o grupo dele) da seleção.</summary>
    public void ToggleSelection(BoardItemViewModel item)
    {
        var members = item.Group is { } g ? Items.Where(i => i.Group == g).ToList() : [item];
        bool on = !item.IsSelected;
        foreach (var m in members) m.IsSelected = on;
        _keepOthers = true;
        Selected = on ? item : SelectedItems.LastOrDefault();
        _keepOthers = false;
        OnPropertyChanged(nameof(SelectionCount));
    }

    public void ClearSelection()
    {
        Selected = null;
        foreach (var i in Items) i.IsSelected = false;
        OnPropertyChanged(nameof(SelectionCount));
    }

    public void SelectAll() => SelectMany(Items.ToList(), add: false);

    // ---------- busca ----------

    partial void OnSearchChanged(string value)
    {
        _query = BoardItemViewModel.Normalize((value ?? "").Trim());
        foreach (var it in Items) it.IsDimmed = !it.Matches(_query);
        RefreshOutline();
    }

    /// <summary>Busca no quadro (Ctrl+F): destaca os resultados.</summary>
    public List<BoardItemViewModel> BoardSearchHits { get; private set; } = [];

    partial void OnBoardSearchChanged(string value)
    {
        var q = BoardItemViewModel.Normalize((value ?? "").Trim());
        BoardSearchHits = q.Length == 0 ? [] : Items.Where(i => i.Matches(q)).OrderBy(i => i.Y).ThenBy(i => i.X).ToList();
        foreach (var it in Items)
        {
            it.IsSearchHit = q.Length > 0 && BoardSearchHits.Contains(it);
            it.IsDimmed = q.Length > 0 ? !it.IsSearchHit : !it.Matches(_query);
        }
    }

    // ---------- câmera ----------

    /// <summary>Onde a câmera está (salvo junto com o quadro). Null = nunca aberto.</summary>
    public Camera? Camera { get; private set; }

    /// <summary>A tela informa a câmera atual (não conta como edição).</summary>
    public void ReportCamera(double x, double y, double zoom)
    {
        Camera = new Camera { X = Math.Round(x, 1), Y = Math.Round(y, 1), Zoom = Math.Round(zoom, 4) };
        if (Math.Abs(zoom - Zoom) > 0.0005) Zoom = zoom;
        else MarkDirty(touchClock: false);
    }

    public void AddScene(string name)
    {
        if (Camera is null) return;
        Scenes.Add(new Scene { Nome = name, X = Camera.X, Y = Camera.Y, Zoom = Camera.Zoom });
        MarkDirty();
    }

    public void RemoveScene(Scene s)
    {
        Scenes.Remove(s);
        MarkDirty();
    }

    // ---------- itens ----------

    int TopZ() => Items.Count == 0 ? 0 : Items.Max(i => i.Z);
    public int NextId() => ++_seq;

    /// <summary>Cria um item dos tipos clássicos centralizado em (cx, cy), em coordenadas do mundo.</summary>
    public BoardItemViewModel Add(ItemKind kind, string color, double cx, double cy, NoteStyle? style = null)
    {
        var size = BoardItemViewModel.DefaultSize(kind);

        var m = new BoardItem
        {
            Tipo = BoardItem.KindToJson(kind),
            Cor = color,
            X = Math.Round(cx - size.Width / 2),
            Y = Math.Round(cy - size.Height / 2),
            Fixacao = kind == ItemKind.PostIt ? "alfinete" : "nenhuma",
            Titulo = "",
            Texto = kind switch
            {
                ItemKind.Text => "Texto",
                ItemKind.TextBox => "Escreva aqui",
                ItemKind.PostIt => "Lembrete",
                _ => "",
            },
        };

        if (kind == ItemKind.Page)
        {
            m.SemEspiral = true; // a página nasce sem espiral (o menu do botão direito põe)
            var path = StorageService.CreatePage("Nova nota", "# Nova nota\n\nEscreva aqui…\n");
            m.Arquivo = StorageService.Rel(path);
            m.Titulo = Path.GetFileNameWithoutExtension(path);
            m.Texto = "Escreva aqui…";
        }

        if (kind == ItemKind.PostIt && style is { } s)
        {
            if (s == NoteStyle.Lined)
            {
                m.Pautado = true;
                m.Titulo = "Lista";
                m.Texto = "Primeiro item\nSegundo item";
            }
            else m.Fixacao = BoardItem.FasteningToJson((Fastening)(int)s);
        }

        var vm = AddItem(m);
        if (kind == ItemKind.PostIt)
        {
            LastColor = color;
            if (style is { } ns) NewStyle = ns;
        }
        if (vm.IsPlainNote || kind is ItemKind.Text or ItemKind.TextBox or ItemKind.Block) BeginEdit(vm, EditField.Text);
        return vm;
    }

    /// <summary>Acrescenta um item pronto (formas, linhas, imagens...). Recebe id e camada novos.</summary>
    public BoardItemViewModel AddItem(BoardItem m, bool select = true)
    {
        Checkpoint();
        m.Id = ++_seq;
        m.Z = TopZ() + 1;
        m.Criado ??= DateTime.Today.ToString("yyyy-MM-dd");
        var vm = new BoardItemViewModel(m);
        vm.PropertyChanged += OnItemChanged;
        Items.Add(vm);
        vm.IsDimmed = !vm.Matches(_query);
        if (select)
        {
            EndEdit();
            Selected = vm;
        }
        Remember(m);
        StructureChanged();
        return vm;
    }

    // ---------- repetir o último item criado (2 cliques com o botão direito) ----------

    /// <summary>Guarda o molde do item recém-criado em estado.json (traços soltos e conectores não contam).</summary>
    static void Remember(BoardItem m)
    {
        if (m.Tipo == "desenho" || m.Linha is { De.Item: not null } or { Para.Item: not null }) return;
        var t = StorageService.CloneItem(m);
        t.Id = 0;
        t.Z = 0;
        t.Grupo = null;
        t.Bloqueado = false;
        StorageService.State.UltimoItem = t;
        StorageService.SaveState();
    }

    /// <summary>Coloca, centrado no ponto, outro item igual ao último criado (sem molde: uma página).</summary>
    public BoardItemViewModel RepeatLast(Point at)
    {
        if (StorageService.State.UltimoItem is not { } t)
            return Add(ItemKind.Page, "branco", at.X, at.Y);
        var kind = BoardItem.KindFromJson(t.Tipo);
        // Página é um arquivo: repetir cria uma página nova, com a mesma cor.
        if (kind == ItemKind.Page) return Add(ItemKind.Page, t.Cor, at.X, at.Y);

        var m = StorageService.CloneItem(t);
        Point center;
        if (m.Linha is { } l) center = new Point((l.De.X + l.Para.X) / 2, (l.De.Y + l.Para.Y) / 2);
        else
        {
            var size = BoardItemViewModel.DefaultSize(kind);
            center = new Point(m.X + (m.Largura ?? size.Width) / 2, m.Y + (m.Altura ?? size.Height) / 2);
        }
        var vm = PasteModels([m], at - center)[0];
        if (vm.IsPlainNote || kind is ItemKind.Text or ItemKind.TextBox or ItemKind.Block) BeginEdit(vm, EditField.Text);
        return vm;
    }

    public void Remove(BoardItemViewModel vm)
    {
        Checkpoint();
        RemoveCore(vm);
        StructureChanged();
    }

    void RemoveCore(BoardItemViewModel vm)
    {
        if (EditingItem == vm) EndEdit();
        if (Selected == vm) Selected = null;
        vm.IsSelected = false;
        vm.PropertyChanged -= OnItemChanged;
        Items.Remove(vm);
        // Linhas presas a este item ficam soltas onde estavam.
        foreach (var l in Items.Where(i => i.Line is not null))
        {
            bool changed = false;
            if (l.Line!.De.Item == vm.Id) { l.Line.De.Item = null; changed = true; }
            if (l.Line!.Para.Item == vm.Id) { l.Line.Para.Item = null; changed = true; }
            if (changed) l.Touch(nameof(BoardItemViewModel.Line));
        }
    }

    /// <summary>Exclui todos os itens selecionados.</summary>
    public void RemoveSelected()
    {
        var list = SelectedItems.ToList();
        if (list.Count == 0) return;
        Checkpoint();
        foreach (var i in list) RemoveCore(i);
        StructureChanged();
    }

    public BoardItemViewModel Duplicate(BoardItemViewModel src)
    {
        var copies = DuplicateMany([src], new Vector(30, 30));
        return copies[0];
    }

    /// <summary>Copia vários itens com um deslocamento (Ctrl+D, Alt + arrastar). Linhas entre eles continuam ligadas.</summary>
    public List<BoardItemViewModel> DuplicateMany(IReadOnlyList<BoardItemViewModel> src, Vector offset, bool select = true)
    {
        var models = src.Select(s => s.ToModel()).ToList();
        return PasteModels(models, offset, select);
    }

    List<BoardItemViewModel> PasteModels(List<BoardItem> models, Vector offset, bool select = true)
    {
        Checkpoint();
        var ids = new Dictionary<int, int>();
        var groups = new Dictionary<int, int>();
        var result = new List<BoardItemViewModel>();
        int top = TopZ();
        foreach (var m in models.OrderBy(m => m.Z))
        {
            int old = m.Id;
            m.Id = ++_seq;
            ids[old] = m.Id;
            m.X += offset.X;
            m.Y += offset.Y;
            m.Z = ++top;
            m.Criado = DateTime.Today.ToString("yyyy-MM-dd");
            if (m.Grupo is { } g)
            {
                if (!groups.TryGetValue(g, out var ng)) groups[g] = ng = NewGroupId();
                m.Grupo = ng;
            }
        }
        foreach (var m in models)
        {
            if (m.Linha is { } l)
            {
                foreach (var end in new[] { l.De, l.Para })
                {
                    end.X += offset.X;
                    end.Y += offset.Y;
                    end.Item = end.Item is { } id && ids.TryGetValue(id, out var nid) ? nid : null;
                }
            }
            var vm = new BoardItemViewModel(m);
            vm.PropertyChanged += OnItemChanged;
            Items.Add(vm);
            vm.IsDimmed = !vm.Matches(_query);
            result.Add(vm);
        }
        if (select)
        {
            EndEdit();
            SelectMany(result, add: false);
        }
        StructureChanged();
        return result;
    }

    int NewGroupId() => Items.Select(i => i.Group ?? 0).DefaultIfEmpty(0).Max() + 1 + Rng.Next(1000);

    // ---------- camadas ----------

    void Renumber(List<BoardItemViewModel> order)
    {
        for (int i = 0; i < order.Count; i++) order[i].Z = i + 1;
    }

    public void BringToFront(BoardItemViewModel vm)
    {
        int top = TopZ();
        if (vm.Z != top || Items.Count(i => i.Z == top) > 1) vm.Z = top + 1;
    }

    public void SendToBack(BoardItemViewModel vm) => vm.Z = Items.Min(i => i.Z) - 1;

    /// <summary>Sobe ou desce uma posição na pilha de camadas.</summary>
    public void Step(BoardItemViewModel vm, int dir)
    {
        Checkpoint();
        var order = Items.OrderBy(i => i.Z).ToList();
        int i = order.IndexOf(vm), j = Math.Clamp(i + dir, 0, order.Count - 1);
        if (i == j) return;
        order.RemoveAt(i);
        order.Insert(j, vm);
        Renumber(order);
    }

    public void ToTop(IEnumerable<BoardItemViewModel> items)
    {
        Checkpoint();
        foreach (var i in items.OrderBy(i => i.Z).ToList()) BringToFront(i);
    }

    public void ToBottom(IEnumerable<BoardItemViewModel> items)
    {
        Checkpoint();
        foreach (var i in items.OrderByDescending(i => i.Z).ToList()) SendToBack(i);
    }

    /// <summary>Arrastar na lista "No quadro": coloca o item logo acima (ou abaixo) de outro.</summary>
    public void PlaceAbove(BoardItemViewModel moving, BoardItemViewModel target, bool above)
    {
        if (moving == target) return;
        Checkpoint();
        var order = Items.OrderBy(i => i.Z).ToList();
        order.Remove(moving);
        int t = order.IndexOf(target);
        order.Insert(above ? t + 1 : t, moving);
        Renumber(order);
    }

    // ---------- cor e estilo ----------

    public void SetColor(BoardItemViewModel vm, string key)
    {
        Checkpoint();
        vm.ColorKey = key;
        if (vm.Kind == ItemKind.PostIt) LastColor = key;
        if (vm.Kind is ItemKind.Shape or ItemKind.TextBox or ItemKind.Line or ItemKind.Table)
        {
            var c = Palette.Get(key);
            var s = vm.Style.Clone();
            if (vm.Kind == ItemKind.Line) s.Borda = c.Ink.ToString();
            else if (vm.Kind == ItemKind.TextBox) s.CorTexto = c.Ink.ToString();
            else { s.Preenchimento = c.Paper.ToString(); s.Borda = c.Ink.ToString(); }
            vm.Style = s;
        }
    }

    public void SetStyle(BoardItemViewModel vm, NoteStyle style)
    {
        Checkpoint();
        if (style == NoteStyle.Lined)
        {
            vm.Lined = !vm.Lined;
            if (vm.Lined && string.IsNullOrWhiteSpace(vm.Title)) vm.Title = "Lista";
        }
        else vm.Fastening = (Fastening)(int)style;
    }

    /// <summary>Muda o estilo livre de todos os selecionados (inspetor).</summary>
    public void EditStyle(Action<ItemStyle> change)
    {
        Checkpoint(coalesce: true);
        foreach (var vm in SelectedItems)
        {
            var s = vm.Style.Clone();
            change(s);
            vm.Style = s;
        }
    }

    public void CopyStyle(BoardItemViewModel vm)
    {
        _styleClip = vm.Style.Clone();
        _noteStyleClip = (vm.ColorKey, vm.Fastening, vm.Lined);
    }

    public bool HasStyleClip => _styleClip is not null;

    public void PasteStyle()
    {
        if (_styleClip is null || _noteStyleClip is not { } n) return;
        Checkpoint();
        foreach (var vm in SelectedItems)
        {
            vm.ColorKey = n.Color;
            if (vm.Kind is ItemKind.PostIt or ItemKind.Page or ItemKind.Block) vm.Fastening = n.Fastening;
            if (vm.Kind == ItemKind.PostIt) vm.Lined = n.Lined;
            vm.Style = _styleClip.Clone();
        }
    }

    public void ToggleLock(BoardItemViewModel item)
    {
        Checkpoint();
        bool on = !item.Locked;
        foreach (var i in item.IsSelected ? SelectedItems.ToList() : [item]) i.Locked = on;
    }

    public void SetDescription(BoardItemViewModel item, string text)
    {
        Checkpoint(coalesce: true);
        item.Description = text;
    }

    // ---------- grupos ----------

    public void GroupSelection()
    {
        var sel = SelectedItems.ToList();
        if (sel.Count < 2) return;
        Checkpoint();
        int g = NewGroupId();
        foreach (var i in sel) i.Group = g;
    }

    public void Ungroup()
    {
        var sel = SelectedItems.Where(i => i.Group is not null).ToList();
        if (sel.Count == 0) return;
        Checkpoint();
        foreach (var i in sel) i.Group = null;
    }

    // ---------- alinhar e distribuir ----------

    public void Align(string how)
    {
        var sel = SelectedItems.Where(i => !i.Locked && i.Kind != ItemKind.Line).ToList();
        if (sel.Count < 2) return;
        Checkpoint();
        var boxes = sel.Select(i => i.Box).ToList();
        double l = boxes.Min(b => b.Left), r = boxes.Max(b => b.Right), t = boxes.Min(b => b.Top), bt = boxes.Max(b => b.Bottom);
        foreach (var i in sel)
        {
            var b = i.Box;
            switch (how)
            {
                case "esquerda": i.X = l; break;
                case "direita": i.X = r - b.Width; break;
                case "centro": i.X = Math.Round((l + r) / 2 - b.Width / 2); break;
                case "topo": i.Y = t; break;
                case "base": i.Y = bt - b.Height; break;
                case "meio": i.Y = Math.Round((t + bt) / 2 - b.Height / 2); break;
            }
        }
        CommitMove();
    }

    public void Distribute(bool horizontal)
    {
        var sel = SelectedItems.Where(i => !i.Locked && i.Kind != ItemKind.Line).ToList();
        if (sel.Count < 3) return;
        Checkpoint();
        if (horizontal)
        {
            sel = sel.OrderBy(i => i.X).ToList();
            double total = sel.Sum(i => i.Box.Width), span = sel[^1].Box.Right - sel[0].X;
            double gap = (span - total) / (sel.Count - 1), x = sel[0].X;
            foreach (var i in sel) { i.X = Math.Round(x); x += i.Box.Width + gap; }
        }
        else
        {
            sel = sel.OrderBy(i => i.Y).ToList();
            double total = sel.Sum(i => i.Box.Height), span = sel[^1].Box.Bottom - sel[0].Y;
            double gap = (span - total) / (sel.Count - 1), y = sel[0].Y;
            foreach (var i in sel) { i.Y = Math.Round(y); y += i.Box.Height + gap; }
        }
        CommitMove();
    }

    // ---------- área de transferência ----------

    public void Copy()
    {
        var sel = SelectedItems.ToList();
        if (sel.Count == 0) return;
        _clipboard = sel.Select(i => i.ToModel()).ToList();
        try
        {
            var data = new DataObject();
            data.SetData(ClipFormat, StorageService.SerializeItems(_clipboard));
            data.SetText(string.Join("\n\n", sel.Select(i => i.DisplayName)));
            Clipboard.SetDataObject(data);
        }
        catch { }
    }

    public void Cut()
    {
        Copy();
        RemoveSelected();
    }

    /// <summary>Cola itens copiados do Quadtab. Retorna false se a área de transferência não tem itens.</summary>
    public bool PasteItems(Point? at)
    {
        List<BoardItem>? models = null;
        try
        {
            if (Clipboard.ContainsData(ClipFormat) && Clipboard.GetData(ClipFormat) is string json) models = StorageService.DeserializeItems(json);
        }
        catch { }
        models ??= _clipboard is null ? null : StorageService.DeserializeItems(StorageService.SerializeItems(_clipboard));
        if (models is null || models.Count == 0) return false;
        var minX = models.Min(m => m.X);
        var minY = models.Min(m => m.Y);
        var offset = at is { } p ? new Vector(p.X - minX, p.Y - minY) : new Vector(30, 30);
        PasteModels(models, offset);
        return true;
    }

    // ---------- edição de texto ----------

    public void BeginEdit(BoardItemViewModel vm, EditField field)
    {
        Checkpoint();
        if (EditingItem is not null && EditingItem != vm) EditingItem.Editing = EditField.None;
        Selected = vm;
        EditingItem = vm;
        vm.Editing = field;
    }

    public void EndEdit()
    {
        if (EditingItem is null) return;
        EditingItem.Editing = EditField.None;
        EditingItem = null;
    }

    /// <summary>Chamado ao soltar um item arrastado: reordena a lista da lateral.</summary>
    public void CommitMove()
    {
        RefreshOutline();
        MarkDirty();
    }

    void StructureChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SelectionCount));
        RefreshOutline();
        MarkDirty();
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoring) return;
        switch (e.PropertyName)
        {
            case nameof(BoardItemViewModel.Title):
            case nameof(BoardItemViewModel.Text):
                if (sender is BoardItemViewModel vm && _query.Length > 0)
                {
                    vm.IsDimmed = !vm.Matches(_query);
                    RefreshOutline();
                }
                MarkDirty();
                break;
            case nameof(BoardItemViewModel.ColorKey):
            case nameof(BoardItemViewModel.Fastening):
            case nameof(BoardItemViewModel.Lined):
            case nameof(BoardItemViewModel.Z):
            case nameof(BoardItemViewModel.Locked):
            case nameof(BoardItemViewModel.Spiral):
            case nameof(BoardItemViewModel.Group):
            case nameof(BoardItemViewModel.Description):
            case nameof(BoardItemViewModel.Style):
            case nameof(BoardItemViewModel.W):
            case nameof(BoardItemViewModel.H):
            case nameof(BoardItemViewModel.Rotation):
            case nameof(BoardItemViewModel.Line):
            case nameof(BoardItemViewModel.Strokes):
            case nameof(BoardItemViewModel.Table):
            case nameof(BoardItemViewModel.Crop):
            case nameof(BoardItemViewModel.Radius):
            case nameof(BoardItemViewModel.ShapeKey):
            case nameof(BoardItemViewModel.X):
            case nameof(BoardItemViewModel.Y):
                MarkDirty();
                break;
        }
    }

    /// <summary>Atualiza a lista em grupos mexendo só no que mudou (sem recriar as linhas).</summary>
    void RefreshOutline()
    {
        var matching = Items.Where(i => i.Matches(_query)).OrderBy(i => i.Y).ThenBy(i => i.X).ToList();
        foreach (var g in OutlineGroups)
        {
            var target = matching.Where(i => GroupOf(i) == g.Key).ToList();
            for (int i = 0; i < target.Count; i++)
            {
                if (i < g.Items.Count && ReferenceEquals(g.Items[i], target[i])) continue;
                int j = g.Items.IndexOf(target[i]);
                if (j >= 0) g.Items.Move(j, i);
                else g.Items.Insert(i, target[i]);
            }
            while (g.Items.Count > target.Count) g.Items.RemoveAt(g.Items.Count - 1);
            g.Changed();
        }
    }

    // ---------- desfazer / refazer ----------

    string Snapshot() => StorageService.SerializeItems(Items.OrderBy(i => i.Id).Select(i => i.ToModel()).ToList())
                         + "|" + Palette.ModelKey(Model);

    /// <summary>
    /// Guarda o estado atual antes de uma mudança. coalesce = mudanças seguidas (inspetor, digitação)
    /// viram um único passo de desfazer.
    /// </summary>
    public void Checkpoint(bool coalesce = false)
    {
        if (_restoring) return;
        if (coalesce && (DateTime.Now - _lastCheckpoint).TotalMilliseconds < 800) { _lastCheckpoint = DateTime.Now; return; }
        _lastCheckpoint = DateTime.Now;
        var snap = Snapshot();
        if (snap == _undo.LastOrDefault()) return;
        _undo.Add(snap);
        if (_undo.Count > 500) _undo.RemoveAt(0);
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo() => Travel(_undo, _redo);
    public void Redo() => Travel(_redo, _undo);

    void Travel(List<string> from, List<string> to)
    {
        EndEdit();
        var current = Snapshot();
        // Pula fotos iguais ao estado atual.
        while (from.Count > 0 && from[^1] == current) from.RemoveAt(from.Count - 1);
        if (from.Count == 0) return;
        var snap = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(current);
        Restore(snap);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    void Restore(string snap)
    {
        _restoring = true;
        int bar = snap.LastIndexOf('|');
        var models = StorageService.DeserializeItems(snap[..bar]);
        Model = Palette.ParseModel(snap[(bar + 1)..]);
        var byId = Items.ToDictionary(i => i.Id);
        var keep = new HashSet<int>(models.Select(m => m.Id));
        foreach (var vm in Items.Where(i => !keep.Contains(i.Id)).ToList())
        {
            vm.PropertyChanged -= OnItemChanged;
            vm.IsSelected = false;
            Items.Remove(vm);
        }
        foreach (var m in models)
        {
            if (byId.TryGetValue(m.Id, out var vm)) vm.Load(m);
            else
            {
                vm = new BoardItemViewModel(m);
                vm.PropertyChanged += OnItemChanged;
                Items.Add(vm);
            }
            vm.IsDimmed = !vm.Matches(_query);
        }
        Selected = null;
        _restoring = false;
        StructureChanged();
    }

    // ---------- salvamento ----------

    void MarkDirty(bool touchClock = true)
    {
        if (_saveTimer is null || _restoring) return; // ainda no construtor
        if (touchClock)
        {
            _lastEdit = DateTime.Now;
            OnPropertyChanged(nameof(Subtitle));
            IsSaving = true;
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Grava agora, se houver mudança pendente (ao fechar ou trocar de quadro).</summary>
    public void Flush()
    {
        if (!_saveTimer.IsEnabled) return;
        _saveTimer.Stop();
        StorageService.SaveBoard(FilePath, ToBoard());
        IsSaving = false;
    }

    public void Close()
    {
        Flush();
        _clockTimer.Stop();
    }

    public Board ToBoard() => new()
    {
        Titulo = BoardTitle,
        Modelo = Palette.ModelKey(Model),
        Camera = Camera,
        TituloOculto = TitleHidden,
        Grade = ShowGrid,
        Diagrama = DiagramMode,
        Cenas = Scenes.ToList(),
        Itens = Items.OrderBy(i => i.Id).Select(i => i.ToModel()).ToList(),
    };
}
