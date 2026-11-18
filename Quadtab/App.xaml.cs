using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Quadtab.Models;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab;

/// <summary>
/// Aqui usamos a classe App como ponto de entrada: inicia o cofre e o tema, abre a janela e guarda o quadro
/// aberto (ViewModel) e a biblioteca (Library). Também troca de quadro, cuida da prévia de designs e registra erros.
/// </summary>
public partial class App : Application
{
    /// <summary>Quadro aberto agora.</summary>
    public static MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>Todos os quadros e páginas do cofre.</summary>
    public static LibraryViewModel Library { get; private set; } = null!;

    static MainWindow? Window => Current?.MainWindow as MainWindow;

    // ---------- início ----------

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        StorageService.WriteFailed += OnWriteFailed;

        // Desenho pela placa de vídeo (nunca forçar o modo por software).
        RenderOptions.ProcessRenderMode = RenderMode.Default;

        // O WPF limita as animações a ~60 fps por padrão; em telas de 120 Hz ou mais, vai até 120.
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 120 });

        StorageService.Initialize();
        ThemeService.Initialize();

        var start = StorageService.State.QuadroAberto is { } rel && File.Exists(StorageService.Full(rel))
            ? StorageService.Full(rel)
            : StorageService.BoardFiles().FirstOrDefault() ?? StorageService.EmptyBoardPath; // nenhum quadro: tela vazia
        ViewModel = new MainViewModel(StorageService.LoadBoard(start), start);
        Library = new LibraryViewModel();
        if (!StorageService.IsEmptyBoard(start))
        {
            StorageService.Touch(start);
            Library.Visit(start);
        }

        MainWindow = new MainWindow(ViewModel);
        MainWindow.Show();
    }

    // ---------- quadros ----------

    /// <summary>Abre outro quadro (salva o atual antes).</summary>
    public static void OpenBoard(string path, bool makeThumb = true)
    {
        if (ViewModel is not null && string.Equals(ViewModel.FilePath, path, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            Library.GalleryOpen = false;
            Library.Visit(path);
            return;
        }
        if (makeThumb) MakeThumbnail();
        ViewModel?.Close();
        ViewModel = new MainViewModel(StorageService.LoadBoard(path), path);
        bool empty = StorageService.IsEmptyBoard(path);
        if (!empty) StorageService.Touch(path);
        Window?.SetBoard(ViewModel);
        Library.GalleryOpen = false;
        Library.Refresh();
        if (!empty) Library.Visit(path);
        MemoryService.Request();
    }

    // ---------- prévia de design ----------

    static string? _previewReturn;
    static Design? _previewDesign;

    /// <summary>"Visualizar design": mostra o design no quadro, só para ver, sem criar arquivo.</summary>
    public static void PreviewDesign(Design d)
    {
        if (ViewModel is { IsPreview: false } current)
        {
            MakeThumbnail();
            _previewReturn = current.FilePath;
        }
        ViewModel?.Close();
        _previewDesign = d;
        ViewModel = MainViewModel.Preview(d.Build(), d.Name);
        Window?.SetBoard(ViewModel);
        Library.GalleryOpen = false;
        Library.Refresh();
    }

    /// <summary>"Adicionar quadro" na prévia: cria o quadro com o design (e a câmera de agora) e abre.</summary>
    public static void AddPreviewAsBoard()
    {
        if (_previewDesign is not { } d || ViewModel is not { IsPreview: true } vm) return;
        try
        {
            var board = d.Build();
            board.Camera = vm.Camera ?? board.Camera;
            var path = StorageService.CreateBoard("", d.Name, board);
            _previewDesign = null; _previewReturn = null;
            OpenBoard(path, makeThumb: false);
        }
        catch (Exception ex) { Log(ex); }
    }

    /// <summary>Sai da prévia e volta ao quadro que estava aberto antes.</summary>
    public static void ExitPreview()
    {
        if (ViewModel is not { IsPreview: true }) return;
        var back = _previewReturn is { } p && File.Exists(p) ? p : StorageService.EmptyBoardPath;
        _previewDesign = null; _previewReturn = null;
        OpenBoard(back, makeThumb: false);
    }

    // ---------- notas e itens ----------

    /// <summary>Abre uma página no editor ao lado do quadro.</summary>
    public static void OpenPage(string path)
    {
        Window?.OpenPage(path);
        Library.Visit(path);
    }

    /// <summary>Abre o quadro (se preciso), seleciona o item e leva a câmera até ele.</summary>
    public static void FocusItem(string boardPath, int id)
    {
        OpenBoard(boardPath);
        if (ViewModel.Items.FirstOrDefault(i => i.Id == id) is not { } item) return;
        ViewModel.EndEdit();
        ViewModel.SelectWithGroup(item);
        Window?.Board.FocusItem(item);
    }

    /// <summary>Renomeia uma página: o arquivo, os itens deste quadro e os links [[ ]] das outras páginas.</summary>
    public static void RenamePage(string path, string newName)
    {
        var oldName = Path.GetFileNameWithoutExtension(path);
        var target = StorageService.RenamePage(path, newName);
        var fileName = Path.GetFileNameWithoutExtension(target);
        var title = newName.Trim();
        var current = StorageService.ReadPage(target);
        var titled = Rendering.Markdown.WithTitle(current, title);
        if (titled != current.Replace("\r", "")) StorageService.WritePage(target, titled);
        foreach (var f in StorageService.PageFiles())
        {
            var text = StorageService.ReadPage(f);
            var replaced = Rendering.Markdown.WikiLink().Replace(text, m =>
                string.Equals(m.Groups[1].Value.Trim(), oldName, StringComparison.OrdinalIgnoreCase) ? m.Value.Replace(m.Groups[1].Value, fileName) : m.Value);
            if (replaced != text) StorageService.WritePage(f, replaced);
        }
        foreach (var it in ViewModel.Items.Where(i => i.FilePath is { } fp && string.Equals(StorageService.Full(fp), path, StringComparison.OrdinalIgnoreCase)))
        {
            it.FilePath = StorageService.Rel(target);
            it.Title = title;
        }
        // Outros quadros que usam a nota passam a apontar para o nome novo (senão, ao abrir, recriariam a nota antiga).
        StorageService.UpdateBoardsUsing(path, ViewModel.FilePath, (_, hits) =>
        {
            foreach (var h in hits)
            {
                h.Arquivo = StorageService.Rel(target);
                h.Titulo = title;
            }
            return true;
        });
        Window?.PageRenamed(path, target);
    }

    /// <summary>Excluir a nota pelo Cofre: sai do quadro aberto e o arquivo some de vez.</summary>
    public static void DeleteNote(string path)
    {
        foreach (var it in ViewModel.Items.Where(i => i.FileFullPath is { } f && string.Equals(f, path, StringComparison.OrdinalIgnoreCase)).ToList())
            ViewModel.Remove(it);
        // Os outros quadros que usam a nota também a perdem (com as setas ligadas a ela): nenhum fica com link quebrado.
        StorageService.UpdateBoardsUsing(path, ViewModel.FilePath, (b, hits) =>
        {
            var ids = hits.Select(h => h.Id).ToHashSet();
            bool Linked(LineEnd? end) => end?.Item is { } id && ids.Contains(id);
            b.Itens.RemoveAll(i => ids.Contains(i.Id) || i.Linha is { } l && (Linked(l.De) || Linked(l.Para)));
            foreach (var i in b.Itens.Where(i => i.AnexadoA is { } p && ids.Contains(p)))
            {
                i.AnexadoA = null;
                i.AnexoLado = null;
            }
            return true;
        });
        if (!File.Exists(path)) return;
        StorageService.DeletePage(path);
        PageDeleted(path);
    }

    public static void PageDeleted(string path) => Window?.PageDeleted(path);

    /// <summary>Miniatura do quadro atual para a galeria.</summary>
    public static void MakeThumbnail() => Window?.MakeThumbnail();

    // ---------- saída e erros ----------

    protected override void OnExit(ExitEventArgs e)
    {
        // Cada passo isolado: um erro ao salvar não impede o resto nem o fechamento.
        foreach (var step in new Action[] { MakeThumbnail, () => ViewModel?.Close(), () => Window?.FlushPage(), StorageService.SaveState })
        {
            try { step(); }
            catch (Exception ex) { Log(ex); }
        }
        base.OnExit(e);
    }

    // Um erro inesperado não fecha o app: fica registrado em Cofre\.quadtab\erros.log.
    static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
    }

    // Uma gravação falhou (pode vir de outra thread): registra e o status do quadro mostra o erro.
    static void OnWriteFailed(string path, Exception ex)
    {
        Log(new IOException($"Não foi possível gravar \"{path}\".", ex));
        Current?.Dispatcher.BeginInvoke(() => { if (ViewModel is { } vm) vm.SaveError = true; });
    }

    static readonly object LogLock = new();
    static string? _lastLog;
    static DateTime _lastLogAt;

    /// <summary>
    /// Registra um erro em erros.log (nunca lança). A mesma falha repetida entra uma vez por minuto, e o arquivo
    /// passa para erros-antigo.log ao chegar a 1 MB (não cresce sem fim).
    /// </summary>
    public static void Log(Exception ex)
    {
        try
        {
            var text = ex.ToString();
            lock (LogLock)
            {
                if (text == _lastLog && DateTime.Now - _lastLogAt < TimeSpan.FromMinutes(1)) return;
                _lastLog = text;
                _lastLogAt = DateTime.Now;
                Directory.CreateDirectory(StorageService.LogDir);
                var file = Path.Combine(StorageService.LogDir, "erros.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1_000_000)
                    File.Move(file, Path.Combine(StorageService.LogDir, "erros-antigo.log"), overwrite: true);
                File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n\n");
            }
        }
        catch { }
    }
}
