using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Editors;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class TextEditorStatusCoordinatorTests
{
	[TestMethod]
	public void Zoom_DefaultsToOneHundred()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			Assert.AreEqual(100, coordinator.Zoom);
		});
	}

	[TestMethod]
	public void TryHandleZoom_PositiveDelta_UpdatesZoomAndRaisesCallbacks()
	{
		STATestHelper.RunInSTA(() =>
		{
			int zoomCallbacks = 0;
			double appliedFontSize = 0.0;
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => zoomCallbacks++);

			bool changed = coordinator.TryHandleZoom(120, 50, 200, 10, 12.0, size => appliedFontSize = size);

			Assert.IsTrue(changed);
			Assert.AreEqual(110, coordinator.Zoom);
			Assert.AreEqual(1, zoomCallbacks);
			Assert.AreEqual(13.2, appliedFontSize, 0.001);
		});
	}

	[TestMethod]
	public void TryHandleZoom_NegativeDelta_DecreasesZoom()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			bool changed = coordinator.TryHandleZoom(-120, 50, 200, 10, 12.0, _ => { });

			Assert.IsTrue(changed);
			Assert.AreEqual(90, coordinator.Zoom);
		});
	}

	[TestMethod]
	public void TryHandleZoom_AtMaxZoom_ReturnsFalse()
	{
		STATestHelper.RunInSTA(() =>
		{
			int zoomCallbacks = 0;
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => zoomCallbacks++);
			coordinator.Zoom = 200;

			bool changed = coordinator.TryHandleZoom(120, 50, 200, 10, 12.0, _ => { });

			Assert.IsFalse(changed);
			Assert.AreEqual(200, coordinator.Zoom);
			Assert.AreEqual(0, zoomCallbacks);
		});
	}

	[TestMethod]
	public void TryHandleZoom_AtMinZoom_ReturnsFalse()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });
			coordinator.Zoom = 50;

			bool changed = coordinator.TryHandleZoom(-120, 50, 200, 10, 12.0, _ => { });

			Assert.IsFalse(changed);
			Assert.AreEqual(50, coordinator.Zoom);
		});
	}

	[TestMethod]
	public void TryHandleZoom_ZeroDelta_ReturnsFalse()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			bool changed = coordinator.TryHandleZoom(0, 50, 200, 10, 12.0, _ => { });

			Assert.IsFalse(changed);
		});
	}

	[TestMethod]
	public void Attach_RaisesStatusOnCaretMove()
	{
		STATestHelper.RunInSTA(() =>
		{
			var textArea = new TextArea { Document = new TextDocument("ab") };
			int statusCallbacks = 0;
			using var coordinator = new TextEditorStatusCoordinator(textArea, () => statusCallbacks++, () => { });
			coordinator.Attach();

			textArea.Caret.Position = new TextViewPosition(1, 2);

			Assert.AreEqual(1, statusCallbacks);
		});
	}

	[TestMethod]
	public void Dispose_StopsRaisingStatusOnCaretMove()
	{
		STATestHelper.RunInSTA(() =>
		{
			var textArea = new TextArea { Document = new TextDocument("ab") };
			int statusCallbacks = 0;
			var coordinator = new TextEditorStatusCoordinator(textArea, () => statusCallbacks++, () => { });
			coordinator.Attach();
			coordinator.Dispose();

			textArea.Caret.Position = new TextViewPosition(1, 2);

			Assert.AreEqual(0, statusCallbacks);
		});
	}

	[TestMethod]
	public void Attach_Twice_RaisesOneStatusCallback()
	{
		STATestHelper.RunInSTA(() =>
		{
			var textArea = new TextArea { Document = new TextDocument("ab") };
			int statusCallbacks = 0;
			using var coordinator = new TextEditorStatusCoordinator(textArea, () => statusCallbacks++, () => { });
			coordinator.Attach();
			coordinator.Attach();

			textArea.Caret.Position = new TextViewPosition(1, 2);

			Assert.AreEqual(1, statusCallbacks);
		});
	}

	[TestMethod]
	public void Attach_RaisesStatusOnSelectionChange()
	{
		STATestHelper.RunInSTA(() =>
		{
			var textArea = new TextArea { Document = new TextDocument("abcd") };
			int statusCallbacks = 0;
			using var coordinator = new TextEditorStatusCoordinator(textArea, () => statusCallbacks++, () => { });
			coordinator.Attach();

			textArea.Caret.Position = new TextViewPosition(1, 4);
			statusCallbacks = 0;
			textArea.Selection = Selection.Create(textArea, 1, 2);

			Assert.AreEqual(1, statusCallbacks);
		});
	}

	[TestMethod]
	public void Dispose_Twice_UnsubscribesOnceAndDoesNotThrow()
	{
		STATestHelper.RunInSTA(() =>
		{
			var textArea = new TextArea { Document = new TextDocument("ab") };
			int statusCallbacks = 0;
			var coordinator = new TextEditorStatusCoordinator(textArea, () => statusCallbacks++, () => { });
			coordinator.Attach();
			coordinator.Dispose();
			coordinator.Dispose();

			textArea.Caret.Position = new TextViewPosition(1, 2);

			Assert.AreEqual(0, statusCallbacks);
		});
	}

	[TestMethod]
	public void Attach_AfterDispose_ThrowsObjectDisposedException()
	{
		STATestHelper.RunInSTA(() =>
		{
			var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });
			coordinator.Dispose();

			Assert.ThrowsExactly<ObjectDisposedException>(() => coordinator.Attach());
		});
	}

	[TestMethod]
	public void TryHandleZoom_MinZoomGreaterThanMaxZoom_ThrowsArgumentOutOfRangeException()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
				coordinator.TryHandleZoom(120, 200, 50, 10, 12.0, _ => { }));
		});
	}

	[TestMethod]
	public void TryHandleZoom_NonPositiveZoomStepSize_ThrowsArgumentOutOfRangeException()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
				coordinator.TryHandleZoom(120, 50, 200, 0, 12.0, _ => { }));
		});
	}

	[TestMethod]
	public void TryHandleZoom_NonFiniteDefaultFontSize_ThrowsArgumentOutOfRangeException()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
				coordinator.TryHandleZoom(120, 50, 200, 10, double.PositiveInfinity, _ => { }));
		});
	}

	[TestMethod]
	public void TryHandleZoom_NonPositiveDefaultFontSize_ThrowsArgumentOutOfRangeException()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
				coordinator.TryHandleZoom(120, 50, 200, 10, 0.0, _ => { }));
		});
	}

	[TestMethod]
	public void TryHandleZoom_NullApplyFontSize_ThrowsArgumentNullException()
	{
		STATestHelper.RunInSTA(() =>
		{
			using var coordinator = new TextEditorStatusCoordinator(new TextArea(), () => { }, () => { });

			Assert.ThrowsExactly<ArgumentNullException>(() =>
				coordinator.TryHandleZoom(120, 50, 200, 10, 12.0, null!));
		});
	}
}
