using System.ComponentModel;
using System.Windows.Controls;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.Services;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Texto solto: só letras grandes, direto no quadro. A cor acompanha o tema para continuar legível.</summary>
public partial class TextLabel : UserControl
{
    BoardItemViewModel? _vm;

    public TextLabel()
    {
        InitializeComponent();
        // O item pode ser reaproveitado para outro texto (virtualização): acompanha a troca de DataContext.
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVm;
            _vm = e.NewValue as BoardItemViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVm;
            Apply();
        };
        Loaded += (_, _) => { ThemeService.Changed += Apply; BoardSurfaces.Changed += Apply; Apply(); };
        Unloaded += (_, _) => { ThemeService.Changed -= Apply; BoardSurfaces.Changed -= Apply; };
    }

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoardItemViewModel.ColorKey)) Apply();
    }

    /// <summary>"Branco" = cor do texto do tema; as outras: tinta forte no claro e papel claro no escuro.</summary>
    void Apply()
    {
        if (_vm is null) return;
        // Na lousa (escura) e no tema escuro do liso, a cor clara do papel; na lousa vira "giz".
        bool darkSurface = BoardSurfaces.Model == BoardModel.Lousa || (BoardSurfaces.Model == BoardModel.Liso && ThemeService.IsDark);
        if (_vm.ColorKey == "branco" && BoardSurfaces.Model == BoardModel.Liso) Label.SetResourceReference(TrackedText.ForegroundProperty, "CardTextBrush");
        else Label.Foreground = darkSurface ? _vm.PaperBrush : _vm.InkBrush;
        Editor.SetResourceReference(ForegroundProperty, "CardTextBrush");
        Label.TextShadows = null;
    }
}
