using Avalonia.Controls;
namespace OpenUtau.App.Controls {
    /// <summary>Compatibility entry for existing mixer bindings, part names and field access.</summary>
    public class DawValueField : UTvTU.DawUI.Controls.DawParameterField {
        public DawValueField() {
            UseLegacyPresentation();
            // Compiled XAML has completed its scope; provide instance-local aliases for old callers.
            var surface = this.FindControl<Border>("PART_Surface");
            var scope = new NameScope();
            scope.Register("Readout", Readout); scope.Register("Editor", Editor);
            scope.Register("PART_Readout", Readout); scope.Register("PART_Editor", Editor);
            if (surface != null) scope.Register("PART_Surface", surface);
            scope.Complete(); NameScope.SetNameScope(this, scope);
        }
    }
}
