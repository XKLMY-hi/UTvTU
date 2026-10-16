using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace UTvTU.DawUI.Controls;

public enum DawDensity { Standard, Compact, Dense }
public enum DawVariant { Filled, Tonal, Outlined, Text }

/// <summary>Density can be set once on a parent panel; individual controls can override it.</summary>
public sealed class DawStyle : AvaloniaObject {
    public static readonly AttachedProperty<DawDensity> DensityProperty =
        AvaloniaProperty.RegisterAttached<DawStyle, Control, DawDensity>("Density", DawDensity.Compact, inherits: true);
    public static DawDensity GetDensity(Control control) => control.GetValue(DensityProperty);
    public static void SetDensity(Control control, DawDensity value) => control.SetValue(DensityProperty, value);
    internal static void Variant(Control control, DawVariant value) {
        foreach (var name in new[] { "filled", "tonal", "outlined", "text" }) control.Classes.Remove(name);
        control.Classes.Add(value.ToString().ToLowerInvariant());
    }
}

public class DawButton : Button {
    public static readonly StyledProperty<DawDensity> DensityProperty = DawStyle.DensityProperty.AddOwner<DawButton>();
    public static readonly StyledProperty<DawVariant> VariantProperty =
        AvaloniaProperty.Register<DawButton, DawVariant>(nameof(Variant), DawVariant.Tonal);
    public DawDensity Density { get => GetValue(DensityProperty); set => SetValue(DensityProperty, value); }
    public DawVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public DawButton() {
        this[!ThemeProperty] = new DynamicResourceExtension("DawLibraryButtonTheme");
        DawStyle.Variant(this, Variant);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == VariantProperty) DawStyle.Variant(this, Variant);
    }
}

public sealed class DawIconButton : DawButton {
    public static readonly StyledProperty<Geometry?> IconDataProperty = AvaloniaProperty.Register<DawIconButton, Geometry?>(nameof(IconData));
    public Geometry? IconData { get => GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public DawIconButton() {
        this[!ThemeProperty] = new DynamicResourceExtension("DawLibraryIconButtonTheme");
        var icon = new Avalonia.Controls.Shapes.Path { Width = 16, Height = 16, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        icon.Bind(Avalonia.Controls.Shapes.Path.DataProperty, new Binding(nameof(IconData)) { Source = this });
        icon.Bind(Avalonia.Controls.Shapes.Path.FillProperty, new Binding(nameof(Foreground)) { Source = this });
        Content = icon;
    }
}

public sealed class DawToggleButton : ToggleButton {
    public static readonly StyledProperty<DawDensity> DensityProperty = DawStyle.DensityProperty.AddOwner<DawToggleButton>();
    public static readonly StyledProperty<DawVariant> VariantProperty = AvaloniaProperty.Register<DawToggleButton, DawVariant>(nameof(Variant), DawVariant.Outlined);
    public DawDensity Density { get => GetValue(DensityProperty); set => SetValue(DensityProperty, value); }
    public DawVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public DawToggleButton() {
        this[!ThemeProperty] = new DynamicResourceExtension("DawLibraryToggleTheme");
        DawStyle.Variant(this, Variant);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == VariantProperty) DawStyle.Variant(this, Variant);
    }
}

public sealed class DawSwitch : ToggleButton {
    public static readonly StyledProperty<DawDensity> DensityProperty = DawStyle.DensityProperty.AddOwner<DawSwitch>();
    public DawDensity Density { get => GetValue(DensityProperty); set => SetValue(DensityProperty, value); }
    public DawSwitch() => this[!ThemeProperty] = new DynamicResourceExtension("DawLibrarySwitchTheme");
}
