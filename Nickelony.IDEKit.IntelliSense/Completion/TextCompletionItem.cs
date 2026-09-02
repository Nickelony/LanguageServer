namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Represents one shared completion entry that can be rendered by any scripting editor.
/// </summary>
/// <remarks>
/// Optional fields drive optional UI regions. Missing detail, description, resolve callbacks,
/// or protocol text-edit metadata should suppress those behaviors rather than requiring a
/// language-specific completion DTO. The built-in completion filter matches <see cref="InsertText"/>;
/// hosts or custom filters may use <see cref="FilterText"/> instead.
/// </remarks>
public sealed class TextCompletionItem
{
	private readonly Func<CancellationToken, Task<TextCompletionItem>>? _resolveAsync;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionItem"/> class.
	/// </summary>
	/// <param name="label">The display label shown in the completion list.</param>
	/// <param name="insertText">
	/// The text inserted when the item is committed, or <see langword="null"/> or whitespace to use
	/// <paramref name="label"/>.
	/// </param>
	/// <param name="description">
	/// Optional descriptive content for tooltip or detail rendering; non-Markdown text is trimmed.
	/// </param>
	/// <param name="priority">The sort priority used by the completion UI.</param>
	/// <param name="kind">The semantic category used for icons and styling.</param>
	/// <param name="detail">Optional short detail text shown beside the label.</param>
	/// <param name="filterText">Optional alternate text that a host or custom filter can use to match the item.</param>
	/// <param name="isDescriptionMarkdown">Whether <paramref name="description"/> should be rendered as Markdown.</param>
	/// <param name="resolveAsync">An optional asynchronous resolver for lazily loading richer content.</param>
	/// <param name="textEdit">Optional protocol-style insert and replace ranges for custom commit behavior.</param>
	/// <param name="requestDocumentVersion">
	/// The originating document version associated with the item for staleness checks.
	/// </param>
	/// <param name="requestGeneration">
	/// The originating request generation associated with the item for staleness checks.
	/// </param>
	/// <param name="insertCaretOffset">An optional caret position to use after commit.</param>
	public TextCompletionItem(
		string label,
		string? insertText = null,
		string? description = null,
		double priority = 0.0,
		TextCompletionItemKind? kind = null,
		string? detail = null,
		string? filterText = null,
		bool isDescriptionMarkdown = false,
		Func<CancellationToken, Task<TextCompletionItem>>? resolveAsync = null,
		TextCompletionTextEdit? textEdit = null,
		int? requestDocumentVersion = null,
		int? requestGeneration = null,
		int? insertCaretOffset = null)
	{
		Label = label;
		InsertText = string.IsNullOrWhiteSpace(insertText) ? label : insertText;
		Description = string.IsNullOrWhiteSpace(description)
			? null
			: isDescriptionMarkdown
				? description
				: description.Trim();
		Priority = priority;
		Kind = kind ?? TextCompletionItemKind.Generic;
		Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
		FilterText = string.IsNullOrWhiteSpace(filterText) ? label : filterText;
		IsDescriptionMarkdown = isDescriptionMarkdown;
		TextEdit = textEdit;
		RequestDocumentVersion = requestDocumentVersion;
		RequestGeneration = requestGeneration;
		InsertCaretOffset = insertCaretOffset;

		_resolveAsync = resolveAsync;
	}

	/// <summary>
	/// Gets the display label shown in the completion list.
	/// </summary>
	public string Label { get; }

	/// <summary>
	/// Gets the text inserted when the item is committed.
	/// </summary>
	public string InsertText { get; }

	/// <summary>
	/// Gets optional descriptive content for tooltip or detail rendering.
	/// </summary>
	public string? Description { get; }

	/// <summary>
	/// Gets the sort priority used by the completion UI.
	/// </summary>
	public double Priority { get; }

	/// <summary>
	/// Gets the semantic category used for icons and styling.
	/// </summary>
	public TextCompletionItemKind Kind { get; }

	/// <summary>
	/// Gets optional short detail text shown beside or above the label.
	/// </summary>
	public string? Detail { get; }

	/// <summary>
	/// Gets optional alternate text that a host or custom filter can use to match the item.
	/// </summary>
	public string FilterText { get; }

	/// <summary>
	/// Gets a value indicating whether <see cref="Description"/> should be rendered as Markdown.
	/// </summary>
	public bool IsDescriptionMarkdown { get; }

	/// <summary>
	/// Gets the optional protocol-style edit payload used when committing the item.
	/// </summary>
	public TextCompletionTextEdit? TextEdit { get; }

	/// <summary>
	/// Gets the originating document version associated with the item for staleness checks.
	/// </summary>
	public int? RequestDocumentVersion { get; }

	/// <summary>
	/// Gets the originating request generation associated with the item for staleness checks.
	/// </summary>
	public int? RequestGeneration { get; }

	/// <summary>
	/// Gets the optional caret offset to place after commit.
	/// </summary>
	public int? InsertCaretOffset { get; }

	/// <summary>
	/// Gets a value indicating whether this item can be asynchronously resolved.
	/// </summary>
	public bool CanResolve => _resolveAsync is not null;

	/// <summary>
	/// Resolves the item to richer content when a resolve callback is available.
	/// </summary>
	/// <param name="cancellationToken">The cancellation token for the resolve request.</param>
	/// <returns>The resolved completion item.</returns>
	public Task<TextCompletionItem> ResolveAsync(CancellationToken cancellationToken = default)
	{
		return _resolveAsync is null
			? Task.FromResult(this)
			: _resolveAsync(cancellationToken);
	}

	/// <summary>
	/// Creates a copy of the item with a resolve callback attached.
	/// </summary>
	/// <param name="resolveAsync">The resolve callback to attach.</param>
	/// <returns>A completion item with resolve support.</returns>
	public TextCompletionItem WithResolveCallback(Func<CancellationToken, Task<TextCompletionItem>> resolveAsync)
	{
		return new(Label, InsertText, Description, Priority, Kind, Detail, FilterText, IsDescriptionMarkdown,
			resolveAsync, TextEdit, RequestDocumentVersion, RequestGeneration, InsertCaretOffset);
	}

	/// <summary>
	/// Creates a copy stamped with request metadata used by hosts to reject stale completions.
	/// </summary>
	/// <param name="requestDocumentVersion">The originating document version.</param>
	/// <param name="requestGeneration">The originating request generation.</param>
	/// <returns>A completion item stamped with the supplied request context.</returns>
	public TextCompletionItem WithRequestContext(int requestDocumentVersion, int requestGeneration)
	{
		if (RequestDocumentVersion == requestDocumentVersion && RequestGeneration == requestGeneration)
			return this;

		Func<CancellationToken, Task<TextCompletionItem>>? resolveAsync = _resolveAsync is null
			? null
			: async cancellationToken =>
			{
				TextCompletionItem resolvedItem = await _resolveAsync(cancellationToken).ConfigureAwait(false);
				return resolvedItem.WithRequestContext(requestDocumentVersion, requestGeneration);
			};

		return new(Label, InsertText, Description, Priority, Kind, Detail, FilterText,
			IsDescriptionMarkdown, resolveAsync, TextEdit, requestDocumentVersion, requestGeneration, InsertCaretOffset);
	}

	/// <summary>
	/// Creates a copy for commit-time use that removes the protocol-style text-edit payload and
	/// stamps the item with the supplied request context.
	/// </summary>
	/// <param name="requestDocumentVersion">The originating document version.</param>
	/// <param name="requestGeneration">The originating request generation.</param>
	/// <returns>
	/// A completion item without a protocol-style text-edit payload and with the supplied request context.
	/// </returns>
	public TextCompletionItem WithFilteredCommitContext(int requestDocumentVersion, int requestGeneration)
	{
		if (RequestDocumentVersion == requestDocumentVersion
			&& RequestGeneration == requestGeneration
			&& TextEdit is null)
		{
			return this;
		}

		Func<CancellationToken, Task<TextCompletionItem>>? resolveAsync = _resolveAsync is null
			? null
			: async cancellationToken =>
			{
				TextCompletionItem resolvedItem = await _resolveAsync(cancellationToken).ConfigureAwait(false);
				return resolvedItem.WithFilteredCommitContext(requestDocumentVersion, requestGeneration);
			};

		return new(
			Label, InsertText, Description, Priority, Kind, Detail, FilterText, IsDescriptionMarkdown, resolveAsync,
			textEdit: null,
			requestDocumentVersion: requestDocumentVersion,
			requestGeneration: requestGeneration,
			insertCaretOffset: InsertCaretOffset);
	}

	/// <summary>
	/// Creates a copy that merges richer resolved content onto the current item.
	/// </summary>
	/// <param name="resolvedItem">The resolved completion item carrying richer detail and description.</param>
	/// <returns>A merged completion item that preserves the original commit metadata.</returns>
	public TextCompletionItem WithResolvedContent(TextCompletionItem resolvedItem)
	{
		string? detail = string.IsNullOrWhiteSpace(resolvedItem.Detail) ? Detail : resolvedItem.Detail;
		string? description = string.IsNullOrWhiteSpace(resolvedItem.Description) ? Description : resolvedItem.Description;

		bool isDescriptionMarkdown = string.IsNullOrWhiteSpace(resolvedItem.Description)
			? IsDescriptionMarkdown
			: resolvedItem.IsDescriptionMarkdown;

		TextCompletionItemKind kind = resolvedItem.Kind == TextCompletionItemKind.Generic ? Kind : resolvedItem.Kind;

		return new(
			Label, InsertText, description, Priority, kind, detail, FilterText, isDescriptionMarkdown,
			resolveAsync: null,
			textEdit: TextEdit,
			requestDocumentVersion: RequestDocumentVersion,
			requestGeneration: RequestGeneration,
			insertCaretOffset: InsertCaretOffset);
	}
}
