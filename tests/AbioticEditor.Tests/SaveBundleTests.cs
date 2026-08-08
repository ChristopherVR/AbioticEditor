using System.IO.Compression;
using System.Text;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Reading a zipped save folder back in - the other end of the browser's EXPORT.
/// </summary>
/// <remarks>
/// Two shapes turn up in practice and both have to work: the zip the editor writes, which puts
/// the saves at the top, and the one a player makes by zipping the world folder in their file
/// manager, which puts them under a folder named after the world.
/// </remarks>
public sealed class SaveBundleTests
{
    [Fact]
    public void Reads_the_zip_the_editor_itself_writes()
    {
        var zip = Zip(("WorldSave_Facility.sav", "facility"), ("PlayerData/Player_1.sav", "player"));

        var bundle = SaveBundle.Read(zip, "Cascade.zip");

        // Nothing wraps the saves, so the world is named after the file it came in.
        Assert.Equal("Cascade", bundle.Name);
        Assert.Equal(2, bundle.Saves.Count);
        Assert.Equal("facility", Text(bundle.Saves["WorldSave_Facility.sav"]));
        Assert.Equal("player", Text(bundle.Saves["PlayerData/Player_1.sav"]));
    }

    [Fact]
    public void Reads_a_world_folder_zipped_by_hand_and_takes_its_name()
    {
        var zip = Zip(("Cascade/WorldSave_Facility.sav", "facility"), ("Cascade/PlayerData/Player_1.sav", "player"));

        var bundle = SaveBundle.Read(zip, "whatever-the-file-was-called.zip");

        // The wrapping folder names the world and disappears from the paths inside, so a save
        // lands at the same place it would have from an editor-written zip.
        Assert.Equal("Cascade", bundle.Name);
        Assert.Equal("facility", Text(bundle.Saves["WorldSave_Facility.sav"]));
        Assert.Equal("player", Text(bundle.Saves["PlayerData/Player_1.sav"]));
    }

    /// <summary>
    /// A world whose only saves are player saves must not lose its PlayerData folder.
    /// </summary>
    /// <remarks>
    /// PlayerData would otherwise look exactly like the wrapping folder the case above strips,
    /// and stripping it would put player saves at the top of the world - where the game does not
    /// keep them and where the editor would hand them back wrong on the next export.
    /// </remarks>
    [Fact]
    public void Keeps_PlayerData_when_it_is_the_only_folder()
    {
        var zip = Zip(("PlayerData/Player_1.sav", "player"), ("PlayerData/Player_2.sav", "other"));

        var bundle = SaveBundle.Read(zip, "Cascade.zip");

        Assert.Equal("Cascade", bundle.Name);
        Assert.Contains("PlayerData/Player_1.sav", bundle.Saves.Keys);
    }

    [Fact]
    public void Ignores_everything_that_is_not_a_save()
    {
        var zip = Zip(("WorldSave_Facility.sav", "facility"), ("readme.txt", "hello"), ("PlayerData/notes.json", "{}"));

        var bundle = SaveBundle.Read(zip, "Cascade.zip");

        Assert.Equal("WorldSave_Facility.sav", Assert.Single(bundle.Saves.Keys));
    }

    [Fact]
    public void Refuses_a_zip_with_no_saves_in_it_and_says_what_to_zip_instead()
    {
        var zip = Zip(("holiday-photo.png", "not a save"));

        var failure = Assert.Throws<InvalidDataException>(() => SaveBundle.Read(zip, "photos.zip"));

        Assert.Contains("world folder", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream Zip(params (string Path, string Contents)[] entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in entries)
            {
                using var stream = archive.CreateEntry(path).Open();
                stream.Write(Encoding.UTF8.GetBytes(contents));
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
