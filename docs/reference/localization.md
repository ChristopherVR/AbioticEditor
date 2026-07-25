# Localization

The Razor host is the single user interface. Its localization is split by purpose:

- `src/AbioticEditor.Web/Localization/AppResources.resx` contains the neutral English editor catalog.
- `AppResources.de.resx`, `AppResources.es.resx`, `AppResources.fr.resx`, and `AppResources.ru.resx` contain shipped translations.
- `HostLanguageService` provides host-specific navigation and formatting strings and resolves the RESX catalog.
- Plugins may contribute strings through `PluginLocalizations`; plugin values override bundled values for the requested culture.

Add a user-facing editor string to the neutral RESX file first, add the same key to every shipped locale, then render it with `HostLanguageService.Resource`. Host-only strings use `Text`, `Detail`, or `Inventory` according to their existing catalog.

Do not hard-code host prose in Razor components. Dynamic game-data labels, item rows, actor names, coordinates, and save values are not localization resources.

Run the localization coverage tests after changing any catalog:

```console
dotnet test tests/AbioticEditor.Tests --filter FullyQualifiedName~HostLanguageServiceTests
```

Language selection is persisted locally. Missing plugin or locale-specific values fall back to the neutral English resource.
