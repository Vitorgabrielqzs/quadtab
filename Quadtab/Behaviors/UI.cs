using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Quadtab.Behaviors;

/// <summary>Propriedades extras usadas pelos estilos de botão (cantos e cor do hover).</summary>
public static class UI
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(CornerRadius), typeof(UI), new FrameworkPropertyMetadata(new CornerRadius(0)));

    public static CornerRadius GetRadius(DependencyObject d) => (CornerRadius)d.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject d, CornerRadius v) => d.SetValue(RadiusProperty, v);

    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(UI), new FrameworkPropertyMetadata(null));

    public static Brush? GetHoverBackground(DependencyObject d) => (Brush?)d.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject d, Brush? v) => d.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty HoverForegroundProperty = DependencyProperty.RegisterAttached(
        "HoverForeground", typeof(Brush), typeof(UI), new FrameworkPropertyMetadata(null));

    public static Brush? GetHoverForeground(DependencyObject d) => (Brush?)d.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(DependencyObject d, Brush? v) => d.SetValue(HoverForegroundProperty, v);

    /// <summary>Qual parte do item foi clicada duas vezes ("title" ou "text").</summary>
    public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
        "Field", typeof(string), typeof(UI), new FrameworkPropertyMetadata(null));

    public static string? GetField(DependencyObject d) => (string?)d.GetValue(FieldProperty);
    public static void SetField(DependencyObject d, string? v) => d.SetValue(FieldProperty, v);
}

/// <summary>true quando o valor é igual ao parâmetro (usado em botões de opção ligados a um enum).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

/// <summary>bool → Visible/Collapsed (com Invert para o contrário).</summary>
public sealed class VisibleWhen : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Color → SolidColorBrush congelado.</summary>
public sealed class BrushOf : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Color c ? Rendering.ShadowCache.BrushOf(c) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → ângulo da setinha (aberta = 90°).</summary>
public sealed class ArrowAngle : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 90.0 : 0.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Número maior que zero → Visible.</summary>
public sealed class NonZero : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Caminho de arquivo → imagem carregada sem travar o arquivo (para miniaturas).</summary>
public sealed class ImageFromPath : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !System.IO.File.Exists(path)) return null;
        try
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            img.UriSource = new Uri(path);
            if (parameter is string w && int.TryParse(w, out var width)) img.DecodePixelWidth = width;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null → Visible (mostra algo quando não há valor).</summary>
public sealed class NullToVisible : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is null) ^ (parameter as string == "inv") ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Nome de um ícone ("IcPage") → a geometria dele.</summary>
public sealed class IconOf : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) as Geometry : null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
