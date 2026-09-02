using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class BookmarkMarginTests
{
	[TestMethod]
	public void MeasureOverride_ReservesFixedWidth()
	{
		STATestHelper.RunInSTA(() =>
		{
			var margin = CreateMargin(new TextDocument("one\r\ntwo"));

			margin.Measure(new Size(100.0, 100.0));

			Assert.AreEqual(16.0, margin.DesiredSize.Width);
		});
	}

	[TestMethod]
	public void MarginWidth_CanBeOverridden()
	{
		STATestHelper.RunInSTA(() =>
		{
			double original = BookmarkMargin.MarginWidth;

			try
			{
				BookmarkMargin.MarginWidth = 24.0;

				var margin = CreateMargin(new TextDocument("one\r\ntwo"));
				margin.Measure(new Size(100.0, 100.0));

				Assert.AreEqual(24.0, BookmarkMargin.MarginWidth);
				Assert.AreEqual(24.0, margin.DesiredSize.Width);
			}
			finally
			{
				BookmarkMargin.MarginWidth = original;
			}
		});
	}

	[TestMethod]
	public void IconBrush_CanBeOverridden()
	{
		STATestHelper.RunInSTA(() =>
		{
			SolidColorBrush original = BookmarkMargin.IconBrush;
			var custom = new SolidColorBrush(Colors.Red);

			try
			{
				BookmarkMargin.IconBrush = custom;

				Assert.AreSame(custom, BookmarkMargin.IconBrush);
			}
			finally
			{
				BookmarkMargin.IconBrush = original;
			}
		});
	}

	[TestMethod]
	public void IconGeometry_CanBeOverridden()
	{
		STATestHelper.RunInSTA(() =>
		{
			Geometry original = BookmarkMargin.IconGeometry;
			Geometry custom = Geometry.Parse("M0,0 L10,0 L10,10 Z");

			try
			{
				BookmarkMargin.IconGeometry = custom;

				Assert.AreSame(custom, BookmarkMargin.IconGeometry);
			}
			finally
			{
				BookmarkMargin.IconGeometry = original;
			}
		});
	}

	private static BookmarkMargin CreateMargin(TextDocument document)
		=> new(new BookmarkCoordinator(() => document));
}
