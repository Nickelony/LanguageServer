using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Provides access to the tooltip associated with an AvalonEdit <see cref="CompletionWindow"/>.
/// </summary>
/// <remarks>
/// The completion window does not expose its tooltip publicly, so the lookup is performed lazily and an
/// unavailable tooltip is reported as a failed attempt. The lookup reflects the private
/// <c>toolTip</c> field of AvalonEdit 6.3.x, walking the type hierarchy with declared-only lookups so the
/// accessor survives the field moving from <see cref="CompletionWindow"/> to its base class; an AvalonEdit
/// upgrade that renames or removes the field is a breaking change for this accessor (the accessor then reports
/// a failed attempt, and the tooltip-aware controller paths degrade to no tooltip reporting).
/// </remarks>
internal static class CompletionWindowToolTipAccess
{
	private static readonly Lazy<FieldInfo?> s_toolTipField = new(FindToolTipField);

	/// <summary>
	/// Gets a value indicating whether the AvalonEdit tooltip field this accessor reflects exists in the
	/// referenced AvalonEdit version. When it is missing, <see cref="TryGetToolTip"/> always reports a failed
	/// attempt and controller tooltip paths are disabled.
	/// </summary>
	internal static bool IsFieldAvailable => s_toolTipField.Value is not null;

	/// <summary>
	/// Tries to retrieve the tooltip associated with the completion window.
	/// </summary>
	/// <param name="completionWindow">The completion window whose tooltip is requested.</param>
	/// <param name="toolTip">The resolved tooltip when available.</param>
	/// <returns><see langword="true"/> when the tooltip field contains a <see cref="ToolTip"/>; otherwise, <see langword="false"/>.</returns>
	internal static bool TryGetToolTip(CompletionWindow completionWindow, [NotNullWhen(true)] out ToolTip? toolTip)
	{
		toolTip = s_toolTipField.Value?.GetValue(completionWindow) as ToolTip;
		return toolTip is not null;
	}

	// GetField never returns private fields declared on a base type, so each level is searched explicitly.
	// The walk stops at object; the tooltip is a WPF-free AvalonEdit type, so no deeper level can declare it.
	private static FieldInfo? FindToolTipField()
	{
		for (Type? type = typeof(CompletionWindow); type is not null && type != typeof(object); type = type.BaseType)
		{
			FieldInfo? field = type.GetField("toolTip", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

			if (field is not null)
				return field;
		}

		return null;
	}
}
