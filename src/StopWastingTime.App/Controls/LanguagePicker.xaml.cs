using System.Windows;
using System.Windows.Controls;

namespace StopWastingTime.App.Controls;

/// <summary>
/// Switches the interface language on the spot. It talks to the one <c>Localizer</c> directly, so it can
/// sit anywhere, whatever the data context around it happens to be.
/// </summary>
public partial class LanguagePicker : UserControl
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact),
        typeof(bool),
        typeof(LanguagePicker),
        new PropertyMetadata(false));

    public LanguagePicker() => InitializeComponent();

    /// <summary>Shows "ES" and "EN" instead of the full names, for tight spots such as the launcher.</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }
}
