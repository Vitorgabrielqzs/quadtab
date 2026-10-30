using System.Windows;

namespace Quadtab.Rendering;

/// <summary>
/// Câmera do quadro infinito: ponto do mundo no centro da tela (Cx, Cy) + zoom.
/// Tela = (mundo − centro) × zoom + metade da tela.
/// Os movimentos (zoom suave, rodinha, inércia, viagens animadas) avançam em Step(), uma vez por quadro.
/// </summary>
public sealed class BoardCamera
{
    public const double MinZoom = 0.1, MaxZoom = 4;

    public double Cx, Cy;
    public double Zoom = 1;
    public Size Viewport;

    // zoom suave, preso ao ponto do cursor
    double _targetZoom = 1;
    Point _anchor;
    bool _zooming;

    // rodinha: deslocamento ainda a aplicar (pixels de tela)
    Vector _panLeft;

    // inércia ao soltar (pixels de tela por segundo)
    Vector _velocity;

    // viagem animada (ajustar, ir até um item)
    double _fromX, _fromY, _fromZ, _toX, _toY, _toZ, _t, _duration;
    bool _traveling;

    public bool IsMoving => _zooming || _traveling || _panLeft.LengthSquared > 0.25 || _velocity.LengthSquared > 100;

    public static double Clamp(double z) => Math.Clamp(z, MinZoom, MaxZoom);

    public Point ToWorld(Point screen) =>
        new(Cx + (screen.X - Viewport.Width / 2) / Zoom, Cy + (screen.Y - Viewport.Height / 2) / Zoom);

    public Point ToScreen(Point world) =>
        new((world.X - Cx) * Zoom + Viewport.Width / 2, (world.Y - Cy) * Zoom + Viewport.Height / 2);

    /// <summary>Área do mundo visível na tela.</summary>
    public Rect VisibleWorld =>
        new(Cx - Viewport.Width / 2 / Zoom, Cy - Viewport.Height / 2 / Zoom, Viewport.Width / Zoom, Viewport.Height / Zoom);

    /// <summary>Zoom suave multiplicando o atual, mantendo parado o ponto sob "anchor".</summary>
    public void ZoomBy(double factor, Point anchor)
    {
        _traveling = false;
        _targetZoom = Clamp((_zooming ? _targetZoom : Zoom) * factor);
        _anchor = anchor;
        _zooming = true;
    }

    /// <summary>Move o conteúdo na tela (suave, para rodinha e touchpad).</summary>
    public void PanSmooth(Vector screenDelta)
    {
        _traveling = false;
        _velocity = default;
        _panLeft += screenDelta;
    }

    /// <summary>Move o conteúdo na hora (arrastar).</summary>
    public void PanNow(Vector screenDelta)
    {
        Cx -= screenDelta.X / Zoom;
        Cy -= screenDelta.Y / Zoom;
    }

    public void Fling(Vector screenVelocity) => _velocity = screenVelocity;

    public void Stop()
    {
        _zooming = _traveling = false;
        _panLeft = _velocity = default;
    }

    /// <summary>Viagem animada até um centro e zoom.</summary>
    public void TravelTo(double cx, double cy, double zoom, double seconds = 0.55)
    {
        Stop();
        _fromX = Cx; _fromY = Cy; _fromZ = Zoom;
        _toX = cx; _toY = cy; _toZ = Clamp(zoom);
        _t = 0;
        _duration = seconds;
        _traveling = seconds > 0;
        if (!_traveling) { Cx = cx; Cy = cy; Zoom = _toZ; }
    }

    /// <summary>Avança os movimentos. Retorna true se a câmera mudou.</summary>
    public bool Step(double dt)
    {
        bool changed = false;

        if (_traveling)
        {
            _t += dt / _duration;
            double k = Anim.Ease.Ease(Math.Min(1, _t));
            Zoom = Math.Exp(Math.Log(_fromZ) + (Math.Log(_toZ) - Math.Log(_fromZ)) * k); // zoom em escala logarítmica
            Cx = _fromX + (_toX - _fromX) * k;
            Cy = _fromY + (_toY - _fromY) * k;
            if (_t >= 1) _traveling = false;
            return true;
        }

        if (_zooming)
        {
            double k = 1 - Math.Exp(-dt / 0.07);
            var world = ToWorld(_anchor);
            double lz = Math.Log(Zoom), lt = Math.Log(_targetZoom);
            Zoom = Math.Abs(lt - lz) < 0.002 ? _targetZoom : Math.Exp(lz + (lt - lz) * k);
            if (Zoom == _targetZoom) _zooming = false;
            Cx = world.X - (_anchor.X - Viewport.Width / 2) / Zoom;
            Cy = world.Y - (_anchor.Y - Viewport.Height / 2) / Zoom;
            changed = true;
        }

        if (_panLeft.LengthSquared > 0.25)
        {
            double k = 1 - Math.Exp(-dt / 0.06);
            var d = _panLeft * k;
            if (_panLeft.LengthSquared < 1) d = _panLeft;
            _panLeft -= d;
            PanNow(d);
            changed = true;
        }
        else _panLeft = default;

        if (_velocity.LengthSquared > 100)
        {
            PanNow(_velocity * dt);
            _velocity *= Math.Exp(-dt / 0.3); // atrito
            changed = true;
        }
        else _velocity = default;

        return changed;
    }
}
