using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text;
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
    const long MaxUndoBytes = 32 * 1024 * 1024;
    readonly List<byte[]> _undo = [];
    readonly List<byte[]> _redo = [];
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
        ReadOnly = board.SomenteLeitura;
        SidebarOpen = StorageService.State.LateralAberta;
        NewStyle = Enum.TryParse<NoteStyle>(StorageService.State.EstiloPostit, out var ns) ? ns : NoteStyle.Pin;
        Search = "";
        BoardSearch = "";
        OutlineOpen = StorageService.State.Abertos.Contains("noquadro");
        foreach (var s in board.Cenas) Scenes.Add(s);

        var usedNotes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool separated = false;
        // Arquivo editado à mão ou danificado: sem itens nulos nem dois itens com o mesmo número.
        board.Itens = (board.Itens ?? []).Where(m => m is not null).ToList();
        var usedIds = new HashSet<int>();
        int nextFree = board.Itens.Select(m => m.Id).DefaultIfEmpty(0).Max();
        foreach (var m in board.Itens)
        {
            if (!usedIds.Add(m.Id)) { m.Id = ++nextFree; usedIds.Add(m.Id); }
            Sanitize(m);
            MigrateNote(m);
            MigratePage(m);
            // Cópias antigas dividiam o mesmo arquivo (e o mesmo título): cada nota ganha o seu.
            if (m.Tipo == "pagina" && m.Arquivo is { } file && !usedNotes.Add(file))
            {
                SeparateNote(m);
                separated = true;
            }
            var vm = new BoardItemViewModel(m);
            vm.PropertyChanged += OnItemChanged;
            Items.Add(vm);
            _seq = Math.Max(_seq, m.Id);
        }
        _lastEdit = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
        // Arquivo antigo com seta presa a um item que não existe mais: a ponta fica solta, para a linha poder ser arrastada.
        var known = new HashSet<int>(Items.Select(i => i.Id));
        foreach (var l in Items.Select(i => i.Line).OfType<LineData>())
            foreach (var end in new[] { l.De, l.Para })
                if (end.Item is { } id && !known.Contains(id)) { end.Item = null; end.Lado = null; }
        // Anexo em círculo ou preso a um item que não existe: fica solto (senão a arrumação nunca terminaria).
        var byId = Items.ToDictionary(i => i.Id);
        foreach (var item in Items)
        {
            var seen = new HashSet<int> { item.Id };
            for (var p = item; p.AttachedTo is { } parent; p = byId[parent])
                if (!byId.ContainsKey(parent) || !seen.Add(parent))
                {
                    p.AttachedTo = null;
                    p.AttachSide = null;
                    break;
                }
        }
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
        if (separated) MarkDirty(); // grava logo os arquivos novos, para não separar de novo na próxima vez
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

    /// <summary>Valores que o WPF não aceita (arquivo danificado ou editado à mão) viram valores válidos, para o quadro abrir sempre.</summary>
    static void Sanitize(BoardItem m)
    {
        static double Coord(double v) => double.IsFinite(v) ? Math.Clamp(v, -1e7, 1e7) : 0;
        static double? Size(double? v) => v is { } s && double.IsFinite(s) && s > 0 ? Math.Min(s, 1e5) : null;
        m.X = Coord(m.X);
        m.Y = Coord(m.Y);
        m.Largura = Size(m.Largura);
        m.Altura = Size(m.Altura);
        m.Raio = double.IsFinite(m.Raio) ? Math.Clamp(m.Raio, 0, 1000) : 0;
        if (m.Recorte is not { Length: 4 } || m.Recorte.Any(c => !double.IsFinite(c))) m.Recorte = null;
        if (m.Estilo is { } st)
        {
            st.Tamanho = double.IsFinite(st.Tamanho) ? Math.Clamp(st.Tamanho, 8, 400) : 18;
            st.Espessura = double.IsFinite(st.Espessura) ? Math.Clamp(st.Espessura, 0, 100) : 2;
            st.Opacidade = double.IsFinite(st.Opacidade) ? Math.Clamp(st.Opacidade, 0, 1) : 1;
        }
        if (m.Linha is { } l)
        {
            l.De ??= new LineEnd();
            l.Para ??= new LineEnd();
            foreach (var end in new[] { l.De, l.Para })
            {
                end.X = Coord(end.X);
                end.Y = Coord(end.Y);
            }
            if (l.DobraX is { } dx && !double.IsFinite(dx)) l.DobraX = null;
            if (l.DobraY is { } dy && !double.IsFinite(dy)) l.DobraY = null;
        }
        m.Tracos = m.Tracos?.Where(t => t?.Pontos is not null).ToList();
        m.Tabela = m.Tabela?.Where(r => r is not null).Select(r => r.Select(c => c ?? "").ToList()).ToList();
    }

    /// <summary>Blocos com o título padrão antigo ("Bloco de vidro") ficam com o título livre.</summary>
    static void MigrateNote(BoardItem m)
    {
        if (m.Tipo == "bloco" && m.Titulo == "Bloco de vidro") m.Titulo = "";
    }

    /// <summary>Dá à nota um arquivo só dela, com o mesmo título e texto (só o nome do arquivo recebe número se já existir).</summary>
    static void SeparateNote(BoardItem m)
    {
        var path = StorageService.CreatePage(string.IsNullOrWhiteSpace(m.Titulo) ? "Nova nota" : m.Titulo, $"# {m.Titulo}\n\n{m.Texto}\n");
        m.Arquivo = StorageService.Rel(path);
    }

    /// <summary>Páginas antigas (título + texto no quadro) viram arquivos .md no cofre.</summary>
    static void MigratePage(BoardItem m)
    {
        if (m.Tipo != "pagina") return;
        if (m.Arquivo is { } rel && File.Exists(StorageService.Full(rel)))
        {
            var text = StorageService.ReadPage(StorageService.Full(rel));
            m.Titulo = StorageService.PageTitle(StorageService.Full(rel));
            m.Texto = Markdown.StripTitle(text, m.Titulo);
            return;
        }
        var name = string.IsNullOrWhiteSpace(m.Titulo) ? "Nova nota" : m.Titulo;
        var existing = StorageService.FindPage(name);
        var path = existing ?? StorageService.CreatePage(name, $"# {name}\n\n{m.Texto}\n");
        m.Arquivo = StorageService.Rel(path);
        m.Titulo = StorageService.PageTitle(path);
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

    /// <summary>Somente visualização ("…" → Bloquear quadro): o quadro não aceita mudanças.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool ReadOnly { get; set; }

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
    public string ModelName => Palette.ModelName(Model);
    public int ModelIndex => (int)Model;
    public string Subtitle => $"Editado {EditedAgo()}";
    public string StatusText => $"{(IsSaving ? "Salvando…" : "Salvo")}{(ReadOnly ? " · somente visualização" : "")}";
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
    partial void OnReadOnlyChanged(bool value)
    {
        if (value) { EndEdit(); ClearSelection(); }
        MarkDirty(touchClock: false);
    }
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
            m.Titulo = "Nova nota";
            m.Texto = "Escreva aqui…";
        }

        if (kind == ItemKind.Table) m.Tabela = [["Coluna 1", "Coluna 2", "Coluna 3"], ["", "", ""], ["", "", ""]];

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
        else if (kind == ItemKind.Page) BeginEdit(vm, EditField.Title); // nota nova: já escrevendo o título
        return vm;
    }

    /// <summary>Título: texto solto grande, já escrevendo; cor e caligrafia pelo botão direito.</summary>
    public BoardItemViewModel AddTitle(double cx, double cy)
    {
        var vm = Add(ItemKind.Text, "branco", cx, cy);
        vm.Text = "Título";
        var st = vm.Style.Clone();
        st.Tamanho = 64;
        vm.Style = st;
        Remember(vm.ToModel()); // 2 cliques no vazio repetem o título, não um texto pequeno
        return vm;
    }

    public void SetFont(BoardItemViewModel vm, string font)
    {
        if (vm.Style.Fonte == font) return;
        Checkpoint();
        var st = vm.Style.Clone();
        st.Fonte = font;
        vm.Style = st;
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
        t.AnexadoA = null;
        t.AnexoLado = null;
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
        if (kind == ItemKind.PostIt)
        {
            // Post-it repetido segue a última cor e o último estilo usados (mesmo se mudados depois de criar).
            m.Cor = LastColor;
            m.Pautado = NewStyle == NoteStyle.Lined;
            if (!m.Pautado) m.Fixacao = BoardItem.FasteningToJson((Fastening)(int)NewStyle);
        }
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

    // ---------- tabelas: linhas e colunas sem limite ----------

    /// <summary>Linha nova embaixo ou coluna nova à direita; com coluna nova, a tabela alarga para as outras não encolherem.</summary>
    public void TableAdd(BoardItemViewModel table, bool row)
    {
        if (table.Table is not { Count: > 0 } t) return;
        Checkpoint();
        int cols = t.Max(r => r.Count);
        var copy = t.Select(r => r.ToList()).ToList();
        if (row) copy.Add(Enumerable.Repeat("", cols).ToList());
        else
        {
            for (int i = 0; i < copy.Count; i++)
            {
                while (copy[i].Count < cols) copy[i].Add("");
                copy[i].Add(i == 0 ? $"Coluna {cols + 1}" : "");
            }
            table.W = table.Box.Width + table.Box.Width / cols;
        }
        table.Table = copy;
    }

    /// <summary>Arrastando o + da tabela: deixa com tantas linhas ou colunas, a partir da tabela como estava (voltar o arraste devolve o texto).</summary>
    public void TableResize(BoardItemViewModel table, List<List<string>> original, double originalWidth, bool row, int count)
    {
        int cols = original.Max(r => r.Count);
        if (count == (row ? table.Table!.Count : table.Table!.Max(r => r.Count))) return;
        var copy = original.Select(r => r.ToList()).ToList();
        if (row)
        {
            copy = copy.Take(count).ToList();
            while (copy.Count < count) copy.Add(Enumerable.Repeat("", cols).ToList());
        }
        else
        {
            for (int i = 0; i < copy.Count; i++)
            {
                var r = copy[i];
                while (r.Count < cols) r.Add("");
                if (r.Count > count) r.RemoveRange(count, r.Count - count);
                while (r.Count < count) r.Add(i == 0 ? $"Coluna {r.Count + 1}" : "");
            }
            table.W = originalWidth * count / cols;
        }
        table.Table = copy;
    }

    /// <summary>Tira a última linha ou coluna; a tabela fica com pelo menos uma de cada.</summary>
    public void TableRemove(BoardItemViewModel table, bool row)
    {
        if (table.Table is not { Count: > 0 } t) return;
        int cols = t.Max(r => r.Count);
        if (row ? t.Count <= 1 : cols <= 1) return;
        Checkpoint();
        var copy = t.Select(r => r.ToList()).ToList();
        if (row) copy.RemoveAt(copy.Count - 1);
        else
        {
            foreach (var r in copy)
                if (r.Count == cols) r.RemoveAt(cols - 1);
            table.W = table.Box.Width - table.Box.Width / cols;
        }
        table.Table = copy;
    }

    // ---------- anexos: um item preso em cima ou embaixo de outro ----------

    IEnumerable<BoardItemViewModel> AttachedItems(BoardItemViewModel parent) => Items.Where(i => i.AttachedTo == parent.Id);

    /// <summary>Tudo o que está anexado a este item, inclusive anexos dos anexos.</summary>
    public List<BoardItemViewModel> AttachedTree(BoardItemViewModel parent)
    {
        var result = new List<BoardItemViewModel>();
        void Walk(BoardItemViewModel p)
        {
            foreach (var c in AttachedItems(p))
                if (!result.Contains(c)) { result.Add(c); Walk(c); }
        }
        Walk(parent);
        return result;
    }

    const double AttachInset = 14, AttachGap = 10;

    public void Attach(BoardItemViewModel child, BoardItemViewModel parent, string side)
    {
        child.AttachPrevW = child.W;
        child.AttachPrevH = child.H;
        child.AttachedTo = parent.Id;
        child.AttachSide = side;
        LayoutAttached(parent);
        MarkDirty();
    }

    public void Detach(BoardItemViewModel child)
    {
        var parent = Items.FirstOrDefault(i => i.Id == child.AttachedTo);
        child.AttachedTo = null;
        child.AttachSide = null;
        child.W = child.AttachPrevW;
        child.H = child.AttachPrevH;
        child.AttachPrevW = child.AttachPrevH = null;
        if (parent is not null) LayoutAttached(parent);
        MarkDirty();
    }

    /// <summary>Põe os anexos dentro do principal, empilhados e com margem; o principal reserva o espaço e cresce junto.</summary>
    public void LayoutAttached(BoardItemViewModel parent)
    {
        var kids = AttachedItems(parent).OrderBy(c => c.Y).ThenBy(c => c.Id).ToList();
        var box = parent.Box;
        double inLeft = parent.Kind == ItemKind.Page && parent.Spiral ? 32 : AttachInset;
        double inner = box.Width - inLeft - AttachInset;
        foreach (var c in kids) FitAttached(c, inner);

        var above = kids.Where(c => c.AttachSide == "cima").ToList();
        var below = kids.Where(c => c.AttachSide != "cima").ToList();
        double Stack(List<BoardItemViewModel> list) => list.Count == 0 ? 0 : list.Sum(c => c.Box.Height) + AttachGap * (list.Count - 1);
        double aboveHeight = Stack(above), belowHeight = Stack(below);
        double top = above.Count == 0 ? 0 : aboveHeight + AttachInset, bottom = below.Count == 0 ? 0 : belowHeight + AttachInset;
        if (Math.Abs(parent.AttachTop - top) > 0.5) parent.AttachTop = top;
        if (Math.Abs(parent.AttachBottom - bottom) > 0.5) parent.AttachBottom = bottom;

        // O post-it tem a faixa adesiva em cima e o rodapé embaixo: os anexos ficam entre os dois.
        bool postIt = parent.Kind == ItemKind.PostIt;
        void Place(List<BoardItemViewModel> list, double y)
        {
            foreach (var c in list)
            {
                double width = IsFillKind(c) ? inner : c.Box.Width;
                c.X = box.X + inLeft + (inner - width) / 2;
                c.Y = y;
                if (c.Z <= parent.Z) c.Z = parent.Z + 1; // sempre por cima do principal
                y += c.Box.Height + AttachGap;
                LayoutAttached(c);
            }
        }
        Place(above, box.Y + (postIt ? 30 : AttachInset));
        Place(below, box.Bottom - (postIt ? 26 : AttachInset) - belowHeight);
    }

    static bool IsFillKind(BoardItemViewModel c) => c.Kind is ItemKind.Page or ItemKind.PostIt or ItemKind.Block or ItemKind.TextBox or ItemKind.Table;

    /// <summary>Notas, post-its e cartões ocupam a largura toda; formas e imagens só encolhem se não couberem.</summary>
    static void FitAttached(BoardItemViewModel c, double inner)
    {
        if (inner <= 0) return;
        if (IsFillKind(c))
        {
            if (c.W != inner) c.W = inner;
        }
        else if (c.Box.Width > inner)
        {
            double ratio = inner / c.Box.Width;
            c.H = c.Box.Height * ratio;
            c.W = inner;
        }
    }

    /// <summary>Um item mudou de tamanho: acerta os anexos dele e a posição dele no principal.</summary>
    public void OnItemResized(BoardItemViewModel item)
    {
        LayoutAttached(item);
        if (item.AttachedTo is { } id && Items.FirstOrDefault(i => i.Id == id) is { } parent) LayoutAttached(parent);
    }

    void RemoveCore(BoardItemViewModel vm)
    {
        if (!Items.Contains(vm)) return; // já saiu junto com outro item (seta do item apagado)
        if (EditingItem == vm) EndEdit();
        foreach (var c in AttachedItems(vm).ToList()) Detach(c);
        if (vm.AttachedTo is not null) Detach(vm);
        if (Selected == vm) Selected = null;
        vm.IsSelected = false;
        vm.PropertyChanged -= OnItemChanged;
        Items.Remove(vm);
        // Seta ligada só sai junto com o item: nenhuma fica solta no quadro (Ctrl+Z traz os dois de volta).
        foreach (var l in Items.Where(i => i.Line is { } d && (d.De.Item == vm.Id || d.Para.Item == vm.Id)).ToList())
            RemoveCore(l);
        DeleteNoteFile(vm);
    }

    /// <summary>Nota apagada não fica guardada: o .md some de vez (Ctrl+Z o recria nesta sessão). Só fica se outro item ou quadro ainda o usa.</summary>
    void DeleteNoteFile(BoardItemViewModel vm)
    {
        if (vm.Kind != ItemKind.Page || vm.FileFullPath is not { } path || !File.Exists(path)) return;
        if (Items.Any(i => i.Kind == ItemKind.Page && string.Equals(i.FileFullPath, path, StringComparison.OrdinalIgnoreCase))) return;
        if (StorageService.NoteUsedElsewhere(path, FilePath)) return;
        try
        {
            StorageService.DeletePage(path);
            App.PageDeleted(path);
        }
        catch (IOException ex) { App.Log(ex); }
        catch (UnauthorizedAccessException ex) { App.Log(ex); }
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
        // Os anexos vão junto: sem eles a cópia do principal nasceria menor.
        var all = src.Concat(src.SelectMany(AttachedTree)).Distinct();
        var models = all.Select(s => s.ToModel()).ToList();
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
            Sanitize(m);
            m.X += offset.X;
            m.Y += offset.Y;
            m.Z = ++top;
            m.Criado = DateTime.Today.ToString("yyyy-MM-dd");
            if (m.Tipo == "pagina") SeparateNote(m); // a cópia de uma nota é outra nota, com título próprio
            if (m.Grupo is { } g)
            {
                if (!groups.TryGetValue(g, out var ng)) groups[g] = ng = NewGroupId();
                m.Grupo = ng;
            }
        }
        foreach (var m in models)
        {
            // Copiado junto com o principal continua anexado; copiado sozinho fica solto.
            m.AnexadoA = m.AnexadoA is { } parentId && ids.TryGetValue(parentId, out var newParent) ? newParent : null;
            if (m.AnexadoA is null)
            {
                m.AnexoLado = null;
                m.AnexoLargura = m.AnexoAltura = null; // cópia solta: o tamanho atual é o dela
            }
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
        foreach (var c in AttachedTree(vm)) c.Z = ++top + 1;
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
            else
            {
                s.Preenchimento = ColorMath.WithAlpha(c.Paper, FillAlpha(vm)).ToString(); // trocar a cor mantém a transparência do fundo
                s.Borda = (c.IsStrong ? c.Paper : c.Ink).ToString();
                s.CorTexto = c.IsDark ? "#FFFFFFFF" : "#FF1D2130"; // texto branco só dentro de item escuro
            }
            vm.Style = s;
        }
    }

    /// <summary>Quanto do fundo da forma aparece (0 a 1).</summary>
    public static double FillAlpha(BoardItemViewModel vm) =>
        vm.FillBrush is System.Windows.Media.SolidColorBrush b ? b.Color.A / 255.0 : 1;

    /// <summary>Transparência só do fundo da forma; a borda e o texto continuam firmes. Arrastar a barra conta como um desfazer só.</summary>
    public void SetFillTransparency(BoardItemViewModel vm, double percent)
    {
        Checkpoint(coalesce: true);
        var color = vm.Style.Preenchimento is null ? vm.Color.Paper : ((System.Windows.Media.SolidColorBrush)vm.FillBrush).Color;
        var s = vm.Style.Clone();
        s.Preenchimento = ColorMath.WithAlpha(color, 1 - Math.Clamp(percent, 0, 100) / 100).ToString();
        vm.Style = s;
    }

    public void SetStyle(BoardItemViewModel vm, NoteStyle style)
    {
        Checkpoint();
        if (vm.Kind == ItemKind.PostIt) NewStyle = style; // os próximos post-its seguem o último estilo escolhido
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
        _clipboard = sel.Concat(sel.SelectMany(AttachedTree)).Distinct().Select(i => i.ToModel()).ToList();
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
        var item = EditingItem;
        item.Editing = EditField.None;
        EditingItem = null;
        if (item.Kind == ItemKind.Page) SyncNote(item);
    }

    /// <summary>
    /// Nota editada direto no quadro: o título e o texto são gravados no .md ("# Título" + texto). Título novo também
    /// renomeia o arquivo (os links [[ ]] acompanham); títulos iguais em notas diferentes só numeram o nome do arquivo.
    /// </summary>
    static void SyncNote(BoardItemViewModel note)
    {
        if (note.FileFullPath is not { } path || !File.Exists(path)) return;
        try
        {
            var title = note.Title.Trim();
            if (title.Length > 0 && title != Markdown.TitleOf(StorageService.ReadPage(path)))
            {
                App.RenamePage(path, title); // também troca FilePath e Title do item
                if (note.FileFullPath is not { } renamed) return;
                path = renamed;
            }
            var content = $"# {note.Title}\n\n{note.Text.TrimEnd()}\n";
            if (StorageService.ReadPage(path) != content) StorageService.WritePage(path, content);
        }
        catch (IOException ex) { App.Log(ex); }
        catch (UnauthorizedAccessException ex) { App.Log(ex); }
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
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SelectionCount));
        RefreshOutline();
        MarkDirty();
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_restoring) return;
        if (e.PropertyName is nameof(BoardItemViewModel.X) or nameof(BoardItemViewModel.Y) && sender is BoardItemViewModel movedItem)
            LayoutAttached(movedItem); // o anexo segue o principal (setas, alinhar, distribuir...)
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
    /// <summary>Numera os post-its do quadro pela ordem em que foram colados (1, 2, 3...).</summary>
    void RenumberNotes()
    {
        int n = 0;
        foreach (var it in Items.Where(i => i.Kind == ItemKind.PostIt).OrderBy(i => i.Id)) it.NoteNumber = ++n;
    }

    void RefreshOutline()
    {
        RenumberNotes();
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

    static byte[] Pack(string text)
    {
        using var output = new MemoryStream();
        using (var zip = new DeflateStream(output, CompressionLevel.Fastest)) zip.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }

    static string Unpack(byte[] data)
    {
        using var zip = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Guarda o estado atual antes de uma mudança. coalesce = mudanças seguidas (inspetor, digitação)
    /// viram um único passo de desfazer.
    /// </summary>
    public void Checkpoint(bool coalesce = false)
    {
        if (_restoring) return;
        if (coalesce && (DateTime.Now - _lastCheckpoint).TotalMilliseconds < 800) { _lastCheckpoint = DateTime.Now; return; }
        _lastCheckpoint = DateTime.Now;
        var snap = Pack(Snapshot());
        if (_undo.Count > 0 && snap.AsSpan().SequenceEqual(_undo[^1])) return;
        _undo.Add(snap);
        // Cada passo é o quadro inteiro: comprimido e com teto, para o histórico não crescer sem parar.
        long total = _undo.Sum(s => (long)s.Length);
        while (_undo.Count > 1 && total > MaxUndoBytes)
        {
            total -= _undo[0].Length;
            _undo.RemoveAt(0);
        }
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // Somente visualização: desfazer e refazer também não mudam nada.
    public void Undo() { if (!ReadOnly) Travel(_undo, _redo); }
    public void Redo() { if (!ReadOnly) Travel(_redo, _undo); }

    void Travel(List<byte[]> from, List<byte[]> to)
    {
        EndEdit();
        var current = Pack(Snapshot());
        // Pula fotos iguais ao estado atual.
        while (from.Count > 0 && from[^1].AsSpan().SequenceEqual(current)) from.RemoveAt(from.Count - 1);
        if (from.Count == 0) return;
        var snap = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(current);
        Restore(Unpack(snap));
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
            DeleteNoteFile(vm);
        }
        foreach (var m in models)
        {
            // Desfazer a exclusão de uma nota: o arquivo volta com o título e o texto dela.
            if (m is { Tipo: "pagina", Arquivo: { } rel } && !File.Exists(StorageService.Full(rel)))
                StorageService.WritePage(StorageService.Full(rel), $"# {m.Titulo}\n\n{m.Texto}\n");
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
        SomenteLeitura = ReadOnly,
        Cenas = Scenes.ToList(),
        Itens = Items.OrderBy(i => i.Id).Select(i => i.ToModel()).ToList(),
    };
}
