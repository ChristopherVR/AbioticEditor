using AbioticEditor.Core.Ini;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class IniDocumentSessionTests
{
    [Fact]
    public void Known_setting_is_staged_once_and_uses_the_supplied_default()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ini-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "SandboxSettings.ini");
        try
        {
            File.WriteAllText(path, "; existing comment\n");
            var session = new IniDocumentSession(new AbioticIniFile(path, AbioticIniKind.SandboxSettings));
            var setting = new SandboxSettingDefinition("Example", "Example", "", "", "True", "Bool", "", "", []);
            Assert.True(session.AddSetting(setting));
            Assert.False(session.AddSetting(setting));
            Assert.True(session.IsDirty);
            Assert.DoesNotContain("Example", File.ReadAllText(path), StringComparison.Ordinal);
            session.Save();
            Assert.Contains("Example=True", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Equal("; existing comment\n", File.ReadAllText(path + ".bak"));
            session.Revert();
            Assert.False(session.IsDirty);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Saving_a_repeated_setting_refreshes_the_effective_order_and_keeps_a_backup()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ini-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "SandboxSettings.ini");
        const string original = "[SandboxSettings]\n; keep this comment\nEnemySpawnRate=1\nEnemySpawnRate=2\nbEnabled=True\n";
        try
        {
            File.WriteAllText(path, original);
            var session = new IniDocumentSession(new AbioticIniFile(path, AbioticIniKind.SandboxSettings));
            session.Sections.Single().Entries[0].Value = "3";
            session.Save();

            Assert.Equal(original, File.ReadAllText(path + ".bak"));
            Assert.Contains("; keep this comment", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.False(session.IsDirty);
            Assert.Equal("3", session.Sections.Single().Entries.Last(entry => entry.Key == "EnemySpawnRate").Value);
            Assert.Equal("True", session.Sections.Single().Entries.Single(entry => entry.Key == "bEnabled").Value);
            var reopened = new IniDocumentSession(new AbioticIniFile(path, AbioticIniKind.SandboxSettings));
            Assert.Equal(reopened.Sections.Single().Entries.Select(entry => (entry.Key, entry.Value)),
                session.Sections.Single().Entries.Select(entry => (entry.Key, entry.Value)));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
