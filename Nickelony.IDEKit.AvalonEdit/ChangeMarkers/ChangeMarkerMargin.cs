using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.ChangeMarkers;

/// <summary>
/// Displays a marker beside each visible document line returned by an <see cref="ILineStatusSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// The source is queried during rendering. A source that implements
/// <see cref="IChangeNotificationSource"/> is followed while the margin is connected to a text view;
/// a source without notifications requires the host to invalidate the margin when its marked lines change.
/// </para>
/// <para>
/// The marker and the reserved width scale with the margin's font size.
/// </para>
/// <para>
/// The bar drawing is default sample behavior: a derived margin can override
/// <see cref="LineStatusMarginBase.DrawMarker"/> for a different marker shape, or derive from
/// <see cref="LineStatusMarginBase"/> directly for a different marker model.
/// </para>
/// </remarks>
public class ChangeMarkerMargin : LineStatusMarginBase
{
	private static readonly SolidColorBrush s_defaultMarkerBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x1E, 0x90, 0xFF));

	static ChangeMarkerMargin()
	{
		// A 4-DIP bar matches the width familiar from diff views, instead of the
		// 16-DIP reserved width that the base class uses for icon margins.
		MarginWidthProperty.OverrideMetadata(
			typeof(ChangeMarkerMargin),
			new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
	}

	private readonly ILineStatusSource _markerSource;

	/// <summary>
	/// Identifies the <see cref="MarkerBrush"/> dependency property.
	/// </summary>
	public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.Register(
		nameof(MarkerBrush),
		typeof(Brush),
		typeof(ChangeMarkerMargin),
		new FrameworkPropertyMetadata(s_defaultMarkerBrush, FrameworkPropertyMetadataOptions.AffectsRender),
		ValidateMarkerBrush);

	/// <summary>
	/// Gets or sets the brush used to draw change markers.
	/// </summary>
	/// <remarks>
	/// The default is a sample value chosen to be visible in common themes; hosts should set it to
	/// match their own theme.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned brush is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The assigned value is not a <see cref="Brush"/>; the property's validation callback rejects it and
	/// the setter throws.
	/// </exception>
	public Brush MarkerBrush
	{
		get => (Brush)GetValue(MarkerBrushProperty);
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			SetValue(MarkerBrushProperty, value);
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ChangeMarkerMargin"/> class.
	/// </summary>
	/// <param name="markerSource">The source whose marked lines are rendered.</param>
	/// <exception cref="ArgumentNullException"><paramref name="markerSource"/> is <see langword="null"/>.</exception>
	public ChangeMarkerMargin(ILineStatusSource markerSource)
	{
		ArgumentNullException.ThrowIfNull(markerSource);
		_markerSource = markerSource;

		if (markerSource is IChangeNotificationSource notifyingSource)
			SetNotifyingSource(notifyingSource);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers()
		=> _markerSource.GetMarkedLineNumbers();

	/// <inheritdoc/>
	protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
	{
		double markerWidth = GetEffectiveMarginWidth();

		drawingContext.DrawRectangle(MarkerBrush, null, new Rect(0.0, visualTop, markerWidth, visualLine.Height));
	}

	/// <summary>
	/// Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass.
	/// </summary>
	private static bool ValidateMarkerBrush(object value)
		=> value is Brush;
}
