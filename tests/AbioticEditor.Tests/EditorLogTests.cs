using System.IO;
using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Tests;

/// <summary>
/// EditorLog is process-global static state, so these tests serialize on a collection
/// and always restore the default (disabled, %LOCALAPPDATA% directory) when done.
/// </summary>
[Collection("EditorLog")]
public class EditorLogTests : IDisposable
{
    private readonly string _originalDirectory;
    private readonly string _tempDir;

    public EditorLogTests()
    {
        _originalDirectory = EditorLog.LogDirectory;
        _tempDir = Path.Combine(Path.GetTempPath(), "AbioticEditorTests", Path.GetRandomFileName());
        EditorLog.LogDirectory = _tempDir;
        EditorLog.Enabled = false;
    }

    public void Dispose()
    {
        EditorLog.Enabled = false;
        EditorLog.LogDirectory = _originalDirectory;
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Disabled_ByDefault_WritesNothing()
    {
        EditorLog.Info("Test", "should not appear");
        Assert.False(Directory.Exists(_tempDir), "Disabled logger must not even create the directory.");
    }

    [Fact]
    public void Enabled_WritesTimestampedLines_WithLevelsAndException()
    {
        EditorLog.Enabled = true;
        EditorLog.Info("AreaA", "hello info");
        EditorLog.Warn("AreaB", "hello warn");
        EditorLog.Error("AreaC", "hello error", new InvalidOperationException("boom"));

        var path = EditorLog.CurrentLogFilePath;
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);
        Assert.Contains("[INFO ] AreaA: hello info", text);
        Assert.Contains("[WARN ] AreaB: hello warn", text);
        Assert.Contains("[ERROR] AreaC: hello error", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void Retention_KeepsAtMostSevenFiles()
    {
        Directory.CreateDirectory(_tempDir);
        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_tempDir, $"editor-2025010{i}.log"), "old");
        }

        EditorLog.Enabled = true;
        EditorLog.Info("Test", "trigger prune via new daily file");

        var files = Directory.GetFiles(_tempDir, "editor-*.log");
        Assert.True(files.Length <= EditorLog.MaxLogFiles,
            $"Expected at most {EditorLog.MaxLogFiles} files, found {files.Length}.");
        // Today's file (the newest name) must have survived the prune.
        Assert.Contains(files, f => string.Equals(f, EditorLog.CurrentLogFilePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OwnIoErrors_AreSwallowed()
    {
        // Point the logger at an impossible path - logging must not throw.
        EditorLog.LogDirectory = Path.Combine(_tempDir, "bad\0name");
        EditorLog.Enabled = true;
        EditorLog.Info("Test", "this write fails silently");
    }
}
