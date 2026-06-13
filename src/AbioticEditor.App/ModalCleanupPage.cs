namespace AbioticEditor.App;

/// <summary>
/// A modal <see cref="ContentPage"/> that runs one-time cleanup <b>only when it is actually
/// popped from the modal stack</b> - not when it is merely obscured by another modal pushed on
/// top of it.
///
/// <para>
/// This distinction matters for plugin-hosting pages. A plugin can push its own modal over the
/// host page: a web tool can call <c>abiotic.ui.openSettings()</c>/<c>openPlugins()</c>, and an
/// editor-tool view is free to <c>PushModalAsync</c>. That fires <see cref="Page.OnDisappearing"/>
/// on the host even though it is still alive underneath, so tearing a WebView down or disposing
/// the tool context there would break the tool the moment the child modal closes. Instead we
/// drive cleanup off the window's <see cref="Window.ModalPopped"/> event matching <c>this</c>
/// exact page, which is unambiguous regardless of what the plugin pushed on top.
/// </para>
/// </summary>
internal abstract class ModalCleanupPage : ContentPage
{
    private Window? _window;
    private bool _cleaned;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Subscribe once. OnAppearing also fires when a child modal we pushed is popped and this
        // page resurfaces, so the null-check keeps us from re-subscribing.
        if (_window is null && Application.Current?.Windows is { Count: > 0 } windows)
        {
            _window = windows[0];
            _window.ModalPopped += OnModalPopped;
        }
    }

    private void OnModalPopped(object? sender, ModalPoppedEventArgs e)
    {
        if (ReferenceEquals(e.Modal, this))
        {
            Cleanup();
        }
    }

    private void Cleanup()
    {
        if (_cleaned)
        {
            return;
        }
        _cleaned = true;
        if (_window is { } window)
        {
            window.ModalPopped -= OnModalPopped;
            _window = null;
        }
        OnModalRemoved();
    }

    /// <summary>Called exactly once, when this page is genuinely removed from the modal stack.</summary>
    protected abstract void OnModalRemoved();
}
