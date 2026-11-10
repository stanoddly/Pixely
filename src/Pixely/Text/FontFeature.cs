namespace Pixely.Text;

/// <summary>
/// An OpenType feature that shaping applies: <c>new FontFeature("tnum")</c> turns tabular figures on, and
/// <c>new FontFeature("liga", 0)</c> turns standard ligatures off. The tag is the feature's four-letter OpenType tag. A value
/// above 1 selects an alternate, as in <c>new FontFeature("salt", 2)</c>.
/// </summary>
public readonly record struct FontFeature(string Tag, int Value = 1);
