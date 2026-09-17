using AbioticEditor.Core.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Copies the live-editing transport's own diagnostics - the native helper process's console
/// output (already piped to <c>helper.log</c> by <see cref="LiveAgentSetup"/>'s launcher) and the
/// UE4SS Lua mod's own log lines (written to a sibling <c>lua.log</c> by that mod's own
/// <c>logLine</c> helper, since it runs inside the game, a separate OS process this app cannot
/// call <see cref="EditorLog"/> from directly) - into this editor's own unified log file.
///
/// <para>Before this existed, tracking down a live-editing bug meant adding a throwaway
/// <c>print()</c> line to the Lua mod, restarting the game (this game's Lua hot-reload is
/// typically off), and manually finding and reading UE4SS's own separate log file. Both source
/// files already exist independent of this service; this only decides whether, and how, their
/// content reaches somewhere the editor's own diagnostics already read from.</para>
///
/// <para>Lives in the desktop host project, not the shared Razor library: the files it tails only
/// ever exist next to a live-agent helper this host can launch (see <see cref="LiveAgentSetup"/>),
/// and a browser host has nowhere to read %LOCALAPPDATA% from in the first place, the same
/// reasoning <see cref="ILiveEditingCapability"/> itself is desktop-only for.</para>
///
/// <para>Runs for the app's whole lifetime (registered as a hosted service, not tied to any one
/// live session) since the helper can log a connection attempt, or fail to even start, before any
/// live session exists at all. Only ever WRITES to <see cref="EditorLog"/> while
/// <see cref="EditorLog.Enabled"/> is true - the same opt-in switch every other diagnostic line in
/// this app already respects - polled on each tick rather than pushed, since neither
/// <see cref="HostSettingsService"/> nor <see cref="HostDiagnosticsStore"/> raises a
/// changed-settings event to hook into instead.</para>
/// </summary>
public sealed class LiveAgentLogBridgeService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditorLiveAgent");

    // helper.log: the native helper's own stdout/stderr, already captured by LiveAgentSetup's
    // launcher (see LaunchHelperHidden) - this service only reads it, never writes it.
    // lua.log: the UE4SS Lua mod's own logLine() output (see main.lua) - a SEPARATE file from
    // helper.log on purpose, since two different OS processes appending to the very same file
    // without coordinating a lock risks interleaved/corrupted lines; merging the two back together
    // by timestamp happens here instead, where it is one reader's job rather than two writers'.
    private readonly TailedFile _helper = new(Path.Combine(RootDir, "helper.log"), "LiveAgentHelper");
    private readonly TailedFile _lua = new(Path.Combine(RootDir, "lua.log"), "LiveAgentLua");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                if (EditorLog.Enabled)
                {
                    _helper.MirrorNewLines();
                    _lua.MirrorNewLines();
                }
                else
                {
                    // Diagnostics off: keep both positions pinned to "end of file" so nothing
                    // floods in as a wall of backlog the moment the player turns logging back on -
                    // only genuinely NEW lines from that point forward are ever mirrored.
                    _helper.SkipToEnd();
                    _lua.SkipToEnd();
                }
            }
            catch (Exception)
            {
                // A diagnostics feature must never take the app down - see EditorLog's own
                // "writes must never break the feature being diagnosed" rule, which this mirrors.
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>One log file this service tails: remembers how far it has already read, copies
    /// only the bytes written since, and recovers cleanly if the file shrinks (this mod's own
    /// lua.log truncates itself past a size cap - see main.lua's logLine) or does not exist yet
    /// (the helper has not launched, or this is a browser host with no live editing at all).</summary>
    private sealed class TailedFile(string path, string area)
    {
        private long _position;
        private bool _positionInitialized;

        public void SkipToEnd()
        {
            var length = TryGetLength();
            if (length is { } value) _position = value;
            _positionInitialized = true;
        }

        public void MirrorNewLines()
        {
            var length = TryGetLength();
            if (length is not { } currentLength) return;

            // First tick this file was ever seen with logging already on (app just started with
            // the setting already enabled): start from the end, not byte zero - the same "no
            // backlog flood" reasoning as SkipToEnd, just for the enabled-at-startup case instead
            // of the toggled-on-later one.
            if (!_positionInitialized) { _position = currentLength; _positionInitialized = true; return; }

            if (currentLength < _position) _position = 0; // Truncated/rotated since last read.
            if (currentLength == _position) return;

            string[] newLines;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(_position, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();
                // Only count bytes actually consumed as read, not the whole file, so a line still
                // being written mid-append (rare, but this file has no writer-side locking) is
                // picked up again whole on the next tick instead of being split across two.
                _position += System.Text.Encoding.UTF8.GetByteCount(text);
                newLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException)
            {
                return; // Being written to/rotated right now - try again next tick.
            }

            foreach (var rawLine in newLines)
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (line.Contains("fail", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    EditorLog.Warn(area, line);
                else
                    EditorLog.Info(area, line);
            }
        }

        private long? TryGetLength()
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.Length : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
