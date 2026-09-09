using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
public sealed class TextCodeActionValidationTests
{
	[TestMethod]
	public void Controller_NullArguments_Throw()
	{
		var editor = WPFTestHost.CreateEditor("sample");
		TextCodeActionMenuSkin skin = CodeActionTestHost.CreateSkin();

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(null!, skin, CreateHooks()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, null!, CreateHooks()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, skin, hooks: null!));
	}

	[TestMethod]
	public void Controller_MissingRequiredHooks_Throw()
	{
		var editor = WPFTestHost.CreateEditor("sample");
		TextCodeActionMenuSkin skin = CodeActionTestHost.CreateSkin();

		var missingStateBuilder = new TextCodeActionControllerHooks
		{
			BuildRequestState = null!,
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
			ExecuteActionAsync = static _ => Task.CompletedTask
		};

		var missingRequest = new TextCodeActionControllerHooks
		{
			BuildRequestState = static _ => null,
			RequestCodeActionsAsync = null!,
			ExecuteActionAsync = static _ => Task.CompletedTask
		};

		var missingExecute = new TextCodeActionControllerHooks
		{
			BuildRequestState = static _ => null,
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
			ExecuteActionAsync = null!
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, skin, hooks: missingStateBuilder));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, skin, hooks: missingRequest));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, skin, hooks: missingExecute));
	}

	[TestMethod]
	public void Controller_CreatedOffOwnerThread_Throws()
	{
		var editor = WPFTestHost.CreateEditor("sample");
		TextCodeActionMenuSkin skin = CodeActionTestHost.CreateSkin();
		Exception? caught = null;

		var thread = new Thread(() =>
		{
			try
			{
				using var controller = new TextCodeActionController(
					editor.TextArea,
					skin,
					hooks: new TextCodeActionControllerHooks
					{
						BuildRequestState = static context => new TextCodeActionRequestState(context.DocumentText, 0, 0),
						RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
						ExecuteActionAsync = static _ => Task.CompletedTask
					});
			}
			catch (Exception exception)
			{
				caught = exception;
			}
		});

		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(caught);
	}

	[TestMethod]
	public void Controller_NullSkinBrushes_Throw()
	{
		var editor = WPFTestHost.CreateEditor("sample");

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(
			editor.TextArea,
			new TextCodeActionMenuSkin(null!, Brushes.Black, Brushes.White),
			CreateHooks()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(
			editor.TextArea,
			new TextCodeActionMenuSkin(Brushes.Gray, null!, Brushes.White),
			CreateHooks()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(
			editor.TextArea,
			new TextCodeActionMenuSkin(Brushes.Gray, Brushes.Black, null!),
			CreateHooks()));
	}

	private static TextCodeActionControllerHooks CreateHooks() => new()
	{
		BuildRequestState = static _ => null,
		RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
		ExecuteActionAsync = static _ => Task.CompletedTask
	};
}
