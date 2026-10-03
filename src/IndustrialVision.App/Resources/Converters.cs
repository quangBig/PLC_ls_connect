using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using IndustrialVision.Core.Enums;

namespace IndustrialVision.App.Resources;

/// <summary>
/// Converts ConnectionStatus enum to a display color for status indicators.
/// </summary>
public class ConnectionStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Disconnected => new SolidColorBrush(Color.FromRgb(120, 120, 120)),   // Gray
                ConnectionStatus.Connecting => new SolidColorBrush(Color.FromRgb(255, 193, 7)),       // Amber
                ConnectionStatus.Connected => new SolidColorBrush(Color.FromRgb(33, 150, 243)),       // Blue
                ConnectionStatus.Ready => new SolidColorBrush(Color.FromRgb(76, 175, 80)),            // Green
                ConnectionStatus.Error => new SolidColorBrush(Color.FromRgb(244, 67, 54)),            // Red
                _ => new SolidColorBrush(Color.FromRgb(120, 120, 120))
            };
        }
        return new SolidColorBrush(Color.FromRgb(120, 120, 120));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts ConnectionStatus enum to display text.
/// </summary>
public class ConnectionStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionStatus status)
        {
            return status switch
            {
                ConnectionStatus.Disconnected => "DISCONNECTED",
                ConnectionStatus.Connecting => "CONNECTING...",
                ConnectionStatus.Connected => "CONNECTED",
                ConnectionStatus.Ready => "READY",
                ConnectionStatus.Error => "ERROR",
                _ => "UNKNOWN"
            };
        }
        return "UNKNOWN";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts InspectionResult to display color.
/// </summary>
public class InspectionResultToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is InspectionResult result)
        {
            return result switch
            {
                InspectionResult.OK => new SolidColorBrush(Color.FromRgb(76, 175, 80)),   // Green
                InspectionResult.NG => new SolidColorBrush(Color.FromRgb(244, 67, 54)),   // Red
                _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))                     // Gray
            };
        }
        return new SolidColorBrush(Color.FromRgb(158, 158, 158));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts MachineState to display text.
/// </summary>
public class MachineStateToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is MachineState state)
        {
            return state switch
            {
                MachineState.Starting => "STARTING",
                MachineState.Initializing => "INITIALIZING",
                MachineState.Connecting => "CONNECTING",
                MachineState.Ready => "READY",
                MachineState.WaitingTrigger => "WAITING TRIGGER",
                MachineState.Lighting => "LIGHTING",
                MachineState.Capturing => "CAPTURING",
                MachineState.Processing => "PROCESSING OCR",
                MachineState.SendingResult => "SENDING RESULT",
                MachineState.Completed => "COMPLETED",
                MachineState.Error => "ERROR",
                MachineState.Stopped => "STOPPED",
                _ => "UNKNOWN"
            };
        }
        return "UNKNOWN";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Boolean to Visibility converter.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Boolean to Brush converter for active states (e.g. Light ON/OFF).
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green ON
        return new SolidColorBrush(Color.FromRgb(49, 50, 68));       // Gray/Dark OFF
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Boolean to ON / OFF text converter.
/// </summary>
public class BoolToOnOffTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return "ON";
        return "OFF";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Nullable boolean to Brush converter (for Ping Status: true=Green, false=Red, null=Gray).
/// </summary>
public class NullableBoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // Green
                : new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red
        }
        return new SolidColorBrush(Color.FromRgb(69, 71, 90));      // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Boolean to Red Brush converter (true=Red, false=Dark Gray).
/// </summary>
public class BoolToRedColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
        return new SolidColorBrush(Color.FromRgb(49, 50, 68));      // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Boolean to Amber/Orange Brush converter (true=Orange, false=Dark Gray).
/// </summary>
public class BoolToOrangeColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(255, 167, 38)); // Amber/Orange
        return new SolidColorBrush(Color.FromRgb(49, 50, 68));       // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Boolean to Blue Brush converter (true=Blue, false=Dark Gray).
/// </summary>
public class BoolToBlueColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(66, 165, 245)); // Blue
        return new SolidColorBrush(Color.FromRgb(49, 50, 68));       // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Boolean to Cyan Brush converter (true=Cyan, false=Dark Gray).
/// </summary>
public class BoolToCyanColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush(Color.FromRgb(38, 198, 218)); // Cyan
        return new SolidColorBrush(Color.FromRgb(49, 50, 68));       // Gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


