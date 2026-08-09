using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MediaOrganizer.Core.Logging;

namespace MediaOrganizer.Desktop.Converters;

public sealed class LogLevelBrushConverter : IValueConverter
{
    public static readonly LogLevelBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LogLevel level
            ? level switch
            {
                LogLevel.Error => new SolidColorBrush(Color.Parse("#C42B1C")),
                LogLevel.Warn => new SolidColorBrush(Color.Parse("#CA5010")),
                _ => new SolidColorBrush(Color.Parse("#0067C0"))
            }
            : Brushes.Gray;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
