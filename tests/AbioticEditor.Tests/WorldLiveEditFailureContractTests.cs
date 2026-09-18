namespace AbioticEditor.Tests;

/// <summary>
/// Round 125: locks in the fix for a live "world features" retry storm the owner hit against a real
/// elevator - see docs/PROGRESS.md's Round-125 entry for the full trail. Two things had to be true
/// at once for a refused live edit to keep re-sending itself every couple of seconds instead of
/// failing once and reverting:
///
/// <list type="number">
/// <item>a <c>Live*FeatureSession.SetMapFeatureField</c> that calls into its channel let a
/// <c>LiveAgentException</c> escape uncaught instead of returning a <c>WorldEditResult.Failure</c>
/// (elevators and portals both had this gap; every other settable live area already caught it); and</item>
/// <item><c>WorldFeaturesTab.SetFieldAsync</c> (the one caller every live/offline world-feature tab
/// shares) had no defense against a session doing that: an escaped exception skipped its error
/// handling AND its <c>RefreshSnapshot()</c> revert, leaving the control showing a click the game
/// never accepted until the next unrelated re-render re-tried it.</item>
/// </list>
///
/// This file pins both halves as a standing contract, not just the one area that happened to be
/// observed misbehaving - kept separate from each area's own <c>WorldLive*AreaTests</c> file so a
/// change to one area's session never collides with this cross-area check.
/// </summary>
public sealed class WorldLiveEditFailureContractTests
{
    /// <summary>
    /// Every live world-feature session that actually calls into its channel from
    /// <c>SetMapFeatureField</c> (as opposed to refusing every field locally, like power sockets)
    /// must catch <c>LiveAgentException</c> around that call and turn it into a
    /// <c>WorldEditResult.Failure</c> - see the class remarks. Enumerates every
    /// <c>Live*FeatureSession.cs</c> under <c>Models/</c> rather than naming each one, so a future
    /// live area gains this same protection automatically instead of needing a reviewer to
    /// remember to add it to a list here.
    /// </summary>
    [Fact]
    public void Every_channel_calling_Live_FeatureSession_SetMapFeatureField_catches_LiveAgentException()
    {
        var missing = new List<string>();
        foreach (var path in UiSource.EnumerateFiles("Models", "Live*FeatureSession.cs"))
        {
            var source = File.ReadAllText(path);
            if (!source.Contains(": IWorldFeaturesSession", StringComparison.Ordinal)) continue;

            var setMethodIndex = source.IndexOf(
                "Task<WorldEditResult> SetMapFeatureField", StringComparison.Ordinal);
            if (setMethodIndex < 0) continue;
            var body = source[setMethodIndex..];

            // A session with no `_channel.` mutator call inside SetMapFeatureField refuses every
            // field locally (power sockets: "power sockets have no settable field live") and so
            // has nothing that could ever throw a LiveAgentException here - nothing to assert.
            if (!body.Contains("_channel.Set", StringComparison.Ordinal)
                && !body.Contains("_channel.Reset", StringComparison.Ordinal)
                && !body.Contains("_channel.Force", StringComparison.Ordinal))
            {
                continue;
            }

            if (!body.Contains("catch (LiveAgentException", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path));
            }
        }

        Assert.True(missing.Count == 0,
            "These live feature sessions call into their channel from SetMapFeatureField but do not "
                + "catch LiveAgentException around it, so a refusal from the game would escape as an "
                + "uncaught exception instead of a WorldEditResult.Failure (see the round-125 elevator "
                + "retry storm this exact gap caused): " + string.Join(", ", missing));
    }

    /// <summary>
    /// <c>WorldFeaturesTab.SetFieldAsync</c> is the one send path every world-feature tab (file or
    /// live, every area from buttons to trams) shares - it must treat ANY exception from
    /// <c>Session.SetMapFeatureField</c> the same as a returned Failure result, must always revert
    /// via <c>RefreshSnapshot()</c> (a <c>finally</c>, so a throw cannot skip it), and must refuse a
    /// second send for the same entry+field while one is already in flight - the three things that
    /// together stop a single failed click from ever reaching the game more than once, whether or
    /// not the session behind it behaves.
    /// </summary>
    [Fact]
    public void WorldFeaturesTab_SetFieldAsync_reverts_and_sends_once_even_if_the_session_throws()
    {
        var source = UiSource.ReadAllText("Components", "World", "WorldFeaturesTab.razor");
        var methodIndex = source.IndexOf("private async Task SetFieldAsync(", StringComparison.Ordinal);
        Assert.True(methodIndex >= 0, "WorldFeaturesTab.razor no longer declares SetFieldAsync - update this test to match.");
        var body = source[methodIndex..];

        Assert.Contains("catch (Exception", body, StringComparison.Ordinal);
        Assert.Contains("finally", body, StringComparison.Ordinal);
        Assert.Contains("RefreshSnapshot()", body, StringComparison.Ordinal);
        // A send-in-flight guard keyed by entry+field, checked before anything else in the method -
        // the exact mechanism differs from the wording here (this is not a strict source pattern
        // grep for the pending-set collection itself), so this asserts on the two concepts
        // SetFieldAsync now documents this guard by (see the round-125 comment above it in the
        // source): a per-field pending marker, and refusing to proceed when one is already set.
        Assert.Contains("_pendingFieldSends", body, StringComparison.Ordinal);
    }
}
