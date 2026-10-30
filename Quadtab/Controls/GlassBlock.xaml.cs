using System.ComponentModel;
using System.Windows.Controls;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Bloco de vidro colado no quadro: através dele aparece a superfície desfocada.</summary>
public partial class GlassBlock : UserControl
{
    BoardItemViewModel? _vm;

    public GlassBlock()
    {
        InitializeComponent();
        // O item pode ser reaproveitado para outro bloco (virtualização): acompanha a troca de DataContext.
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVm;
            _vm = e.NewValue as BoardItemViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVm;
            UpdateBackdrop();
        };
        Loaded += (_, _) => BoardSurfaces.Changed += UpdateBackdrop;
        Unloaded += (_, _) => BoardSurfaces.Changed -= UpdateBackdrop;
    }

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardItemViewModel.X) or nameof(BoardItemViewModel.Y)) UpdateBackdrop();
    }

    void UpdateBackdrop()
    {
        if (_vm is null) return;
        Glass.BackdropBrush = BoardSurfaces.BlurredSurfaceFor(BoardSurfaces.Model, _vm.X, _vm.Y);
    }
}
