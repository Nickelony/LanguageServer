using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>
/// Configures how Markdown tooltip content is rendered and how its links and code blocks are handled.
/// </summary>
/// <remarks>
/// Instances are immutable records. Assigned collections are copied and frozen on assignment, so
/// later changes to an assigned collection are not observed; record equality still compares the
/// copied collection instances by reference, not their contents.
/// </remarks>
public sealed record MarkdownToolTipOptions
{
	// The defaults are shared by every instance; an assigned collection replaces its field with a
	// frozen copy under the same case-insensitive semantics. The static fields precede Default because
	// static initializers run in textual order and Default captures them in its field initializers.
	private static readonly FrozenSet<string> s_defaultSupportedHyperlinkSchemes = FrozenSet.ToFrozenSet<string>(
		[Uri.UriSchemeHttp, Uri.UriSchemeHttps],
		StringComparer.OrdinalIgnoreCase);

	private static readonly FrozenDictionary<string, string> s_defaultHighlightingAliases = FrozenDictionary.ToFrozenDictionary<string, string>(
		[
			new KeyValuePair<string, string>("csharp", ".cs"),
			new KeyValuePair<string, string>("json5", ".json")
		],
		StringComparer.OrdinalIgnoreCase);

	/// <summary>Gets the default rendering options.</summary>
	public static MarkdownToolTipOptions Default { get; } = new();

	private readonly IReadOnlySet<string> _supportedHyperlinkSchemes = s_defaultSupportedHyperlinkSchemes;
	private readonly IReadOnlyDictionary<string, string> _highlightingAliases = s_defaultHighlightingAliases;

	/// <summary>Gets or initializes whether content that exceeds the theme limits may scroll.</summary>
	/// <remarks>
	/// When <see langword="true"/>, a viewer that exceeds <see cref="MarkdownToolTipTheme.MaxHeight"/> or
	/// <see cref="MarkdownToolTipTheme.MaxWidth"/> shows scroll bars, and a code block that exceeds
	/// <see cref="MarkdownToolTipTheme.MaxVisibleCodeBlockLines"/> scrolls inside the block. When
	/// <see langword="false"/>, no part of the content can be scrolled by any input - overflow is
	/// clipped at the theme limits - and the mouse wheel is left to an enclosing host scroller.
	/// </remarks>
	public bool AllowScrolling { get; init; } = true;

	/// <summary>
	/// Gets or initializes the URI schemes that may be opened from hyperlinks.
	/// </summary>
	/// <remarks>
	/// The assigned set is copied and frozen on assignment and always compared case-insensitively, so
	/// later changes to the assigned collection are not observed and the shipped default's semantics
	/// are preserved. The default set allows HTTP and HTTPS links. Email autolinks (for example
	/// <c>&lt;user@example.com&gt;</c>) target <c>mailto:</c> and stay unopenable until the set allows
	/// the <c>mailto</c> scheme.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public IReadOnlySet<string> SupportedHyperlinkSchemes
	{
		get => _supportedHyperlinkSchemes;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_supportedHyperlinkSchemes = FrozenSet.ToFrozenSet(value, StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// Gets or initializes additional highlighting aliases used when resolving a code block language.
	/// Keys are language identifiers taken from a code block info string; values are highlighting
	/// definition names or file extensions understood by AvalonEdit.
	/// </summary>
	/// <remarks>
	/// The assigned map is copied and frozen on assignment and always compared case-insensitively, so
	/// later changes to the assigned collection are not observed and the shipped default's semantics
	/// are preserved. Aliases are consulted only when the language resolves neither as a highlighting
	/// definition name (matched case-insensitively) nor as a file extension. The default map maps
	/// <c>csharp</c> to <c>.cs</c> and <c>json5</c> to <c>.json</c>. Languages whose fence name already
	/// matches an AvalonEdit name or extension (for example <c>python</c>, <c>javascript</c>, <c>cs</c>,
	/// or <c>js</c>) need no alias, and a language AvalonEdit has no definition for (for example
	/// <c>lua</c> or <c>ts</c>) resolves to no highlighting unless the host registers it; use
	/// <see cref="CustomHighlightingInstaller"/> for host-owned definitions.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
	public IReadOnlyDictionary<string, string> HighlightingAliases
	{
		get => _highlightingAliases;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_highlightingAliases = FrozenDictionary.ToFrozenDictionary(value, StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// Gets or initializes a callback that can install custom syntax highlighting on a code block editor.
	/// The renderer invokes the callback before built-in highlighting resolution. Returning
	/// <see langword="true"/> means the callback handled highlighting and skips built-in resolution;
	/// returning <see langword="false"/> uses built-in resolution instead.
	/// </summary>
	/// <remarks>
	/// The callback owns any state or resources it installs. When custom highlighting holds disposable
	/// state (for example a TextMate model with a tokenizer thread), attach its cleanup to the editor's
	/// lifecycle, such as <c>editor.Unloaded</c>; the renderer never disposes host-installed state.
	/// </remarks>
	public Func<AvalonTextEditor, string?, bool>? CustomHighlightingInstaller { get; init; }

	/// <summary>
	/// Gets or initializes a callback that opens a supported hyperlink instead of the default
	/// operating-system shell execution.
	/// </summary>
	/// <remarks>
	/// The callback runs on the UI thread when a hyperlink whose scheme is listed in
	/// <see cref="SupportedHyperlinkSchemes"/> is activated. Return <see langword="true"/> when the
	/// link was handled. A <see langword="false"/> result or an exception counts as not handled and
	/// falls back to the operating system's default protocol handler, exactly like a missing callback;
	/// a host that wants to suppress the activation should return <see langword="true"/> without
	/// opening anything, or exclude the scheme from <see cref="SupportedHyperlinkSchemes"/>. Exceptions
	/// are reported as a warning.
	/// </remarks>
	public Func<Uri, bool>? OpenHyperlink { get; init; }

	/// <summary>
	/// Gets or initializes an optional logger for rendering diagnostics.
	/// </summary>
	/// <remarks>
	/// When set, rendering failures, hyperlink-open failures, and code-block languages that resolve to
	/// no highlighting are reported through it. When <see langword="null"/>, failures are not logged;
	/// rendering still falls back to plain text.
	/// </remarks>
	public ILogger? Logger { get; init; }
}
