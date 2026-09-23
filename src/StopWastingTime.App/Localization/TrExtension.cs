using System.Windows.Markup;

namespace StopWastingTime.App.Localization;

/// <summary>
/// <c>{loc:Tr Focus_Title}</c> in XAML: the text for that key, which follows the language picker
/// without the view having to be rebuilt.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Localizer.Bind(Key).ProvideValue(serviceProvider);
}
