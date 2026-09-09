namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes how a host should update the completion session after processing input.
/// </summary>
/// <remarks>
/// <para>
/// A decision is in one of three normal states: leave the session unchanged (<see cref="None"/>),
/// dismiss the session (<see cref="Close"/>), or open (or refresh) the session with the supplied
/// items and replacement range (<see cref="Open"/>). A decision may also request a close and carry a
/// replacement at the same time; a host applies the close first. The fourth state,
/// <see cref="NoMatches"/>, reports that the provider returned candidates that the current word
/// filtered out completely, so a host can dismiss a narrowing session instead of keeping stale
/// entries. The constructor and the factory methods enforce the state invariant: an opening
/// decision carries at least one item and both replacement offsets, and a decision without items
/// carries no offsets.
/// </para>
/// <para>
/// The record's value equality compares <see cref="Items"/> by list reference; two decisions that
/// carry equal item sequences are not equal.
/// </para>
/// <para>
/// The constructor stores the supplied list as-is, so the caller keeps ownership of it. Use
/// <see cref="Open"/> to create a decision that owns a copy of the supplied items.
/// </para>
/// </remarks>
public readonly record struct TextCompletionSessionDecision
{
	private readonly IReadOnlyList<TextCompletionItem>? _items;
	private readonly int? _startOffset;
	private readonly int? _endOffset;
	private readonly bool _allItemsFilteredOut;

	/// <summary>
	/// Initializes a decision in one of the documented states.
	/// </summary>
	/// <remarks>
	/// Use <see cref="None"/>, <see cref="Close"/>, <see cref="Open"/>, or <see cref="NoMatches"/> to
	/// create a state explicitly; this constructor is available for adapters that build a decision
	/// from state data and enforces the same state invariant. The supplied list is stored as-is, so
	/// an empty list is rejected and a caller that mutates a stored list changes the decision's
	/// contents afterwards.
	/// </remarks>
	/// <param name="shouldClose">Whether an active completion session should be dismissed.</param>
	/// <param name="items">
	/// The items to show when opening or refreshing a completion session; at least one item is
	/// required when the list is supplied.
	/// </param>
	/// <param name="startOffset">The zero-based start offset for replacement when opening the session.</param>
	/// <param name="endOffset">The zero-based end offset for replacement when opening the session.</param>
	/// <param name="allItemsFilteredOut">
	/// <see langword="true"/> when the decision reports that the provider returned candidates that
	/// the current word filtered out completely; such a decision carries no items or offsets.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The argument combination violates the state invariant: items without both replacement
	/// offsets, offsets without items, an empty items list, or a filtered-out report that carries
	/// items.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is negative, or <paramref name="endOffset"/> is before
	/// <paramref name="startOffset"/>.
	/// </exception>
	public TextCompletionSessionDecision(
		bool shouldClose,
		IReadOnlyList<TextCompletionItem>? items,
		int? startOffset,
		int? endOffset,
		bool allItemsFilteredOut = false)
	{
		ValidateState(items, startOffset, endOffset, allItemsFilteredOut, nameof(items), nameof(startOffset), nameof(endOffset));

		ShouldClose = shouldClose;
		_items = items;
		_startOffset = startOffset;
		_endOffset = endOffset;
		_allItemsFilteredOut = allItemsFilteredOut;
	}

	/// <summary>
	/// Gets a value indicating whether an active completion session should be dismissed. A decision
	/// may both request a close and carry a replacement.
	/// </summary>
	public bool ShouldClose { get; init; }

	/// <summary>
	/// Gets the items to show when opening or refreshing a completion session, or <see langword="null"/>
	/// when the decision does not open one.
	/// </summary>
	public IReadOnlyList<TextCompletionItem>? Items => _items;

	/// <summary>
	/// Gets the zero-based replacement start offset when opening the session, or <see langword="null"/>
	/// when the decision does not open one.
	/// </summary>
	public int? StartOffset => _startOffset;

	/// <summary>
	/// Gets the zero-based replacement end offset when opening the session, or <see langword="null"/>
	/// when the decision does not open one.
	/// </summary>
	public int? EndOffset => _endOffset;

	/// <summary>
	/// Gets a value indicating whether the decision reports that the provider returned candidates
	/// that the current word filtered out completely.
	/// </summary>
	/// <remarks>
	/// The state carries no items and no replacement range; it lets a host dismiss a session that
	/// narrowed to nothing while leaving a session with no candidates unchanged (<see cref="None"/>).
	/// </remarks>
	public bool AllItemsFilteredOut => _allItemsFilteredOut;

	/// <summary>
	/// Validates the decision state: items require at least one entry and both replacement offsets,
	/// and no items means no offsets and no filtered-out report.
	/// </summary>
	/// <param name="items">The items carried by the decision.</param>
	/// <param name="startOffset">The replacement start offset.</param>
	/// <param name="endOffset">The replacement end offset.</param>
	/// <param name="allItemsFilteredOut">Whether the decision reports a filtered-out result.</param>
	/// <param name="itemsParameterName">The items parameter or property name reported by the exception.</param>
	/// <param name="startOffsetParameterName">The start-offset parameter or property name reported by the exception.</param>
	/// <param name="endOffsetParameterName">The end-offset parameter or property name reported by the exception.</param>
	/// <exception cref="ArgumentException">The item/offset combination is inconsistent.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A replacement offset range is negative or reversed.</exception>
	private static void ValidateState(
		IReadOnlyList<TextCompletionItem>? items,
		int? startOffset,
		int? endOffset,
		bool allItemsFilteredOut,
		string itemsParameterName,
		string startOffsetParameterName,
		string endOffsetParameterName)
	{
		if (items is null)
		{
			if (startOffset is not null || endOffset is not null)
				throw new ArgumentException("A decision without items does not carry a replacement range.", itemsParameterName);

			return;
		}

		if (allItemsFilteredOut)
			throw new ArgumentException("A decision that carries items cannot report that every item was filtered out.", itemsParameterName);

		if (items.Count == 0)
			throw new ArgumentException("A decision that carries items must supply at least one item.", itemsParameterName);

		if (startOffset is null || endOffset is null)
			throw new ArgumentException(
				"A decision that carries items must supply both replacement offsets.",
				startOffset is null ? startOffsetParameterName : endOffsetParameterName);

		if (startOffset.Value < 0)
			throw new ArgumentOutOfRangeException(startOffsetParameterName, startOffset, "The replacement start offset must not be negative.");

		if (endOffset.Value < startOffset.Value)
			throw new ArgumentOutOfRangeException(endOffsetParameterName, endOffset, "The replacement end offset must not be before the start offset.");
	}

	/// <summary>
	/// Gets a decision that leaves the current completion session unchanged.
	/// </summary>
	/// <remarks>
	/// The value is value-equal to <c>default(TextCompletionSessionDecision)</c>; use the property to
	/// express "leave the session unchanged" explicitly.
	/// </remarks>
	public static TextCompletionSessionDecision None { get; } = new(false, null, null, null);

	/// <summary>
	/// Gets a decision that dismisses the active completion session.
	/// </summary>
	/// <remarks>
	/// Hosts produce this decision when a completion trigger no longer applies; the shared
	/// <see cref="TextCompletionSessionKernel"/> never returns it.
	/// </remarks>
	public static TextCompletionSessionDecision Close { get; } = new(true, null, null, null);

	/// <summary>
	/// Gets a decision that reports no items because the current word filtered every candidate out.
	/// </summary>
	/// <remarks>
	/// The shared <see cref="TextCompletionSessionKernel"/> returns this decision when the provider
	/// returned candidates that the word filter removed completely, so a host can dismiss a session
	/// that narrowed to nothing instead of keeping stale entries. Like <see cref="None"/>, the
	/// decision does not request a close: the host decides how the session reacts, and a host that
	/// does not distinguish the state can treat it exactly like <see cref="None"/>.
	/// </remarks>
	public static TextCompletionSessionDecision NoMatches { get; } = new(false, null, null, null, true);

	/// <summary>
	/// Creates a decision that opens or refreshes the completion session for the supplied range.
	/// </summary>
	/// <remarks>
	/// The decision captures the current contents of <paramref name="items"/>, so later changes to the
	/// caller's collection do not affect the returned decision. An empty list is rejected because an
	/// empty session has no presentation; use <see cref="None"/> when the provider returned nothing
	/// and <see cref="NoMatches"/> when the current word filtered every candidate out.
	/// </remarks>
	/// <param name="items">The completion items to display; at least one item is required.</param>
	/// <param name="startOffset">The zero-based replacement start offset.</param>
	/// <param name="endOffset">The zero-based replacement end offset.</param>
	/// <returns>An opening completion-session decision.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="items"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="items"/> is empty.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is negative, or <paramref name="endOffset"/> is before
	/// <paramref name="startOffset"/>.
	/// </exception>
	public static TextCompletionSessionDecision Open(
		IReadOnlyList<TextCompletionItem> items,
		int startOffset,
		int endOffset)
	{
		ArgumentNullException.ThrowIfNull(items);

		if (items.Count == 0)
			throw new ArgumentException("The decision must carry at least one item.", nameof(items));

		// The offsets are validated here so an exception from Open reports Open's own parameter
		// names instead of the constructor's.
		if (startOffset < 0)
			throw new ArgumentOutOfRangeException(nameof(startOffset), startOffset, "The replacement start offset must not be negative.");

		if (endOffset < startOffset)
			throw new ArgumentOutOfRangeException(nameof(endOffset), endOffset, "The replacement end offset must not be before the start offset.");

		return new(false, Array.AsReadOnly([.. items]), startOffset, endOffset);
	}

	/// <summary>
	/// Deconstructs the decision into its state components, including
	/// <see cref="AllItemsFilteredOut"/> so the <see cref="NoMatches"/> state stays distinguishable
	/// from <see cref="None"/>.
	/// </summary>
	/// <param name="shouldClose">Receives whether the session should be dismissed.</param>
	/// <param name="items">Receives the items to show, when opening.</param>
	/// <param name="startOffset">Receives the replacement start offset, when opening.</param>
	/// <param name="endOffset">Receives the replacement end offset, when opening.</param>
	/// <param name="allItemsFilteredOut">Receives whether the decision reports a filtered-out result.</param>
	public void Deconstruct(
		out bool shouldClose,
		out IReadOnlyList<TextCompletionItem>? items,
		out int? startOffset,
		out int? endOffset,
		out bool allItemsFilteredOut)
	{
		shouldClose = ShouldClose;
		items = Items;
		startOffset = StartOffset;
		endOffset = EndOffset;
		allItemsFilteredOut = AllItemsFilteredOut;
	}
}
