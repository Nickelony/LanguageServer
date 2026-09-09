using System.Windows.Input;

namespace Nickelony.IDEKit.KeyBindings.Tests;

[TestClass]
public class KeyBindingServiceTests
{
	private static CommandCatalog<TestCommand> CreateCatalog()
	{
		return new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.NewFile, nameof(TestCommand.NewFile), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.N, ModifierKeys.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Save, nameof(TestCommand.Save), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.S, ModifierKeys.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.S, ModifierKeys.Control | ModifierKeys.Shift)),
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.F9, ModifierKeys.None)),
			new CommandDescriptor<TestCommand>(TestCommand.Exit, nameof(TestCommand.Exit), isRemappable: false, isHostReserved: true,
				new KeyCombo(Key.F4, ModifierKeys.Alt)),
			new CommandDescriptor<TestCommand>(TestCommand.Undo, nameof(TestCommand.Undo), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.Z, ModifierKeys.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Redo, nameof(TestCommand.Redo), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.Y, ModifierKeys.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.Find, nameof(TestCommand.Find), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.F, ModifierKeys.Control),
				new KeyCombo(Key.H, ModifierKeys.Control)),
			new CommandDescriptor<TestCommand>(TestCommand.GoToDefinition, nameof(TestCommand.GoToDefinition), isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.F12, ModifierKeys.None))
		]);
	}

	private static IKeyBindingService<TestCommand> CreateService()
		=> new KeyBindingService<TestCommand>(CreateCatalog(), new KeyBindingOverrideCollection(), _ => true);

	[TestMethod]
	public void TryGetCommand_KnownKeyCombos_ReturnsExpectedCommands()
	{
		var service = CreateService();

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(Key.S, ModifierKeys.Control), out TestCommand saveCommand));
		Assert.AreEqual(TestCommand.Save, saveCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(Key.Z, ModifierKeys.Control), out TestCommand undoCommand));
		Assert.AreEqual(TestCommand.Undo, undoCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(Key.F9, ModifierKeys.None), out TestCommand buildCommand));
		Assert.AreEqual(TestCommand.Build, buildCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(Key.F12, ModifierKeys.None), out TestCommand goToDefinitionCommand));
		Assert.AreEqual(TestCommand.GoToDefinition, goToDefinitionCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(Key.F4, ModifierKeys.Alt), out TestCommand exitCommand));
		Assert.AreEqual(TestCommand.Exit, exitCommand);
	}

	[TestMethod]
	public void TryGetCommand_UnknownKeyCombos_ReturnsFalse()
	{
		var service = CreateService();

		Assert.IsFalse(service.TryGetCommand(new KeyCombo(Key.A, ModifierKeys.None), out _));
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(Key.X, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt), out _));
	}

	[TestMethod]
	public void GetBindings_Save_ReturnsCtrlS()
	{
		var service = CreateService();

		IReadOnlyList<KeyCombo> bindings = service.GetBindings(TestCommand.Save);

		Assert.AreEqual(1, bindings.Count);
		Assert.AreEqual(new KeyCombo(Key.S, ModifierKeys.Control), bindings[0]);
	}

	[TestMethod]
	public void GetBindings_Find_ReturnsTwoBindings()
	{
		var service = CreateService();

		IReadOnlyList<KeyCombo> bindings = service.GetBindings(TestCommand.Find);

		Assert.AreEqual(2, bindings.Count);
		Assert.IsTrue(bindings.Contains(new KeyCombo(Key.F, ModifierKeys.Control)));
		Assert.IsTrue(bindings.Contains(new KeyCombo(Key.H, ModifierKeys.Control)));
	}

	[TestMethod]
	public void TryGetCommand_Find_ResolvesPrimaryAndSecondaryBindings()
	{
		var service = CreateService();

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(Key.F, ModifierKeys.Control), out TestCommand primaryCommand));
		Assert.AreEqual(TestCommand.Find, primaryCommand);

		Assert.IsTrue(service.TryGetCommand(new KeyCombo(Key.H, ModifierKeys.Control), out TestCommand secondaryCommand));
		Assert.AreEqual(TestCommand.Find, secondaryCommand);
	}

	[TestMethod]
	public void GetBindings_UncatalogedCommand_ReturnsEmpty()
	{
		var service = CreateService();

		IReadOnlyList<KeyCombo> bindings = service.GetBindings(TestCommand.None);

		Assert.AreEqual(0, bindings.Count);
	}

	[TestMethod]
	public void GetDisplayText_KnownCommand_ReturnsNonEmptyText()
	{
		var service = CreateService();

		string displayText = service.GetDisplayText(TestCommand.Save);

		Assert.IsFalse(string.IsNullOrEmpty(displayText));
	}

	[TestMethod]
	public void GetDisplayText_Find_UsesSeparatorForMultipleBindings()
	{
		var service = CreateService();

		string displayText = service.GetDisplayText(TestCommand.Find);

		Assert.IsTrue(displayText.Contains('/'));
	}

	[TestMethod]
	public void GetDisplayText_UncatalogedCommand_ReturnsFallback()
	{
		var service = CreateService();

		string displayText = service.GetDisplayText(TestCommand.None, "Fallback");

		Assert.AreEqual("Fallback", displayText);
	}

	[TestMethod]
	public void Validate_HostReserved_ReturnsReserved()
	{
		var service = CreateService();

		KeyBindingValidationResult result = service.Validate(TestCommand.Exit, [new KeyCombo(Key.X, ModifierKeys.Control)]);

		Assert.AreEqual(KeyBindingValidationResult.Reserved, result);
	}

	[TestMethod]
	public void Reset_RemovesCommandOverrideAndRestoresDefault()
	{
		var overrides = new KeyBindingOverrideCollection();
		overrides.Overrides.Add(new KeyBindingOverrideEntry
		{
			CommandId = nameof(TestCommand.Save),
			Bindings = [new KeyBindingSettings { KeyName = "X", Modifiers = (int)ModifierKeys.Control }]
		});

		bool saved = false;
		var service = new KeyBindingService<TestCommand>(CreateCatalog(), overrides, o => { saved = true; return true; });

		// The configured Save binding should be Ctrl+X.
		IReadOnlyList<KeyCombo> bindingsBefore = service.GetBindings(TestCommand.Save);
		Assert.AreEqual(1, bindingsBefore.Count);
		Assert.AreEqual(new KeyCombo(Key.X, ModifierKeys.Control), bindingsBefore[0]);

		service.Reset(TestCommand.Save);

		// Reset should restore Save's default Ctrl+S binding.
		IReadOnlyList<KeyCombo> bindingsAfter = service.GetBindings(TestCommand.Save);
		Assert.AreEqual(1, bindingsAfter.Count);
		Assert.AreEqual(new KeyCombo(Key.S, ModifierKeys.Control), bindingsAfter[0]);
		Assert.IsTrue(saved);
	}

	[TestMethod]
	public void ResetAll_RestoresDefaultBindingForOverriddenCommand()
	{
		var overrides = new KeyBindingOverrideCollection();
		overrides.Overrides.Add(new KeyBindingOverrideEntry
		{
			CommandId = nameof(TestCommand.Save),
			Bindings = [new KeyBindingSettings { KeyName = "X", Modifiers = (int)ModifierKeys.Control }]
		});

		bool saved = false;
		var service = new KeyBindingService<TestCommand>(CreateCatalog(), overrides, o => { saved = true; return true; });

		service.ResetAll();

		// ResetAll should restore Save's default Ctrl+S binding.
		IReadOnlyList<KeyCombo> bindings = service.GetBindings(TestCommand.Save);
		Assert.AreEqual(1, bindings.Count);
		Assert.AreEqual(new KeyCombo(Key.S, ModifierKeys.Control), bindings[0]);
		Assert.IsTrue(saved);
	}

	[TestMethod]
	public void BindingsChanged_FiresOnReset()
	{
		var service = CreateService();
		bool fired = false;
		service.BindingsChanged += (_, _) => fired = true;

		service.Reset(TestCommand.Save);

		Assert.IsTrue(fired);
	}

	[TestMethod]
	public void KeyCombo_GetDisplayText_IncludesModifiersAndKey()
	{
		var keyCombo = new KeyCombo(Key.S, ModifierKeys.Control | ModifierKeys.Shift);

		string displayText = keyCombo.GetDisplayText();

		Assert.IsTrue(displayText.Contains("Ctrl+"));
		Assert.IsTrue(displayText.Contains("Shift+"));
		Assert.IsTrue(displayText.Contains("S"));
	}

	[TestMethod]
	public void KeyCombo_GetDisplayText_UsesFriendlyKeyNames()
	{
		Assert.AreEqual("Backspace", new KeyCombo(Key.Back, ModifierKeys.None).GetDisplayText());
		Assert.AreEqual("Enter", new KeyCombo(Key.Return, ModifierKeys.None).GetDisplayText());
		Assert.AreEqual("Esc", new KeyCombo(Key.Escape, ModifierKeys.None).GetDisplayText());
		Assert.AreEqual("Ctrl+Space", new KeyCombo(Key.Space, ModifierKeys.Control).GetDisplayText());
	}

	[TestMethod]
	public void Reset_CommandWithDifferingSerializedId_RemovesTheOverride()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Save, "editor.save", isRemappable: true, isHostReserved: false,
				new KeyCombo(Key.S, ModifierKeys.Control))
		]);

		var overrides = new KeyBindingOverrideCollection();
		overrides.Overrides.Add(new KeyBindingOverrideEntry
		{
			CommandId = "editor.save",
			Bindings = [new KeyBindingSettings { KeyName = nameof(Key.F5), Modifiers = (int)ModifierKeys.None }]
		});

		KeyBindingOverrideCollection? saved = null;
		var service = new KeyBindingService<TestCommand>(catalog, overrides, snapshot => { saved = snapshot; return true; });

		Assert.AreEqual(new KeyCombo(Key.F5, ModifierKeys.None), service.GetBindings(TestCommand.Save)[0]);

		service.Reset(TestCommand.Save);

		Assert.AreEqual(new KeyCombo(Key.S, ModifierKeys.Control), service.GetBindings(TestCommand.Save)[0]);
		Assert.IsNotNull(saved);
		Assert.AreEqual(0, saved!.Overrides.Count);
	}

	[TestMethod]
	public void ResolveBindings_EmptyOverrideForHostReservedCommand_KeepsCatalogDefaults()
	{
		var overrides = new KeyBindingOverrideCollection();
		overrides.Overrides.Add(new KeyBindingOverrideEntry { CommandId = nameof(TestCommand.Exit), Bindings = [] });

		var service = new KeyBindingService<TestCommand>(CreateCatalog(), overrides, _ => true);

		IReadOnlyList<KeyCombo> bindings = service.GetBindings(TestCommand.Exit);
		Assert.AreEqual(1, bindings.Count);
		Assert.AreEqual(new KeyCombo(Key.F4, ModifierKeys.Alt), bindings[0]);
	}

	[TestMethod]
	public void ResolveBindings_EmptyOverrideForRemappableCommand_UnbindsTheCommand()
	{
		var overrides = new KeyBindingOverrideCollection();
		overrides.Overrides.Add(new KeyBindingOverrideEntry { CommandId = nameof(TestCommand.Save), Bindings = [] });

		var service = new KeyBindingService<TestCommand>(CreateCatalog(), overrides, _ => true);

		Assert.AreEqual(0, service.GetBindings(TestCommand.Save).Count);
		Assert.IsFalse(service.TryGetCommand(new KeyCombo(Key.S, ModifierKeys.Control), out _));
	}

	[TestMethod]
	public void ResolveBindings_OverrideWithNumericUndefinedKey_FallsBackToDefaults()
	{
		var overrides = new KeyBindingOverrideCollection();
		overrides.Overrides.Add(new KeyBindingOverrideEntry
		{
			CommandId = nameof(TestCommand.Save),
			Bindings = [new KeyBindingSettings { KeyName = "99999", Modifiers = 0 }]
		});

		var service = new KeyBindingService<TestCommand>(CreateCatalog(), overrides, _ => true);

		Assert.AreEqual(new KeyCombo(Key.S, ModifierKeys.Control), service.GetBindings(TestCommand.Save)[0]);
	}

	[TestMethod]
	public void Validate_Outcomes_AreReportedPerPolicy()
	{
		var service = CreateService();

		Assert.AreEqual(KeyBindingValidationResult.Valid,
			service.Validate(TestCommand.Save, [new KeyCombo(Key.F5, ModifierKeys.None)]));
		Assert.AreEqual(KeyBindingValidationResult.DuplicateInCommand,
			service.Validate(TestCommand.Save, [new KeyCombo(Key.F5, ModifierKeys.None), new KeyCombo(Key.F5, ModifierKeys.None)]));
		Assert.AreEqual(KeyBindingValidationResult.Conflict,
			service.Validate(TestCommand.Save, [new KeyCombo(Key.Z, ModifierKeys.Control)]));
		Assert.AreEqual(KeyBindingValidationResult.NotRemappable,
			service.Validate(TestCommand.None, [new KeyCombo(Key.F5, ModifierKeys.None)]));
	}
}
