using System.IO;
using System.Windows;
using System.Windows.Threading;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab;

public partial class App : Application
{
    /// <summary>Quadro aberto agora.</summary>
    public static MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>Todos os quadros e páginas do cofre.</summary>
    public static LibraryViewModel Library { get; private set; } = null!;

    static MainWindow? Window => Current?.MainWindow as MainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // Desenho pela placa de vídeo (nunca forçar o modo por software).
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;

        // O WPF limita as animações a ~60 fps por padrão; em telas de 120 Hz ou mais, vai até 120.
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata { DefaultValue = 120 });

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

        var window = new MainWindow(ViewModel);
        MainWindow = window;

        // Modo de conferência (desenvolvimento): QUADTAB_PRINT=arquivo.png salva uma imagem do app e fecha.
        if (Environment.GetEnvironmentVariable("QUADTAB_PRINT") is { Length: > 0 } print)
        {
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -30000;
            window.Top = 0;
            DevSnapshot.Run(window, print, Environment.GetEnvironmentVariable("QUADTAB_PRINT_ACAO") ?? "");
        }
        window.Show();
    }

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
    }

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
        var newTitle = Path.GetFileNameWithoutExtension(target);
        foreach (var f in StorageService.PageFiles())
        {
            var text = StorageService.ReadPage(f);
            var replaced = Rendering.Markdown.WikiLink().Replace(text, m =>
                string.Equals(m.Groups[1].Value.Trim(), oldName, StringComparison.OrdinalIgnoreCase) ? m.Value.Replace(m.Groups[1].Value, newTitle) : m.Value);
            if (replaced != text) StorageService.WritePage(f, replaced);
        }
        foreach (var it in ViewModel.Items.Where(i => i.FilePath is { } fp && string.Equals(StorageService.Full(fp), path, StringComparison.OrdinalIgnoreCase)))
        {
            it.FilePath = StorageService.Rel(target);
            it.Title = newTitle;
        }
        Window?.PageRenamed(path, target);
    }

    /// <summary>Miniatura do quadro atual para a galeria.</summary>
    public static void MakeThumbnail() => Window?.MakeThumbnail();

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

    /// <summary>Registra um erro em erros.log (nunca lança).</summary>
    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(StorageService.LogDir);
            File.AppendAllText(Path.Combine(StorageService.LogDir, "erros.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
