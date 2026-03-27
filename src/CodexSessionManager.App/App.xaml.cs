// NOSONAR - CLSCompliant(false) is declared at assembly level for this project.
using System.Diagnostics.CodeAnalysis; // NOSONAR - Codacy SonarC# S3990 false positive; assembly-level CLSCompliant(false) is already declared.
using System.Windows;

namespace CodexSessionManager.App;

[SuppressMessage("Code Smell", "S2333", Justification = "The application entry point is paired with XAML-generated partial members.")]
[ExcludeFromCodeCoverage]
public partial class App : Application // NOSONAR - partial is required because XAML generates the companion partial type.
{
}

