using System.Globalization;
using Avalonia.Data.Converters;
using Deskrig.Core.Models;

namespace Deskrig.Desktop.Infrastructure;

/// <summary>Formats a whole DisplayProfileEntry's Width/Height as "1920x1080" for the editor's read-only
/// resolution column - simpler and more portable than an Avalonia MultiBinding for two fields.</summary>
public sealed class ResolutionConverter : IValueConverter
{
    public static readonly ResolutionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DisplayProfileEntry e ? $"{e.Width}x{e.Height}" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>"—" for null, "150%" otherwise - the Avalonia DataGrid text column equivalent of WPF's
/// TargetNullValue+StringFormat combo on a binding.</summary>
public sealed class DpiScaleConverter : IValueConverter
{
    public static readonly DpiScaleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? $"{i}%" : "—";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
