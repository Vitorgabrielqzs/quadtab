using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Quadtab.Services;

/// <summary>
/// Tema claro/escuro. Na primeira abertura segue o Windows; depois vale a última escolha do botão da
/// lateral (salva em estado.json). As cores da interface mudam suavemente (0,3 s).
/// </summary>
public static class ThemeService
{
    static ResourceDictionary? _current;

    // Pincéis da interface: sempre os mesmos objetos, só a cor muda (assim dá para animar a troca).
    static readonly Dictionary<object, SolidColorBrush> Brushes = new();

    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    public static void Initialize()
    {
        Apply(StorageService.State.Tema switch { "escuro" => true, "claro" => false, _ => ReadSystemIsDark() }, animate: false);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (StorageService.State.Tema is not null) return; // o usuário escolheu no botão
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    var dark = ReadSystemIsDark();
                    if (dark != IsDark) Apply(dark, animate: true);
                });
        };
    }

    /// <summary>Botão de tema: alterna e guarda a escolha.</summary>
    public static void Toggle()
    {
        StorageService.State.Tema = IsDark ? "claro" : "escuro";
        StorageService.SaveState();
        Apply(!IsDark, animate: true);
    }

    /// <summary>Ajustes → volta a seguir o Windows.</summary>
    public static void FollowSystem()
    {
        StorageService.State.Tema = null;
        StorageService.SaveState();
        var dark = ReadSystemIsDark();
        if (dark != IsDark) Apply(dark, animate: true);
    }

    static bool ReadSystemIsDark()
    {
        // Para testes: QUADTAB_TEMA=claro ou escuro força um tema.
        var forced = Environment.GetEnvironmentVariable("QUADTAB_TEMA");
        if (forced == "claro") return false;
        if (forced == "escuro") return true;

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    static void Apply(bool dark, bool animate)
    {
        IsDark = dark;
        var dict = new ResourceDictionary
        {
            Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative),
        };
        var app = Application.Current.Resources;
        var merged = app.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;

        // Os pincéis ficam nos recursos do app (vencem o dicionário do tema). Na troca, cada um vira um
        // pincel novo que anima do tom antigo para o novo (o WPF congela os pincéis já usados).
        if (animate && !SystemParameters.ClientAreaAnimation) animate = false;
        foreach (var key in dict.Keys)
        {
            if (dict[key] is not SolidColorBrush b) continue;
            var from = Brushes.TryGetValue(key, out var old) ? old.Color : b.Color;
            var live = new SolidColorBrush(from);
            if (animate && from != b.Color)
                live.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(b.Color, TimeSpan.FromSeconds(0.3)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            else live.Color = b.Color;
            Brushes[key] = live;
            app[key] = live;
        }

        foreach (Window w in Application.Current.Windows) ApplyToWindow(w);
        Changed?.Invoke();
    }

    /// <summary>Deixa a borda fina da janela (desenhada pelo Windows 11) no tom certo e com cantos arredondados.</summary>
    public static void ApplyToWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));      // DWMWA_USE_IMMERSIVE_DARK_MODE
        int round = 2;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));     // DWMWA_WINDOW_CORNER_PREFERENCE = arredondado
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
