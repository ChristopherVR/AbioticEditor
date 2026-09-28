using GamePassStorage;
using CoreGp = AbioticEditor.Core.GamePass;

namespace AbioticEditor.Tests;

/// <summary>
/// The seam between Core and the GamePassStorage library (submodules/GamePassStorage). The
/// library's own behaviour is tested in its repository; these tests only pin down that it stays
/// independent of this repo and that Core's forwarding enums keep matching it.
/// </summary>
public class GamePassStorageTests
{
    [Fact]
    public void The_storage_library_does_not_depend_on_core_or_any_abiotic_assembly()
    {
        var referenced = typeof(WgsStore).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain(referenced, n => n!.StartsWith("AbioticEditor", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n!.Contains("UeSaveGame", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, n => n!.Contains("CUE4Parse", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_forwarding_enums_carry_exactly_the_same_values_as_the_library()
    {
        static Dictionary<string, uint> Values<T>() where T : struct, Enum
            => Enum.GetNames<T>().ToDictionary(n => n, n => Convert.ToUInt32(Enum.Parse<T>(n), System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(Values<WgsEntryState>(), Values<CoreGp.WgsEntryState>());
        Assert.Equal(Values<WgsSyncState>(), Values<CoreGp.WgsSyncState>());
    }
}
