using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

[STATestClass]
public sealed class TextMateColorizingTransformerTests
{
	[TestMethod]
	public void ColorizeLine_WithRealModelAndGrammar_AppliesResolvedStyle()
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
			model.SetGrammar(new Registry(registryOptions).LoadGrammar(registryOptions.GetScopeByLanguageId("lua")));

			var editor = new TextEditor { Document = document };
			var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme
			{
				Rules =
				[
					// Matches the root Lua scope present on every token, so the assertion does not
					// depend on grammar details.
					new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }
				]
			});

			var transformer = new TextMateColorizingTransformer(editor.TextArea.TextView, model, resolver);
			editor.TextArea.TextView.LineTransformers.Add(transformer);

			Window window = WPFTestHost.ShowInHostWindow(editor);

			try
			{
				// The paint path must not drive TextMateSharp's tokenizer (the transformer never forces
				// tokenization), so wait for the model's background pass before painting.
				WaitForTokenization(model, lineIndex: 0);

				editor.UpdateLayout();
				editor.TextArea.TextView.Redraw();
				WPFTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);

				Assert.IsTrue(editor.TextArea.TextView.VisualLinesValid);

				VisualLine visualLine = editor.TextArea.TextView.VisualLines.First();
				bool sawThemedForeground = false;

				foreach (VisualLineElement element in visualLine.Elements)
				{
					if (element.TextRunProperties.ForegroundBrush is SolidColorBrush brush
						&& brush.Color == Color.FromRgb(0xFF, 0x00, 0x00))
					{
						sawThemedForeground = true;
						break;
					}
				}

				Assert.IsTrue(sawThemedForeground);
			}
			finally
			{
				window.Close();
				editor.TextArea.TextView.LineTransformers.Remove(transformer);
				transformer.Dispose();
			}
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void Dispose_DetachesModelListener()
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var editor = new TextEditor { Document = document };
			var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme());
			var transformer = new TextMateColorizingTransformer(editor.TextArea.TextView, model, resolver);

			Assert.AreEqual(1, GetModelListenerCount(model));

			transformer.Dispose();

			Assert.AreEqual(0, GetModelListenerCount(model));
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void Dispose_CalledTwice_IsIdempotent()
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var editor = new TextEditor { Document = document };
			var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme());
			var transformer = new TextMateColorizingTransformer(editor.TextArea.TextView, model, resolver);

			transformer.Dispose();
			transformer.Dispose();

			Assert.AreEqual(0, GetModelListenerCount(model));
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void ModelTokensChanged_CoalescesBurstsIntoOneQueuedRedrawAndSkipsItAfterDispose()
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var editor = new TextEditor { Document = document };
			var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme());
			List<Action> queuedRedraws = [];
			int redrawCount = 0;
			var transformer = new TextMateColorizingTransformer(
				editor.TextArea.TextView,
				model,
				resolver,
				action =>
				{
					queuedRedraws.Add(action);
					return null;
				},
				() => redrawCount++);
			var listener = (IModelTokensChangedListener)transformer;
			var tokenChange = new ModelTokensChangedEvent([], model);

			// A burst of notifications coalesces: only the first one of an unqueued burst dispatches.
			listener.ModelTokensChanged(tokenChange);
			listener.ModelTokensChanged(tokenChange);
			listener.ModelTokensChanged(tokenChange);

			Assert.AreEqual(1, queuedRedraws.Count);

			// Running the queued dispatch redraws once and opens the gate for the next burst.
			queuedRedraws[0]();
			listener.ModelTokensChanged(tokenChange);

			Assert.AreEqual(1, redrawCount);
			Assert.AreEqual(2, queuedRedraws.Count);

			// A disposed transformer neither dispatches nor redraws an already queued dispatch.
			transformer.Dispose();
			queuedRedraws[1]();
			listener.ModelTokensChanged(tokenChange);

			Assert.AreEqual(1, redrawCount);
			Assert.AreEqual(2, queuedRedraws.Count);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void ModelTokensChanged_QueueRedrawThrows_ResetsGateAndAllowsNextDispatch()
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var editor = new TextEditor { Document = document };
			var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme());
			List<Action> queuedRedraws = [];
			int queueAttempts = 0;
			var transformer = new TextMateColorizingTransformer(
				editor.TextArea.TextView,
				model,
				resolver,
				action =>
				{
					// Simulate a text view whose dispatcher has already shut down.
					if (queueAttempts++ == 0)
						throw new InvalidOperationException("The dispatcher has shut down.");

					queuedRedraws.Add(action);
					return null;
				},
				() => { });
			var listener = (IModelTokensChangedListener)transformer;
			var tokenChange = new ModelTokensChangedEvent([], model);

			// The failed dispatch must not escape and must not leave the coalescing gate closed.
			listener.ModelTokensChanged(tokenChange);

			Assert.AreEqual(0, queuedRedraws.Count);

			listener.ModelTokensChanged(tokenChange);

			Assert.AreEqual(1, queuedRedraws.Count);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void ModelTokensChanged_AbortedQueuedRedraw_ReopensGateForLaterNotifications()
	{
		var document = new TextDocument("local value = 1\n");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var editor = new TextEditor { Document = document };
			var resolver = new TextMateThemeStyleResolver(new TextMateTokenTheme());
			List<Action> queuedRedraws = [];
			DispatcherOperation? pendingOperation = null;
			int queueAttempts = 0;
			int redrawCount = 0;
			var transformer = new TextMateColorizingTransformer(
				editor.TextArea.TextView,
				model,
				resolver,
				action =>
				{
					queueAttempts++;

					// The first queue attempt hands out a real pending dispatcher operation that the
					// test aborts before the dispatcher can run it, which is what a dispatcher
					// shutdown after acceptance produces; later attempts are merely recorded.
					if (pendingOperation is null)
					{
						pendingOperation = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, action);
						return pendingOperation;
					}

					queuedRedraws.Add(action);
					return null;
				},
				() => redrawCount++);
			var listener = (IModelTokensChangedListener)transformer;
			var tokenChange = new ModelTokensChangedEvent([], model);

			// The queued redraw is aborted while still pending, so its callback never runs.
			listener.ModelTokensChanged(tokenChange);
			pendingOperation!.Abort();

			// The abort reopened the coalescing gate, so the next notification queues a redraw again.
			listener.ModelTokensChanged(tokenChange);

			Assert.AreEqual(2, queueAttempts);
			Assert.AreEqual(1, queuedRedraws.Count);
			Assert.AreEqual(0, redrawCount);

			// Running the requeued dispatch redraws once and reopens the gate for the next burst.
			queuedRedraws[0]();

			Assert.AreEqual(1, redrawCount);

			listener.ModelTokensChanged(tokenChange);

			Assert.AreEqual(3, queueAttempts);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	// The transformer's paint path skips lines the model still considers invalid, so tests wait for the
	// background pass instead of calling ForceTokenization from another thread (driving TextMateSharp's
	// tokenizer concurrently is exactly the corruption the transformer avoids).
	private static void WaitForTokenization(TMModel model, int lineIndex)
	{
		// The wait doubles TextMateSharp's three-second per-line budget as a CI margin; a settled line
		// returns immediately.
		for (int attempt = 0; attempt < 1200; attempt++)
		{
			if (model.GetLineTokens(lineIndex) is not null && !model.IsLineInvalid(lineIndex))
				return;

			Thread.Sleep(5);
		}

		Assert.Fail(
			$"The model did not tokenize line {lineIndex}: " +
			$"invalid={model.IsLineInvalid(lineIndex)} tokenized={model.GetLineTokens(lineIndex) is not null}.");
	}

	// TextMateSharp exposes no public listener surface, so the detach contract is verified through its
	// listener list. The helper fails loudly if the field is ever renamed.
	private static int GetModelListenerCount(TMModel model)
		=> WPFTestHost.GetPrivateField<List<IModelTokensChangedListener>>(model, "listeners").Count;
}
