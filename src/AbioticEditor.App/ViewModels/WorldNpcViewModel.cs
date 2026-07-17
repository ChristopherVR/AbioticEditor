using System.ComponentModel;
using AbioticEditor.App.Services;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>One narrative NPC / trader from a world save, with editable state.</summary>
public sealed class WorldNpcViewModel : INotifyPropertyChanged
{
    private WorldNpc _original;
    private readonly Action _onChanged;
    private bool _isDead;
    private string? _state;

    public WorldNpcViewModel(WorldNpc source, Action onChanged)
    {
        _original = source;
        _onChanged = onChanged;
        _isDead = source.IsDead;
        _state = source.State;
        _petName = source.CustomName ?? string.Empty;

        // The picker needs the stored value present even if the enum catalog is empty.
        var options = GameDataServices.NpcStates.ToList();
        if (_state is not null && !options.Contains(_state, StringComparer.Ordinal))
        {
            options.Insert(0, _state);
        }
        StateOptions = options;
    }

    public string Id => _original.Id;

    /// <summary>Display name; reflects a staged pet rename immediately.</summary>
    public string ActorName => IsPet && !string.IsNullOrWhiteSpace(_petName)
        ? _petName
        : _original.ActorName;

    /// <summary>True for tamed companions from the <c>PetNPC</c> map.</summary>
    public bool IsPet => _original.IsPet;

    public IReadOnlyList<string> StateOptions { get; }

    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    public string Identity => IsPet
        ? Loc["WorldNpc_TamedPetCompanion"]
        : NpcLocalization.LabelFor(Id, ActorName);

    // Keyed on the catalog's stable hint id, not the (localized) Identity text.
    public bool IsHologram => !IsPet
        && NpcIdentityCatalog.MatchedHint(Id, ActorName) == "Human_Hologram";

    public string ContextText
    {
        get
        {
            var location = _original.X == 0 && _original.Y == 0 && _original.Z == 0
                ? Loc["WorldNpc_NoRecordedPosition"]
                : Loc.Format("WorldNpc_LastAtCoords", $"{_original.X:F0}", $"{_original.Y:F0}", $"{_original.Z:F0}");
            return IsPet
                ? $"{Identity} · {location}."
                : Loc.Format("WorldNpc_ContextWithRosterHint", Identity, location);
        }
    }

    public bool IsDead
    {
        get => _isDead;
        private set
        {
            if (_isDead == value) return;
            _isDead = value;
            foreach (var p in new[] { nameof(IsDead), nameof(IsDirty), nameof(StatusText), nameof(CanRevive) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
            _onChanged();
        }
    }

    public string StatusText => _isDead ? Loc["WorldNpc_StatusRemovedDead"] : Loc["WorldNpc_StatusAlive"];

    /// <summary>Only dead entries offer an action - marking living story NPCs dead just deletes content.</summary>
    public bool CanRevive => _isDead;

    /// <summary>
    /// Brings a removed NPC back: alive + the default script phase (state 3, the value
    /// every living fixture NPC carries). Story-scripted departures may reappear.
    /// </summary>
    public void Revive()
    {
        var defaultState = StateOptions.FirstOrDefault(s => s.EndsWith('3'));
        if (defaultState is not null) State = defaultState;
        IsDead = false;
    }

    public string? State
    {
        get => _state;
        set
        {
            if (_state == value || value is null) return;
            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            _onChanged();
        }
    }

    private string _petName;

    /// <summary>The player-given pet name, editable for pets ("Rex"). Staged until SAVE.</summary>
    public string PetName
    {
        get => _petName;
        set
        {
            if (_petName == value) return;
            _petName = value;
            Core.Diagnostics.EditorLog.Info("Edit", $"Pet {Id} renamed: '{_original.CustomName}' -> '{value}'");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PetName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActorName)));
            _onChanged();
        }
    }

    public bool IsDirty => _isDead != _original.IsDead
        || !string.Equals(_state, _original.State, StringComparison.Ordinal)
        || !string.Equals(_petName, _original.CustomName ?? string.Empty, StringComparison.Ordinal);

    public WorldNpc ToCurrent() => _original with
    {
        IsDead = _isDead,
        State = _state,
        CustomName = string.IsNullOrWhiteSpace(_petName) ? null : _petName,
    };

    public void AcceptBaseline()
    {
        _original = ToCurrent();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
    }

    public void Revert()
    {
        IsDead = _original.IsDead;
        State = _original.State;
        PetName = _original.CustomName ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
