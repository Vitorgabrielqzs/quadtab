using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Quadtab.Services;

/// <summary>
/// Aqui usamos a classe MemoryService para devolver ao Windows a memória que o app não está usando (quase tudo é do driver de vídeo e do WPF, que
/// só precisam dela ao abrir). Acontece só quando o app não está sendo usado: minimizado ou em segundo
/// plano há 15 s. Antes também limpava com a janela aberta (3 s depois de trocar de quadro ou 60 s
/// parado), mas aí o próximo movimento precisava trazer tudo de volta do disco e o app congelava.
/// </summary>
public static class MemoryService
{
    static readonly TimeSpan BackgroundDelay = TimeSpan.FromSeconds(15);

    static readonly DispatcherTimer Timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
    static Window? _window;
    static bool _clean;
    static uint _lastInput;
    static DateTime _deactivatedAt = DateTime.MaxValue;

    // ---------- funções do Windows (última entrada do usuário e esvaziar a memória do processo) ----------
    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize, dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

    /// <summary>Escuta a janela (ativar, desativar, minimizar) e liga o relógio que confere a cada 5 s.</summary>
    public static void Attach(Window window)
    {
        _window = window;
        window.Activated += (_, _) => { _deactivatedAt = DateTime.MaxValue; _clean = false; };
        window.Deactivated += (_, _) => _deactivatedAt = DateTime.Now;
        window.StateChanged += (_, _) => _clean = false;
        Timer.Tick += (_, _) => OnTick();
        Timer.Start();
    }

    /// <summary>Trocou de quadro: a próxima vez que o app ficar fora de uso, limpa de novo.</summary>
    public static void Request() => _clean = false;

    // A cada 5 s: houve entrada do usuário? O app está fora de uso? Então limpa.
    static void OnTick()
    {
        if (_window is null) return;
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return;
        if (_window.IsActive && info.dwTime != _lastInput) _clean = false;
        _lastInput = info.dwTime;
        if (_clean) return;

        // Só fora de uso: com a janela ativa, mesmo parada, o usuário pode voltar a mexer a qualquer momento.
        bool away = _window.WindowState == WindowState.Minimized || (!_window.IsActive && DateTime.Now - _deactivatedAt >= BackgroundDelay);
        if (away) Trim();
    }

    // Coleta o lixo (compactando objetos grandes) e esvazia a memória em segundo plano.
    static void Trim()
    {
        _clean = true;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var process = GetCurrentProcess();
        Task.Run(() => EmptyWorkingSet(process));
    }
}
