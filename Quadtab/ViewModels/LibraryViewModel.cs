using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quadtab.Models;
using Quadtab.Services;

namespace Quadtab.ViewModels;

public enum SideKind { Board, Page, Item, Heading, Folder, File, More }

/// <summary>
/// Uma linha da barra lateral: quadro, página, item de um quadro, título de página, pasta ou arquivo do
/// cofre, ou "Ver todos". Os filhos são lidos só quando a linha abre.
/// </summary>
public partial class SideNode : ObservableObject
{
    public SideNode(SideKind kind, string path, string name, int depth, string section)
    {
        Kind = kind;
        Path = path;
        Name = name;
        Depth = depth;
        Section = section;
        _building = true;
        IsOpen = CanExpand && StorageService.State.Abertos.Contains(OpenKey);
        _building = false;
    }

    readonly bool _building;

    public SideKind Kind { get; }
    public string Path { get; }
    public int Depth { get; }
    public string Section { get; }
    public int? ItemId { get; init; }
    public string? Icon { get; init; }
    public Brush? Dot { get; init; }
    public bool Expandable { get; init; } = true;
    public ObservableCollection<SideNode> Children { get; } = [];
    internal bool Loaded;

    [ObservableProperty] public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArrowAngle))]
    public partial bool IsOpen { get; set; }

    [ObservableProperty] public partial bool IsCurrent { get; set; }
    [ObservableProperty] public partial bool IsPinned { get; set; }
    [ObservableProperty] public partial bool IsImportant { get; set; }
    [ObservableProperty] public partial bool IsRenaming { get; set; }

    public bool CanExpand => Expandable && Kind is SideKind.Board or SideKind.Page or SideKind.Folder;
    public bool HasActions => Kind is SideKind.Board or SideKind.Page;
    public bool IsMore => Kind == SideKind.More;
    public bool HasIcon => Icon is not null || Dot is not null;
    public double ArrowAngle => IsOpen ? 90 : 0;
    public System.Windows.Thickness Indent => new(4 + Depth * 16, 0, 6, 0);
    string OpenKey => $"{Section}:{Path}";

    /// <summary>Balão de ajuda da linha.</summary>
    public string Tip => Kind switch
    {
        SideKind.Board => IsCurrent ? "Este é o quadro aberto" : "Abre este quadro",
        SideKind.Page => "Abre esta nota ao lado do quadro",
        SideKind.Item => "Leva a câmera até este item",
        SideKind.Heading => "Abre a nota deste título",
        SideKind.Folder => "Abre ou fecha esta pasta",
        SideKind.File => "Abre este arquivo",
        _ => "Mostra a lista completa",
    };

    public string PinTip => IsPinned ? "Tira dos fixados" : "Fixa no topo da barra lateral";
    public string StarTip => IsImportant ? "Tira dos importantes" : "Marca como importante";

    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(Tip));
    partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinTip));
    partial void OnIsImportantChanged(bool value) => OnPropertyChanged(nameof(StarTip));

    partial void OnIsOpenChanged(bool value)
    {
        if (!CanExpand || _building) return;
        MainViewModel.SaveOpen(OpenKey, value);
        if (value) App.Library?.LoadChildren(this);
    }
}

/// <summary>Um cartão da galeria "Todos os quadros".</summary>
public sealed class GalleryCard
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required DateTime Modified { get; init; }
    public string? Thumb { get; init; }
    public BoardModel Model { get; init; }
    public bool IsFavorite { get; init; }
    public TrashEntry? Trash { get; init; }
    public string DateLabel => Trash is { } t
        ? $"Apagado em {t.DeletedAt:dd/MM} · some em {Math.Max(0, 30 - (int)(DateTime.Now - t.DeletedAt).TotalDays)} dias"
        : $"Editado em {Modified:dd/MM/yyyy HH:mm}";
    public bool IsTrash => Trash is not null;
}

/// <summary>Um resultado do "Procurar" em outros quadros e páginas.</summary>
public sealed record SearchResult(string Path, string Name, string Snippet, bool IsPage);

/// <summary>Tudo o que existe no cofre: lateral, galeria, busca e o histórico voltar/avançar.</summary>
public partial class LibraryViewModel : ObservableObject
{
    const int MaxBoards = 8, MaxPinned = 6, MaxPages = 10, MaxChildren = 14;

    readonly Dictionary<string, (DateTime Stamp, Board Board, string Text)> _meta = new();

    public LibraryViewModel()
    {
        var open = StorageService.State.Abertos;
        PinnedOpen = open.Contains("fixados");
        ImportantOpen = open.Contains("importantes");
        PagesOpen = open.Contains("paginas");
        VaultOpen = open.Contains("cofre");
        GallerySort = "data";
        GalleryGroup = "todos";
        StorageService.LibraryChanged += Refresh;
        Refresh();
    }

    public ObservableCollection<SideNode> Boards { get; } = [];
    public ObservableCollection<SideNode> Pinned { get; } = [];
    public ObservableCollection<SideNode> Important { get; } = [];
    public ObservableCollection<SideNode> Pages { get; } = [];
    public ObservableCollection<SideNode> Vault { get; } = [];
    public ObservableCollection<GalleryCard> Gallery { get; } = [];
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    // ---------- seções que abrem e fecham (estado salvo) ----------

    [ObservableProperty][NotifyPropertyChangedFor(nameof(PinnedArrow))] public partial bool PinnedOpen { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ImportantArrow))] public partial bool ImportantOpen { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PagesArrow))] public partial bool PagesOpen { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(VaultArrow))] public partial bool VaultOpen { get; set; }
    partial void OnPinnedOpenChanged(bool value) => MainViewModel.SaveOpen("fixados", value);
    partial void OnImportantOpenChanged(bool value) => MainViewModel.SaveOpen("importantes", value);
    partial void OnPagesOpenChanged(bool value) => MainViewModel.SaveOpen("paginas", value);
    partial void OnVaultOpenChanged(bool value) => MainViewModel.SaveOpen("cofre", value);
    public double PinnedArrow => PinnedOpen ? 0 : -90;
    public double ImportantArrow => ImportantOpen ? 0 : -90;
    public double PagesArrow => PagesOpen ? 0 : -90;
    public double VaultArrow => VaultOpen ? 0 : -90;

    public bool HasPinned => Pinned.Count > 0;
    public bool HasImportant => Important.Count > 0;

    [RelayCommand] void TogglePinnedSection() => PinnedOpen = !PinnedOpen;
    [RelayCommand] void ToggleImportantSection() => ImportantOpen = !ImportantOpen;
    [RelayCommand] void TogglePagesSection() => PagesOpen = !PagesOpen;
    [RelayCommand] void ToggleVaultSection() => VaultOpen = !VaultOpen;

    // ---------- galeria ----------

    [ObservableProperty] public partial bool GalleryOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GalleryTitle))]
    public partial string GalleryGroup { get; set; }

    [ObservableProperty] public partial string GallerySort { get; set; }

    public string GalleryTitle => GalleryGroup switch
    {
        "fixados" => "Fixados",
        "importantes" => "Importantes",
        "apagados" => "Apagados recentemente",
        _ => "Todos os quadros",
    };

    public int TrashCount => StorageService.TrashEntries().Count();

    partial void OnGallerySortChanged(string value) => BuildGallery();
    partial void OnGalleryGroupChanged(string value) => BuildGallery();

    [RelayCommand]
    void ShowGallery(string group)
    {
        // Clicar de novo no mesmo botão fecha a galeria.
        if (GalleryOpen && GalleryGroup == group) { GalleryOpen = false; return; }
        App.MakeThumbnail();
        GalleryGroup = group;
        BuildGallery();
        GalleryOpen = true;
    }

    [RelayCommand] void CloseGallery() => GalleryOpen = false;

    // ---------- histórico (voltar / avançar) ----------

    readonly List<string> _history = [];
    int _index = -1;
    bool _traveling;

    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }

    /// <summary>Chamado ao abrir um quadro ou uma página.</summary>
    public void Visit(string path)
    {
        if (_traveling) return;
        if (_index >= 0 && string.Equals(_history[_index], path, StringComparison.OrdinalIgnoreCase)) return;
        if (_index < _history.Count - 1) _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        _history.Add(path);
        if (_history.Count > 100) _history.RemoveAt(0);
        _index = _history.Count - 1;
        UpdateTravel();
    }

    [RelayCommand] void GoBack() => Travel(-1);
    [RelayCommand] void GoForward() => Travel(+1);

    void Travel(int dir)
    {
        int i = _index + dir;
        while (i >= 0 && i < _history.Count && !File.Exists(_history[i])) i += dir;
        if (i < 0 || i >= _history.Count) return;
        _index = i;
        _traveling = true;
        try
        {
            var p = _history[i];
            if (p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) App.OpenPage(p);
            else App.OpenBoard(p);
        }
        finally { _traveling = false; }
        UpdateTravel();
    }

    void UpdateTravel()
    {
        CanGoBack = _index > 0;
        CanGoForward = _index < _history.Count - 1;
    }

    // ---------- leitura do cofre ----------

    (Board Board, string Text) Meta(string path)
    {
        var stamp = File.GetLastWriteTime(path);
        if (_meta.TryGetValue(path, out var m) && m.Stamp == stamp) return (m.Board, m.Text);
        var b = StorageService.LoadBoard(path);
        var text = string.Join("\n", b.Itens.Select(i => i.Titulo + " " + i.Texto + " " + i.Descricao));
        _meta[path] = (stamp, b, text);
        return (b, text);
    }

    static string NameOf(string path) => System.IO.Path.GetFileNameWithoutExtension(path);
    static bool IsPagePath(string path) => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    SideNode Node(string path, int depth, string section)
    {
        bool page = IsPagePath(path);
        var n = new SideNode(page ? SideKind.Page : SideKind.Board, path, NameOf(path), depth, section)
        {
            Icon = page ? "IcPage" : section == "quadros" ? null : "IcBoard",
            Expandable = !page || Rendering.Markdown.Headings(StorageService.ReadPage(path)).Any(),
        };
        n.IsCurrent = !page && string.Equals(path, App.ViewModel?.FilePath, StringComparison.OrdinalIgnoreCase);
        n.IsPinned = StorageService.IsPinned(path);
        n.IsImportant = StorageService.IsFavorite(path);
        if (n.IsOpen) LoadChildren(n);
        return n;
    }

    /// <summary>Quadros pela ordem do último aberto (os outros depois, pela data).</summary>
    IEnumerable<string> BoardsByRecent()
    {
        var all = StorageService.BoardFiles().ToList();
        var recent = StorageService.State.Recentes.Select(StorageService.Full)
            .Where(p => all.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
        return recent.Concat(all.Where(p => !recent.Contains(p, StringComparer.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTime));
    }

    public void Refresh()
    {
        var boards = BoardsByRecent().ToList();
        Fill(Boards, boards.Take(MaxBoards).Select(p => Node(p, 0, "quadros")), boards.Count > MaxBoards ? "todos" : null, "quadros");

        List<string> Marked(List<string> rel) => rel.Select(StorageService.Full).Where(File.Exists).ToList();
        var pinned = Marked(StorageService.State.Fixados);
        Fill(Pinned, pinned.Take(MaxPinned).Select(p => Node(p, 0, "fixados")), pinned.Count > MaxPinned ? "fixados" : null, "fixados");
        var important = Marked(StorageService.State.Favoritos);
        Fill(Important, important.Take(MaxPinned).Select(p => Node(p, 0, "importantes")), important.Count > MaxPinned ? "importantes" : null, "importantes");

        // As páginas ficam só no quadro: a lateral não as lista (nem lê os arquivos à toa).

        Vault.Clear();
        foreach (var n in FolderEntries(StorageService.Root, 0)) Vault.Add(n);

        OnPropertyChanged(nameof(HasPinned));
        OnPropertyChanged(nameof(HasImportant));
        OnPropertyChanged(nameof(TrashCount));
        if (GalleryOpen) BuildGallery();
    }

    static void Fill(ObservableCollection<SideNode> into, IEnumerable<SideNode> nodes, string? moreGroup, string section)
    {
        into.Clear();
        foreach (var n in nodes) into.Add(n);
        if (moreGroup is not null) into.Add(new SideNode(SideKind.More, moreGroup, "Ver todos", 0, section));
    }

    /// <summary>Lê os filhos de uma linha: itens do quadro, títulos da página ou conteúdo da pasta.</summary>
    public void LoadChildren(SideNode n)
    {
        n.Children.Clear();
        n.Loaded = true;
        switch (n.Kind)
        {
            case SideKind.Board:
                {
                    var items = BoardChildren(n.Path).ToList();
                    foreach (var (id, name, icon, dot) in items.Take(MaxChildren))
                        n.Children.Add(new SideNode(SideKind.Item, n.Path, name, n.Depth + 1, n.Section) { ItemId = id, Icon = icon, Dot = dot });
                    if (items.Count > MaxChildren) n.Children.Add(new SideNode(SideKind.More, "abrir:" + n.Path, "Ver todos", n.Depth + 1, n.Section));
                    if (items.Count == 0) n.Children.Add(new SideNode(SideKind.More, "abrir:" + n.Path, "Quadro vazio", n.Depth + 1, n.Section));
                    break;
                }
            case SideKind.Page:
                foreach (var (_, text) in Rendering.Markdown.Headings(StorageService.ReadPage(n.Path)).Take(MaxChildren))
                    n.Children.Add(new SideNode(SideKind.Heading, n.Path, text, n.Depth + 1, n.Section) { Icon = "IcTitle" });
                break;
            case SideKind.Folder:
                foreach (var c in FolderEntries(n.Path, n.Depth + 1)) n.Children.Add(c);
                break;
        }
    }

    /// <summary>Itens de um quadro (o aberto vem da tela; os outros, do arquivo), de cima para baixo.</summary>
    IEnumerable<(int Id, string Name, string Icon, Brush? Dot)> BoardChildren(string path)
    {
        if (App.ViewModel is { } vm && string.Equals(vm.FilePath, path, StringComparison.OrdinalIgnoreCase))
            return vm.Items.Where(i => i.Kind != ItemKind.Line).OrderBy(i => Math.Round(i.Y / 120)).ThenBy(i => i.X)
                .Select(i => (i.Id, i.DisplayName, IconFor(i.Kind), i.Kind is ItemKind.PostIt ? (Brush?)i.PaperBrush : null));
        return Meta(path).Board.Itens.Where(i => i.Tipo != "linha").OrderBy(i => Math.Round(i.Y / 120)).ThenBy(i => i.X)
            .Select(i =>
            {
                var kind = BoardItem.KindFromJson(i.Tipo);
                var name = (string.IsNullOrWhiteSpace(i.Titulo) ? i.Texto : i.Titulo).Split('\n')[0].Trim();
                if (name.Length == 0) name = kind switch { ItemKind.Image => "Imagem", ItemKind.Drawing => "Desenho", ItemKind.Shape => "Forma", _ => "Sem texto" };
                return (i.Id, name, IconFor(kind), kind == ItemKind.PostIt ? (Brush?)Palette.Get(i.Cor).PaperBrush : null);
            });
    }

    static string IconFor(ItemKind k) => k switch
    {
        ItemKind.Page => "IcPage",
        ItemKind.Block => "IcBlock",
        ItemKind.Text => "IcText",
        ItemKind.TextBox => "IcTextBox",
        ItemKind.Shape => "IcShapes",
        ItemKind.Drawing => "IcPen",
        ItemKind.Image => "IcImage",
        ItemKind.Video => "IcPlay",
        ItemKind.File => "IcFile",
        ItemKind.Link => "IcLink",
        ItemKind.Table => "IcTable",
        _ => "IcBoard",
    };

    /// <summary>Árvore da pasta Cofre: subpastas primeiro, depois os arquivos (sem os ocultos).</summary>
    static IEnumerable<SideNode> FolderEntries(string dir, int depth)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var d in Directory.EnumerateDirectories(dir).Where(d => !System.IO.Path.GetFileName(d).StartsWith('.'))
                     .OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
            yield return new SideNode(SideKind.Folder, d, System.IO.Path.GetFileName(d), depth, "cofre") { Icon = "IcFolder" };
        foreach (var f in Directory.EnumerateFiles(dir).Where(f => !System.IO.Path.GetFileName(f).StartsWith('.') && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
        {
            var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
            var icon = ext switch
            {
                StorageService.BoardExt => "IcBoard",
                ".md" => "IcPage",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "IcImage",
                ".mp4" or ".mov" or ".webm" or ".wmv" => "IcPlay",
                _ => "IcFile",
            };
            var name = ext is StorageService.BoardExt or ".md" ? NameOf(f) : System.IO.Path.GetFileName(f);
            yield return new SideNode(SideKind.File, f, name, depth, "cofre") { Icon = icon };
        }
    }

    /// <summary>Os itens do quadro aberto mudaram: atualiza os filhos abertos dele.</summary>
    public void RefreshCurrent()
    {
        var cur = App.ViewModel?.FilePath;
        if (cur is null) return;
        foreach (var n in AllNodes().Where(n => n.Kind == SideKind.Board && n.IsOpen && string.Equals(n.Path, cur, StringComparison.OrdinalIgnoreCase)).ToList())
            LoadChildren(n);
    }

    /// <summary>Marca o quadro aberto (pílula de destaque).</summary>
    public void MarkCurrent()
    {
        var cur = App.ViewModel?.FilePath;
        foreach (var n in AllNodes().Where(n => n.Kind == SideKind.Board))
            n.IsCurrent = string.Equals(n.Path, cur, StringComparison.OrdinalIgnoreCase);
    }

    IEnumerable<SideNode> AllNodes()
    {
        static IEnumerable<SideNode> Walk(IEnumerable<SideNode> nodes)
        {
            foreach (var n in nodes)
            {
                yield return n;
                foreach (var c in Walk(n.Children)) yield return c;
            }
        }
        return Walk(Boards.Concat(Pinned).Concat(Important).Concat(Pages));
    }

    public void BuildGallery()
    {
        Gallery.Clear();
        IEnumerable<GalleryCard> cards;
        if (GalleryGroup == "apagados")
        {
            cards = StorageService.TrashEntries().Select(e => new GalleryCard
            {
                Path = e.TrashPath, Name = e.Name, Modified = e.DeletedAt, Trash = e,
            });
        }
        else
        {
            var files = GalleryGroup switch
            {
                "fixados" => StorageService.State.Fixados.Select(StorageService.Full).Where(f => File.Exists(f) && !IsPagePath(f)),
                "importantes" => StorageService.State.Favoritos.Select(StorageService.Full).Where(f => File.Exists(f) && !IsPagePath(f)),
                _ => StorageService.BoardFiles(),
            };
            cards = files.Select(f => new GalleryCard
            {
                Path = f,
                Name = NameOf(f),
                Modified = File.GetLastWriteTime(f),
                Thumb = File.Exists(StorageService.ThumbPath(f)) ? StorageService.ThumbPath(f) : null,
                Model = Palette.ParseModel(Meta(f).Board.Modelo),
                IsFavorite = StorageService.IsFavorite(f),
            });
        }
        cards = GallerySort == "nome"
            ? cards.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            : cards.OrderByDescending(c => c.Modified);
        foreach (var c in cards) Gallery.Add(c);
    }

    /// <summary>Procurar: nomes e textos de todos os quadros e páginas.</summary>
    public List<SearchResult> SearchEverywhere(string query)
    {
        SearchResults.Clear();
        var q = BoardItemViewModel.Normalize(query.Trim());
        if (q.Length >= 2)
        {
            foreach (var f in StorageService.BoardFiles())
            {
                var name = NameOf(f);
                var text = Meta(f).Text;
                if (BoardItemViewModel.Normalize(name).Contains(q) || BoardItemViewModel.Normalize(text).Contains(q))
                    SearchResults.Add(new SearchResult(f, name, Snippet(text, q), false));
            }
            foreach (var f in StorageService.PageFiles())
            {
                var name = NameOf(f);
                var text = StorageService.ReadPage(f);
                if (BoardItemViewModel.Normalize(name).Contains(q) || BoardItemViewModel.Normalize(text).Contains(q))
                    SearchResults.Add(new SearchResult(f, name, Snippet(text, q), true));
            }
        }
        return SearchResults.ToList();
    }

    static string Snippet(string text, string q)
    {
        var hit = text.Split('\n').FirstOrDefault(l => BoardItemViewModel.Normalize(l).Contains(q)) ?? "";
        hit = hit.Trim();
        return hit.Length > 70 ? hit[..70] + "…" : hit;
    }

    // ---------- ações ----------

    public void NewBoard(string? folder = null)
    {
        var rel = folder is null ? "" : System.IO.Path.GetRelativePath(StorageService.BoardsDir, folder);
        if (rel == ".") rel = "";
        var path = StorageService.CreateBoard(rel, "Novo quadro");
        App.OpenBoard(path);
        StartRename(path);
    }

    public void NewFolder(string parent)
    {
        var rel = System.IO.Path.GetRelativePath(StorageService.BoardsDir, parent);
        if (rel == "." || rel.StartsWith("..", StringComparison.Ordinal)) rel = "";
        StorageService.CreateFolder(rel, "Nova pasta");
    }

    public void NewPage()
    {
        var path = StorageService.CreatePage("Nova nota", "# Nova nota\n\n");
        PagesOpen = true;
        Refresh();
        App.OpenPage(path);
    }

    /// <summary>Liga o modo de renomear na linha (dois cliques ou F2).</summary>
    public void StartRename(string path)
    {
        bool first = true;
        foreach (var n in AllNodes().Where(n => n.HasActions))
        {
            bool on = first && string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase);
            n.IsRenaming = on;
            if (on) first = false;
        }
    }

    public void CommitRename(SideNode node, string newName)
    {
        node.IsRenaming = false;
        newName = newName.Trim();
        if (newName.Length == 0 || newName == node.Name) return;
        if (node.Kind == SideKind.Board)
        {
            var oldRel = StorageService.Rel(node.Path);
            string target;
            if (App.ViewModel is { } cur && string.Equals(cur.FilePath, node.Path, StringComparison.OrdinalIgnoreCase))
            {
                cur.Rename(newName);
                target = cur.FilePath;
            }
            else target = StorageService.RenameBoard(node.Path, newName);
            RenameMarks(oldRel, StorageService.Rel(target));
        }
        else if (node.Kind == SideKind.Page)
        {
            var oldRel = StorageService.Rel(node.Path);
            App.RenamePage(node.Path, newName);
            var page = StorageService.FindPage(newName);
            if (page is not null) RenameMarks(oldRel, StorageService.Rel(page));
        }
        StorageService.NotifyLibrary();
    }

    // Fixados, importantes e recentes acompanham o novo nome.
    static void RenameMarks(string oldRel, string newRel)
    {
        var s = StorageService.State;
        foreach (var list in new[] { s.Fixados, s.Favoritos, s.Recentes })
        {
            int i = list.FindIndex(r => string.Equals(r, oldRel, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) list[i] = newRel;
        }
        StorageService.SaveState();
    }

    public void Duplicate(string path) => StorageService.DuplicateBoard(path);
    public void ToggleFavorite(string path) => StorageService.ToggleFavorite(path);
    public void TogglePinned(string path) => StorageService.TogglePinned(path);

    public void Move(string boardPath, string folderPath)
    {
        var rel = System.IO.Path.GetRelativePath(StorageService.BoardsDir, folderPath);
        if (rel == ".") rel = "";
        if (rel.StartsWith("..", StringComparison.Ordinal)) return; // só dentro de Quadros\
        bool isCurrent = App.ViewModel is { } cur && string.Equals(cur.FilePath, boardPath, StringComparison.OrdinalIgnoreCase);
        if (isCurrent) App.ViewModel!.Flush();
        var target = StorageService.MoveBoard(boardPath, rel);
        RenameMarks(StorageService.Rel(boardPath), StorageService.Rel(target));
        if (isCurrent) App.OpenBoard(target, makeThumb: false);
    }

    public void Trash(string path)
    {
        bool isCurrent = App.ViewModel is { } cur && string.Equals(cur.FilePath, path, StringComparison.OrdinalIgnoreCase);
        if (isCurrent) App.ViewModel!.Close();
        // As notas ficam só no quadro: vão para a lixeira junto com ele (restaurar o quadro as recria).
        foreach (var it in StorageService.LoadBoard(path).Itens)
            if (it is { Tipo: "pagina", Arquivo: { } rel } && File.Exists(StorageService.Full(rel)))
                try { StorageService.TrashPage(StorageService.Full(rel)); } catch (IOException) { }
        StorageService.TrashBoard(path);
        if (isCurrent)
        {
            var next = StorageService.State.Recentes.Select(StorageService.Full).FirstOrDefault(File.Exists)
                       ?? StorageService.BoardFiles().FirstOrDefault()
                       ?? StorageService.CreateBoard("", "Novo quadro");
            App.OpenBoard(next, makeThumb: false);
        }
    }

    public void Restore(TrashEntry e) => StorageService.RestoreBoard(e);
    public void DeleteForever(TrashEntry e) => StorageService.DeleteForever(e);
}
