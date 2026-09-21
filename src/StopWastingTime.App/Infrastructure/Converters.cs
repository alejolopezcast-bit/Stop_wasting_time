using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StopWastingTime.App.Infrastructure;

/// <summary>
/// Picks one of two resources by a boolean: <c>ConverterParameter="WhenTrue|WhenFalse"</c>.
/// <para>
/// It exists so selected states stay in the resource dictionary instead of being repeated as triggers on
/// every button, and because a WPF trigger comparing an <c>object</c> property against the literal
/// "True" never matches an actual boolean.
/// </para>
/// </summary>
public sealed class BooleanToResourceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var keys = (parameter as string ?? string.Empty).Split('|');
        var key = value is true ? keys.ElementAtOrDefault(0) : keys.ElementAtOrDefault(1);

        if (string.IsNullOrWhiteSpace(key))
        {
            return DependencyProperty.UnsetValue;
        }

        return Application.Current?.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Looks up a resource by name, so a view model can say which icon it wants with a plain string instead
/// of holding on to a piece of WPF.
/// </summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
        {
            return DependencyProperty.UnsetValue;
        }

        return Application.Current?.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows something only while a text box is empty, which is how the placeholder hints are done: WPF has
/// no placeholder of its own.
/// </summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
