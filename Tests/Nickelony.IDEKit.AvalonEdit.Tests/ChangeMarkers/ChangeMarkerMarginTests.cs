using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.ChangeMarkers;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[TestClass]
public sealed class ChangeMarkerMarginTests
{
	[TestMethod]
	public void MeasureOverride_ReservesFixedWidth()
	{
		STATestHelper.RunInSTA(() =>
		{
			ChangeMarkerMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

			margin.Measure(new Size(100.0, 100.0));

			Assert.AreEqual(4.0, margin.DesiredSize.Width);
		});
	}

	[TestMethod]
	public void MarginWidth_CanBeOverridden()
	{
		STATestHelper.RunInSTA(() =>
		{
			double original = ChangeMarkerMargin.MarginWidth;

			try
			{
				ChangeMarkerMargin.MarginWidth = 6.0;

				ChangeMarkerMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));
				margin.Measure(new Size(100.0, 100.0));

				Assert.AreEqual(6.0, ChangeMarkerMargin.MarginWidth);
				Assert.AreEqual(6.0, margin.DesiredSize.Width);
			}
			finally
			{
				ChangeMarkerMargin.MarginWidth = original;
			}
		});
	}

	[TestMethod]
	public void MarkerBrush_CanBeOverridden()
	{
		STATestHelper.RunInSTA(() =>
		{
			SolidColorBrush original = ChangeMarkerMargin.MarkerBrush;
			var custom = new SolidColorBrush(Colors.Red);

			try
			{
				ChangeMarkerMargin.MarkerBrush = custom;

				Assert.AreSame(custom, ChangeMarkerMargin.MarkerBrush);
			}
			finally
			{
				ChangeMarkerMargin.MarkerBrush = original;
			}
		});
	}

	private static ChangeMarkerMargin CreateMargin(TextDocument document)
		=> new(new EmptyMarkerSource());

	private sealed class EmptyMarkerSource : IChangeMarkerSource
	{
		public IReadOnlyList<DocumentLine> GetMarkedLines()
			=> [];
	}
}
