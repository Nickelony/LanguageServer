namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Exposes the logical version captured when a text edit target is preflighted.
/// </summary>
public interface ITextEditTargetVersion
{
	/// <summary>
	/// Gets the current target version.
	/// </summary>
	long Version { get; }
}
