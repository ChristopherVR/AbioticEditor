using System.Diagnostics;
using System.Runtime.Versioning;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

[SupportedOSPlatform("windows")]
public sealed class ChildProcessJobTests
{
    private static Process StartSleeper()
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add("Start-Sleep -Seconds 120");
        return Process.Start(info)!;
    }

    [SkippableFact]
    public void Closing_the_job_ends_the_child_process()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Job objects are Windows-only.");

        using var child = StartSleeper();
        try
        {
            using var job = ChildProcessJob.TryCreate();
            Assert.NotNull(job);
            Assert.True(job.TryAdd(child));
            Assert.False(child.HasExited);

            job.Dispose();

            Assert.True(child.WaitForExit(10_000), "The child should be ended when its job closes.");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
        }
    }

    [SkippableFact]
    public void A_child_not_in_the_job_is_left_running()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Job objects are Windows-only.");

        using var inJob = StartSleeper();
        using var outside = StartSleeper();
        try
        {
            using var job = ChildProcessJob.TryCreate();
            Assert.NotNull(job);
            Assert.True(job.TryAdd(inJob));

            job.Dispose();

            Assert.True(inJob.WaitForExit(10_000));
            Assert.False(outside.WaitForExit(1_000), "A process that was never bound must not be touched.");
        }
        finally
        {
            if (!outside.HasExited) outside.Kill(entireProcessTree: true);
            if (!inJob.HasExited) inJob.Kill(entireProcessTree: true);
        }
    }
}
