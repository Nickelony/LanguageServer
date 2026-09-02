namespace Nickelony.IDEKit.Core.Themes.Tests;

[TestClass]
public sealed class ThemeCatalogTests
{
	private sealed record TestTheme(string Name, IReadOnlyList<string>? Aliases = null);

	private static ThemeCatalog<TestTheme> CreateCatalog(IEnumerable<TestTheme> themes, string defaultThemeName)
		=> new(themes, static theme => theme.Name, static theme => theme.Aliases ?? [], defaultThemeName);

	private static TestTheme Theme(string name, params string[]? aliases)
		=> new(name, aliases);

	[TestMethod]
	public void Themes_AreOrderedWithDefaultFirstThenByName()
	{
		var catalog = CreateCatalog(
		[
			Theme("Zulu"),
			Theme("Alpha"),
			Theme("Default"),
			Theme("beta")
		], "Default");

		CollectionAssert.AreEqual(
			new[]
			{
				"Default",
				"Alpha",
				"beta",
				"Zulu"
			}, catalog.Themes.Select(static theme => theme.Name).ToArray());
	}

	[TestMethod]
	public void GetTheme_ResolvesByName()
	{
		TestTheme alpha = Theme("Alpha");
		var catalog = CreateCatalog([alpha, Theme("Beta")], "Beta");

		Assert.AreSame(alpha, catalog.GetTheme("Alpha"));
	}

	[TestMethod]
	public void GetTheme_ResolvesByAlias()
	{
		TestTheme alpha = Theme("Alpha", "Al");
		var catalog = CreateCatalog([alpha, Theme("Beta")], "Beta");

		Assert.AreSame(alpha, catalog.GetTheme("Al"));
	}

	[TestMethod]
	public void GetTheme_IsCaseInsensitive()
	{
		TestTheme alpha = Theme("Alpha", "Al");
		var catalog = CreateCatalog([alpha, Theme("Beta")], "Beta");

		Assert.AreSame(alpha, catalog.GetTheme("aLpHa"));
		Assert.AreSame(alpha, catalog.GetTheme("aL"));
	}

	[TestMethod]
	public void GetTheme_UnknownName_ReturnsDefault()
	{
		TestTheme beta = Theme("Beta");
		var catalog = CreateCatalog([Theme("Alpha"), beta], "Beta");

		Assert.AreSame(beta, catalog.GetTheme("DoesNotExist"));
	}

	[TestMethod]
	public void GetTheme_WhitespaceOrEmptyName_ReturnsDefault()
	{
		TestTheme beta = Theme("Beta");
		var catalog = CreateCatalog([Theme("Alpha"), beta], "Beta");

		Assert.AreSame(beta, catalog.GetTheme(string.Empty));
		Assert.AreSame(beta, catalog.GetTheme("   "));
	}

	[TestMethod]
	public void DefaultTheme_ResolvesConfiguredSelection()
	{
		TestTheme defaultTheme = Theme("Gamma");
		var catalog = CreateCatalog([Theme("Alpha"), defaultTheme, Theme("Beta")], "Gamma");

		Assert.AreSame(defaultTheme, catalog.DefaultTheme);
	}

	[TestMethod]
	public void DefaultTheme_FallsBackToFirstOrderedWhenSelectionAbsent()
	{
		var catalog = CreateCatalog([Theme("Zulu"), Theme("Alpha"), Theme("beta")], "Missing");

		Assert.AreSame("Alpha", catalog.DefaultTheme.Name);
	}

	[TestMethod]
	public void MalformedEntries_AreIsolatedFromOrderingAndLookup()
	{
		var catalog = CreateCatalog(
		[
			Theme(string.Empty),
			Theme("   "),
			Theme("Valid")
		], "Valid");

		Assert.AreEqual(1, catalog.Themes.Count);
		Assert.AreSame("Valid", catalog.Themes[0].Name);
		Assert.AreSame(catalog.Themes[0], catalog.GetTheme("Valid"));
		Assert.IsFalse(catalog.TryGetTheme("", out _));
	}

	[TestMethod]
	public void DuplicateLookupName_KeepsFirstOrderedOccurrence()
	{
		TestTheme first = Theme("Alpha", "Shared");
		TestTheme second = Theme("Beta", "Shared");
		var catalog = CreateCatalog([first, second], "Gamma");

		Assert.AreSame(first, catalog.GetTheme("Shared"));
		Assert.AreSame(second, catalog.GetTheme("Beta"));
	}

	[TestMethod]
	public void DuplicateThemeName_KeepsFirstOrderedOccurrence()
	{
		TestTheme first = Theme("Alpha");
		TestTheme second = Theme("alpha");
		var catalog = CreateCatalog([first, second], "Beta");

		Assert.AreSame(first, catalog.GetTheme("Alpha"));
	}

	[TestMethod]
	public void NullAliases_AreIgnoredWithoutThrowing()
	{
		var catalog = CreateCatalog([Theme("Alpha", null)], "Alpha");

		Assert.AreSame("Alpha", catalog.GetTheme("Alpha").Name);
		Assert.AreEqual(1, catalog.Themes.Count);
	}

	[TestMethod]
	public void EmptyCatalog_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => CreateCatalog([], "Default"));
	}

	[TestMethod]
	public void CatalogOfOnlyMalformedEntries_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => CreateCatalog([Theme(string.Empty), Theme("  ")], "Default"));
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new ThemeCatalog<TestTheme>(null!, static theme => theme.Name, static theme => theme.Aliases ?? [], "Default"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new ThemeCatalog<TestTheme>([Theme("A")], null!, static theme => theme.Aliases ?? [], "Default"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new ThemeCatalog<TestTheme>([Theme("A")], static theme => theme.Name, null!, "Default"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new ThemeCatalog<TestTheme>([Theme("A")], static theme => theme.Name, static theme => theme.Aliases ?? [], null!));
	}
}
