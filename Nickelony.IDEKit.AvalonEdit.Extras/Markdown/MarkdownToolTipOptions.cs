using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Extras.Markdown;

/// <summary>
/// Configures how Markdown tooltip content is rendered and how its links and code blocks are handled.
/// </summary>
public sealed record MarkdownToolTipOptions
{
	/// <summary>Gets the default rendering options.</summary>
	public static MarkdownToolTipOptions Default { get; } = new();

	/// <summary>Gets or initializes whether content that exceeds the theme limits may scroll.</summary>
	public bool AllowScrolling { get; init; } = true;

	/// <summary>
	/// Gets or initializes the URI schemes that may be opened from hyperlinks.
	/// The default set allows HTTP and HTTPS links and uses case-insensitive comparison. A custom set controls
	/// its own comparison behavior.
	/// </summary>
	public IReadOnlySet<string> SupportedHyperlinkSchemes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		Uri.UriSchemeHttp,
		Uri.UriSchemeHttps
	};

	/// <summary>
	/// Gets or initializes a callback that can install custom syntax highlighting on a code block editor.
	/// The renderer invokes the callback before built-in highlighting resolution. Returning
	/// <see langword="true"/> skips built-in resolution; returning <see langword="false"/> uses it instead.
	/// </summary>
	public Func<AvalonTextEditor, string?, bool>? InstallCustomHighlighting { get; init; }
}
