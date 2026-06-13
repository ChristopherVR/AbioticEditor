using System.ComponentModel;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// One ground item from a world save's <c>DroppedItemMap</c>. Wraps the standard slot VM
/// (no eager icon extraction - there can be 1000+ of these) plus a delete marker.
/// </summary>
public sealed class DroppedItemViewModel : INotifyPropertyChanged
{
    private readonly WorldEditorViewModel _owner;
    private bool _isDeleted;

    public DroppedItemViewModel(WorldEditorViewModel owner, WorldDroppedItem source)
    {
        _owner = owner;
        Source = source;
        Slot = new InventorySlotViewModel(
            InventoryKind.Main, source.Slot,
            GameDataServices.Catalog?.Find(source.Slot.ItemId), iconPath: null);
        DeleteCommand = new RelayCommand(() => IsDeleted = true);
        RestoreCommand = new RelayCommand(() => IsDeleted = false);
    }

    public WorldDroppedItem Source { get; }
    public InventorySlotViewModel Slot { get; }
    public string Id => Source.Id;
    public bool NoDespawn => Source.NoDespawn;

    public string DisplayName => Slot.DisplayName;
    public string SubText => $"{Slot.ItemId} · ×{Slot.Count}" + (NoDespawn ? " · no despawn" : "");

    public ICommand DeleteCommand { get; }
    public ICommand RestoreCommand { get; }

    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted == value) return;
            _isDeleted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeleted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNotDeleted)));
            _owner.OnDroppedItemChanged();
        }
    }

    public bool IsNotDeleted => !_isDeleted;

    public WorldDroppedItem ToCurrent() => Source with { Slot = Slot.ToCurrentSlot() };

    public event PropertyChangedEventHandler? PropertyChanged;
}
