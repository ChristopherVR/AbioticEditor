using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Steam;

namespace AbioticEditor.Web.Services;

/// <summary>Reports offline Steam integration state. The editor never collects Steam credentials.</summary>
public sealed class SteamStatusService
{
    public SteamStatus GetStatus()
    {
        var path = AfInstallLocator.FindSteamPath();
        var accounts = SteamPersonaIndex.LoadMachineAccounts();
        var loginUsersPath = path is null ? null : Path.Combine(path, "config", "loginusers.vdf");
        return new SteamStatus(path, loginUsersPath, accounts.OrderBy(account => account.Value, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

public sealed record SteamStatus(string? SteamPath, string? LoginUsersPath, IReadOnlyList<KeyValuePair<string, string>> Accounts)
{
    public bool SteamInstalled => !string.IsNullOrWhiteSpace(SteamPath);
    public bool LocalAccountCacheAvailable => Accounts.Count > 0;
}
