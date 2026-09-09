namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditPreparationResultTests
{
	[TestMethod]
	public void Valid_NullEdits_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditPreparationResult.Valid(null!));
	}

	[TestMethod]
	public void Valid_EmptyEdits_IsValidWithoutDiagnostics()
	{
		TextEditPreparationResult result = TextEditPreparationResult.Valid(new PreparedTextEdits([]));

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(0, result.Diagnostics.Count);
	}

	[TestMethod]
	public void Invalid_EmptyDiagnostics_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => TextEditPreparationResult.Invalid([]));
	}

	[TestMethod]
	public void Invalid_WithDiagnostics_IsInvalidWithoutOperations()
	{
		TextEditPreparationResult result = TextEditPreparationResult.Invalid(
			[new TextEditPreparationDiagnostic(0, null, "The edit is null.")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(0, result.Edits.Operations.Count);
		Assert.AreEqual(1, result.Diagnostics.Count);
	}
}
