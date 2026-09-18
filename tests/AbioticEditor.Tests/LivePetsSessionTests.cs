using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// Regression coverage for the "Delete doesn't remove the item immediately - it stays for a good
/// few seconds" live-mode bug, for <see cref="LivePetsSession"/>'s removal path. Mirrors
/// <c>LiveDroppedItemsSessionTests</c>'s fake-channel/gate pattern.
/// </summary>
public sealed class LivePetsSessionTests
{
    [Fact]
    public async Task Remove_drops_the_pet_before_the_reconciling_list_scan_completes()
    {
        var channel = new FakePetsChannel();
        channel.SetPet("pet-1", "NPC_Monster_Pest_C", isDead: false);
        channel.SetPet("pet-2", "NPC_Monster_Skink_C", isDead: false);
        var session = await LivePetsSession.ConnectAsync(new LivePetsChannel(channel));
        Assert.Equal(2, session.Pets.Count);

        IWorldPetsSession petsSession = session;
        channel.ArmListGate();
        await petsSession.RemovePetAsync("pet-1");

        Assert.Single(session.Pets);
        Assert.Equal("pet-2", session.Pets[0].Id);

        channel.ReleaseListGate();
    }

    private sealed class FakePetsChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, (string? NpcClass, bool IsDead)> _pets = new(StringComparer.Ordinal);

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetPet(string id, string? npcClass, bool isDead) => _pets[id] = (npcClass, isDead);

        // Lets a test hold "pets.list" open to prove a caller does not (and, after
        // LivePetsSession's background-reconciliation fix, no longer needs to) wait on it.
        private TaskCompletionSource? _listGate;
        public void ArmListGate() => _listGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseListGate() => _listGate?.TrySetResult();

        public async Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command == "pets.list" && _listGate is { } gate) await gate.Task.ConfigureAwait(false);
            var payloadElement = payload is null ? default : JsonSerializer.SerializeToElement(payload, JsonOptions);
            object? result = command switch
            {
                "pets.list" => new
                {
                    pets = _pets.Select(kv => new
                    {
                        id = kv.Key,
                        npcClass = kv.Value.NpcClass,
                        isDead = kv.Value.IsDead,
                        customName = (string?)null,
                        x = 0.0,
                        y = 0.0,
                        z = 0.0,
                        limbHealth = new Dictionary<string, double>(),
                        xp = 0,
                        matched = true,
                    }).ToList(),
                    isHost = true,
                    available = true,
                    reason = (string?)null,
                    supportsSpeciesChange = false,
                    supportsRemoval = true,
                },
                "pets.remove" => ApplyRemove(payloadElement),
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return element.Deserialize<TResponse>(JsonOptions)!;
        }

        private object? ApplyRemove(JsonElement payload)
        {
            var id = payload.GetProperty("id").GetString()!;
            _pets.Remove(id);
            return null;
        }
    }
}
