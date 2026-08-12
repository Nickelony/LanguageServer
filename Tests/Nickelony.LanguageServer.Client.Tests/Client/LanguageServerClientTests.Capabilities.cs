using System.Reflection;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public void FreshClient_ExposesConservativeCapabilitiesBeforeStartup()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(0L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.AreEqual(0, client.SemanticTokenTypes.Count);
		Assert.AreEqual(0, client.SemanticTokenModifiers.Count);
		Assert.IsFalse(client.SupportsCompletionResolve);
		Assert.IsFalse(client.SupportsReferences);
		Assert.IsFalse(client.SupportsRename);
		Assert.IsFalse(client.SupportsFormatting);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.IsFalse(client.SupportsSemanticTokensDelta);
	}

	[TestMethod]
	public void ActiveSessionBeforeHandshake_ExposesConservativeCapabilities()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);
		object session = CreateTransportSession(client, 3, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(3L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.AreEqual(0, client.SemanticTokenTypes.Count);
		Assert.AreEqual(0, client.SemanticTokenModifiers.Count);
		Assert.IsFalse(client.SupportsCompletionResolve);
		Assert.IsFalse(client.SupportsReferences);
		Assert.IsFalse(client.SupportsRename);
		Assert.IsFalse(client.SupportsFormatting);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.IsFalse(client.SupportsSemanticTokensDelta);
	}

	[TestMethod]
	public void CaptureServerCapabilities_UsesFullTextSyncWhenServerAdvertisesFullSync()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    }
			  }
			}
			"""));

		Assert.AreEqual(TextDocumentSyncKind.Full, client.TextDocumentSyncKind);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RejectsMissingDocumentChangeSupport()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		TargetInvocationException exception = Assert.ThrowsExactly<TargetInvocationException>(() =>
			InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {}
				  }
				}
				""")));

		Assert.IsInstanceOfType(exception.InnerException, typeof(NotSupportedException));
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesReferenceRenameAndFormattingProviders()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "referencesProvider": {},
			    "renameProvider": { "prepareProvider": true },
			    "documentFormattingProvider": true
			  }
			}
			"""));

		Assert.IsTrue(client.SupportsReferences);
		Assert.IsTrue(client.SupportsRename);
		Assert.IsTrue(client.SupportsFormatting);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesSemanticTokensLegendAndDeltaSupport()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "semanticTokensProvider": {
			      "full": {
			        "delta": true
			      },
			      "legend": {
			        "tokenTypes": ["function", "variable"],
			        "tokenModifiers": ["declaration"]
			      }
			    }
			  }
			}
			"""));

		Assert.IsTrue(client.SupportsSemanticTokensDelta);
		Assert.IsTrue(client.SupportsSemanticTokensFull);

		CollectionAssert.AreEqual(new[] { "function", "variable" }, client.SemanticTokenTypes.ToArray());
		CollectionAssert.AreEqual(new[] { "declaration" }, client.SemanticTokenModifiers.ToArray());
	}

	[TestMethod]
	public void CaptureServerCapabilities_TracksWhenFullSemanticTokensAreUnsupported()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "semanticTokensProvider": {
			      "full": false,
			      "legend": {
			        "tokenTypes": ["function"],
			        "tokenModifiers": []
			      }
			    }
			  }
			}
			"""));

		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.IsFalse(client.SupportsSemanticTokensDelta);

		CollectionAssert.AreEqual(new[] { "function" }, client.SemanticTokenTypes.ToArray());
	}

	[TestMethod]
	public void CaptureServerCapabilities_RejectsMissingCapabilitiesPayload()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		TargetInvocationException exception = Assert.ThrowsExactly<TargetInvocationException>(() =>
			InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse("""{}""")));

		Assert.IsInstanceOfType(exception.InnerException, typeof(NotSupportedException));
	}

	[TestMethod]
	public void CaptureServerCapabilities_RejectsMissingTextDocumentSyncCapability()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		TargetInvocationException exception = Assert.ThrowsExactly<TargetInvocationException>(() =>
			InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "referencesProvider": true,
				    "renameProvider": true
				  }
				}
				""")));

		Assert.IsInstanceOfType(exception.InnerException, typeof(NotSupportedException));
	}

	[TestMethod]
	public void DeserializeInitializeResponse_UnsupportedCapabilityShapesDegradePredictably()
	{
		InitializeResponse response = DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": "invalid"
			    },
			    "referencesProvider": "unexpected",
			    "renameProvider": [true],
			    "documentFormattingProvider": 123,
			    "semanticTokensProvider": {
			      "full": [],
			      "legend": {
			        "tokenTypes": ["function"],
			        "tokenModifiers": ["declaration"]
			      }
			    }
			  }
			}
			""");

		Assert.IsNotNull(response.Capabilities);
		Assert.AreEqual(TextDocumentSyncKind.None, response.Capabilities.TextDocumentSync?.Kind);
		Assert.IsFalse(response.Capabilities.ReferencesProvider?.IsSupported ?? true);
		Assert.IsFalse(response.Capabilities.RenameProvider?.IsSupported ?? true);
		Assert.IsFalse(response.Capabilities.DocumentFormattingProvider?.IsSupported ?? true);
		Assert.IsFalse(response.Capabilities.SemanticTokensProvider?.Full?.SupportsDelta ?? true);

		CollectionAssert.AreEqual(new[] { "function" }, response.Capabilities.SemanticTokensProvider?.Legend?.TokenTypes);
		CollectionAssert.AreEqual(new[] { "declaration" }, response.Capabilities.SemanticTokensProvider?.Legend?.TokenModifiers);
	}

	[TestMethod]
	public void DeserializeInitializeResponse_BooleanTextDocumentSyncDegradesToNone()
	{
		InitializeResponse response = DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": true
			  }
			}
			""");

		Assert.IsNotNull(response.Capabilities);
		Assert.AreEqual(TextDocumentSyncKind.None, response.Capabilities.TextDocumentSync?.Kind);
	}

	[TestMethod]
	public void SemanticTokenCapabilityLists_CannotBeMutatedThroughCollectionCasts()
	{
		using var client = new LanguageServerClient(@"C:\Workspace", "lua-language-server.exe", s_defaultClientOptions);

		InvokePrivateMethod(client, "CaptureServerCapabilities", DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "semanticTokensProvider": {
			      "legend": {
			        "tokenTypes": ["function"],
			        "tokenModifiers": ["declaration"]
			      }
			    }
			  }
			}
			"""));

		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)client.SemanticTokenTypes)[0] = "class");
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)client.SemanticTokenModifiers)[0] = "readonly");

		CollectionAssert.AreEqual(new[] { "function" }, client.SemanticTokenTypes.ToArray());
		CollectionAssert.AreEqual(new[] { "declaration" }, client.SemanticTokenModifiers.ToArray());
	}
}
