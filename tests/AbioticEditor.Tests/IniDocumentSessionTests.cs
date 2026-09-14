using AbioticEditor.Core.Ini;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class IniDocumentSessionTests
{
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
