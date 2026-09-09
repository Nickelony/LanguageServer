namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed class LuaDocumentVersionPolicyTests
{
	[TestMethod]
	public void TryAccept_RejectsOlderPositiveVersion()
	{
		bool accepted = LuaDocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 4, out int acceptedVersion);

		Assert.IsFalse(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}

	[TestMethod]
	public void TryAccept_PreservesCurrentVersionForUnversionedPayload()
	{
		bool accepted = LuaDocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 0, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}

	[TestMethod]
	public void TryAccept_AdvancesToNewerPositiveVersion()
	{
		bool accepted = LuaDocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 6, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(6, acceptedVersion);
	}

	[TestMethod]
	public void TryAccept_KeepsIdenticalVersion()
	{
		bool accepted = LuaDocumentVersionPolicy.TryAccept(currentVersion: 5, incomingVersion: 5, out int acceptedVersion);

		Assert.IsTrue(accepted);
		Assert.AreEqual(5, acceptedVersion);
	}

	[TestMethod]
	public void IsPayloadCurrent_AcceptsMatchingOrUnknownVersionsOnly()
	{
		Assert.IsTrue(LuaDocumentVersionPolicy.IsPayloadCurrent(documentVersion: 0, payloadVersion: 5));
		Assert.IsTrue(LuaDocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 0));
		Assert.IsTrue(LuaDocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 5));
		Assert.IsFalse(LuaDocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 4));
		Assert.IsFalse(LuaDocumentVersionPolicy.IsPayloadCurrent(documentVersion: 5, payloadVersion: 6));
	}
}
