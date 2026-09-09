using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerResponseParserTests
{
	[TestMethod]
	public void ParseSignatureHelp_UsesParameterLabelOffsetsAndActiveParameter()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 0,
				activeParameter = 1,
				signatures = new[]
				{
					new
					{
						label = "spawn(room, objectName)",
						documentation = new
						{
							kind = "markdown",
							value = "Spawns an object."
						},
						parameters = new object[]
						{
							new
							{
								label = new[] { 6, 10 },
								documentation = "Room id."
							},
							new
							{
								label = new[] { 12, 22 },
								documentation = "Object name."
							}
						}
					}
				}
			}));

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(1, signatureInfo.Signatures.Count);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room, objectName)", signatureInfo.ActiveSignature.Label);
		Assert.AreEqual("Spawns an object.", signatureInfo.ActiveSignature.Documentation);
		Assert.AreEqual(1, signatureInfo.ActiveParameterIndex);
		Assert.AreEqual(2, signatureInfo.ActiveSignature.Parameters.Count);
		Assert.AreEqual("objectName", signatureInfo.ActiveSignature.Parameters[1].Label);
		Assert.AreEqual("Object name.", signatureInfo.ActiveSignature.Parameters[1].Documentation);
	}

	[TestMethod]
	public void ParseSignatureHelp_MalformedMiddleSignature_DoesNotShiftTheActiveSignature()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 2,
				signatures = new object[]
				{
					new { label = "first()" },
					42,
					new { label = "third()" },
					new { label = "fourth()" }
				}
			}));

		// The malformed element keeps its array position as an unusable placeholder, so the active
		// index still selects the signature the server meant instead of the one after it.
		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(3, signatureInfo.Signatures.Count);
		Assert.AreEqual("first()", signatureInfo.Signatures[0].Label);
		Assert.AreEqual("third()", signatureInfo.Signatures[1].Label);
		Assert.AreEqual("fourth()", signatureInfo.Signatures[2].Label);
		Assert.AreEqual(1, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("third()", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void ParseSignatureHelp_NormalizesMarkupForThePlainTextModel()
	{
		const string documentation = "Summary\n```lua\nlocal value = 1\n```";

		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				signatures = new[]
				{
					new
					{
						label = "spawn()",
						documentation = new
						{
							kind = "markdown",
							value = documentation
						}
					}
				}
			}));

		// The signature model is plain text only, so fence lines are normalized away before construction.
		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual("Summary\nlocal value = 1", signatureInfo.ActiveSignature.Documentation);
	}

	[TestMethod]
	public void ParseSignatureHelp_ExplicitNullActiveParameter_PreservesTheNoActiveParameterState()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 0,
				activeParameter = (int?)null,
				signatures = new[]
				{
					new
					{
						label = "spawn(room, objectName)",
						parameters = new object[]
						{
							new { label = "room" },
							new { label = "objectName" }
						}
					}
				}
			}));

		// LSP 3.18: an explicit null means no parameter is active, unlike an omitted property.
		Assert.IsNotNull(signatureInfo);
		Assert.IsNull(signatureInfo.ActiveParameterIndex);
	}

	[TestMethod]
	public void ParseSignatureHelp_NegativeActiveParameter_MapsToNoActiveParameter()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 0,
				activeParameter = -1,
				signatures = new[]
				{
					new
					{
						label = "spawn(room, objectName)",
						parameters = new object[]
						{
							new { label = "room" },
							new { label = "objectName" }
						}
					}
				}
			}));

		// A negative index is never valid: it follows the pre-3.18 "no active parameter" convention,
		// so no parameter is highlighted instead of the first one.
		Assert.IsNotNull(signatureInfo);
		Assert.IsNull(signatureInfo.ActiveParameterIndex);
	}

	[TestMethod]
	public void ParseSignatureHelp_ExplicitNullSignatureActiveParameter_KeepsPayloadFromApplying()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 0,
				activeParameter = 1,
				signatures = new[]
				{
					new
					{
						label = "move(x, y)",
						activeParameter = (int?)null,
						parameters = new object[]
						{
							new { label = "x" },
							new { label = "y" }
						}
					}
				}
			}));

		// A signature-level explicit null overrides the payload-level index, per the LSP precedence rule.
		Assert.IsNotNull(signatureInfo);
		Assert.IsNull(signatureInfo.ActiveParameterIndex);
	}

	[TestMethod]
	public void ParseSignatureHelp_UsesSignatureLevelActiveParameterWhenResponseOmitsIt()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				signatures = new[]
				{
					new
					{
						label = "move(x, y)",
						activeParameter = 1,
						parameters = new object[]
						{
							new { label = "x" },
							new { label = "y" }
						}
					}
				}
			}));

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(1, signatureInfo.ActiveParameterIndex);
		Assert.AreEqual("y", signatureInfo.ActiveSignature.Parameters[1].Label);
	}

	[TestMethod]
	public void ParseSignatureHelp_OutOfRangeActiveParameter_FallsBackToFirstParameter()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 0,
				activeParameter = 9,
				signatures = new[]
				{
					new
					{
						label = "spawn(room, objectName)",
						parameters = new object[]
						{
							new { label = "room" },
							new { label = "objectName" }
						}
					}
				}
			}));

		// The LSP default rule selects the first parameter when the protocol value is outside the range.
		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(2, signatureInfo.ActiveSignature.Parameters.Count);
		Assert.AreEqual(0, signatureInfo.ActiveParameterIndex);
		Assert.AreEqual("room", signatureInfo.ActiveSignature.Parameters[signatureInfo.ActiveParameterIndex!.Value].Label);
	}

	[TestMethod]
	public void ParseSignatureHelp_PreservesAllSignaturesAndActiveSelection()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 1,
				signatures = new[]
				{
					new { label = "spawn(room)" },
					new { label = "spawn(room, objectName)" }
				}
			}));

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(2, signatureInfo.Signatures.Count);
		Assert.AreEqual(1, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room, objectName)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void ParseSignatureHelp_SignatureLevelActiveParameterOverridesResponseLevel()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 0,
				activeParameter = 0,
				signatures = new[]
				{
					new
					{
						label = "move(x, y)",
						activeParameter = 1,
						parameters = new object[]
						{
							new { label = "x" },
							new { label = "y" }
						}
					}
				}
			}));

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(1, signatureInfo.ActiveParameterIndex);
		Assert.AreEqual("y", signatureInfo.ActiveSignature.Parameters[1].Label);
	}

	[TestMethod]
	public void ParseSignatureHelp_SkipsSignaturesWithoutLabels()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = 0,
				signatures = new[]
				{
					new { label = "   " },
					new { label = "spawn(room)" }
				}
			}));

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(1, signatureInfo.Signatures.Count);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void ParseSignatureHelp_NegativeActiveSignature_SelectsTheFirstSignature()
	{
		TextSignatureHelp? signatureInfo = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new
			{
				activeSignature = -3,
				signatures = new[]
				{
					new { label = "spawn(room)" },
					new { label = "spawn(room, objectName)" }
				}
			}));

		Assert.IsNotNull(signatureInfo);
		Assert.AreEqual(0, signatureInfo.ActiveSignatureIndex);
		Assert.AreEqual("spawn(room)", signatureInfo.ActiveSignature.Label);
	}

	[TestMethod]
	public void ParseSignatureHelp_FractionalActiveParameter_BehavesLikeTheAbsentValue()
	{
		object signature = new
		{
			label = "move(x, y)",
			parameters = new object[]
			{
				new { label = "x" },
				new { label = "y" }
			}
		};

		TextSignatureHelp? absent = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new { activeSignature = 0, signatures = new[] { signature } }));
		TextSignatureHelp? fractional = LuaLanguageServerResponseParser.ParseSignatureHelp(
			DeserializeSignatureHelpResponse(new { activeSignature = 0, activeParameter = 1.5, signatures = new[] { signature } }));

		// A fractional value cannot be an index; it falls back to the not-specified sentinel like an
		// absent value.
		Assert.IsNotNull(absent);
		Assert.IsNotNull(fractional);
		Assert.AreEqual(absent.ActiveParameterIndex, fractional.ActiveParameterIndex);
	}
}
