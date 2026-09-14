using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
namespace AbioticEditor.Tests;
public sealed class WeaponCoatingSaveTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Coating_and_wear_round_trip_and_unrelated_slots_stay_unchanged(bool explicitRemoval)
    {
        if (Fixtures.CascadeDir is not { } root) return;
        var path=Path.Combine(root,"PlayerData","Player_76561198128277890.sav");
        if(!File.Exists(path))return;
        var data=PlayerSaveReader.ReadFromFile(path);
        var before=data.Inventory;
        var slot=before.Main.First(s=>!s.IsEmpty);
        var changed=slot with { CoatingIndex=6, CoatingDurability=73 };
        PlayerSaveWriter.ApplyInventory(data,before with { Main=before.Main.Select(s=>s.Index==slot.Index?changed:s).ToArray() });
        using var buffer=new MemoryStream(); data.Raw.WriteTo(buffer);buffer.Position=0;
        var after=PlayerSaveReader.ReadFrom(SaveGame.LoadFrom(buffer)).Inventory;
        Assert.Equal(6,after.Main[slot.Index].CoatingIndex);
        Assert.Equal(73,after.Main[slot.Index].CoatingDurability);
        foreach(var untouched in before.Main.Where(s=>s.Index!=slot.Index))Assert.Equal(untouched,after.Main[untouched.Index]);
        Assert.Equal(before.Hotbar,after.Hotbar);
        Assert.Equal(before.Equipment,after.Equipment);
        PlayerSaveWriter.ApplyInventory(data,after with { Main=after.Main.Select(s=>s.Index==slot.Index?s with {CoatingIndex=explicitRemoval ? -1 : null,CoatingDurability=explicitRemoval ? 0 : null}:s).ToArray() });
        using var removed=new MemoryStream();data.Raw.WriteTo(removed);removed.Position=0;
        Assert.Equal(-1,PlayerSaveReader.ReadFrom(SaveGame.LoadFrom(removed)).Inventory.Main[slot.Index].CoatingIndex);
    }
}
