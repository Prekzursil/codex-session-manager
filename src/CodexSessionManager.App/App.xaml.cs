using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace CodexSessionManager.App;

[SuppressMessage("Code Smell", "S2333", Justification = "The application entry point is paired with XAML-generated partial members.")]
[ExcludeFromCodeCoverage]
public partial class App : Application
{
}

