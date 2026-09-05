namespace Nickelony.IDEKit.AvalonEdit.Extras.TextMate.Highlighting;

/// <summary>
/// Provides the token styling rules used to resolve TextMate scopes into visual styles.
/// </summary>
public sealed class TextMateTokenTheme
{
	private IReadOnlyList<TextMateTokenThemeRule> _rules = [];

	/// <summary>
	/// Gets or initializes the token styling rules of the theme.
	/// The assigned collection is copied, but its rule objects are not cloned.
	/// </summary>
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

/// <summary>
/// Describes the visual style associated with one or more TextMate scopes.
/// </summary>
public sealed class TextMateTokenThemeRule
{
	/// <summary>
	/// Gets or sets the TextMate scope selector or comma-separated selectors matched by the rule.
	/// A token scope matches a selector when it equals the selector or extends it with a dot-separated suffix.
	/// </summary>
	public string Scope { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the foreground color of the rule as a WPF-compatible color string.
	/// Leave empty to avoid changing the foreground.
	/// </summary>
	public string Foreground { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the space-separated font styles of the rule.
	/// Recognized, case-sensitive values are <c>bold</c>, <c>italic</c>, <c>underline</c>,
	/// <c>strikethrough</c>, and <c>none</c>.
	/// </summary>
	public string FontStyle { get; set; } = string.Empty;
}
