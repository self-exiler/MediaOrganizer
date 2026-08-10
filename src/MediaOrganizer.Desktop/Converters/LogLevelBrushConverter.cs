using Avalonia.Data.Converters;
using Avalonia.Media;
using MediaOrganizer.Core.Logging;

namespace MediaOrganizer.Desktop.Converters;

public sealed class LogLevelBrushConverter : FuncValueConverter<LogLevel, IBrush>
{
    public static readonly LogLevelBrushConverter Instance = new();

    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#C42B1C"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#CA5010"));
    private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#0067C0"));

    private LogLevelBrushConverter() : base(level => level switch
    {
        LogLevel.Error => Error,
        LogLevel.Warn => Warn,
        _ => Info
    })
    {
    }
}
