using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// Regression coverage for the "Delete doesn't remove the item immediately - it stays for a good
/// few seconds" live-mode bug, for <see cref="LiveCorpsesFeatureSession"/>'s removal path. Mirrors
/// <c>LiveDroppedItemsSessionTests</c>'s fake-channel/gate pattern.
/// </summary>
public sealed class LiveCorpsesFeatureSessionTests
{
    [Fact]
    public async Task Remove_drops_the_corpse_before_the_reconciling_list_scan_completes()
    {
        var channel = new FakeCorpsesChannel();
        channel.SetCorpse("corpse-1", "A dead worker");
        channel.SetCorpse("corpse-2", "A dead soldier");
        var session = await LiveCorpsesFeatureSession.ConnectAsync(new LiveCorpsesChannel(channel));
        Assert.Equal(2, session.Corpses.Count);

        channel.ArmListGate();
        var result = await session.RemoveMapFeatureEntry(LiveCorpsesFeatureSession.CorpsesFeatureId, "corpse-1");

        Assert.False(result.IsError);
        Assert.Single(session.Corpses);
        Assert.Equal("corpse-2", session.Corpses[0].Id);

        channel.ReleaseListGate();
    }

    private sealed class FakeCorpsesChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, string> _corpses = new(StringComparer.Ordinal);

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetCorpse(string id, string label) => _corpses[id] = label;

        // Lets a test hold "corpses.list" open to prove a caller does not (and, after
        // LiveCorpsesFeatureSession's background-reconciliation fix, no longer needs to) wait on it.
        private TaskCompletionSource? _listGate;
        public void ArmListGate() => _listGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseListGate() => _listGate?.TrySetResult();

        public async Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command == "corpses.list" && _listGate is { } gate) await gate.Task.ConfigureAwait(false);
            var payloadElement = payload is null ? default : JsonSerializer.SerializeToElement(payload, JsonOptions);
            object? result = command switch
            {
                "corpses.list" => new
                {
                    corpses = _corpses.Select(kv => new
                    {
                        id = kv.Key,
                        label = kv.Value,
                        gibbed = (bool?)false,
                        looted = (bool?)false,
                        x = 0.0,
                        y = 0.0,
                        z = 0.0,
                    }).ToList(),
                    isHost = true,
                },
                "corpses.remove" => ApplyRemove(payloadElement),
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return element.Deserialize<TResponse>(JsonOptions)!;
        }

        private object? ApplyRemove(JsonElement payload)
        {
            var id = payload.GetProperty("id").GetString()!;
            _corpses.Remove(id);
            return null;
        }
    }
}
