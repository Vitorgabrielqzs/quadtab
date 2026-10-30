using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Quadtab.Services;

/// <summary>
/// Devolve ao Windows a memória que o app não está usando (quase tudo é do driver de vídeo e do WPF, que
/// só precisam dela ao abrir). Acontece só com o usuário parado: depois de abrir ou trocar de quadro,
/// minimizado, em segundo plano ou sem mexer por um tempo. O que voltar a ser usado volta sozinho.
/// </summary>
public static class MemoryService
{
    const int QuietMs = 3000, IdleMs = 60_000;
    static readonly TimeSpan BackgroundDelay = TimeSpan.FromSeconds(15);

    static readonly DispatcherTimer Timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
    static Window? _window;
    static bool _pending = true, _clean;
    static uint _lastInput;
    static DateTime _deactivatedAt = DateTime.MaxValue;

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize, dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

    public static void Attach(Window window)
    {
        _window = window;
        window.Activated += (_, _) => { _deactivatedAt = DateTime.MaxValue; _clean = false; };
        window.Deactivated += (_, _) => _deactivatedAt = DateTime.Now;
        window.StateChanged += (_, _) => _clean = false;
        Timer.Tick += (_, _) => OnTick();
        Timer.Start();
    }

    /// <summary>Pede uma limpeza assim que o usuário parar (ex.: acabou de trocar de quadro).</summary>
    public static void Request()
    {
        _pending = true;
        _clean = false;
    }

    static void OnTick()
    {
        if (_window is null) return;
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return;
        if (_window.IsActive && info.dwTime != _lastInput) _clean = false;
        _lastInput = info.dwTime;
        if (_clean) return;

        long idle = unchecked((uint)Environment.TickCount - info.dwTime);
        bool away = _window.WindowState == WindowState.Minimized || DateTime.Now - _deactivatedAt >= BackgroundDelay;
        if (away || idle >= IdleMs || (_pending && idle >= QuietMs)) Trim();
    }

    static void Trim()
    {
        _pending = false;
        _clean = true;
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var process = GetCurrentProcess();
        Task.Run(() => EmptyWorkingSet(process));
    }
}
