using System.ComponentModel;
using System.Windows.Controls;
using Quadtab.Models;
using Quadtab.Rendering;
using Quadtab.ViewModels;

namespace Quadtab.Controls;

/// <summary>Texto solto: só letras grandes, com relevo. Na lousa vira "giz" (cor do papel).</summary>
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
        Loaded += (_, _) => { BoardSurfaces.Changed += Apply; Apply(); };
        Unloaded += (_, _) => BoardSurfaces.Changed -= Apply;
    }

    void OnVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoardItemViewModel.ColorKey)) Apply();
    }

    void Apply()
    {
        if (_vm is null) return;
        var model = BoardSurfaces.Model;
        var brush = model == BoardModel.Lousa ? _vm.PaperBrush : _vm.InkBrush;
        Label.Foreground = brush;
        Editor.Foreground = model == BoardModel.Lousa ? _vm.InkBrush : brush;
        Label.TextShadows = model switch
        {
            BoardModel.Lousa => "0 0 2 0 #66FFFFFF",
            BoardModel.Branco or BoardModel.Pontilhado => "0 1 1 0 #1F000000",
            _ => "0 1 0 0 #59FFFFFF, 0 3 4 0 #4D281400",
        };
    }
}
