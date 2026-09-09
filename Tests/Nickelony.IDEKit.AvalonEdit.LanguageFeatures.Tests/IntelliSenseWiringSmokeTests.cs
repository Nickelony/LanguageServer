using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Constructs the wiring the package README documents, using only the public API, so a breaking change to
/// the documented construction stops compiling here instead of letting the README drift away from the code.
/// </summary>
[STATestClass]
public sealed class IntelliSenseWiringSmokeTests
{
	[TestMethod]
	public async Task ReadmeWiring_ConstructsEverySubsystemAndRunsTheStandardScenarios()
	{
		var editor = new TextEditor { Text = "sample" };
		using var hostWindow = WPFTestHost.ShowInHostWindow(editor);

		int scheduledRequestCount = 0;
		TextCompletionController? completion = null;

		completion = new TextCompletionController(
			editor.TextArea,
			new CompletionWindowSkin(Brushes.Gray, Brushes.Black, Brushes.White),
			TextCompletionControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0)
			},
			new TextCompletionControllerHooks
			{
				ConfigureWindow = window => window.FontSize = editor.FontSize,
				GetDisplayInfo = item => (item.Text, null),
				ToolTipSkin = CompletionToolTipSkin.Default with
				{
					Background = Brushes.Black,
					BorderBrush = Brushes.Gray
				},
				ScheduledRequestAsync = () =>
				{
					scheduledRequestCount++;
					return completion!.RequestAsync(
						_ => Task.FromResult(TextCompletionSessionDecision.Open([new TextCompletionItem("sample")], 0, 5)));
				}
			});

		var hover = new TextHoverController(
			editor,
			new TextHoverControllerHooks
			{
				GetOffsetFromPoint = point => editor.GetPositionFromPoint(point) is { } position
					? editor.Document.GetOffset(position.Location)
					: null,
				BuildEvaluationState = offset => new TextHoverEvaluationState(
					ShouldRequestHover: true,
					RequestOffset: offset,
					CanShowHoverContent: true,
					CanShowDiagnosticFallback: false,
					DiagnosticInfo: null),
				RequestHoverAsync = (offset, cancellationToken) => Task.FromResult<Nickelony.IDEKit.IntelliSense.Hover.TextHoverInfo?>(null),
				ShowToolTip = (hoverInfo, diagnosticInfo) => { }
			});

		var signatures = new TextSignatureHelpController(
			new TextSignatureHelpControllerHooks
			{
				GetCurrentCaretOffset = () => editor.CaretOffset,
				RequestSignatureHelpAsync = (offset, context, cancellationToken) => Task.FromResult<Nickelony.IDEKit.IntelliSense.Signatures.TextSignatureHelp?>(null),
				ShowSignatureHelp = _ => { },
				DismissSignatureHelp = () => { }
			});

		var codeActions = new TextCodeActionController(
			editor.TextArea,
			new TextCodeActionMenuSkin(Brushes.Gray, Brushes.Black, Brushes.White),
			hooks: new TextCodeActionControllerHooks
			{
				BuildRequestState = _ => null,
				RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
				ExecuteActionAsync = _ => Task.CompletedTask
			});

		editor.TextArea.LeftMargins.Add(codeActions.Margin);

		var colorizer = new SemanticTokensColorizer(editor.TextArea.TextView, new TestSemanticTokenStyleResolver());
		editor.TextArea.TextView.LineTransformers.Add(colorizer);

		try
		{
			// The scheduled request runs the standard pipeline: the debounced callback opens the window
			// through the package's default completion-data adapter, so no item mapper is needed here.
			completion.ScheduleRequest();
			DispatcherTestUtils.PumpUntil(() => scheduledRequestCount > 0);
			DispatcherTestUtils.PumpUntil(() => completion.CurrentPresentation.IsListVisible);

			Assert.IsTrue(completion.CurrentPresentation.IsListVisible);

			completion.CloseWindow();

			Assert.IsFalse(completion.CurrentPresentation.IsListVisible);

			// The hover and signature controllers run their standard entry points with the wired hooks.
			await hover.HandleMouseHoverAsync(new MouseEventArgs(Mouse.PrimaryDevice, 0));
			await signatures.RequestAsync(editor.CaretOffset);

			Assert.IsFalse(signatures.SelectNextSignature());

			// The code-action hooks veto every context, so the controller publishes no actions and its
			// caret- and host-anchored open seams stay closed.
			Assert.IsFalse(codeActions.HasActions);
			Assert.IsFalse(codeActions.TryOpenActions());
			Assert.IsFalse(codeActions.TryOpenActions(1, new Point(0.0, 0.0)));

			// The diagnostics factory projects segments from the same provider that feeds the renderer.
			IReadOnlyList<TextDiagnostic> diagnostics = [new TextDiagnostic(TextDiagnosticSeverity.Error, "message", 0, 4)];
			DiagnosticsRenderer renderer = TextDiagnosticSegmentFactory.CreateRenderer(() => diagnostics);

			Assert.IsNotNull(renderer);
			Assert.AreEqual(1, TextDiagnosticSegmentFactory.CreateProvider(() => diagnostics)().Count);

			// The semantic colorizer applies a token and reports the change.
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new Nickelony.IDEKit.Core.Text.TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
		}
		finally
		{
			completion.Dispose();
			hover.Dispose();
			signatures.Dispose();
			codeActions.Dispose();
			editor.TextArea.LeftMargins.Remove(codeActions.Margin);
			editor.TextArea.TextView.LineTransformers.Remove(colorizer);
		}
	}
}
