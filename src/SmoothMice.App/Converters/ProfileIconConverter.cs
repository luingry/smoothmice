using System.Globalization;
using System.Windows.Data;

namespace SmoothMice.App.Converters;

/// <summary>Profile display name → the icon of the executable it applies to (see <see cref="Resolve"/>).</summary>
public sealed class ProfileIconConverter : IValueConverter
{
    /// <summary>Maps a profile display name to an executable path; set by the owning window.</summary>
    public Func<string, string?>? Resolve { get; set; }

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string name && Resolve is not null
            ? ExecutableIconConverter.GetIcon(Resolve(name))
            : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
