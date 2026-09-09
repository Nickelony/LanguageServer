using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <summary>
/// Renders the code-action indicator (a light-bulb icon) for the visible indicator line and opens the
/// actions menu when the left mouse button is pressed on that line.
/// </summary>
/// <remarks>
/// <para>
/// The margin is driven by its <see cref="TextCodeActionController"/>: the controller reports the
/// indicator's line and raises a change notification when the available actions change, so a connected
/// margin invalidates itself without further host calls.
/// </para>
/// <para>
/// A left-button press on the indicator's line opens the actions menu anchored at the click; a press
/// on a row without the indicator does nothing. The margin reserves the base class's standard width
/// and the icon scales with the margin's font size, so the margin width stays proportional to the
/// text when the editor font changes.
/// </para>
/// <para>
/// The icon's brush and geometry are replaceable through the inherited dependency properties (the
/// defaults are the system gray-text color and the library's light-bulb icon), and hosts are
/// expected to set the brush to their theme's hint or accent color. The left-press gesture is the
/// library default and can be turned off with <see cref="OpenOnLeftPress"/>; a host that owns the
/// gesture opens the same menu through
/// <see cref="TextCodeActionController.TryOpenActions(int, Point)"/> with its own anchor.
/// </para>
/// </remarks>
public sealed class TextCodeActionMargin : LineStatusIconMarginBase
{
	private static readonly Geometry s_defaultIconGeometry = CreateIconGeometry();
	private static readonly SolidColorBrush s_defaultIconBrush = BrushHelpers.CreateFrozenBrush(SystemColors.GrayTextColor);

	static TextCodeActionMargin()
	{
		IconBrushProperty.OverrideMetadata(
			typeof(TextCodeActionMargin),
			new FrameworkPropertyMetadata(s_defaultIconBrush, FrameworkPropertyMetadataOptions.AffectsRender));
		IconGeometryProperty.OverrideMetadata(
			typeof(TextCodeActionMargin),
			new FrameworkPropertyMetadata(s_defaultIconGeometry, FrameworkPropertyMetadataOptions.AffectsRender));
	}

	private readonly TextCodeActionController _controller;

	/// <summary>
	/// Identifies the <see cref="OpenOnLeftPress"/> dependency property.
	/// </summary>
	public static readonly DependencyProperty OpenOnLeftPressProperty = DependencyProperty.Register(
		nameof(OpenOnLeftPress),
		typeof(bool),
		typeof(TextCodeActionMargin),
		new FrameworkPropertyMetadata(true));

	/// <summary>
	/// Gets or sets a value indicating whether a left-button press on the indicator's line opens the
	/// actions menu. Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// Set this to <see langword="false"/> when the host owns the open gesture. The margin then leaves
	/// the press untouched, and the host opens the same menu through
	/// <see cref="TextCodeActionController.TryOpenActions(int, Point)"/> with its own anchor.
	/// </remarks>
	public bool OpenOnLeftPress
	{
		get => (bool)GetValue(OpenOnLeftPressProperty);
		set => SetValue(OpenOnLeftPressProperty, value);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionMargin"/> class.
	/// </summary>
	/// <param name="controller">The controller that supplies the indicator line and receives clicks.</param>
	/// <exception cref="ArgumentNullException"><paramref name="controller"/> is <see langword="null"/>.</exception>
	public TextCodeActionMargin(TextCodeActionController controller)
	{
		ArgumentNullException.ThrowIfNull(controller);
		_controller = controller;

		SetNotifyingSource(controller);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers()
		=> _controller.GetIndicatorLineNumbers();

	/// <inheritdoc/>
	protected override bool OnIconClicked(VisualLine visualLine, Point position)
	{
		// The host owns the gesture when it disabled the default one; the press stays untouched.
		if (!OpenOnLeftPress)
			return false;

		return _controller.TryOpenActionsFromMargin(visualLine.FirstDocumentLine.LineNumber, position);
	}

	/// <inheritdoc/>
	protected override Point ResolveClickPosition(MouseButtonEventArgs e)
		=> ClickPositionResolver is Func<MouseButtonEventArgs, Point> resolver ? resolver(e) : base.ResolveClickPosition(e);

	/// <summary>
	/// Gets or sets an optional resolver for the click position used by
	/// <see cref="LineStatusIconMarginBase.OnMouseLeftButtonDown(MouseButtonEventArgs)"/>, instead of the event's
	/// own position.
	/// </summary>
	/// <remarks>
	/// A test seam: a synthetic mouse event cannot carry a position, so a test that exercises the routed
	/// click path sets this resolver to a deterministic point.
	/// </remarks>
	internal Func<MouseButtonEventArgs, Point>? ClickPositionResolver { get; set; }

	/// <summary>
	/// Opens the actions menu for the line at the specified margin-relative position when it shows
	/// the indicator.
	/// </summary>
	/// <param name="position">The position in the margin's coordinate space.</param>
	/// <returns><see langword="true"/> when the menu opened; otherwise, <see langword="false"/>.</returns>
	internal bool TryOpenActionsAt(Point position)
		=> TryGetVisualLineAt(position, out VisualLine? visualLine)
			&& _controller.TryOpenActionsFromMargin(visualLine.FirstDocumentLine.LineNumber, position);

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			// The glass is a circle; the stem is a tapering trapezoid that overlaps the glass, so the
			// two figures read as one bulb silhouette.
			context.BeginFigure(new Point(5.5, 0.8), true, true);
			context.ArcTo(new Point(9.9, 5.2), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);
			context.ArcTo(new Point(5.5, 9.6), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);
			context.ArcTo(new Point(1.1, 5.2), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);
			context.ArcTo(new Point(5.5, 0.8), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);

			context.BeginFigure(new Point(4.4, 8.6), true, true);
			context.LineTo(new Point(6.6, 8.6), true, false);
			context.LineTo(new Point(6.0, 11.6), true, false);
			context.LineTo(new Point(5.0, 11.6), true, false);
		}

		geometry.Freeze();
		return geometry;
	}
}
