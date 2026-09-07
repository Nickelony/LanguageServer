using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>
/// Tracks editor navigation positions as a back/forward history, recording only positions that
/// represent a meaningful change from the previously recorded position.
/// </summary>
/// <typeparam name="TLocation">The host's location type.</typeparam>
public sealed class NavigationHistory<TLocation>
{
	private readonly Stack<TLocation> _backStack = new();
	private readonly Stack<TLocation> _forwardStack = new();
	private readonly Func<TLocation, TLocation, bool> _isEquivalent;
	private readonly Func<TLocation, TLocation, bool> _isMeaningfulChange;

	// The current position is read only when this flag is set.
	private bool _hasCurrentLocation;
	private TLocation _currentLocation;
	private int _suppressionDepth;

	/// <summary>
	/// Creates a navigation history that records positions using the supplied comparison predicates.
	/// </summary>
	/// <param name="isEquivalent">Determines whether two positions are equivalent and should not both be recorded.</param>
	/// <param name="isMeaningfulChange">Determines whether a new position is a meaningful move from the previously recorded position.</param>
	/// <exception cref="ArgumentNullException">Thrown when either predicate is <see langword="null"/>.</exception>
	public NavigationHistory(
		Func<TLocation, TLocation, bool> isEquivalent,
		Func<TLocation, TLocation, bool> isMeaningfulChange)
	{
		ArgumentNullException.ThrowIfNull(isEquivalent);
		ArgumentNullException.ThrowIfNull(isMeaningfulChange);

		_isEquivalent = isEquivalent;
		_isMeaningfulChange = isMeaningfulChange;
		_currentLocation = default!;
	}

	/// <summary>
	/// Gets whether a previous position can be navigated to.
	/// </summary>
	public bool CanNavigateBack => _backStack.Count > 0;

	/// <summary>
	/// Gets whether a later position can be navigated to.
	/// </summary>
	public bool CanNavigateForward => _forwardStack.Count > 0;

	/// <summary>
	/// Observes the current editor position, recording the previous position and clearing the
	/// forward stack when the change is meaningful. Equivalent or otherwise non-meaningful changes
	/// update the current position without changing the stacks.
	/// </summary>
	/// <param name="location">The current editor position.</param>
	public void Observe(TLocation location)
	{
		if (_suppressionDepth > 0)
		{
			_currentLocation = location;
			_hasCurrentLocation = true;
			return;
		}

		if (!_hasCurrentLocation)
		{
			_currentLocation = location;
			_hasCurrentLocation = true;
			return;
		}

		if (!_isMeaningfulChange(_currentLocation, location))
		{
			_currentLocation = location;
			return;
		}

		PushDistinct(_backStack, _currentLocation);
		_forwardStack.Clear();
		_currentLocation = location;
	}

	/// <summary>
	/// Records the position before a programmatic jump, such as definition navigation, without
	/// observing the intermediate caret movement. The target is used to determine whether the jump
	/// is equivalent; callers must update the current position after the jump.
	/// </summary>
	/// <param name="currentLocation">The position before the programmatic jump.</param>
	/// <param name="targetLocation">The position reached by the programmatic jump.</param>
	public void RecordProgrammaticJump(TLocation currentLocation, TLocation targetLocation)
	{
		_currentLocation = currentLocation;
		_hasCurrentLocation = true;

		if (_isEquivalent(currentLocation, targetLocation))
			return;

		PushDistinct(_backStack, currentLocation);
		_forwardStack.Clear();
	}

	/// <summary>
	/// Sets the current position without recording anything.
	/// </summary>
	/// <param name="location">The position to set as current.</param>
	public void SetCurrentLocation(TLocation location)
	{
		_currentLocation = location;
		_hasCurrentLocation = true;
	}

	/// <summary>
	/// Tries to navigate to the previous position, recording <paramref name="currentLocation"/>
	/// on the forward stack.
	/// </summary>
	/// <param name="currentLocation">The position to record on the forward stack.</param>
	/// <param name="targetLocation">The previous position, valid only when the method returns <see langword="true"/>.</param>
	public bool TryNavigateBack(TLocation currentLocation, [MaybeNullWhen(false)] out TLocation targetLocation)
	{
		targetLocation = default;

		if (_backStack.Count == 0)
			return false;

		PushDistinct(_forwardStack, currentLocation);
		targetLocation = _backStack.Pop();
		_currentLocation = targetLocation;
		_hasCurrentLocation = true;
		return true;
	}

	/// <summary>
	/// Tries to navigate to the next position, recording <paramref name="currentLocation"/>
	/// on the back stack.
	/// </summary>
	/// <param name="currentLocation">The position to record on the back stack.</param>
	/// <param name="targetLocation">The next position, valid only when the method returns <see langword="true"/>.</param>
	public bool TryNavigateForward(TLocation currentLocation, [MaybeNullWhen(false)] out TLocation targetLocation)
	{
		targetLocation = default;

		if (_forwardStack.Count == 0)
			return false;

		PushDistinct(_backStack, currentLocation);
		targetLocation = _forwardStack.Pop();
		_currentLocation = targetLocation;
		_hasCurrentLocation = true;
		return true;
	}

	/// <summary>
	/// Suppresses position recording until the returned scope is disposed.
	/// </summary>
	public IDisposable SuppressRecording()
	{
		_suppressionDepth++;
		return new RecordingScope(this);
	}

	private void PushDistinct(Stack<TLocation> stack, TLocation location)
	{
		if (stack.Count == 0 || !_isEquivalent(stack.Peek(), location))
			stack.Push(location);
	}

	private sealed class RecordingScope : IDisposable
	{
		private NavigationHistory<TLocation>? _owner;

		public RecordingScope(NavigationHistory<TLocation> owner) => _owner = owner;

		public void Dispose()
		{
			if (_owner is null)
				return;

			_owner._suppressionDepth = Math.Max(0, _owner._suppressionDepth - 1);
			_owner = null;
		}
	}
}
