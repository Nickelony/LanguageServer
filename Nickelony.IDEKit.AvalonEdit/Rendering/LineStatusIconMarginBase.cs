using ICSharpCode.AvalonEdit.Rendering;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Rendering;

/// <summary>
/// Provides the shared marker plumbing for line-status margins that draw a replaceable icon on every
/// visible marked line and run a click action for it: the icon brush and geometry dependency properties,
/// the centered and font-scaled drawing, and the left-press handling that resolves the clicked line.
/// </summary>
/// <remarks>
/// <para>
/// The margin draws <see cref="IconGeometry"/> with <see cref="IconBrush"/>, scaled with the margin's font
/// size and centered in the reserved width. The geometry is authored for the design font size and
/// repositioned from its own bounds, so any authored origin works.
/// </para>
/// <para>
/// A left press resolves the visual line under the pointer - the click position comes from
/// <see cref="ResolveClickPosition"/>, which a subclass with an injectable click seam overrides - and
/// invokes <see cref="OnIconClicked"/>. A subclass that performed an action returns <see langword="true"/>,
/// which marks the press handled; a press that resolves to no line stays untouched.
/// </para>
/// <para>
/// The dependency properties are registered here so sibling margins share one implementation; a derived
/// margin registers its own icon defaults by overriding the property metadata. The reserved width and the
/// marker scale are inherited from <see cref="LineStatusMarginBase"/> and follow the margin's font size.
/// </para>
/// </remarks>
public abstract class LineStatusIconMarginBase : LineStatusMarginBase
{
	/// <summary>
	/// Identifies the <see cref="IconBrush"/> dependency property.
	/// </summary>
	/// <remarks>
	/// The base registration defaults to a neutral system gray; a derived margin overrides the property
	/// metadata with its own default icon color.
	/// </remarks>
	public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
		nameof(IconBrush),
		typeof(Brush),
		typeof(LineStatusIconMarginBase),
		new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender),
		ValidateIconBrush);

	/// <summary>
	/// Identifies the <see cref="IconGeometry"/> dependency property.
	/// </summary>
	/// <remarks>
	/// The base registration defaults to an empty geometry, which draws nothing; a derived margin overrides
	/// the property metadata with its own icon geometry.
	/// </remarks>
	public static readonly DependencyProperty IconGeometryProperty = DependencyProperty.Register(
		nameof(IconGeometry),
		typeof(Geometry),
		typeof(LineStatusIconMarginBase),
		new FrameworkPropertyMetadata(Geometry.Empty, FrameworkPropertyMetadataOptions.AffectsRender),
		ValidateIconGeometry);

	/// <summary>
	/// Gets or sets the brush used to draw the marker icon.
	/// </summary>
	/// <remarks>
	/// A derived margin overrides the property metadata with its own default and documents that default on
	/// its type; hosts should set the brush to their theme's marker color.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned brush is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The assigned value is not a <see cref="Brush"/>; the property's validation callback rejects it and
	/// the setter throws.
	/// </exception>
	public Brush IconBrush
	{
		get => (Brush)GetValue(IconBrushProperty);
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			SetValue(IconBrushProperty, value);
		}
	}

	/// <summary>
	/// Gets or sets the geometry used to draw the marker icon.
	/// </summary>
	/// <remarks>
	/// A derived margin overrides the property metadata with its own default icon and documents that default
	/// on its type. The geometry is authored for the design font size and scaled and repositioned from its
	/// own bounds, so any authored origin works.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned geometry is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The assigned value is not a <see cref="Geometry"/>; the property's validation callback rejects it and
	/// the setter throws.
	/// </exception>
	public Geometry IconGeometry
	{
		get => (Geometry)GetValue(IconGeometryProperty);
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			SetValue(IconGeometryProperty, value);
		}
	}

	/// <inheritdoc/>
	protected internal override void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop)
	{
		Geometry iconGeometry = IconGeometry;
		Brush iconBrush = IconBrush;
		double scale = GetFontScale();

		Rect bounds = iconGeometry.Bounds;

		// A margin without an icon (the base default) draws nothing instead of computing a transform from
		// empty bounds.
		if (bounds.IsEmpty)
			return;

		double iconWidth = bounds.Width * scale;
		double iconHeight = bounds.Height * scale;

		double availableWidth = GetEffectiveMarginWidth();
		double iconLeft = (availableWidth - iconWidth) / 2.0;
		double iconTop = visualTop + ((visualLine.Height - iconHeight) / 2.0);

		var transform = new MatrixTransform(
			scale,
			0.0,
			0.0,
			scale,
			iconLeft - (bounds.Left * scale),
			iconTop - (bounds.Top * scale));

		transform.Freeze();

		drawingContext.PushTransform(transform);
		drawingContext.DrawGeometry(iconBrush, null, iconGeometry);
		drawingContext.Pop();
	}

	/// <inheritdoc/>
	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
	{
		base.OnMouseLeftButtonDown(e);

		if (e.Handled)
			return;

		Point position = ResolveClickPosition(e);

		if (!TryGetVisualLineAt(position, out VisualLine? visualLine))
			return;

		if (OnIconClicked(visualLine, position))
			e.Handled = true;
	}

	/// <summary>
	/// Runs the margin's click action for the icon of the clicked visual line.
	/// </summary>
	/// <remarks>
	/// The call runs for a left press that resolved to a visual line; returning <see langword="true"/> marks
	/// the press handled. The base implementation does nothing.
	/// </remarks>
	/// <param name="visualLine">The visual line under the click position.</param>
	/// <param name="position">The click position in the margin's coordinate space.</param>
	/// <returns><see langword="true"/> when the press was handled; otherwise, <see langword="false"/>.</returns>
	protected virtual bool OnIconClicked(VisualLine visualLine, Point position) => false;

	/// <summary>
	/// Resolves the click position for a press event, in the margin's coordinate space.
	/// </summary>
	/// <remarks>
	/// A subclass with an injectable click seam (a test seam, because a synthetic mouse event carries no
	/// position) overrides this member to consult it.
	/// </remarks>
	/// <param name="e">The press event.</param>
	/// <returns>The click position in the margin's coordinate space.</returns>
	protected virtual Point ResolveClickPosition(MouseButtonEventArgs e) => e.GetPosition(this);

	/// <summary>
	/// Resolves the visual line at the specified position in the margin's coordinate space.
	/// </summary>
	/// <param name="position">The position in the margin's coordinate space.</param>
	/// <param name="visualLine">The resolved visual line when one was found.</param>
	/// <returns><see langword="true"/> when the position resolved to a visual line; otherwise, <see langword="false"/>.</returns>
	protected bool TryGetVisualLineAt(Point position, [NotNullWhen(true)] out VisualLine? visualLine)
	{
		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid)
		{
			visualLine = null;
			return false;
		}

		visualLine = textView.GetVisualLineFromVisualTop(position.Y + textView.VerticalOffset);
		return visualLine is not null;
	}

	/// <inheritdoc/>
	protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
		=> new PointHitTestResult(this, hitTestParameters.HitPoint);

	/// <summary>
	/// Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass;
	/// the CLR property setter throws <see cref="ArgumentNullException"/> first for direct assignments.
	/// </summary>
	private static bool ValidateIconBrush(object value)
		=> value is Brush;

	/// <summary>
	/// Rejects <see langword="null"/> so XAML and <c>SetValue</c> assignments cannot crash the render pass;
	/// the CLR property setter throws <see cref="ArgumentNullException"/> first for direct assignments.
	/// </summary>
	private static bool ValidateIconGeometry(object value)
		=> value is Geometry;
}
