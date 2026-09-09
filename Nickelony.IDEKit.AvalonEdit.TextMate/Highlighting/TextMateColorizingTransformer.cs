using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows.Media;
using System.Windows.Threading;
using TextMateSharp.Model;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;

/// <summary>
/// Applies styles resolved from TextMate tokens to AvalonEdit document lines as they are rendered.
/// The <see cref="TextMateThemeStyleResolver"/> translates each token's scopes into visual formatting.
/// </summary>
/// <remarks>
/// <para>
/// Colorizing runs on the UI thread while AvalonEdit builds visual lines. Lines that the model has
/// not tokenized yet render with the base style until the model's background tokenizer completes
/// them; the transformer never tokenizes from the paint path because TextMateSharp's tokenizer is not
/// safe to drive from two threads at once. Once tokenization completes, the model raises a
/// token-change notification and the transformer redraws, so a line stays uncolored only while the
/// background pass is behind. TextMateSharp budgets three seconds and 10,000 characters per line for
/// that background pass, so a pathological line can delay its coloring but cannot stall the UI thread.
/// </para>
/// <para>
/// Token-change notifications can arrive from the model's tokenizer thread. They are coalesced into a
/// single queued redraw, and the redraw is skipped when the transformer has been disposed or when the
/// text view's dispatcher has already shut down.
/// </para>
/// <para>
/// Coloring follows the model's background pass in document order; a region scrolled into view can
/// stay with the base style until the pass reaches it, because the transformer does not prioritize
/// the visible viewport.
/// </para>
/// <para>
/// Text decorations are applied with AvalonEdit's <c>SetTextDecorations</c> semantics, which union the
/// requested decorations with the decorations already present on the element. Hosts that stack several
/// colorizing transformers should account for that behavior.
/// </para>
/// <para>
/// Disposing removes the token-change listener but does not remove the transformer from
/// <see cref="TextView.LineTransformers"/>; the host owns that removal. TextMateSharp stops the
/// model's tokenizer thread when the model loses its last listener, so a host that disposes a
/// transformer while the editor remains visible should keep another listener or dispose the model.
/// </para>
/// </remarks>
public sealed class TextMateColorizingTransformer : DocumentColorizingTransformer, IDisposable, IModelTokensChangedListener
{
	private const int MaxTypefaceCacheEntryCount = 64;

	private readonly TMModel _model;
	private readonly TextMateThemeStyleResolver _styleResolver;
	private readonly Func<Action, DispatcherOperation?> _queueRedraw;
	private readonly Action _redraw;
	private readonly Dictionary<(Rendering.TextRunStyle Style, Typeface BaseTypeface), Typeface> _typefaceCache = [];
	private bool _isDisposed;
	private int _redrawQueued;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateColorizingTransformer"/> class.
	/// </summary>
	/// <param name="textView">The text view to redraw when tokens change.</param>
	/// <param name="model">The TextMate model providing per-line tokens.</param>
	/// <param name="styleResolver">The resolver translating token scopes into visual styles.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textView"/>, <paramref name="model"/>, or <paramref name="styleResolver"/> is <see langword="null"/>.
	/// </exception>
	public TextMateColorizingTransformer(TextView textView, TMModel model, TextMateThemeStyleResolver styleResolver)
		: this(
			textView,
			model,
			styleResolver,
			action => textView.Dispatcher.BeginInvoke(action),
			() => textView.Redraw())
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="TextMateColorizingTransformer"/> class with an
	/// explicit redraw dispatch, so tests can observe the coalescing behavior that a real text view
	/// does not expose (<see cref="TextView.Redraw()"/> is not virtual).
	/// </summary>
	/// <param name="textView">The text view to redraw when tokens change.</param>
	/// <param name="model">The TextMate model providing per-line tokens.</param>
	/// <param name="styleResolver">The resolver translating token scopes into visual styles.</param>
	/// <param name="queueRedraw">
	/// Queues a redraw action and returns the queued dispatcher operation, or <see langword="null"/> when
	/// the queue cannot report one; the public constructor dispatches it to the text view's dispatcher.
	/// </param>
	/// <param name="redraw">Redraws the text view; the public constructor invokes <see cref="TextView.Redraw()"/>.</param>
	internal TextMateColorizingTransformer(
		TextView textView,
		TMModel model,
		TextMateThemeStyleResolver styleResolver,
		Func<Action, DispatcherOperation?> queueRedraw,
		Action redraw)
	{
		ArgumentNullException.ThrowIfNull(textView);
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(styleResolver);
		ArgumentNullException.ThrowIfNull(queueRedraw);
		ArgumentNullException.ThrowIfNull(redraw);

		_model = model;
		_styleResolver = styleResolver;
		_queueRedraw = queueRedraw;
		_redraw = redraw;

		_model.AddModelTokensChangedListener(this);
	}

	/// <inheritdoc/>
	protected override void ColorizeLine(DocumentLine line)
	{
		if (Volatile.Read(ref _isDisposed))
			return;

		int lineIndex = Math.Max(0, line.LineNumber - 1);
		List<TMToken> tokens = _model.GetLineTokens(lineIndex);

		// A line that is not tokenized yet stays with the base style for this paint. Forcing the
		// tokenization here would drive TextMateSharp's tokenizer from the paint thread while the
		// model's tokenizer thread may run the same tokenizer, which is not thread-safe; the model's
		// token-change notification queues a redraw once the background pass has tokenized the line.
		if (tokens is null || _model.IsLineInvalid(lineIndex))
			return;

		if (tokens.Count == 0)
			return;

		int lineLength = line.Length;

		for (int i = 0; i < tokens.Count; i++)
		{
			TMToken token = tokens[i];
			int startIndex = ClampToLine(token.StartIndex, lineLength);
			int endIndex = i + 1 < tokens.Count
				? ClampToLine(tokens[i + 1].StartIndex, lineLength)
				: lineLength;

			if (endIndex <= startIndex)
				continue;

			Rendering.TextRunStyle style = _styleResolver.Resolve(token.Scopes);

			if (!style.HasFormatting)
				continue;

			int startOffset = line.Offset + startIndex;
			int endOffset = line.Offset + endIndex;

			ChangeLinePart(startOffset, endOffset, element => ApplyStyle(element, style));
		}
	}

	/// <summary>
	/// Stops listening for token changes from the TextMate model.
	/// The transformer is not removed from <see cref="TextView.LineTransformers"/>; the host owns that removal.
	/// </summary>
	public void Dispose()
	{
		if (Volatile.Read(ref _isDisposed))
			return;

		Volatile.Write(ref _isDisposed, true);
		_model.RemoveModelTokensChangedListener(this);
	}

	void IModelTokensChangedListener.ModelTokensChanged(ModelTokensChangedEvent e)
	{
		if (Volatile.Read(ref _isDisposed))
			return;

		// Coalesce bursts of token-change notifications into a single queued redraw. This listener may be
		// invoked from the model's tokenizer thread, so the gate must be thread-safe.
		if (Interlocked.Exchange(ref _redrawQueued, 1) != 0)
			return;

		// Queue the redraw so it does not run while AvalonEdit is building visual lines.
		DispatcherOperation? operation;

		try
		{
			operation = _queueRedraw(() =>
			{
				Interlocked.Exchange(ref _redrawQueued, 0);

				if (!Volatile.Read(ref _isDisposed))
					_redraw();
			});
		}
		catch (Exception exception) when (exception is InvalidOperationException or TaskCanceledException)
		{
			// The view's dispatcher can be unavailable or shut down when the host tears the view down
			// before disposing the transformer. Drop the redraw and reopen the gate; nothing repaints
			// after shutdown anyway, and the next notification would only fail again.
			Interlocked.Exchange(ref _redrawQueued, 0);
			return;
		}

		if (operation is not null)
		{
			// A dispatcher that shuts down after accepting the callback aborts it without running it,
			// which would leave the coalescing gate closed; reopening it on abort keeps later
			// notifications able to queue again. The status is re-checked after subscribing because an
			// operation can abort between being queued and the handler being attached, in which case the
			// event never fires.
			operation.Aborted += (_, _) => Interlocked.Exchange(ref _redrawQueued, 0);

			if (operation.Status == DispatcherOperationStatus.Aborted)
				Interlocked.Exchange(ref _redrawQueued, 0);
		}
	}

	private static int ClampToLine(int index, int lineLength)
		=> Math.Max(0, Math.Min(index, lineLength));

	private void ApplyStyle(VisualLineElement element, Rendering.TextRunStyle style)
	{
		// The applier sets the typeface only when the style requests bold or italic text; the cached
		// derived typeface is resolved on that path only.
		if (style.IsBold || style.IsItalic)
		{
			TextRunStyleApplier.Apply(element, style, GetTypeface(style, element.TextRunProperties.Typeface));
			return;
		}

		TextRunStyleApplier.Apply(element, style);
	}

	private Typeface GetTypeface(Rendering.TextRunStyle style, Typeface baseTypeface)
	{
		var key = (style, baseTypeface);

		if (_typefaceCache.TryGetValue(key, out Typeface? typeface))
			return typeface;

		typeface = style.CreateTypeface(baseTypeface);

		// Bounds the cache for hosts that assign many distinct typefaces to visual line elements; the
		// combinations observed while painting a view are few.
		if (_typefaceCache.Count >= MaxTypefaceCacheEntryCount)
			_typefaceCache.Clear();

		_typefaceCache.Add(key, typeface);
		return typeface;
	}
}
