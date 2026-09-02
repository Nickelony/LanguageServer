namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Computes language-specific indentation decisions for an editor.
/// </summary>
public interface IIndentationPolicy
{
	/// <summary>
	/// Computes the indentation that should be applied to the current line.
	/// </summary>
	/// <param name="context">The indentation inputs for the current line.</param>
	/// <returns>The desired leading whitespace for the current line.</returns>
	string GetDesiredIndentation(in IndentationContext context);
}
