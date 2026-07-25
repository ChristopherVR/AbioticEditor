using AbioticEditor.Core.Ini;

#pragma warning disable CA1716 // Namespace matches the existing Razor component folder.
namespace AbioticEditor.Web.Components.Shared;
#pragma warning restore CA1716

public partial class WorkspaceShell
{
    private static string ConfigChipResourceKey(AbioticIniKind kind) => kind switch
    {
        AbioticIniKind.ServerAdmin => "Main_ChipAdmin",
        AbioticIniKind.SandboxSettings => "Main_ChipSandbox",
        AbioticIniKind.ClientConfig => "Main_ChipClient",
        _ => "Main_ChipIni",
    };

    private static string ConfigDetail(AbioticIniFile config) => config.Kind == AbioticIniKind.SandboxSettings
        ? Path.GetFileName(Path.GetDirectoryName(config.FullPath)) ?? string.Empty
        : string.Empty;
}
