namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes the current state of the text completion presentation.
/// </summary>
/// <remarks>
/// The detail content is opaque to the record: the producer stores the item's documentation object
/// unchanged (a string stays a string), and a host that renders the state decides how to present it.
/// Value equality compares <see cref="DetailContent"/> with the default equality comparison for
/// <see cref="object"/>: string content compares structurally, while a payload that does not
/// override <see cref="object.Equals(object)"/> compares by reference, so two states that render
/// identically can compare unequal. An in-flight request is not part of the visibility union: the
/// union counts only a scheduled (debounced) request, while the in-flight request is observed
/// separately through <see cref="TextCompletionRequestSession"/>.
/// </remarks>
/// <param name="IsListVisible">Whether the completion list is currently shown and tracked.</param>
/// <param name="IsRequestScheduled">Whether a completion request is currently scheduled.</param>
/// <param name="IsDetailVisible">Whether the completion detail popup is visible.</param>
/// <param name="DetailContent">The content of the completion detail popup, when visible.</param>
public readonly record struct TextCompletionPresentationState(
	bool IsListVisible,
	bool IsRequestScheduled,
	bool IsDetailVisible,
	object? DetailContent)
{
	/// <summary>
	/// Gets the empty presentation state with no visible list or detail, no scheduled request, and no content.
	/// </summary>
	public static TextCompletionPresentationState Empty { get; } = new(false, false, false, null);

	/// <summary>
	/// Gets a value indicating whether the completion presentation is visible or a request is pending: the
	/// list or detail is visible, or a debounced request is scheduled. An in-flight request is not part
	/// of this union.
	/// </summary>
	public bool IsPresentationVisibleOrRequestScheduled => IsListVisible || IsDetailVisible || IsRequestScheduled;
}
