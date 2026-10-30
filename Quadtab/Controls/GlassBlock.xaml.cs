using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Bloco de vidro colado no quadro, com título livre e texto. Através dele aparece o fundo do quadro.</summary>
public partial class GlassBlock : UserControl
{
    BoardItemViewModel? _vm;

    public GlassBlock()
    {
        InitializeComponent();
        Glass.SetResourceReference(GlassPanel.BackdropBrushProperty, "CanvasBrush");
        Glass.KeepShadow = true;
        // O item pode ser reaproveitado para outro bloco (virtualização): acompanha a troca de DataContext.
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVm;
            _vm = e.NewValue as BoardItemViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVm;
            ApplyPlaceholder();
        };
    }

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardItemViewModel.Title) or nameof(BoardItemViewModel.Editing)) ApplyPlaceholder();
    }

    /// <summary>"Título" clarinho só quando o título está vazio e ninguém está escrevendo nele.</summary>
    void ApplyPlaceholder() =>
        Placeholder.Visibility = _vm is { IsEditingTitle: false } && string.IsNullOrWhiteSpace(_vm.Title) ? Visibility.Visible : Visibility.Hidden;
}
