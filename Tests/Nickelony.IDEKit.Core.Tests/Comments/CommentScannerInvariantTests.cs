using System.Diagnostics;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentScannerInvariantTests
{
	private static readonly CommentSyntax s_cStyle = CommentSyntaxFixtures.CStyleVerbatim;
	private static readonly CommentSyntax s_luaStyle = CommentSyntaxFixtures.LuaNestedLongBracket;
	private static readonly CommentSyntax s_nestedCStyle = CommentSyntaxFixtures.CStyleNestedDoubleQuoted;

	[TestMethod]
	public void MaskComments_CommentOnlyText_BecomesSpacesOfTheSameLength()
	{
		// The masked sample is asserted directly, so a scanner that stops detecting the comment
		// fails here even when the length and terminator invariants stay intact.
		const string text = "// only a comment";

		string masked = CommentOperations.MaskComments(text, s_cStyle);

		Assert.AreEqual(new string(' ', text.Length), masked);
	}

	[TestMethod]
	public void FindComment_LongQuoteRunInsideARawString_ScansWithoutQuadraticRescan()
	{
		const int QuoteCount = 200_000;

		// The opening run itself becomes the raw-string delimiter, so every following quote of the
		// run is string content. A scanner that rescans the shrinking run from every quote compares
		// about QuoteCount^2 / 2 characters (2 x 10^10 here); the single-pass run skip stays linear.
		string text = new string('"', QuoteCount) + "x; // not a comment";

		// Warm both shapes once so JIT and first-use allocations stay out of the timed runs.
		_ = CommentOperations.FindComment(text, s_cStyle);
		_ = CommentOperations.FindComment(new string('"', QuoteCount * 4) + "x; // not a comment", s_cStyle);

		var stopwatch = Stopwatch.StartNew();
		CommentSpan? comment = CommentOperations.FindComment(text, s_cStyle);
		stopwatch.Stop();
		TimeSpan smallElapsed = stopwatch.Elapsed;

		Assert.IsNull(comment);

		// The guard is a scaling probe, not a wall-clock budget: the same scan runs once more at four
		// times the size, and a quadratic rescan (about sixteen times the per-quote cost) fails the
		// bound while a linear scan stays near five times. A floor keeps a sub-millisecond baseline
		// from turning timer noise into a failure.
		string largerText = new string('"', QuoteCount * 4) + "x; // not a comment";

		stopwatch.Restart();
		_ = CommentOperations.FindComment(largerText, s_cStyle);
		stopwatch.Stop();

		long baselineTicks = Math.Max(smallElapsed.Ticks, TimeSpan.FromMilliseconds(5.0).Ticks);
		Assert.IsTrue(
			stopwatch.Elapsed.Ticks <= baselineTicks * 10,
			$"Scanning four times the quote count took {stopwatch.Elapsed} versus {smallElapsed}; the run is rescanned per quote.");
	}

	[TestMethod]
	public void FindComment_ShortQuoteRunInsideARawString_IsContent()
	{
		// The two-quote run is shorter than the three-quote delimiter, so it cannot close the
		// string; only the real line comment at the end is found.
		string text = "\"\"\"\nab\"\"cd\n\"\"\"\n// real comment";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cStyle);

		Assert.IsNotNull(comment);
		Assert.IsTrue(comment.Value.IsLineComment);
		Assert.AreEqual(text.IndexOf("//", StringComparison.Ordinal), comment.Value.DelimiterStart);
	}

	[TestMethod]
	public void MaskComments_PreservesLengthTerminatorsAndDestroysEveryComment()
	{
		string[] samples =
		[
			"code // tail",
			"// only a comment",
			"a /* block */ b /* second */ c",
			"line one\r\n// comment line\r\nline three\n",
			"/* unclosed block",
			"verbatim @\"// not a comment\" // real",
			"\"\"\"\n// raw content\n\"\"\" // real",
			"/* nested /* inner */ still */ after",
			"",
			"\r",
			"\r\n",
		];

		foreach (string text in samples)
		{
			string masked = CommentOperations.MaskComments(text, s_cStyle);

			Assert.AreEqual(text.Length, masked.Length, $"Masked length for '{text}'.");

			for (int index = 0; index < text.Length; index++)
			{
				bool textIsTerminator = text[index] is '\r' or '\n';
				bool maskedIsTerminator = masked[index] is '\r' or '\n';

				Assert.AreEqual(textIsTerminator, maskedIsTerminator, $"Terminator mismatch at {index} for '{text}'.");
			}

			// Masking replaces comment characters with spaces, so no comment may survive it.
			Assert.IsNull(CommentOperations.FindComment(masked, s_cStyle), $"A comment survived masking of '{text}'.");
		}
	}

	[TestMethod]
	public void MaskComments_LargeDocument_PreservesLengthAndLineStructure()
	{
		const int LineCount = 20_000;
		string text = string.Concat(Enumerable.Repeat("local value = 1 // note\n", LineCount));

		// Warm both shapes once so JIT and first-use allocations stay out of the timed runs.
		_ = CommentOperations.MaskComments(text, s_cStyle);
		_ = CommentOperations.MaskComments(
			string.Concat(Enumerable.Repeat("local value = 1 // note\n", LineCount * 4)),
			s_cStyle);

		var stopwatch = Stopwatch.StartNew();
		string masked = CommentOperations.MaskComments(text, s_cStyle);
		stopwatch.Stop();
		TimeSpan smallElapsed = stopwatch.Elapsed;

		Assert.AreEqual(text.Length, masked.Length);
		Assert.AreEqual(LineCount, masked.Count(character => character == '\n'));
		Assert.IsNull(CommentOperations.FindComment(masked, s_cStyle));

		// Scaling probe: quadrupling the line count must stay near the linear factor; a quadratic
		// pipeline fails the bound. A floor keeps a sub-millisecond baseline from turning timer
		// noise into a failure.
		string largerText = string.Concat(Enumerable.Repeat("local value = 1 // note\n", LineCount * 4));

		stopwatch.Restart();
		string largerMasked = CommentOperations.MaskComments(largerText, s_cStyle);
		stopwatch.Stop();

		Assert.AreEqual(largerText.Length, largerMasked.Length);

		long baselineTicks = Math.Max(smallElapsed.Ticks, TimeSpan.FromMilliseconds(5.0).Ticks);
		Assert.IsTrue(
			stopwatch.Elapsed.Ticks <= baselineTicks * 10,
			$"Masking four times the line count took {stopwatch.Elapsed} versus {smallElapsed}; the pipeline is quadratic.");
	}

	[TestMethod]
	public void EnumerateComments_YieldsOrderedNonOverlappingSpansWithinTheText()
	{
		// Every sample pairs a syntax with the number of comments the scanner must find.
		(string Text, CommentSyntax Syntax, int ExpectedCount)[] samples =
		[
			("a // one\nb /* two */ c", s_cStyle, 2),
			("/*/* nested */*/ // tail", s_cStyle, 2),
			("no comments here", s_cStyle, 0),
			("/* outer /* inner */ tail */ code", s_nestedCStyle, 1),
			("--[[ three ]] -- four", s_luaStyle, 2),
		];

		foreach ((string text, CommentSyntax syntax, int expectedCount) in samples)
		{
			var previousEnd = -1;
			int count = 0;

			foreach (CommentSpan span in CommentOperations.EnumerateComments(text, syntax))
			{
				Assert.IsTrue(
					span.SpanStart >= 0
					&& span.SpanStart >= previousEnd
					&& span.DelimiterStart >= span.SpanStart
					&& span.DelimiterStart <= span.End
					&& span.End <= text.Length,
					$"Invalid span [{span.SpanStart}..{span.End}) (delimiter {span.DelimiterStart}) for '{text}'.");

				previousEnd = span.End;
				count++;
			}

			Assert.AreEqual(expectedCount, count, $"Comment count for '{text}'.");
		}
	}
}
