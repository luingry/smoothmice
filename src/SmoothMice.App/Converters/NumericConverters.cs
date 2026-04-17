using System.Globalization;
using System.Windows.Data;

namespace SmoothMice.App.Converters;

public sealed class DoubleStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d.ToString(culture) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim() ?? "";
        if (double.TryParse(s, NumberStyles.Any, culture, out var d))
            return d;
        return Binding.DoNothing;
    }
}

public sealed class IntStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int i ? i.ToString(culture) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim() ?? "";
        if (int.TryParse(s, NumberStyles.Integer, culture, out var i))
            return i;
        return Binding.DoNothing;
    }
}
