using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

[STATestClass]
public sealed class TextMateHighlightingAttachmentTests
{
	[TestMethod]
	public void Constructor_InstallsTransformerAndStartsTokenization()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var attachment = new TextMateHighlightingAttachment(editor, CreateLuaGrammar(), new TextMateTokenTheme());

		try
		{
			Assert.IsTrue(editor.TextArea.TextView.LineTransformers.Contains(attachment.Transformer));
			Assert.IsFalse(attachment.Model.IsStopped);

			// Tokenization starts because the transformer registers itself as a model listener.
			WaitForTokenization(attachment.Model, 0);

			Assert.IsNotNull(attachment.Model.GetLineTokens(0));
		}
		finally
		{
			attachment.Dispose();
		}
	}

	[TestMethod]
	public void Dispose_RemovesTransformerAndDisposesModel()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var attachment = new TextMateHighlightingAttachment(editor, CreateLuaGrammar(), new TextMateTokenTheme());

		attachment.Dispose();

		// Disposing the model also stops its tokenizer thread and disposes the line list.
		Assert.IsFalse(editor.TextArea.TextView.LineTransformers.Contains(attachment.Transformer));
		Assert.IsTrue(attachment.Model.IsStopped);

		// The disposal is idempotent.
		attachment.Dispose();
	}

	[TestMethod]
	public void Dispose_ThenDocumentEdits_DoNotThrow()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var attachment = new TextMateHighlightingAttachment(editor, CreateLuaGrammar(), new TextMateTokenTheme());

		attachment.Dispose();

		// The line list detached from the document, so later edits no longer reach it; its snapshot
		// stays at the last tracked state of the pre-dispose document.
		editor.Document.Insert(0, "-- comment\n");
		editor.Document.Text = "x";

		Assert.AreEqual(2, attachment.LineList.GetNumberOfLines());
	}

	private static IGrammar CreateLuaGrammar()
	{
		var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
		return new Registry(registryOptions).LoadGrammar(registryOptions.GetScopeByLanguageId("lua"));
	}

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

		Assert.Fail($"The model did not tokenize line {lineIndex}.");
	}
}
