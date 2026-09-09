namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers constructor validation: workspace roots, provider options, unusable document paths, and the
/// missing-client report contract.
/// </summary>
[TestClass]
public sealed class LanguageServerIntelliSenseProviderBaseValidationTests
{
	private const string Content = "line one";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-validation-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	[TestMethod]
	public void Constructor_WithNullWorkspaceRoots_ThrowsArgumentNullException()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => new TestLanguageServerProvider(null!, new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithEmptyWorkspaceRoots_ThrowsArgumentException()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider([], new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithWhitespaceWorkspaceRoot_ThrowsArgumentException()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider(["   "], new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithDuplicateWorkspaceRoots_ThrowsArgumentException()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider([s_workspaceRoot, s_workspaceRoot], new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithCaseVariantDuplicateRoots_ThrowsOnCaseInsensitivePlatforms()
	{
		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
			Assert.Inconclusive("Case-variant duplicates are distinct paths under case-sensitive path identity.");

		string root = Path.Combine(Path.GetTempPath(), "ls-root-case");
		string caseVariantRoot = Path.Combine(Path.GetTempPath(), "LS-ROOT-CASE");

		Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider([root, caseVariantRoot], new FakeLanguageServerClient()));
	}

	[TestMethod]
	public void Constructor_WhenConstructionFailsAfterClientTransfer_DisposesTheClient()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<InvalidOperationException>(() => new ThrowingDocumentStoreProvider([s_workspaceRoot], client));

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
	}

	[TestMethod]
	public void Constructor_WhenRootValidationFails_LeavesTheUnownedClientAlive()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider([], client));

		Assert.AreEqual(0, client.DisposeCallCount);
	}

	[TestMethod]
	public void Constructor_WithDefaultOptions_IsValid()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client, LanguageServerProviderOptions.Default);

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
	}

	[TestMethod]
	public void Constructor_WithNonPositiveRequestTimeout_ThrowsArgumentOutOfRangeException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeout = TimeSpan.Zero }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds(-2) }));
	}

	[TestMethod]
	public void Constructor_WithTooLargeRequestTimeout_ThrowsArgumentOutOfRangeException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1) }));
	}

	[TestMethod]
	public void Constructor_WithInvalidThresholds_ThrowArgumentOutOfRangeException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeoutRestartThreshold = 0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { HardStartupFailureThreshold = 0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = -1 }));
	}

	[TestMethod]
	public void Constructor_WithZeroRequestOnlyDocumentCap_IsValid()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = 0 });

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
	}

	[TestMethod]
	public async Task DocumentMembers_WithUnusablePath_AreNoOps()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		Assert.AreEqual(0, provider.GetDiagnostics("   ").Count);
		provider.OpenDocument("   ", Content);
		provider.UpdateDocument("   ", Content);
		provider.CloseDocument("   ");
		provider.RenameDocument("   ", s_filePath, Content);
		provider.RenameDocument(s_filePath, "   ", Content);

		await Task.Delay(100).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task MissingClient_OpenDocumentReportsPersistentFailureWithoutThrowing()
	{
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client: null);
		var failures = new List<LanguageServerStartupFailure>();
		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await TestWait.ForConditionAsync(() => failures.Count == 1, TimeSpan.FromSeconds(2)).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Failed, provider.State);
		Assert.IsTrue(failures[0].IsPersistent);
		Assert.AreEqual(0, provider.GetDiagnostics(s_filePath).Count);
	}

	/// <summary>
	/// Test provider whose document-store hook throws, so construction fails after the base class has taken
	/// ownership of the client.
	/// </summary>
	private sealed class ThrowingDocumentStoreProvider : TestLanguageServerProvider
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ThrowingDocumentStoreProvider"/> class.
		/// </summary>
		/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
		/// <param name="client">The language-server client owned by the provider.</param>
		public ThrowingDocumentStoreProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, ILanguageServerClient client)
			: base(workspaceRootDirectoryPaths, client)
		{ }

		/// <inheritdoc/>
		protected override TrackedDocumentStore<TestDocumentState> CreateTrackedDocumentStore()
			=> throw new InvalidOperationException("Simulated construction failure after client ownership transfer.");
	}
}
