namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Applies validated text operations to a host-owned document target.
/// </summary>
public interface ITextEditTarget
{
	/// <summary>
	/// Gets the current target content.
	/// </summary>
	string Text { get; }

	/// <summary>
	/// Applies operations ordered from highest to lowest source offset.
	/// </summary>
	/// <param name="operations">The validated operations to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="operations"/> is <see langword="null"/>.</exception>
	void Apply(IReadOnlyList<TextEditOperation> operations);
}
