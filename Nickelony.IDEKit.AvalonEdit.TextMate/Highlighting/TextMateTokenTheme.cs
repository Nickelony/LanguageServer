namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;

/// <summary>
/// Provides the token styling rules used to resolve TextMate scopes into visual styles.
/// </summary>
public sealed record TextMateTokenTheme
{
	private IReadOnlyList<TextMateTokenThemeRule> _rules = [];

	/// <summary>
	/// Gets or initializes the token styling rules of the theme.
	/// The assigned collection is copied, but its rule objects are not cloned. Rules of equal
	/// specificity keep the listed order within one scope push, and a rule with a blank scope
	/// provides the defaults that apply to every token.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public IReadOnlyList<TextMateTokenThemeRule> Rules
	{
		get => _rules;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_rules = Array.AsReadOnly([.. value]);
		}
	}
}
