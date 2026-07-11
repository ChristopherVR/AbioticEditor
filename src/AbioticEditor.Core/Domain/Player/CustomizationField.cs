namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// One appearance choice from a <c>ScientistCustomization_&lt;slot&gt;.sav</c> file.
/// </summary>
/// <param name="PropertyName">Exact save property name, e.g. <c>Customization_Head</c>
/// (note <c>customization_beard</c> is lowercase in the save - match case-insensitively).</param>
/// <param name="Label">Human label for the editor, e.g. "Head".</param>
/// <param name="TableName">The DataTable the value is a row of, e.g. <c>DT_Customization_Head</c>.</param>
/// <param name="CurrentValue">The chosen row name, e.g. <c>Head_M01a</c>.</param>
public sealed record CustomizationField(
    string PropertyName,
    string Label,
    string TableName,
    string CurrentValue);
