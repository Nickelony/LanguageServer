using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

/// <summary>
/// Provides access to the tooltip associated with an AvalonEdit <see cref="CompletionWindow"/>.
/// Because the completion window does not expose its tooltip publicly, the lookup is performed
/// lazily and an unavailable tooltip is reported as a failed attempt.
/// </summary>
public static class CompletionWindowToolTipAccess
{
	private static readonly Lazy<FieldInfo?> s_toolTipField = new(() =>
		typeof(CompletionWindow).GetField("toolTip", BindingFlags.NonPublic | BindingFlags.Instance));

	/// <summary>
	/// Attempts to retrieve the tooltip associated with the completion window.
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
