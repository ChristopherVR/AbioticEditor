using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.Core.LiveEditing;

/// <summary>
/// What is known about a live session, kept as four independent facts. Each is null when the
/// current protocol does not report it: the <c>hello</c> reply carries only the protocol and
/// agent versions, so the game build, the agent's capability list and the UE4SS runtime version
/// are unknown unless a caller learns them elsewhere (for example the bundled UE4SS manifest).
/// A null is never guessed from the installed game.
/// </summary>
/// <param name="GameBuild">The running game's build string, when something reported it.</param>
/// <param name="AgentProtocolVersion">Wire protocol version from <c>hello</c>.</param>
/// <param name="AgentVersion">Agent mod version from <c>hello</c>.</param>
/// <param name="AgentCapabilities">Capability names the agent advertised, or null when it does not advertise any.</param>
/// <param name="Ue4ssVersion">UE4SS runtime version, when known.</param>
public sealed record LiveCompatibilityInfo(
    string? GameBuild,
    int? AgentProtocolVersion,
    string? AgentVersion,
    IReadOnlyCollection<string>? AgentCapabilities,
    string? Ue4ssVersion);

/// <summary>What a live action needs from the session.</summary>
/// <param name="ProtocolVersion">Protocol version the action's wire commands need.</param>
/// <param name="Capability">Capability the agent must advertise, when the protocol has such a list.</param>
public sealed record LiveActionRequirement(int ProtocolVersion, string? Capability = null);

/// <summary>Live-side counterpart of <see cref="OperationSupport"/>.</summary>
public static class LiveCompatibilityEvaluator
{
    /// <summary>The wire protocol version this editor speaks (see <see cref="TcpLiveGameChannel"/>).</summary>
    public const int EditorProtocolVersion = 1;

    /// <summary>
    /// Decides whether an action may be enabled. Protocol and capability mismatches are
    /// Unsupported; a missing game build, capability list or UE4SS version only makes the
    /// action Unverified, because their absence is a gap in the protocol, not a proven mismatch.
    /// </summary>
    public static AreaVerdict Evaluate(LiveCompatibilityInfo info, LiveActionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(requirement);

        if (info.AgentProtocolVersion is not int protocol)
            return new(OperationSupportLevel.Unsupported, "No live agent has answered the handshake.");
        if (protocol != EditorProtocolVersion || protocol < requirement.ProtocolVersion)
            return new(OperationSupportLevel.Unsupported,
                $"The agent speaks protocol {protocol}; this editor speaks {EditorProtocolVersion} and the action needs {requirement.ProtocolVersion}.");

        if (requirement.Capability is { } capability)
        {
            if (info.AgentCapabilities is null)
                return new(OperationSupportLevel.Unverified,
                    $"The agent does not advertise capabilities, so '{capability}' cannot be confirmed.");
            if (!info.AgentCapabilities.Contains(capability, StringComparer.Ordinal))
                return new(OperationSupportLevel.Unsupported, $"The agent does not offer '{capability}'.");
        }

        if (info.GameBuild is null)
            return new(OperationSupportLevel.Unverified, "The running game's build is not reported, so this action is unverified for it.");
        if (!string.Equals(info.GameBuild, SaveVersionRegistry.ValidatedGameBuild, StringComparison.Ordinal))
            return new(OperationSupportLevel.Unverified,
                $"Game build {info.GameBuild} differs from the validated build {SaveVersionRegistry.ValidatedGameBuild}.");
        if (info.Ue4ssVersion is null)
            return new(OperationSupportLevel.Unverified, "The UE4SS runtime version is not known.");

        return new(OperationSupportLevel.Supported, "Protocol, game build and UE4SS runtime are all known and compatible.");
    }
}

/// <summary>A support level plus its reason, for values that are not tied to a save area.</summary>
/// <param name="Level">Supported, unverified or unsupported.</param>
/// <param name="Reason">Plain-language explanation.</param>
public readonly record struct AreaVerdict(OperationSupportLevel Level, string Reason)
{
    /// <summary>True unless <see cref="OperationSupportLevel.Unsupported"/>.</summary>
    public bool Enabled => Level != OperationSupportLevel.Unsupported;
}
