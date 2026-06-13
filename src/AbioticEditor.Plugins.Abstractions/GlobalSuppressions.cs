using System.Diagnostics.CodeAnalysis;

// "Error" is the intended name on both members: IPluginLog mirrors the Info/Warn/Error
// log-level triad, and IConsoleCommandContext.Error is the stderr writer paired with Out
// (matching System.Console.Error / TextWriter conventions). Renaming for CA1716 would make
// the API less obvious to the C# authors these contracts target.
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Scope = "member", Target = "~M:AbioticEditor.Plugins.IPluginLog.Error(System.String,System.Exception)",
    Justification = "Matches the Info/Warn/Error log-level naming; intentional.")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Scope = "member", Target = "~P:AbioticEditor.Plugins.Cli.IConsoleCommandContext.Error",
    Justification = "Stderr writer paired with Out, matching Console.Error conventions; intentional.")]
