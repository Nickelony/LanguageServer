using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Accepts completion responses in either LSP wire form when reading and serializes the typed response using the
/// completion-list form.
/// </summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is
/// not invoked for JSON null. Malformed elements inside a completion list - non-object entries and objects whose
/// members do not bind, such as an illegal text-edit union or a partial position - are skipped with a warning
/// instead of failing the whole response. Writing always emits the completion-list shape; the item-array form is
/// not reconstructed, and an absent <c>items</c> member stays a JSON <see langword="null"/> so the tolerant
/// round-trip preserves it.
/// </remarks>
public sealed class CompletionResponseJsonConverter : JsonConverter<CompletionResponse>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionResponseJsonConverter"/> class.
	/// </summary>
	public CompletionResponseJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="CompletionResponseJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public CompletionResponseJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override CompletionResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		IReadOnlyList<CompletionItemPayload>? items = null;
		bool isIncomplete = false;

		if (root.ValueKind == JsonValueKind.Array)
		{
			items = DeserializeItems(root, itemDefaultsElement: null, options);
		}
		else if (root.ValueKind == JsonValueKind.Object)
		{
			bool hasSupportedItemsShape = false;

			if (JsonElementReadHelpers.TryGetProperty(root, "items", out JsonElement itemsElement))
			{
				if (itemsElement.ValueKind == JsonValueKind.Array)
				{
					JsonElement? itemDefaultsElement = JsonElementReadHelpers.TryGetProperty(root, "itemDefaults", out JsonElement defaultsElement)
						&& defaultsElement.ValueKind == JsonValueKind.Object
							? defaultsElement
							: null;

					items = DeserializeItems(itemsElement, itemDefaultsElement, options);

					hasSupportedItemsShape = true;
				}
				else if (itemsElement.ValueKind == JsonValueKind.Null)
				{
					items = null;
					hasSupportedItemsShape = true;
				}
				else
				{
					_logger.LogWarning("Ignoring malformed completion-list payload because 'items' had unsupported JSON kind {Kind}.", itemsElement.ValueKind);
				}
			}
			else
			{
				_logger.LogWarning("Ignoring malformed completion-list payload because the 'items' property was missing.");
			}

			if (hasSupportedItemsShape
				&& JsonElementReadHelpers.TryGetProperty(root, "isIncomplete", out JsonElement isIncompleteElement)
				&& (isIncompleteElement.ValueKind == JsonValueKind.True || isIncompleteElement.ValueKind == JsonValueKind.False))
			{
				isIncomplete = isIncompleteElement.GetBoolean();
			}
		}
		else
		{
			_logger.LogWarning("Ignoring malformed completion response because its JSON kind {Kind} is neither an array nor an object.", root.ValueKind);
		}

		return new(items, isIncomplete);
	}

	private List<CompletionItemPayload> DeserializeItems(
		JsonElement itemsElement,
		JsonElement? itemDefaultsElement,
		JsonSerializerOptions options)
	{
		var items = new List<CompletionItemPayload>();

		foreach (JsonElement itemElement in itemsElement.EnumerateArray())
		{
			if (itemElement.ValueKind != JsonValueKind.Object)
			{
				// Spec-shaped items are objects; one malformed element must not fail the whole list.
				if (itemElement.ValueKind != JsonValueKind.Null)
				{
					_logger.LogWarning("Skipping malformed completion item because its JSON kind {Kind} is not an object.",
						itemElement.ValueKind);
				}

				continue;
			}

			try
			{
				items.Add(itemDefaultsElement is JsonElement defaultsElement
					? DeserializeCompletionListItem(itemElement, defaultsElement, options)
					: itemElement.Deserialize<CompletionItemPayload>(options) ?? new CompletionItemPayload());
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				// An item whose members do not bind (for example an illegal text-edit union or a partial
				// position) is dropped with a warning; the rest of the list stays usable.
				_logger.LogWarning(exception, "Skipping malformed completion item because its payload could not be deserialized.");
			}
		}

		return items;
	}

	private static CompletionItemPayload DeserializeCompletionListItem(
		JsonElement itemElement,
		JsonElement itemDefaultsElement,
		JsonSerializerOptions options)
	{
		if (TryParseJsonNode(itemElement.GetRawText()) is not JsonObject itemObject)
			return itemElement.Deserialize<CompletionItemPayload>(options) ?? new CompletionItemPayload();

		ApplyCompletionItemDefaults(itemObject, itemDefaultsElement);
		return itemObject.Deserialize<CompletionItemPayload>(options) ?? new CompletionItemPayload();
	}

	private static void ApplyCompletionItemDefaults(JsonObject itemObject, JsonElement itemDefaultsElement)
	{
		ApplyDefaultPropertyIfMissing(itemObject, itemDefaultsElement, "commitCharacters");
		ApplyDefaultPropertyIfMissing(itemObject, itemDefaultsElement, "data");
		ApplyDefaultPropertyIfMissing(itemObject, itemDefaultsElement, "insertTextFormat");
		ApplyDefaultPropertyIfMissing(itemObject, itemDefaultsElement, "insertTextMode");
		ApplyDefaultEditRangeIfMissing(itemObject, itemDefaultsElement);
	}

	private static void ApplyDefaultPropertyIfMissing(JsonObject itemObject, JsonElement itemDefaultsElement, string propertyName)
	{
		if (itemObject.TryGetPropertyValue(propertyName, out JsonNode? existingValue) && existingValue is not null)
			return;

		if (!itemDefaultsElement.TryGetProperty(propertyName, out JsonElement defaultValue))
			return;

		if (TryParseJsonNode(defaultValue.GetRawText()) is { } defaultNode)
			itemObject[propertyName] = defaultNode;
	}

	private static void ApplyDefaultEditRangeIfMissing(JsonObject itemObject, JsonElement itemDefaultsElement)
	{
		if (itemObject.TryGetPropertyValue("textEdit", out JsonNode? existingTextEdit) && existingTextEdit is not null)
			return;

		if (!JsonElementReadHelpers.TryGetProperty(itemDefaultsElement, "editRange", out JsonElement editRangeElement))
			return;

		string? newText = GetDefaultTextEditNewText(itemObject);

		if (string.IsNullOrEmpty(newText))
			return;

		JsonObject? textEditObject = CreateDefaultTextEdit(editRangeElement, newText);

		if (textEditObject is null)
			return;

		itemObject["textEdit"] = textEditObject;
	}

	private static string? GetDefaultTextEditNewText(JsonObject itemObject)
	{
		// LSP 3.17 defines the default text-edit text as "textEditText ?? label": when a client synthesizes an
		// edit from itemDefaults.editRange, insertText must not be used even though the item carries it.
		return TryGetStringPropertyValue(itemObject, "textEditText")
			?? TryGetStringPropertyValue(itemObject, "label");
	}

	private static string? TryGetStringPropertyValue(JsonObject itemObject, string propertyName)
	{
		if (!itemObject.TryGetPropertyValue(propertyName, out JsonNode? propertyValue)
			|| propertyValue is not JsonValue jsonValue
			|| !jsonValue.TryGetValue(out string? value)
			|| string.IsNullOrEmpty(value))
		{
			return null;
		}

		return value;
	}

	private static JsonObject? CreateDefaultTextEdit(JsonElement editRangeElement, string newText)
	{
		var textEditObject = new JsonObject
		{
			["newText"] = newText
		};

		if (LooksLikeProtocolRange(editRangeElement))
		{
			if (TryParseJsonNode(editRangeElement.GetRawText()) is not { } rangeNode)
				return null;

			textEditObject["range"] = rangeNode;
			return textEditObject;
		}

		if (!TryGetObjectProperty(editRangeElement, "insert", out JsonElement insertRange)
			|| !TryGetObjectProperty(editRangeElement, "replace", out JsonElement replaceRange))
		{
			return null;
		}

		if (TryParseJsonNode(insertRange.GetRawText()) is not { } insertNode || TryParseJsonNode(replaceRange.GetRawText()) is not { } replaceNode)
			return null;

		textEditObject["insert"] = insertNode;
		textEditObject["replace"] = replaceNode;
		return textEditObject;
	}

	/// <summary>
	/// Parses raw JSON text into a node while treating duplicate property names and other malformed input as absent
	/// instead of failing the whole response.
	/// </summary>
	/// <param name="rawText">The raw JSON text to parse.</param>
	/// <returns>The parsed node, or <see langword="null"/> when the text is malformed.</returns>
	private static JsonNode? TryParseJsonNode(string rawText)
	{
		try
		{
			return JsonNode.Parse(rawText);
		}
		catch (JsonException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			// JsonNode.Parse rejects duplicate property names with ArgumentException.
			return null;
		}
	}

	private static bool LooksLikeProtocolRange(JsonElement element)
	{
		return element.ValueKind == JsonValueKind.Object
			&& JsonElementReadHelpers.TryGetProperty(element, "start", out _)
			&& JsonElementReadHelpers.TryGetProperty(element, "end", out _);
	}

	private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
	{
		if (element.TryGetProperty(propertyName, out propertyValue) && propertyValue.ValueKind == JsonValueKind.Object)
			return true;

		propertyValue = default;
		return false;
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CompletionResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteBoolean("isIncomplete", value.IsIncomplete);
		writer.WritePropertyName("items");

		if (value.Items is null)
			writer.WriteNullValue();
		else
			JsonSerializer.Serialize(writer, value.Items, options);

		writer.WriteEndObject();
	}
}
