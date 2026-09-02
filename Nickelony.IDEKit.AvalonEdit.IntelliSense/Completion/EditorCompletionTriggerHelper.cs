namespace Nickelony.IDEKit.AvalonEdit.IntelliSense.Completion;

/// <summary>
/// Provides the shared heuristics used to decide when completion should trigger.
/// </summary>
public static class EditorCompletionTriggerHelper
{
	/// <summary>
	/// Determines whether the input represents a Ctrl+Space completion request.
	/// </summary>
	/// <param name="inputText">The input text to check.</param>
	/// <param name="isCtrlDown">Whether the Control modifier key is pressed.</param>
	/// <returns><see langword="true"/> when the input is a space with the Control modifier; otherwise, <see langword="false"/>.</returns>
	public static bool IsCtrlSpaceInput(string? inputText, bool isCtrlDown)
		=> inputText == " " && isCtrlDown;

	/// <summary>
	/// Determines whether the line text consists of exactly one character.
	/// </summary>
	/// <param name="currentLineText">The current line text.</param>
	/// <returns><see langword="true"/> when the line is a single character; otherwise, <see langword="false"/>.</returns>
	public static bool IsSingleCharacterLine(string? currentLineText)
		=> currentLineText?.Length == 1;

	/// <summary>
	/// Determines whether the line text consists of one character that matches the given predicate.
	/// </summary>
	/// <param name="currentLineText">The current line text.</param>
	/// <param name="characterPredicate">The predicate the single character must satisfy.</param>
	/// <returns><see langword="true"/> when the line is a single matching character; otherwise, <see langword="false"/>.</returns>
	public static bool IsSingleCharacterLine(string? currentLineText, Func<char, bool> characterPredicate)
		=> currentLineText?.Length == 1 && characterPredicate(currentLineText[0]);
}
