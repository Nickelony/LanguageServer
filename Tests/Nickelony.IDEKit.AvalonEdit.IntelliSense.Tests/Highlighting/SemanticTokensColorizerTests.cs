using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.IntelliSense.Highlighting;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Tests;

/// <summary>
/// Tests for <see cref="SemanticTokensColorizer"/> token replacement and clearing, style changes,
/// range handling, modifier handling, document changes, and large token sets.
/// </summary>
[TestClass]
public class SemanticTokensColorizerTests
{
	private sealed class TestSemanticTokenStyleResolver : ISemanticTokenStyleResolver
	{
		private Brush? _foreground = Brushes.Red;
		private bool _isBold;
		private string? _boldModifier;

		public void SetStyle(Brush? foreground, bool isBold = false, string? boldModifier = null)
		{
			_foreground = foreground;
			_isBold = isBold;
			_boldModifier = boldModifier;
		}

		public SemanticTokenStyle Resolve(TextSemanticToken token)
		{
			bool isBold = _isBold || (_boldModifier is not null && token.HasModifier(_boldModifier));
			return new SemanticTokenStyle(_foreground, isBold, null);
		}
	}

	[TestMethod]
	public void SetTokens_AcceptsTokens_WithoutThrowing()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TestSemanticTokenStyleResolver();
			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer("local value = 1", resolver);

			try
			{
				colorizer.SetTokens(
				[
					CreateToken(editor, 0, TextSemanticTokenTypes.Variable),
					CreateToken(editor, 6, TextSemanticTokenTypes.Variable)
				]);
			}
			finally
			{
				window.Close();
			}
		});
	}

	[TestMethod]
	public void SetTokens_EmptySet_AndClearTokens_DoNotThrow()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TestSemanticTokenStyleResolver();
			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer("local value = 1", resolver);

			try
			{
				colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]);
				colorizer.SetTokens([]);
				colorizer.ClearTokens();
			}
			finally
			{
				window.Close();
			}
		});
	}

	[TestMethod]
	public void Rebuild_AfterResolverStyleChange_WithoutThrowing()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TestSemanticTokenStyleResolver();
			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer("local value = 1", resolver);

			try
			{
				colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]);

				// A style change on the resolver is followed by a cache rebuild.
				resolver.SetStyle(Brushes.Blue, isBold: true);
				colorizer.Rebuild();
			}
			finally
			{
				window.Close();
			}
		});
	}

	[TestMethod]
	public void MalformedRange_OutsideDocument_IsIgnoredWithoutThrowing()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TestSemanticTokenStyleResolver();
			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer("local value = 1", resolver);

			try
			{
				// A range that starts beyond the document end must be ignored without changing the text.
				colorizer.SetTokens(
				[
					new TextSemanticToken(new TextRange(10_000, 5), TextSemanticTokenTypes.Variable)
				]);

				Assert.AreEqual("local value = 1", editor.Text);
			}
			finally
			{
				window.Close();
			}
		});
	}

	[TestMethod]
	public void OverlappingTokens_AreAccepted_WithoutThrowing()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TestSemanticTokenStyleResolver();
			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer("local value = 1", resolver);

			try
			{
				colorizer.SetTokens(
				[
					new TextSemanticToken(new TextRange(0, 10), TextSemanticTokenTypes.Variable),
					new TextSemanticToken(new TextRange(3, 4), TextSemanticTokenTypes.Function),
					new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Method)
				]);

				Assert.AreEqual("local value = 1", editor.Text);
			}
			finally
			{
				window.Close();
			}
		});
	}

	[TestMethod]
	public void Modifiers_AreAccepted_WithoutThrowing()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TestSemanticTokenStyleResolver();
			resolver.SetStyle(Brushes.Red, boldModifier: TextSemanticTokenModifiers.Deprecated);

			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer("local value = 1", resolver);

			try
			{
				colorizer.SetTokens(
				[
					new TextSemanticToken(
						new TextRange(0, 5),
						TextSemanticTokenTypes.Variable,
						[TextSemanticTokenModifiers.Deprecated])
				]);

				Assert.AreEqual("local value = 1", editor.Text);
			}
			finally
			{
				window.Close();
			}
		});
	}

	[TestMethod]
	public void DocumentChange_AfterSetTokens_DoesNotThrow()
	{
		STATestHelper.RunInSTA(() =>
		{
			var resolver = new TestSemanticTokenStyleResolver();
			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer("local value = 1", resolver);

			try
			{
				colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]);

				// Redrawing after the document shrinks must not throw for the existing token set.
				editor.Text = "ab";
				editor.TextArea.TextView.Redraw();

				Assert.AreEqual("ab", editor.Text);
			}
			finally
			{
				window.Close();
			}
		});
	}

	[TestMethod]
	public void LargeSemanticTokenSet_IsProcessedWithinAllocationAndTimeBounds()
	{
		STATestHelper.RunInSTA(() =>
		{
			const int lineCount = 400;
			const int tokensPerLine = 10;
			const int iterations = 20;
			string text = CreateDocument(lineCount);
			var resolver = new TestSemanticTokenStyleResolver();

			(ICSharpCode.AvalonEdit.TextEditor editor, SemanticTokensColorizer colorizer, Window window) =
				CreateHostedColorizer(text, resolver);

			try
			{
				TextSemanticToken[] tokens = CreateTokens(editor, lineCount, tokensPerLine);
				colorizer.SetTokens(tokens);
				Assert.AreEqual(text, editor.Text);
				Assert.IsTrue(editor.TextArea.TextView.LineTransformers.Any(transformer => ReferenceEquals(transformer, colorizer)));

				long allocated = MeasureAllocations(() => colorizer.SetTokens(tokens), iterations);
				double elapsed = MeasureElapsed(() => colorizer.SetTokens(tokens), iterations);

				Assert.IsTrue(allocated < 256L * 1024L * 1024L,
					$"Applying {tokens.Length} semantic tokens allocated {allocated} B.");
				Assert.IsTrue(elapsed < 10_000.0,
					$"Applying {tokens.Length} semantic tokens took {elapsed:F1} ms.");
				Assert.AreEqual(text, editor.Text);

				colorizer.ClearTokens();
				Assert.AreEqual(text, editor.Text);
			}
			finally
			{
				window.Close();
			}
		});
	}

	private static (ICSharpCode.AvalonEdit.TextEditor Editor, SemanticTokensColorizer Colorizer, Window Window) CreateHostedColorizer(
		string text,
		ISemanticTokenStyleResolver resolver)
	{
		var editor = new ICSharpCode.AvalonEdit.TextEditor
		{
			Text = text
		};

		Window window = WPFTestHost.ShowInHostWindow(editor);
		var colorizer = new SemanticTokensColorizer(editor.TextArea.TextView, resolver);
		editor.TextArea.TextView.LineTransformers.Add(colorizer);

		return (editor, colorizer, window);
	}

	private static TextSemanticToken CreateToken(ICSharpCode.AvalonEdit.TextEditor editor, int offset, string type)
		=> new(new TextRange(offset, 1), type);

	private static string CreateDocument(int lineCount)
		=> string.Concat(Enumerable.Repeat("local value = 1\n", lineCount));

	private static TextSemanticToken[] CreateTokens(ICSharpCode.AvalonEdit.TextEditor editor, int lineCount, int tokensPerLine)
	{
		var tokens = new TextSemanticToken[lineCount * tokensPerLine];
		int index = 0;

		for (int line = 0; line < lineCount; line++)
		{
			for (int token = 0; token < tokensPerLine; token++)
			{
				int offset = editor.Document.GetOffset(new TextLocation(line + 1, (token % 15) + 1));
				tokens[index++] = new TextSemanticToken(new TextRange(offset, 1), TextSemanticTokenTypes.Variable);
			}
		}

		return tokens;
	}

	private static long MeasureAllocations(Action action, int iterations)
	{
		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int iteration = 0; iteration < iterations; iteration++)
			action();

		return GC.GetAllocatedBytesForCurrentThread() - before;
	}

	private static double MeasureElapsed(Action action, int iterations)
	{
		var stopwatch = Stopwatch.StartNew();

		for (int iteration = 0; iteration < iterations; iteration++)
			action();

		stopwatch.Stop();
		return stopwatch.Elapsed.TotalMilliseconds;
	}
}
