using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenUtau.App.Controls;

/// <summary>Passive page chrome; bindings retain the host's settings and commands.</summary>
public partial class WorkspacePageHeader : UserControl {
    public static readonly StyledProperty<string?> HeadingProperty =
        AvaloniaProperty.Register<WorkspacePageHeader, string?>(nameof(Heading));
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<WorkspacePageHeader, string?>(nameof(Description));
    public static readonly StyledProperty<MaterialShapeKind> ShapeKindProperty =
        AvaloniaProperty.Register<WorkspacePageHeader, MaterialShapeKind>(nameof(ShapeKind), MaterialShapeKind.Arch);
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<WorkspacePageHeader, Geometry?>(nameof(Icon));

    public WorkspacePageHeader() => InitializeComponent();
    public string? Heading { get => GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public MaterialShapeKind ShapeKind { get => GetValue(ShapeKindProperty); set => SetValue(ShapeKindProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
