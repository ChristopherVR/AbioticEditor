using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// Unit tests for <see cref="LiveStorySession.ComputeFlagPlan"/>: the pure flag-list computation
/// behind live <c>story.set</c> (round 77 - the story chapter is a function of world flags, so
/// setting it live means setting/clearing the same flags the offline editor's chapter SET action
/// would write to WorldSave_Facility.sav - see StoryFlagSync.PlanSyncToChapter/PlanClearForwardFlags).
/// </summary>
public sealed class LiveStorySessionFlagPlanTests
{
    [Fact]
    public void Unknown_chapter_row_throws()
    {
        Assert.Throws<InvalidOperationException>(() => LiveStorySession.ComputeFlagPlan("NoSuchChapter", []));
    }

    [Fact]
    public void Moving_forward_to_a_middle_chapter_sets_every_earlier_trigger_and_clears_nothing()
    {
        // "Pens" is chapter index 11 - a middle chapter, not the first or last.
        var target = "Pens";
        var targetIndex = StoryProgressionCatalog.IndexOf(target);
        Assert.True(targetIndex > 0 && targetIndex < StoryProgressionCatalog.Chapters.Count - 1);

        var (flagsToSet, flagsToClear) = LiveStorySession.ComputeFlagPlan(target, currentlySet: []);

        var expectedTriggers = StoryProgressionCatalog.Chapters
            .Take(targetIndex + 1)
            .Where(c => c.TriggerFlag is not null)
            .Select(c => c.TriggerFlag!)
            .ToList();
        foreach (var trigger in expectedTriggers)
        {
            Assert.Contains(trigger, flagsToSet);
        }
        Assert.Empty(flagsToClear);
    }

    [Fact]
    public void Moving_backward_from_a_middle_chapter_clears_the_later_triggers_and_sets_nothing_already_set()
    {
        const string target = "Pens"; // index 11
        var targetIndex = StoryProgressionCatalog.IndexOf(target);

        // Simulate a world that already reached "EndSecurity" (index 19): every chapter trigger
        // through there is set, including everything past the "Pens" target.
        var reachedIndex = StoryProgressionCatalog.IndexOf("EndSecurity");
        Assert.True(reachedIndex > targetIndex);
        var currentlySet = StoryProgressionCatalog.Chapters
            .Take(reachedIndex + 1)
            .Where(c => c.TriggerFlag is not null)
            .Select(c => c.TriggerFlag!)
            .ToList();

        var (flagsToSet, flagsToClear) = LiveStorySession.ComputeFlagPlan(target, currentlySet);

        // Every trigger flag strictly after "Pens" that is currently set must be cleared.
        var expectedCleared = StoryProgressionCatalog.Chapters
            .Skip(targetIndex + 1)
            .Take(reachedIndex - targetIndex)
            .Where(c => c.TriggerFlag is not null)
            .Select(c => c.TriggerFlag!)
            .ToList();
        Assert.NotEmpty(expectedCleared);
        foreach (var trigger in expectedCleared)
        {
            Assert.Contains(trigger, flagsToClear);
        }

        // Nothing already set through the target chapter comes back as something to (re)set.
        var triggersThroughTarget = StoryProgressionCatalog.Chapters
            .Take(targetIndex + 1)
            .Where(c => c.TriggerFlag is not null)
            .Select(c => c.TriggerFlag!);
        foreach (var trigger in triggersThroughTarget)
        {
            Assert.DoesNotContain(trigger, flagsToSet);
        }

        // Nothing beyond the reached chapter (never set in this world) is claimed as clearable.
        var neverSet = StoryProgressionCatalog.Chapters
            .Skip(reachedIndex + 1)
            .Where(c => c.TriggerFlag is not null)
            .Select(c => c.TriggerFlag!);
        foreach (var trigger in neverSet)
        {
            Assert.DoesNotContain(trigger, flagsToClear);
        }
    }
}
