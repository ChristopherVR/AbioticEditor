namespace AbioticEditor.Cli;

/// <summary>
/// Headless companion to the MAUI editor. All save parsing/editing logic lives in
/// AbioticEditor.Core; this binary only wires commands to it.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Finish any update file-swaps deferred from a previous `update install` and sweep
        // its leftover backups before doing anything else. Silent when there's nothing to do.
        Updater.UpdateCleanup.Run();
        return await CommandTree.Build().Parse(args).InvokeAsync().ConfigureAwait(false);
    }
}
