using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class TextDefinitionNavigationTests
{
	[TestMethod]
	public void TryGoToSymbol_LocationWithoutFilePath_PlacesCaretInColumnWithoutSelecting()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(2, 8));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToSymbol(provider, "bravo", null));

			// The caret is placed in the requested column without leaving the line selected.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(editor.CaretOffset, editor.SelectionStart);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_LocationWithSelectionRange_PlacesCaretAtSelectionStart()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var targetRange = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 15));
		var selectionRange = new TextPositionRange(new TextPosition(1, 7), new TextPosition(1, 12));
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(targetRange, selectionRange: selectionRange));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToSymbol(provider, "bravo", null));

			// The selection range identifies the symbol name inside the target range.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_ColumnBeyondLineEnd_ClampsCaretToLineEnd()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(3, 200));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(3);

			Assert.IsTrue(editor.TextArea.TryGoToSymbol(provider, "charlie", null));

			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_ColumnNearIntMax_ClampsCaretToLineEndWithoutOverflow()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(2, int.MaxValue));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToSymbol(provider, "bravo", null));

			// A column that would overflow the offset addition must still clamp to the line end.
			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_LocationBeyondLastLine_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(99));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToSymbol(provider, "alpha", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_CrossFileLocationWithoutCallback_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(1, 1, @"C:\Sources\source.txt"));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToSymbol(provider, "source", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_CrossFileLocationWithCallback_DelegatesAndPropagatesResult()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation location = LocationAt(4, 9, @"C:\Sources\source.txt");
		var provider = new TestDefinitionProvider(location);
		TextDefinitionLocation? delegatedLocation = null;
		bool callbackResult = false;

		bool TryNavigateCrossFile(TextDefinitionLocation target)
		{
			delegatedLocation = target;
			return callbackResult;
		}

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToSymbol(provider, "source", null, TryNavigateCrossFile));
			Assert.AreSame(location, delegatedLocation);
			Assert.AreEqual(0, editor.CaretOffset);

			callbackResult = true;

			Assert.IsTrue(editor.TextArea.TryGoToSymbol(provider, "source", null, TryNavigateCrossFile));
			Assert.AreSame(location, delegatedLocation);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_EmptySymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(1));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToSymbol(provider, "  ", null));
			Assert.IsNull(provider.LastRequest);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_NonNullDiscriminator_ForwardsItToTheProvider()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var discriminator = new TestDiscriminator("kind");
		var provider = new TestDefinitionProvider(LocationAt(2, 8));

		using (hostWindow)
		{
			Assert.IsTrue(editor.TextArea.TryGoToSymbol(provider, "bravo", discriminator));

			// The language-specific discriminator reaches the provider unchanged, so a provider can
			// disambiguate identical symbol names.
			Assert.IsNotNull(provider.LastRequest);
			Assert.AreSame(discriminator, provider.LastRequest.Discriminator);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_NullProvider_ThrowsArgumentNullException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			Assert.ThrowsExactly<ArgumentNullException>(() => editor.TextArea.TryGoToSymbol(null!, "alpha"));
			Assert.ThrowsExactly<ArgumentNullException>(() => TextDefinitionNavigation.TryGoToSymbol(null!, new TestDefinitionProvider(null), "alpha"));
		}
	}

	[TestMethod]
	public void TryGoToSymbol_OffEditorThread_ThrowsInvalidOperationException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(2, 8));
		Exception? captured = null;

		using (hostWindow)
		{
			// The synchronous form applies navigation on the calling thread and verifies the editor thread
			// up front, so calling it elsewhere fails fast instead of corrupting editor state.
			Task.Run(() =>
			{
				try
				{
					editor.TextArea.TryGoToSymbol(provider, "bravo");
				}
				catch (Exception exception)
				{
					captured = exception;
				}
			}).GetAwaiter().GetResult();

			Assert.IsInstanceOfType<InvalidOperationException>(captured);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_NegativeLine_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(
			new TextPositionRange(new TextPosition(-1, 0), new TextPosition(-1, 0))));

		using (hostWindow)
		{
			// A negative line cannot identify a location and must be rejected instead of clamping to line 1.
			Assert.IsFalse(editor.TextArea.TryGoToSymbol(provider, "alpha", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_NegativeCharacter_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(
			new TextPositionRange(new TextPosition(1, -5), new TextPosition(1, -5))));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToSymbol(provider, "bravo", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToSymbol_CharacterAtIntMaxValue_SaturatesToLineEndWithoutOverflow()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(
			new TextPositionRange(new TextPosition(1, int.MaxValue), new TextPosition(1, int.MaxValue))));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToSymbol(provider, "bravo", null));

			// The one-based column addition saturates, so the caret clamps to the line end instead of
			// overflowing into a negative column.
			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task TryGoToSymbolAsync_LocationWithSelectionRange_PlacesCaretAtSelectionStart()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var targetRange = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 15));
		var selectionRange = new TextPositionRange(new TextPosition(1, 7), new TextPosition(1, 12));
		var location = new TextDefinitionLocation(targetRange, selectionRange: selectionRange);

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(await editor.TextArea.TryGoToSymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(location),
				"bravo"));

			// The asynchronous form resolves through the callback and navigates like the synchronous one.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task TryGoToSymbolAsync_CrossFileLocationWithCallback_DelegatesAndPropagatesResult()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation location = LocationAt(4, 9, @"C:\Sources\source.txt");
		TextDefinitionLocation? delegatedLocation = null;
		bool callbackResult = false;

		using (hostWindow)
		{
			Assert.IsFalse(await editor.TextArea.TryGoToSymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(location),
				"source",
				tryNavigateCrossFileDefinitionAsync: (target, cancellationToken) =>
				{
					delegatedLocation = target;
					return Task.FromResult(callbackResult);
				}));

			Assert.AreSame(location, delegatedLocation);

			callbackResult = true;

			Assert.IsTrue(await editor.TextArea.TryGoToSymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(location),
				"source",
				tryNavigateCrossFileDefinitionAsync: (target, cancellationToken) => Task.FromResult(callbackResult)));

			Assert.AreSame(location, delegatedLocation);
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task TryGoToSymbolAsync_EmptySymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool requested = false;

		using (hostWindow)
		{
			Assert.IsFalse(await editor.TextArea.TryGoToSymbolAsync(
				(request, cancellationToken) =>
				{
					requested = true;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
				},
				"  "));

			Assert.IsFalse(requested);
		}
	}

	[TestMethod]
	public async Task TryGoToSymbolAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.TextArea.TryGoToSymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
				"alpha",
				cancellationToken: cancellationTokenSource.Token));
		}
	}

	[TestMethod]
	public async Task TryGoToSymbolAsync_NullResolver_Throws()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			await Assert.ThrowsExactlyAsync<ArgumentNullException>(
				() => editor.TextArea.TryGoToSymbolAsync(null!, "alpha"));
		}
	}

	[TestMethod]
	public async Task TryGoToSymbolAsync_OffEditorThread_FaultsWithInvalidOperationException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		Exception? captured = null;

		using (hostWindow)
		{
			// The asynchronous form also verifies the editor thread before doing any work; the failure is
			// reported through the returned task.
			Task.Run(() =>
			{
				try
				{
					editor.TextArea.TryGoToSymbolAsync(
						(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(2, 8)),
						"bravo").GetAwaiter().GetResult();
				}
				catch (Exception exception)
				{
					captured = exception;
				}
			}).GetAwaiter().GetResult();

			Assert.IsInstanceOfType<InvalidOperationException>(captured);
		}
	}
}
