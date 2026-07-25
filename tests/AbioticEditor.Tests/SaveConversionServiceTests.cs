using AbioticEditor.Web.Services;
using AbioticEditor.Ui;

namespace AbioticEditor.Tests;

public sealed class SaveConversionServiceTests
{
    [Fact]
    public void Recognises_real_Steam_and_GamePass_world_fixtures()
    {
        var steam = Fixtures.CascadeDir;
        var gamePass = Fixtures.GamePassWgsDir;
        if (steam is null || gamePass is null) return;

        Assert.Equal(SaveConversionSourceValidation.Valid,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToGamePass, steam));
        Assert.Equal(SaveConversionSourceValidation.Valid,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToSteam, gamePass));
        Assert.Equal(SaveConversionSourceValidation.MissingGamePassContainer,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToSteam, steam));
        Assert.Equal(SaveConversionSourceValidation.MissingSteamWorldSave,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToGamePass, gamePass));
    }

    [Theory]
    [InlineData(SaveConversionDirection.ToGamePass, "-GamePass")]
    [InlineData(SaveConversionDirection.ToSteam, "-Steam")]
    public void Writes_the_converted_copy_beside_the_selected_folder(SaveConversionDirection direction, string suffix)
    {
        var source = Path.Combine(Path.GetTempPath(), "AbioticEditor conversion source");
        Assert.Equal(Path.GetFullPath(source) + suffix, SaveConversionService.DestinationFor(direction, source));
    }

    [Fact]
    public void Converts_the_GamePass_fixture_through_the_settings_service()
    {
        if (Fixtures.GamePassWgsDir is not { } fixture) return;
        var root = Path.Combine(Path.GetTempPath(), "AbioticEditorTests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Fixture");
        try
        {
            CopyDirectory(fixture, source);
            var output = SaveConversionService.Convert(SaveConversionDirection.ToSteam, source, playerAccountId: null);

            Assert.Equal(source + "-Steam", output);
            Assert.NotEmpty(Directory.EnumerateFiles(output, "WorldSave_*.sav", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Opening_logs_creates_the_folder_before_revealing_it()
    {
        var path = Path.Combine(Path.GetTempPath(), "AbioticEditorTests", Guid.NewGuid().ToString("N"), "logs");
        var navigation = new RecordingNavigation();
        try
        {
            await LogFolderOpener.OpenAsync(navigation, path);

            Assert.True(Directory.Exists(path));
            Assert.Equal(path, navigation.RevealedPath);
        }
        finally
        {
            var root = Directory.GetParent(path)!.FullName;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingNavigation : IExternalNavigationService
    {
        public string? RevealedPath { get; private set; }
        public Task OpenUrlAsync(Uri url, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevealPathAsync(string path, CancellationToken cancellationToken = default)
        {
            Assert.True(Directory.Exists(path));
            RevealedPath = path;
            return Task.CompletedTask;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
