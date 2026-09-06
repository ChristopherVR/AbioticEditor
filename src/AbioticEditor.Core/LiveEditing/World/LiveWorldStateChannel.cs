namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live world clock and weather: reads the running game's day counter, time of day, day/night
/// flag and current weather event, and lets a host set the time or day, trigger a weather event
/// right now, or queue one for the next day - see <c>world.get</c>/<c>world.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c>. The mod side is the reference
/// mod's own settime / setweather / setnextweather commands (host-only there too), driving the
/// game's DayNightManager the same way.
/// </summary>
public sealed class LiveWorldStateChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveWorldState> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<StateWire>("world.get", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LiveWorldState(wire.Day, wire.TimeSeconds, wire.IsNight, wire.Paused,
            string.IsNullOrEmpty(wire.CurrentWeather) ? "None" : wire.CurrentWeather,
            wire.WeatherOptions ?? [], wire.IsHost);
    }

    /// <summary>Applies whichever fields of <paramref name="edit"/> are non-null, immediately.</summary>
    public Task SetAsync(LiveWorldStateEdit edit, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("world.set",
            new SetWire(edit.TimeSeconds, edit.Day, edit.Weather, edit.NextWeather), cancellationToken);

    private sealed record StateWire(int Day, double TimeSeconds, bool IsNight, bool Paused,
        string? CurrentWeather, IReadOnlyList<string>? WeatherOptions, bool IsHost);
    private sealed record SetWire(double? TimeSeconds, int? Day, string? Weather, string? NextWeather);
}

/// <summary>The running game's clock and weather, as read by <see cref="LiveWorldStateChannel.GetAsync"/>.</summary>
/// <param name="Day">The in-game day counter.</param>
/// <param name="TimeSeconds">Seconds into the current in-game day (0..86400), the same unit the
/// world save's <c>TimeOfDay</c> struct stores.</param>
/// <param name="CurrentWeather">The active weather event row name, or <c>None</c>.</param>
/// <param name="WeatherOptions">Every weather event row the game knows (always starting with
/// <c>None</c>), for a picker.</param>
public sealed record LiveWorldState(int Day, double TimeSeconds, bool IsNight, bool Paused,
    string CurrentWeather, IReadOnlyList<string> WeatherOptions, bool IsHost)
{
    public int Hour => (int)(Math.Clamp(TimeSeconds, 0, 86399) / 3600);
    public int Minute => (int)(Math.Clamp(TimeSeconds, 0, 86399) % 3600 / 60);
}

/// <summary>One world edit; a null field is left untouched. <paramref name="Weather"/> triggers
/// that event now; <paramref name="NextWeather"/> queues it for the next in-game day.</summary>
public sealed record LiveWorldStateEdit(double? TimeSeconds = null, int? Day = null,
    string? Weather = null, string? NextWeather = null);
