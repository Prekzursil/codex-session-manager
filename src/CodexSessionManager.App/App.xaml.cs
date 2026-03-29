#pragma warning disable S3990 // Codacy false positive: the assembly already declares CLSCompliant(true).
using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace CodexSessionManager.App;

[SuppressMessage("Compatibility", "S3990", Justification = "The assembly already declares CLSCompliant(true); this file-level report is a persistent analyzer false positive.")]
[SuppressMessage("Code Smell", "S2333", Justification = "The application entry point is paired with XAML-generated partial members.")]
[ExcludeFromCodeCoverage]
public partial class App : Application
{
}

