namespace AbioticEditor.Web.Services;

/// <summary>
/// Lets the page draw part-way through a long piece of work.
/// </summary>
/// <remarks>
/// <para>A browser runs the editor on the same single thread it draws with, so anything that
/// takes seconds has to hand that thread back now and then or the tab simply freezes - no
/// spinner, no progress, no response to clicks, exactly as if the editor had crashed.</para>
///
/// <para><b><see cref="Task.Yield"/> does not do this.</b> It looks like it should, and it is
/// the obvious thing to reach for, but it queues the continuation on the runtime's own scheduler,
/// which drains inside the same JavaScript turn - the browser never gets a look in. Measured on a
/// real world unpacked from a zip: with Task.Yield between every file, the tab still froze for
/// 6.3 seconds and painted two frames. A one-millisecond delay goes through a JavaScript timer
/// instead, which does return to the event loop, and that is the whole difference.</para>
///
/// <para>Costs about a millisecond per call, so call it per file or per save - never per item.</para>
/// </remarks>
internal static class UiBreather
{
    /// <summary>Returns to the browser's event loop so pending renders paint.</summary>
    public static Task BreatheAsync(CancellationToken cancellationToken = default)
        => Task.Delay(1, cancellationToken);
}
