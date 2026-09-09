using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed partial class TextDefinitionNavigationTests
{
	private const string DocumentText = "sample alpha = 1\nsample bravo = 2\nsample charlie = 3";

	private sealed class TestDefinitionProvider(TextDefinitionLocation? location) : ITextDefinitionProvider
	{
		public TextDefinitionRequest? LastRequest { get; private set; }

		public TextDefinitionLocation? GetDefinition(TextDefinitionRequest request)
		{
			LastRequest = request;
			return location;
		}
	}

	private sealed class TestHoverProvider(TextHoverInfo? hoverInfo) : ITextHoverProvider
	{
		public TextHoverInfo? GetHoverInfo(TextHoverRequest request) => hoverInfo;
	}

	private sealed record TestDiscriminator(string Value) : TextDefinitionDiscriminator;

	private static TextDefinitionLocation LocationAt(int oneBasedLine, int oneBasedColumn = 1, string? documentId = null)
		=> new(
			new TextPositionRange(
				new TextPosition(oneBasedLine - 1, oneBasedColumn - 1),
				new TextPosition(oneBasedLine - 1, oneBasedColumn - 1)),
			documentId);

	private static TextHoverInfo CreateHoverInfo()
		=> new("Function sample(value);") { SymbolName = "charlie" };

	private static (TextEditor Editor, HostWindow HostWindow) CreateHostedEditor()
	{
		var editor = new TextEditor
		{
			Text = DocumentText
		};

		return (editor, WPFTestHost.ShowInHostWindow(editor));
	}
}
