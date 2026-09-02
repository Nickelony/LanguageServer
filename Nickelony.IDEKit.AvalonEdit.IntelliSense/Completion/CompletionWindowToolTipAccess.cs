using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

/// <summary>
/// Provides defensive access to the completion window's private tooltip field.
/// AvalonEdit's <see cref="CompletionWindow"/> does not expose its tooltip through a public API,
/// so the field is resolved lazily and defensively: if a future AvalonEdit version renames or
/// removes the field, <see cref="TryGetToolTip"/> returns false and callers degrade gracefully.
/// </summary>
public static class CompletionWindowToolTipAccess
{
	private static readonly Lazy<FieldInfo?> s_toolTipField = new(() =>
		typeof(CompletionWindow).GetField("toolTip", BindingFlags.NonPublic | BindingFlags.Instance));

	/// <summary>
	/// Attempts to retrieve the completion window's private tooltip instance.
	/// </summary>
	/// <param name="completionWindow">The completion window whose tooltip is requested.</param>
	/// <param name="toolTip">The resolved tooltip when available.</param>
	/// <returns><see langword="true"/> when the tooltip field contains a <see cref="ToolTip"/>; otherwise, <see langword="false"/>.</returns>
	public static bool TryGetToolTip(CompletionWindow completionWindow, [NotNullWhen(true)] out ToolTip? toolTip)
	{
		ArgumentNullException.ThrowIfNull(completionWindow);

		toolTip = s_toolTipField.Value?.GetValue(completionWindow) as ToolTip;
		return toolTip is not null;
	}
}
