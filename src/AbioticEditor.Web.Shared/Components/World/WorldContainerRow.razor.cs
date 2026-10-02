namespace AbioticEditor.Web.Components.World;

/// <summary>
/// What one row of the Containers tab's list shows, prepared once by the tab and kept while the
/// crate is unchanged. A record, so a row rebuilt with the same text compares equal and the tab
/// can hand the row its earlier instance (the row then skips redrawing).
/// </summary>
/// <param name="Name">The player-given name, else what the crate is.</param>
/// <param name="Count">Filled slots out of all slots, e.g. "3/21".</param>
/// <param name="Health">Durability readout (live only), or null.</param>
/// <param name="Where">A glance at what is inside (file) or where it stands (live).</param>
/// <param name="Picture">The rendered picture of the container, or null.</param>
/// <param name="IconId">The placeable item whose icon stands in when there is no picture, or null.</param>
public sealed record WorldContainerRowView(string Name, string Count, string? Health, string Where, string? Picture, string? IconId);
