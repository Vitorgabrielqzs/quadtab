using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Bloco de vidro colado no quadro, com título livre e texto. Através dele aparece o fundo do quadro.</summary>
public partial class GlassBlock : UserControl
{
    BoardItemViewModel? _vm;

    public GlassBlock()
    {
        InitializeComponent();
        Glass.KeepShadow = true;
        // O item pode ser reaproveitado para outro bloco (virtualização): acompanha a troca de DataContext.
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVm;
            _vm = e.NewValue as BoardItemViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVm;
            ApplyPlaceholder();
            UpdateBackdrop();
        };
        Loaded += (_, _) => { BoardSurfaces.Changed += UpdateBackdrop; UpdateBackdrop(); };
        Unloaded += (_, _) => BoardSurfaces.Changed -= UpdateBackdrop;
    }

    /// <summary>Através do vidro aparece o fundo do quadro: a cor do tema no liso, a cortiça desfocada etc.</summary>
    void UpdateBackdrop()
    {
        if (_vm is null) return;
        if (BoardSurfaces.Model == BoardModel.Liso) Glass.SetResourceReference(GlassPanel.BackdropBrushProperty, "CanvasBrush");
        else Glass.BackdropBrush = BoardSurfaces.BlurredSurfaceFor(BoardSurfaces.Model, _vm.X, _vm.Y);
    }

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardItemViewModel.Title) or nameof(BoardItemViewModel.Editing)) ApplyPlaceholder();
        else if (e.PropertyName is nameof(BoardItemViewModel.X) or nameof(BoardItemViewModel.Y) && BoardSurfaces.Model == BoardModel.Cortica) UpdateBackdrop();
    }

    /// <summary>"Título" clarinho só quando o título está vazio e ninguém está escrevendo nele.</summary>
    void ApplyPlaceholder() =>
        Placeholder.Visibility = _vm is { IsEditingTitle: false } && string.IsNullOrWhiteSpace(_vm.Title) ? Visibility.Visible : Visibility.Hidden;
}
