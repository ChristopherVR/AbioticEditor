# Localization

The editor's UI is translatable. English ships in the app; other languages can be added two
ways:

1. **In the app** - a translation compiled into the editor (the built-in `de`, `es`, `fr`).
2. **As a plugin** - a translation shipped separately, with **no app rebuild**. This is the
   community path: a plugin can add a whole new language or override individual strings, and it
   can be pure data (a `.resx` or `.json` file, no code at all).

This page covers both, and how the pieces fit together.

## How it works

Every translatable string has a stable **key** (e.g. `Common_Save`, `PlayerVitals_HealAll`).
The neutral/English values live in `src/AbioticEditor.App/Localization/AppResources.resx`. XAML
references a key through the `{loc:Localize}` markup extension:

```xml
<Label Text="{loc:Localize PlayerVitals_HealAll}" />
```

At runtime `LocalizationResourceManager` resolves a key in this order:

1. A **plugin-contributed** value for the active culture (so a pack can add or override).
2. The built-in `.resx` for the active culture, falling back to neutral/English.
3. The key itself, if nothing supplies it (this only shows if a key is referenced but never
   defined - a test guards against that, see [Testing](#testing)).

Changing the language (Settings -> LANGUAGE) re-resolves every binding live; no restart.

## Contributing a translation in the app

To translate the built-in app into a new language `xx`:

1. Copy `src/AbioticEditor.App/Localization/AppResources.resx` to `AppResources.xx.resx`.
2. Translate every `<value>` (keep the `name` keys unchanged).
3. Add `xx` to the available languages in `src/AbioticEditor.App/Services/LocalizationService.cs`.
4. Build the app - the satellite assembly is produced automatically by the SDK.

Keep these intact while translating (a test enforces some of this):

- **Format placeholders** `{0}`, `{0:F1}`, `{0:D2}` stay exactly as-is.
- **File / tech tokens** (`.bak`, `.sav`, `WorldSave_Facility.sav`, `SaveIdentifier`, `usmap`,
  `XP`, `HP`) and **proper nouns** (`Abiotic Factor`, `GATEPal`, `Steam`, `Leyak`, ...) are not
  translated.
- **ALL-CAPS** UI labels stay all-caps so the layout matches.
- XML-escape values: `&` -> `&amp;`, `<` -> `&lt;`, `>` -> `&gt;`.
- Per repo style, never use the em dash character `—`.

## Contributing a translation as a plugin

A plugin can ship translations without touching the app. Three ways, pick what fits:

### 1. Pure-data pack (no code) - the easy path

A plugin whose `runtime` is `localization` runs **no code**. It is just a manifest plus one or
more resource files. This is the recommended way to share a language.

Layout:

```
MyLanguagePack/
  plugin.json
  strings.it.json      (or strings.it.resx)
```

`plugin.json`:

```json
{
  "id": "com.example.italian-language-pack",
  "name": "Italiano (community language pack)",
  "version": "1.0.0",
  "runtime": "localization",
  "localizations": { "it": "strings.it.json" }
}
```

`strings.it.json` - a flat object of `key: translated text` (any key you omit falls back to
English, so a partial pack is fine):

```json
{
  "Common_Save": "SALVA",
  "Common_Close": "CHIUDI",
  "Header_OpenFolder": "APRI CARTELLA"
}
```

`.resx` is accepted too (the standard `<data name="Key"><value>Text</value></data>` table), so
you can copy `AppResources.resx`, rename it, and translate it. Map each culture to its file in
`localizations`; the file name must be a bare name in the plugin folder (no paths).

Drop the folder into a plugin root (`%LOCALAPPDATA%\AbioticEditor\plugins`, or set
`ABIOTIC_PLUGINS_DIR`) and the language is available on next launch. See the working sample in
`plugins/ItalianLanguagePack/`.

### 2. .NET plugin - contribute at runtime

A managed plugin can register strings in `Configure`:

```csharp
public void Configure(IPluginRegistry registry, IPluginHost host)
{
    registry.AddLocalization("it", new Dictionary<string, string>
    {
        ["Common_Save"] = "SALVA",
        ["Common_Close"] = "CHIUDI",
    });
}
```

Useful when the strings are computed, embedded as a resource, or shipped alongside other
capabilities. A .NET plugin may also declare a `localizations` file map in its manifest, exactly
like a pure-data pack.

### 3. JavaScript plugin - contribute at runtime

```js
abiotic.addLocalization("it", {
  Common_Save: "SALVA",
  Common_Close: "CHIUDI",
});
```

JavaScript plugins need no build step. See [the authoring guide](/plugin-authoring) for the rest
of the `abiotic` API.

## Precedence and culture matching

- Plugin contributions are consulted **before** the built-in table, so a pack can override a
  shipped language key-by-key, or add a brand-new language the app does not ship.
- Lookups fall back from a region culture to its neutral parent: a pack that ships `de` answers a
  `de-DE` or `de-AT` UI.
- Later contributions override earlier ones for the same culture + key (last write wins), so two
  packs touching the same key resolve deterministically by load order.

## Adding new strings to the app

When you add UI text, add it as a key rather than a literal:

1. Add `<data name="MySection_Thing"><value>MY TEXT</value></data>` to `AppResources.resx`.
2. Reference it: `Text="{loc:Localize MySection_Thing}"` (ensure the file's root has
   `xmlns:loc="clr-namespace:AbioticEditor.App.Controls"`).
3. Translate the new key in the shipped locale files.

`tools/loc_extract.py` + `tools/loc_merge_resx.py` automate bulk extraction: they recover the
English from the working-tree diff and merge new keys into `AppResources.resx`, grouped by
prefix.

## Testing

`tests/AbioticEditor.Tests/LocalizationTests.cs` guards the system:

- Every `{loc:Localize Key}` referenced in any XAML has a neutral resource entry (so the UI never
  renders a raw key).
- The neutral resx has no duplicate keys.
- Each shipped locale (`de`, `es`, `fr`) is well-formed, covers every neutral key, and has no
  orphan keys.
- Plugin contributions load and resolve: a `.json` pack, a `.resx` pack, a JavaScript
  `addLocalization`, and manifest validation (the `localization` runtime, path-traversal guards).
