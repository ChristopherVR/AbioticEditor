using System.Globalization;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Cli;
using AbioticEditor.Plugins.Events;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;
using Jint;
using Jint.Native;

namespace AbioticEditor.Core.Plugins.Scripting;

/// <summary>Small helpers for reading fields off a JS object-literal spec.</summary>
internal static class JsSpec
{
    public static string Str(JsValue spec, string key, string fallback = "")
    {
        var v = spec.AsObject().Get(key);
        return v.IsString() ? v.AsString() : fallback;
    }

    public static bool Bool(JsValue spec, string key, bool fallback = false)
    {
        var v = spec.AsObject().Get(key);
        return v.IsBoolean() ? v.AsBoolean() : fallback;
    }

    public static JsValue Func(JsValue spec, string key)
    {
        var v = spec.AsObject().Get(key);
        if (v.IsUndefined() || v.IsNull())
        {
            throw new InvalidOperationException($"script spec is missing the '{key}' function.");
        }
        return v;
    }

    public static IEnumerable<JsValue> Array(JsValue spec, string key)
    {
        var v = spec.AsObject().Get(key);
        if (!v.IsArray())
        {
            yield break;
        }
        var array = v.AsArray();
        var length = (uint)array.Length;
        for (uint i = 0; i < length; i++)
        {
            yield return array.Get(i.ToString(CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>A save operation whose body is a JS <c>execute(ctx)</c> function.</summary>
internal sealed class JsSaveOperation : ISaveOperation
{
    private readonly JsRuntime _runtime;
    private readonly JsValue _execute;

    private JsSaveOperation(JsRuntime runtime, JsValue execute) => (_runtime, _execute) = (runtime, execute);

    public string Id { get; private init; } = "operation";
    public string DisplayName { get; private init; } = "Operation";
    public string Description { get; private init; } = string.Empty;
    public SaveKind AppliesTo { get; private init; } = SaveKind.Any;
    public IReadOnlyList<SaveOperationParameter> Parameters { get; private init; } = System.Array.Empty<SaveOperationParameter>();

    public static JsSaveOperation FromSpec(JsValue spec, JsRuntime runtime)
    {
        var kindText = JsSpec.Str(spec, "appliesTo", "Any");
        var parameters = JsSpec.Array(spec, "parameters").Select(p => new SaveOperationParameter(
            JsSpec.Str(p, "name"),
            JsSpec.Str(p, "description"),
            JsSpec.Bool(p, "required"),
            JsSpec.Str(p, "default") is { Length: > 0 } d ? d : null)).ToList();

        return new JsSaveOperation(runtime, JsSpec.Func(spec, "execute"))
        {
            Id = JsSpec.Str(spec, "id", "operation"),
            DisplayName = JsSpec.Str(spec, "displayName", "Operation"),
            Description = JsSpec.Str(spec, "description"),
            AppliesTo = Enum.TryParse<SaveKind>(kindText, ignoreCase: true, out var k) ? k : SaveKind.Any,
            Parameters = parameters,
        };
    }

    public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
    {
        var view = new JsSaveContext(context);
        var ret = _runtime.Invoke(_execute, view);

        // Interpret the JS return: nothing -> success with whatever the script changed; a
        // string -> message; an object -> { success, message, changeCount }.
        var success = true;
        var message = view.ChangeCount > 0 ? $"{view.ChangeCount} change(s)." : "no change.";
        var changeCount = view.ChangeCount;

        if (ret.IsString())
        {
            message = ret.AsString();
        }
        else if (ret.IsObject())
        {
            var o = ret.AsObject();
            if (o.Get("success").IsBoolean()) success = o.Get("success").AsBoolean();
            if (o.Get("message").IsString()) message = o.Get("message").AsString();
            if (o.Get("changeCount").IsNumber()) changeCount = (int)o.Get("changeCount").AsNumber();
        }

        return Task.FromResult(success
            ? new SaveOperationResult(true, message, changeCount)
            : SaveOperationResult.Failed(message));
    }
}

/// <summary>The <c>ctx</c> object a JS save operation receives.</summary>
public sealed class JsSaveContext
{
    private readonly ISaveOperationContext _inner;
    private JsPlayerSave? _player;

    internal JsSaveContext(ISaveOperationContext inner) => _inner = inner;

    public string FilePath => _inner.FilePath;
    public string Kind => _inner.Kind.ToString();
    public bool IsDryRun => _inner.IsDryRun;
    public int ChangeCount { get; private set; }

    /// <summary>A typed helper for player saves (<c>ctx.player</c>); null for other kinds.</summary>
    public JsPlayerSave? Player => _inner.Kind == SaveKind.Player
        ? _player ??= new JsPlayerSave(_inner, MarkChanged)
        : null;

    /// <summary><c>ctx.getParameter('name', 'fallback')</c>.</summary>
    public string GetParameter(string name, string? fallback = null)
        => _inner.GetParameter(name, fallback ?? string.Empty);

    /// <summary><c>ctx.log.info(...)</c> style logging via the host.</summary>
    public void LogInfo(string message) => _inner.Log.Info(message ?? string.Empty);

    public void LogWarn(string message) => _inner.Log.Warn(message ?? string.Empty);

    /// <summary>Signals an edit was made (the host writes the file only if this was called).</summary>
    public void MarkChanged()
    {
        ChangeCount++;
        _inner.MarkChanged();
    }
}

/// <summary>
/// A focused, JS-friendly facade for editing a player save (the common case for script
/// "cheats"/fixes). Reads the typed model over the operation's loaded save and applies edits
/// through the host writer, marking the context changed so the runner persists them.
/// </summary>
public sealed class JsPlayerSave
{
    private readonly Action _markChanged;
    private readonly PlayerSaveData _data;

    internal JsPlayerSave(ISaveOperationContext context, Action markChanged)
    {
        _markChanged = markChanged;
        _data = PlayerSaveReader.ReadFrom(context.Save);
    }

    /// <summary>The character's money (get/set).</summary>
    public int Money
    {
        get => _data.Stats.Money;
        set
        {
            if (value == _data.Stats.Money) return;
            PlayerSaveWriter.ApplyStats(_data, _data.Stats with { Money = value });
            _markChanged();
        }
    }

    /// <summary>Number of skills on the save.</summary>
    public int SkillCount => _data.Skills.Count;

    /// <summary>Number of recipes unlocked.</summary>
    public int RecipeCount => _data.Recipes.Count;

    /// <summary>Raises every skill below <paramref name="level"/> to it; returns how many changed.</summary>
    public int SetAllSkillLevels(int level)
    {
        level = Math.Clamp(level, 1, SkillCatalog.MaxLevel);
        var targetXp = SkillCatalog.XpForLevel(level);
        var changed = 0;
        var updated = _data.Skills.Select(s =>
        {
            if (s.Xp >= targetXp) return s;
            changed++;
            return s with { Xp = targetXp };
        }).ToList();

        if (changed > 0)
        {
            PlayerSaveWriter.ApplySkills(_data, updated);
            _markChanged();
        }
        return changed;
    }
}

/// <summary>A console command whose body is a JS <c>invoke(ctx)</c> function.</summary>
internal sealed class JsConsoleCommand : IConsoleCommand
{
    private readonly JsRuntime _runtime;
    private readonly JsValue _invoke;

    private JsConsoleCommand(JsRuntime runtime, JsValue invoke) => (_runtime, _invoke) = (runtime, invoke);

    public string Name { get; private init; } = "command";
    public string Description { get; private init; } = string.Empty;
    public IReadOnlyList<PluginCommandArgument> Arguments { get; private init; } = System.Array.Empty<PluginCommandArgument>();
    public IReadOnlyList<PluginCommandOption> Options { get; private init; } = System.Array.Empty<PluginCommandOption>();

    public static JsConsoleCommand FromSpec(JsValue spec, JsRuntime runtime)
    {
        var args = JsSpec.Array(spec, "arguments").Select(a => new PluginCommandArgument(
            JsSpec.Str(a, "name"), JsSpec.Str(a, "description"), JsSpec.Bool(a, "required", true))).ToList();
        var options = JsSpec.Array(spec, "options").Select(o => new PluginCommandOption(
            JsSpec.Str(o, "name"), JsSpec.Str(o, "description"), JsSpec.Bool(o, "isFlag"), JsSpec.Bool(o, "required"))).ToList();

        return new JsConsoleCommand(runtime, JsSpec.Func(spec, "invoke"))
        {
            Name = JsSpec.Str(spec, "name", "command"),
            Description = JsSpec.Str(spec, "description"),
            Arguments = args,
            Options = options,
        };
    }

    public Task<int> InvokeAsync(IConsoleCommandContext context, CancellationToken cancellationToken = default)
    {
        var view = new JsCommandContext(context);
        var ret = _runtime.Invoke(_invoke, view);
        var exit = ret.IsNumber() ? (int)ret.AsNumber() : 0;
        return Task.FromResult(exit);
    }
}

/// <summary>The <c>ctx</c> object a JS console command receives.</summary>
public sealed class JsCommandContext
{
    private readonly IConsoleCommandContext _inner;

    internal JsCommandContext(IConsoleCommandContext inner) => _inner = inner;

    public string GetArgument(string name, string? fallback = null)
        => _inner.Arguments.TryGetValue(name, out var v) ? v : fallback ?? string.Empty;

    public string GetOption(string name, string? fallback = null)
        => _inner.GetOption(name, fallback ?? string.Empty);

    public bool GetFlag(string name) => _inner.GetFlag(name);

    /// <summary><c>ctx.print(...)</c> writes a line to stdout.</summary>
    public void Print(string message) => _inner.Out.WriteLine(message ?? string.Empty);

    /// <summary><c>ctx.printError(...)</c> writes a line to stderr.</summary>
    public void PrintError(string message) => _inner.Error.WriteLine(message ?? string.Empty);
}

/// <summary>A menu action whose body is a JS <c>invoke(ctx)</c> function.</summary>
internal sealed class JsMenuAction : IMenuAction
{
    private readonly JsRuntime _runtime;
    private readonly JsValue _invoke;

    private JsMenuAction(JsRuntime runtime, JsValue invoke) => (_runtime, _invoke) = (runtime, invoke);

    public string Id { get; private init; } = "action";
    public string Title { get; private init; } = "Action";
    public string? Glyph { get; private init; }
    public string? Group { get; private init; }

    public static JsMenuAction FromSpec(JsValue spec, JsRuntime runtime)
        => new(runtime, JsSpec.Func(spec, "invoke"))
        {
            Id = JsSpec.Str(spec, "id", "action"),
            Title = JsSpec.Str(spec, "title", "Action"),
            Glyph = JsSpec.Str(spec, "glyph") is { Length: > 0 } g ? g : null,
            Group = JsSpec.Str(spec, "group") is { Length: > 0 } gr ? gr : null,
        };

    public Task InvokeAsync(IMenuActionContext context, CancellationToken cancellationToken = default)
    {
        _runtime.Invoke(_invoke, new JsMenuContext(context));
        return Task.CompletedTask;
    }
}

/// <summary>The <c>ctx</c> object a JS menu action receives.</summary>
public sealed class JsMenuContext
{
    private readonly IMenuActionContext _inner;

    internal JsMenuContext(IMenuActionContext inner) => _inner = inner;

    public string? ActiveSavePath => _inner.ActiveSavePath;

    public string? ActiveSaveKind => _inner.ActiveSaveKind?.ToString();

    /// <summary><c>ctx.notify('message')</c> shows a message to the user (fire-and-forget).</summary>
    public void Notify(string message) => _ = _inner.NotifyAsync(message ?? string.Empty);
}

/// <summary>An HTML/React web tool whose page (and optional bridge handler) come from JS.</summary>
internal sealed class JsWebTool : IWebTool
{
    private readonly JsRuntime _runtime;
    private readonly JsValue? _handle;
    private readonly string _html;
    private readonly string _rootDirectory;
    private readonly string _entryFile;

    private JsWebTool(JsRuntime runtime, JsValue? handle, string html, string rootDirectory, string entryFile)
    {
        _runtime = runtime;
        _handle = handle;
        _html = html;
        _rootDirectory = rootDirectory;
        _entryFile = entryFile;
    }

    public string Id { get; private init; } = "web";
    public string Title { get; private init; } = "Web";
    public string? Glyph { get; private init; }

    public static JsWebTool FromSpec(JsValue spec, JsRuntime runtime)
    {
        var handleValue = spec.AsObject().Get("handleMessage");
        var handle = handleValue.IsUndefined() || handleValue.IsNull() ? null : handleValue;

        return new JsWebTool(
            runtime,
            handle,
            JsSpec.Str(spec, "html"),
            JsSpec.Str(spec, "rootDirectory"),
            JsSpec.Str(spec, "entryFile") is { Length: > 0 } e ? e : "index.html")
        {
            Id = JsSpec.Str(spec, "id", "web"),
            Title = JsSpec.Str(spec, "title", "Web"),
            Glyph = JsSpec.Str(spec, "glyph") is { Length: > 0 } g ? g : null,
        };
    }

    public WebToolContent CreateContent(IWebToolContext context)
    {
        if (!string.IsNullOrEmpty(_html))
        {
            return WebToolContent.FromHtml(_html);
        }
        if (!string.IsNullOrEmpty(_rootDirectory))
        {
            return WebToolContent.FromDirectory(_rootDirectory, _entryFile);
        }
        return WebToolContent.FromHtml("<!doctype html><meta charset=utf-8><body>This web tool defined no content.</body>");
    }

    public Task<string?> HandleMessageAsync(string message, IWebToolContext context, CancellationToken cancellationToken = default)
    {
        if (_handle is null)
        {
            return Task.FromResult<string?>(null);
        }
        var ret = _runtime.Invoke(_handle, message, new JsWebToolContext(context));
        return Task.FromResult(ret.IsString() ? ret.AsString() : ret.IsNull() || ret.IsUndefined() ? null : ret.ToString());
    }
}

/// <summary>The <c>ctx</c> object a JS web-tool message handler receives.</summary>
public sealed class JsWebToolContext
{
    private readonly IWebToolContext _inner;

    internal JsWebToolContext(IWebToolContext inner) => _inner = inner;

    public string? ActiveSavePath => _inner.ActiveSavePath;

    public string? ActiveSaveKind => _inner.ActiveSaveKind?.ToString();

    public void LogInfo(string message) => _inner.Host.Log.Info(message ?? string.Empty);

    /// <summary>
    /// A ready-to-return JSON summary of the open player save (money, skills, recipes), or
    /// <c>{}</c> when no player save is open. Lets a web page render stats in one bridge call.
    /// </summary>
    public string PlayerSummaryJson()
    {
        if (_inner.ActiveSave is null || _inner.ActiveSaveKind != SaveKind.Player)
        {
            return "{}";
        }
        try
        {
            var data = PlayerSaveReader.ReadFrom(_inner.ActiveSave);
            var topLevel = data.Skills.Count == 0 ? 0 : data.Skills.Max(s => s.Level);
            var names = SkillCatalog.Fallback;
            var skills = data.Skills
                .OrderByDescending(s => s.Level)
                .Select(s => new
                {
                    name = s.Index >= 0 && s.Index < names.Count ? names[s.Index].DisplayName : $"Skill {s.Index}",
                    level = s.Level,
                    xp = s.Xp,
                });
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                file = _inner.ActiveSavePath is { } p ? Path.GetFileName(p) : null,
                money = data.Stats.Money,
                skillCount = data.Skills.Count,
                recipeCount = data.Recipes.Count,
                topLevel,
                skills,
            });
        }
        catch (Exception ex)
        {
            _inner.Host.Log.Warn($"web tool could not read player save: {ex.Message}");
            return "{}";
        }
    }
}

/// <summary>The <c>event</c> object a JS event handler receives.</summary>
public sealed class JsEventView
{
    private readonly PluginEvent _event;

    internal JsEventView(PluginEvent pluginEvent) => _event = pluginEvent;

    public string Name => _event.Name;

    public string? SavePath => _event.SavePath;

    public string? SaveKind => _event.SaveKind?.ToString();

    /// <summary><c>event.get('key')</c> reads an arbitrary payload value as a string.</summary>
    public string? Get(string key) => _event.Get(key)?.ToString();
}
