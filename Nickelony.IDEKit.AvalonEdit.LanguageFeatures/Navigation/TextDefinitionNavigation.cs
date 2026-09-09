using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;

/// <summary>
/// Executes definition navigation for AvalonEdit <see cref="TextArea"/> instances using IntelliSense hover and
/// definition providers.
/// </summary>
/// <remarks>
/// <para>
/// A location without a document identifier navigates the supplied text area by moving the caret to the start of its
/// selection range (or of its target range when no selection range is present; the shared
/// <see cref="TextDefinitionLocation.NavigationStart"/> applies that rule), clamped to the end of that line,
/// without selecting text. A location that names another document is never applied to this editor: it is
/// reported through the caller's optional cross-file callback, or reported as not navigated when the
/// callback is omitted, because opening the target document is owned by the host, and all entry points
/// share that delegation. A host assigns the document before
/// invoking these helpers, which access <see cref="TextArea.Document"/> directly; a text area whose document
/// was cleared (or never assigned) is not a supported state for these helpers. A host that drives an
/// AvalonEdit <c>TextEditor</c> passes <c>editor.TextArea</c> to these helpers.
/// </para>
/// <para>
/// Position validation is strict: a location whose start line or character is negative or whose line is
/// beyond the document is reported as not navigated, and so is a provider that returned no location.
/// </para>
/// <para>
/// The provider contract supplies zero-based positions, so out-of-document values are treated as absent rather
/// than silently moved. A column past the end of its line is the one tolerance: it is clamped to the line end by
/// the base navigation helper.
/// </para>
/// <para>
/// Both synchronous and asynchronous forms are available. The synchronous form invokes in-memory providers
/// directly on the calling thread; the asynchronous form awaits resolver delegates and is the right choice
/// whenever a provider may perform I/O or blocking work (for example an LSP-backed provider). Both forms apply
/// the resolved definition navigation on the thread that owns the text area, so they verify up front that the
/// caller is on that thread and fail fast with the dispatcher's threading error otherwise. The asynchronous form
/// marshals its resolver calls and the applied definition navigation back to that thread explicitly, so the
/// guarantee also holds when the awaiting thread captured no synchronization context.
/// </para>
/// <para>
/// Both a hover-first form (for name-catalog providers that resolve definitions by symbol) and an offset-first
/// form (for position-based providers such as an LSP-backed resolver) are available; the offset-first form
/// receives the document snapshot text and the zero-based offset directly.
/// </para>
/// </remarks>
public static class TextDefinitionNavigation
{
	/// <summary>
	/// Resolves the symbol at the specified offset through the hover provider and navigates to its definition.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="hoverProvider">The provider that resolves the hovered symbol.</param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinition">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <returns><see langword="true"/> when a definition location was found and the definition navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="definitionProvider"/>, or <paramref name="hoverProvider"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public static bool TryGoToDefinition(
		this TextArea textArea,
		ITextDefinitionProvider definitionProvider,
		ITextHoverProvider hoverProvider,
		int offset,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionProvider);
		ArgumentNullException.ThrowIfNull(hoverProvider);

		textArea.Dispatcher.VerifyAccess();

		if (offset < 0 || offset > textArea.Document.TextLength)
			return false;

		// The document text is materialized once per definition navigation so the hover and definition requests
		// evaluate the same snapshot.
		string text = textArea.Document.Text;
		TextHoverInfo? hoverInfo = hoverProvider.GetHoverInfo(new TextHoverRequest(text, offset));

		if (hoverInfo is null || hoverInfo.SymbolName is null)
			return false;

		return TryGoToSymbolFromText(
			textArea,
			definitionProvider,
			text,
			hoverInfo.SymbolName,
			hoverInfo.DefinitionDiscriminator,
			tryNavigateCrossFileDefinition);
	}

	/// <summary>
	/// Resolves the definition at the specified offset through an offset-based definition resolver and
	/// navigates to it.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolver">
	/// The callback that resolves a definition location from the document snapshot text and a zero-based
	/// document offset, or <see langword="null"/> when no definition can be resolved.
	/// </param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinition">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <returns><see langword="true"/> when a definition location was found and the definition navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The offset-first entry point for position-based providers - for example an LSP-backed resolver that
	/// answers a definition request for a document position - that resolve a location without a hover or symbol
	/// step. The resolver receives the document snapshot text and the zero-based offset; converting the offset
	/// to provider coordinates (for example a line and column) is part of the resolver, because the coordinate
	/// convention belongs to the provider protocol. <see cref="TryGoToDefinition(TextArea, ITextDefinitionProvider, ITextHoverProvider, int, Func{TextDefinitionLocation, bool}?)"/>
	/// remains the convenience entry point for name-catalog providers that resolve definitions by symbol.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionResolver"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public static bool TryGoToDefinitionAtOffset(
		this TextArea textArea,
		Func<string, int, TextDefinitionLocation?> definitionResolver,
		int offset,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolver);

		textArea.Dispatcher.VerifyAccess();

		if (offset < 0 || offset > textArea.Document.TextLength)
			return false;

		// The document text is materialized once per definition navigation so the resolver evaluates the same
		// snapshot the offset addresses.
		string text = textArea.Document.Text;
		TextDefinitionLocation? location = definitionResolver(text, offset);

		return location is not null
			&& TryNavigate(textArea, location, tryNavigateCrossFileDefinition);
	}

	/// <summary>
	/// Resolves and navigates to the definition of the supplied symbol name.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="symbolName">The name of the symbol whose definition is resolved.</param>
	/// <param name="discriminator">The optional language-specific discriminator that disambiguates the symbol's definition.</param>
	/// <param name="tryNavigateCrossFileDefinition">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <returns><see langword="true"/> when a definition location was found and the definition navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionProvider"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public static bool TryGoToSymbol(
		this TextArea textArea,
		ITextDefinitionProvider definitionProvider,
		string? symbolName,
		TextDefinitionDiscriminator? discriminator = null,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionProvider);

		textArea.Dispatcher.VerifyAccess();

		if (string.IsNullOrWhiteSpace(symbolName))
			return false;

		return TryGoToSymbolFromText(
			textArea,
			definitionProvider,
			textArea.Document.Text,
			symbolName,
			discriminator,
			tryNavigateCrossFileDefinition);
	}

	private static bool TryGoToSymbolFromText(
		TextArea textArea,
		ITextDefinitionProvider definitionProvider,
		string text,
		string symbolName,
		TextDefinitionDiscriminator? discriminator,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition)
	{
		var request = new TextDefinitionRequest(text, symbolName, discriminator);
		TextDefinitionLocation? location = definitionProvider.GetDefinition(request);

		return location is not null && TryNavigate(textArea, location, tryNavigateCrossFileDefinition);
	}

	private static bool TryNavigate(
		TextArea textArea,
		TextDefinitionLocation location,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition)
	{
		if (location.DocumentId is not null)
			return tryNavigateCrossFileDefinition?.Invoke(location) ?? false;

		return NavigateWithinDocument(textArea, location);
	}

	/// <summary>
	/// Resolves the symbol at the specified offset through an asynchronous hover resolver and navigates to its
	/// definition.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="hoverResolverAsync">The callback that resolves the hovered symbol asynchronously.</param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <param name="cancellationToken">A token that propagates cancellation to the resolvers.</param>
	/// <returns><see langword="true"/> when a definition location was found and the definition navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The asynchronous counterpart of <see cref="TryGoToDefinition(TextArea, ITextDefinitionProvider, ITextHoverProvider, int, Func{TextDefinitionLocation, bool}?)"/>
	/// for providers that may perform I/O or blocking work. The document text is materialized once per definition
	/// navigation, so both resolvers evaluate the same snapshot; resolver calls and the applied definition
	/// navigation are marshalled back to the thread that owns the text area explicitly (see the type remarks).
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="definitionResolverAsync"/>, or
	/// <paramref name="hoverResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<bool> TryGoToDefinitionAsync(
		this TextArea textArea,
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		Func<TextHoverRequest, CancellationToken, Task<TextHoverInfo?>> hoverResolverAsync,
		int offset,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentNullException.ThrowIfNull(hoverResolverAsync);

		textArea.Dispatcher.VerifyAccess();
		cancellationToken.ThrowIfCancellationRequested();

		if (offset < 0 || offset > textArea.Document.TextLength)
			return false;

		string text = textArea.Document.Text;
		TextHoverInfo? hoverInfo = await DispatcherInvocation.Run(
			textArea.Dispatcher,
			() => hoverResolverAsync(new TextHoverRequest(text, offset), cancellationToken)).ConfigureAwait(true);

		if (hoverInfo is null || hoverInfo.SymbolName is null)
			return false;

		return await TryGoToSymbolFromTextAsync(
			textArea,
			definitionResolverAsync,
			text,
			hoverInfo.SymbolName,
			hoverInfo.DefinitionDiscriminator,
			tryNavigateCrossFileDefinitionAsync,
			cancellationToken).ConfigureAwait(true);
	}

	/// <summary>
	/// Resolves the definition at the specified offset through an asynchronous offset-based definition
	/// resolver and navigates to it.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolverAsync">
	/// The callback that resolves a definition location asynchronously from the document snapshot text and a
	/// zero-based document offset, or <see langword="null"/> when no definition can be resolved.
	/// </param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <param name="cancellationToken">A token that propagates cancellation to the resolver.</param>
	/// <returns><see langword="true"/> when a definition location was found and the definition navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The asynchronous counterpart of <see cref="TryGoToDefinitionAtOffset(TextArea, Func{string, int, TextDefinitionLocation?}, int, Func{TextDefinitionLocation, bool}?)"/>
	/// for providers that may perform I/O or blocking work. The document text is materialized once per definition
	/// navigation, so the resolver evaluates the same snapshot the offset addresses; resolver calls and the
	/// applied definition navigation are marshalled back to the thread that owns the text area explicitly (see the
	/// type remarks).
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<bool> TryGoToDefinitionAtOffsetAsync(
		this TextArea textArea,
		Func<string, int, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		int offset,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);

		textArea.Dispatcher.VerifyAccess();
		cancellationToken.ThrowIfCancellationRequested();

		if (offset < 0 || offset > textArea.Document.TextLength)
			return false;

		string text = textArea.Document.Text;
		TextDefinitionLocation? location = await DispatcherInvocation.Run(
			textArea.Dispatcher,
			() => definitionResolverAsync(text, offset, cancellationToken)).ConfigureAwait(true);

		return location is not null
			&& await TryNavigateAsync(textArea, location, tryNavigateCrossFileDefinitionAsync, cancellationToken).ConfigureAwait(true);
	}

	/// <summary>
	/// Resolves and navigates to the definition of the supplied symbol name through an asynchronous definition
	/// resolver.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="symbolName">The name of the symbol whose definition is resolved.</param>
	/// <param name="discriminator">The optional language-specific discriminator that disambiguates the symbol's definition.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <param name="cancellationToken">A token that propagates cancellation to the resolver.</param>
	/// <returns><see langword="true"/> when a definition location was found and the definition navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The asynchronous counterpart of <see cref="TryGoToSymbol(TextArea, ITextDefinitionProvider, string?, TextDefinitionDiscriminator?, Func{TextDefinitionLocation, bool}?)"/>
	/// for providers that may perform I/O or blocking work; resolver calls and the applied definition navigation
	/// are marshalled back to the thread that owns the text area explicitly (see the type remarks).
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<bool> TryGoToSymbolAsync(
		this TextArea textArea,
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		string? symbolName,
		TextDefinitionDiscriminator? discriminator = null,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);

		textArea.Dispatcher.VerifyAccess();
		cancellationToken.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(symbolName))
			return false;

		return await TryGoToSymbolFromTextAsync(
			textArea,
			definitionResolverAsync,
			textArea.Document.Text,
			symbolName,
			discriminator,
			tryNavigateCrossFileDefinitionAsync,
			cancellationToken).ConfigureAwait(true);
	}

	private static async Task<bool> TryGoToSymbolFromTextAsync(
		TextArea textArea,
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		string text,
		string symbolName,
		TextDefinitionDiscriminator? discriminator,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync,
		CancellationToken cancellationToken)
	{
		var request = new TextDefinitionRequest(text, symbolName, discriminator);
		TextDefinitionLocation? location = await DispatcherInvocation.Run(
			textArea.Dispatcher,
			() => definitionResolverAsync(request, cancellationToken)).ConfigureAwait(true);

		return location is not null
			&& await TryNavigateAsync(textArea, location, tryNavigateCrossFileDefinitionAsync, cancellationToken).ConfigureAwait(true);
	}

	private static async Task<bool> TryNavigateAsync(
		TextArea textArea,
		TextDefinitionLocation location,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync,
		CancellationToken cancellationToken)
	{
		if (location.DocumentId is not null)
		{
			return tryNavigateCrossFileDefinitionAsync is not null
				&& await DispatcherInvocation.Run(
					textArea.Dispatcher,
					() => tryNavigateCrossFileDefinitionAsync(location, cancellationToken)).ConfigureAwait(true);
		}

		return DispatcherInvocation.Run(textArea.Dispatcher, () => NavigateWithinDocument(textArea, location));
	}

	private static bool NavigateWithinDocument(TextArea textArea, TextDefinitionLocation location)
	{
		// The shared record applies the documented navigation rule: the selection range's start when present,
		// otherwise the target range's start.
		TextPosition start = location.NavigationStart;

		// The provider contract uses zero-based positions; a negative line or character cannot identify a
		// location, so it is rejected symmetrically with the upper bounds instead of being clamped.
		if (start.Line < 0 || start.Line >= textArea.Document.LineCount || start.Character < 0)
			return false;

		// CreateCaretLocation clamps the line and column and creates a zero-length selection, so the
		// caret is placed without selecting the line and typing after a definition navigation replaces nothing. The
		// column addition saturates so a start character at int.MaxValue still clamps to the line end instead of
		// overflowing into a negative column. The file path is only carried for context; without one, the
		// document's own path (or an empty path for an unsaved document) is recorded.
		NavigationLocation navigationLocation = TextAreaNavigationOperations.CreateCaretLocation(
			textArea,
			textArea.Document.FileName ?? string.Empty,
			new TextLocation(start.Line + 1, (int)Math.Min((long)start.Character + 1, int.MaxValue)));

		textArea.ApplyLocation(navigationLocation);
		return true;
	}
}
