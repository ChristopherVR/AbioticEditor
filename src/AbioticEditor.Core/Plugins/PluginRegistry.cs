using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;
using AbioticEditor.Plugins.Events;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Host-side <see cref="IPluginRegistry"/> that simply collects what a plugin registers
/// during <see cref="IAbioticPlugin.Configure"/>. The collected lists are then attached to
/// the plugin's <see cref="PluginDescriptor"/>. Each entry is duplicate-checked by id so a
/// buggy plugin registering the same operation twice can't shadow itself confusingly.
/// </summary>
internal sealed class PluginRegistry : IPluginRegistry
{
    private readonly string _pluginId;

    public PluginRegistry(string pluginId, IPluginHost host)
    {
        _pluginId = pluginId;
        Host = host;
    }

    public IPluginHost Host { get; }

    public List<ISaveOperation> SaveOperations { get; } = new();

    public List<IConsoleCommand> ConsoleCommands { get; } = new();

    public List<IEditorTool> EditorTools { get; } = new();

    public List<IWebTool> WebTools { get; } = new();

    public List<ISaveUpgrader> SaveUpgraders { get; } = new();

    public List<IMenuAction> MenuActions { get; } = new();

    public List<PluginEventSubscription> EventHandlers { get; } = new();

    public List<PluginLocalization> Localizations { get; } = new();

    public void AddSaveOperation(ISaveOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (SaveOperations.Any(o => string.Equals(o.Id, operation.Id, StringComparison.OrdinalIgnoreCase)))
        {
            Host.Log.Warn($"duplicate save operation id '{operation.Id}' ignored.");
            return;
        }
        SaveOperations.Add(operation);
    }

    public void AddConsoleCommand(IConsoleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ConsoleCommands.Any(c => string.Equals(c.Name, command.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Host.Log.Warn($"duplicate console command '{command.Name}' ignored.");
            return;
        }
        ConsoleCommands.Add(command);
    }

    public void AddEditorTool(IEditorTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (EditorTools.Any(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase)))
        {
            Host.Log.Warn($"duplicate editor tool id '{tool.Id}' ignored.");
            return;
        }
        EditorTools.Add(tool);
    }

    public void AddWebTool(IWebTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (WebTools.Any(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase)))
        {
            Host.Log.Warn($"duplicate web tool id '{tool.Id}' ignored.");
            return;
        }
        WebTools.Add(tool);
    }

    public void AddSaveUpgrader(ISaveUpgrader upgrader)
    {
        ArgumentNullException.ThrowIfNull(upgrader);
        if (SaveUpgraders.Any(u => string.Equals(u.Id, upgrader.Id, StringComparison.OrdinalIgnoreCase)))
        {
            Host.Log.Warn($"duplicate save upgrader id '{upgrader.Id}' ignored.");
            return;
        }
        SaveUpgraders.Add(upgrader);
    }

    public void AddMenuAction(IMenuAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (MenuActions.Any(a => string.Equals(a.Id, action.Id, StringComparison.OrdinalIgnoreCase)))
        {
            Host.Log.Warn($"duplicate menu action id '{action.Id}' ignored.");
            return;
        }
        MenuActions.Add(action);
    }

    public void AddEventHandler(string eventName, Action<PluginEvent> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        EventHandlers.Add(new PluginEventSubscription(eventName, handler));
    }

    public void AddLocalization(string culture, IReadOnlyDictionary<string, string> strings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentNullException.ThrowIfNull(strings);
        if (strings.Count == 0)
        {
            return;
        }
        // Copy so a plugin holding a mutable dictionary can't change the strings after the fact.
        Localizations.Add(new PluginLocalization(
            culture, new Dictionary<string, string>(strings, StringComparer.Ordinal)));
    }
}

/// <summary>One event subscription: the event name and the handler to run when it fires.</summary>
public sealed record PluginEventSubscription(string EventName, Action<PluginEvent> Handler);

/// <summary>A bundle of translations a plugin contributes for one culture (key -> translated text).</summary>
public sealed record PluginLocalization(string Culture, IReadOnlyDictionary<string, string> Strings);
