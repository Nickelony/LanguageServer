using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Buffers;
using System.Text;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Encodes and decodes workspace text files and detects their format metadata.
/// </summary>
/// <remarks>
/// The type exposes only static members; the default <see cref="IWorkspaceFileSystem"/> implementation
/// that reads and writes these encodings is <see cref="LocalWorkspaceFileSystem"/>. Decoding
/// Windows-1252 registers the code-pages provider once for the whole process.
/// </remarks>
public static class WorkspaceTextCodec
{
	private static readonly byte[] s_utf8Bom = [0xEF, 0xBB, 0xBF];
	private static readonly byte[] s_utf16LittleEndianBom = [0xFF, 0xFE];
	private static readonly byte[] s_utf16BigEndianBom = [0xFE, 0xFF];
	private static readonly byte[] s_utf32LittleEndianBom = [0xFF, 0xFE, 0x00, 0x00];
	private static readonly byte[] s_utf32BigEndianBom = [0x00, 0x00, 0xFE, 0xFF];

	// The strict encodings have no mutable state, so cached instances replace an allocation per call.
	private static readonly Encoding s_utf8 = new UTF8Encoding(false, true);
	private static readonly Encoding s_utf16LittleEndian = new UnicodeEncoding(false, false, true);
	private static readonly Encoding s_utf16BigEndian = new UnicodeEncoding(true, false, true);

	// The code-pages provider is registered on first use rather than on type load, so a process that
	// never touches Windows-1252 is not mutated. Registration is idempotent and thread-safe.
	private static readonly Lazy<Encoding> s_windows1252 = new(CreateWindows1252Encoding);

	private static readonly bool[] s_undefinedWindows1252Bytes = CreateUndefinedWindows1252ByteTable();

	// The decoding side rejects the five byte values that Windows-1252 leaves undefined; the Windows
	// encoder tables map their C1 code points back to exactly those bytes, so encoding must reject the
	// characters as well or a committed document could never be decoded again.
	private static readonly SearchValues<char> s_undefinedWindows1252CodePoints =
		SearchValues.Create("\u0081\u008D\u008F\u0090\u009D");

	/// <summary>Decodes bytes, detects their text format, and records the newline style.</summary>
	/// <remarks>
	/// A recognized UTF-8 or UTF-16 byte-order mark takes precedence over <paramref name="noBomEncoding"/>.
	/// Bytes without a recognized mark use <paramref name="noBomEncoding"/>. UTF-32 and invalid byte
	/// sequences are rejected with a <see cref="DecoderFallbackException"/>.
	/// </remarks>
	/// <param name="bytes">The encoded bytes to decode.</param>
	/// <param name="noBomEncoding">The encoding to use when the bytes carry no recognized byte-order mark.</param>
	/// <param name="fileFormat">Receives the detected encoding, byte-order mark, and newline style.</param>
	/// <returns>The decoded text.</returns>
	/// <exception cref="DecoderFallbackException">Thrown when the bytes contain a UTF-32 mark or a sequence that is invalid for the detected encoding.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when the bytes carry no recognized byte-order mark and <paramref name="noBomEncoding"/> is not a defined value.</exception>
	public static string Decode(ReadOnlySpan<byte> bytes, TextEncodingKind noBomEncoding, out TextFileFormat fileFormat)
	{
		(TextEncodingKind encoding, bool hasBom, int preambleLength) = DetectEncoding(bytes, noBomEncoding);
		Encoding decoder = GetEncoding(encoding);
		ReadOnlySpan<byte> content = bytes[preambleLength..];

		if (encoding == TextEncodingKind.Windows1252)
			RejectUndefinedWindows1252Bytes(content);

		string text = decoder.GetString(content);
		fileFormat = new TextFileFormat(encoding, hasBom, DetectNewlineStyle(text));
		return text;
	}

	/// <summary>Encodes text using the specified encoding and byte-order-mark setting.</summary>
	/// <remarks>
	/// The content is encoded as supplied; <see cref="TextFileFormat.NewlineStyle"/> is metadata and
	/// does not normalize newline characters. Windows-1252 can be written only without a byte-order
	/// mark, and the five code points that Windows-1252 leaves undefined
	/// (U+0081, U+008D, U+008F, U+0090, U+009D) are rejected because their byte values are rejected
	/// again when the file is decoded.
	/// </remarks>
	/// <param name="content">The text to encode.</param>
	/// <param name="fileFormat">The encoding, byte-order mark, and newline style to associate with the output.</param>
	/// <returns>The encoded bytes.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">Thrown when the format combines Windows-1252 with a byte-order mark.</exception>
	/// <exception cref="EncoderFallbackException">Thrown when the content contains a character that the selected encoding cannot represent, such as an unpaired surrogate, a character outside Windows-1252, or one of the five undefined Windows-1252 code points.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when the format's encoding is not a defined value.</exception>
	public static byte[] Encode(string content, TextFileFormat fileFormat)
	{
		ArgumentNullException.ThrowIfNull(content);

		EnsureEncodable(fileFormat, nameof(fileFormat));

		if (fileFormat.Encoding == TextEncodingKind.Windows1252)
		{
			int undefinedIndex = content.AsSpan().IndexOfAny(s_undefinedWindows1252CodePoints);
			if (undefinedIndex >= 0)
			{
				throw new EncoderFallbackException(
					$"The content contains the control character U+{(int)content[undefinedIndex]:X4}, which Windows-1252 cannot round-trip.");
			}
		}

		Encoding encoder = GetEncoding(fileFormat.Encoding);
		byte[] encoded = encoder.GetBytes(content);

		if (!fileFormat.HasBom)
			return encoded;

		byte[] preamble = GetPreamble(fileFormat.Encoding);
		byte[] result = new byte[preamble.Length + encoded.Length];
		Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
		Buffer.BlockCopy(encoded, 0, result, preamble.Length, encoded.Length);
		return result;
	}

	/// <summary>Detects the newline style used by text content.</summary>
	/// <param name="content">The text to inspect.</param>
	/// <returns>The detected newline style; <see cref="TextNewlineStyle.None"/> for content without newlines.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="content"/> is <see langword="null"/>.</exception>
	public static TextNewlineStyle DetectNewlineStyle(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		bool hasCrLf = false;
		bool hasLf = false;
		bool hasCr = false;

		for (int index = 0; index < content.Length; index++)
		{
			if (content[index] == '\r')
			{
				if (index + 1 < content.Length && content[index + 1] == '\n')
				{
					hasCrLf = true;
					index++;
				}
				else
				{
					hasCr = true;
				}
			}
			else if (content[index] == '\n')
			{
				hasLf = true;
			}
		}

		int styles = (hasCrLf ? 1 : 0) + (hasLf ? 1 : 0) + (hasCr ? 1 : 0);
		if (styles == 0)
			return TextNewlineStyle.None;
		if (styles > 1)
			return TextNewlineStyle.Mixed;
		if (hasCrLf)
			return TextNewlineStyle.CrLf;
		if (hasLf)
			return TextNewlineStyle.Lf;
		return TextNewlineStyle.Cr;
	}

	// The shared format validation for every boundary that accepts a format. The encoding must be a
	// defined value, and the Windows-1252 and byte-order mark combination cannot be encoded. Callers
	// pass the parameter name of the argument they accepted the format through, so the rule has one
	// home and one message.
	internal static void EnsureEncodable(TextFileFormat fileFormat, string paramName)
	{
		EnsureDefinedEncoding(fileFormat.Encoding, paramName);

		if (fileFormat.HasBom && fileFormat.Encoding == TextEncodingKind.Windows1252)
			throw new ArgumentException("Windows-1252 does not support a BOM.", paramName);
	}

	// The encoding values accepted by the codec. Every boundary that accepts a format or a no-BOM
	// encoding validates the value before it can be stored on a document and fail only much later.
	internal static void EnsureDefinedEncoding(TextEncodingKind encoding, string paramName)
	{
		if (encoding is not (TextEncodingKind.Utf8
			or TextEncodingKind.Utf16LittleEndian
			or TextEncodingKind.Utf16BigEndian
			or TextEncodingKind.Windows1252))
		{
			throw new ArgumentOutOfRangeException(
				paramName,
				encoding,
				"The text encoding is not a defined value.");
		}
	}

	private static (TextEncodingKind Encoding, bool HasBom, int PreambleLength) DetectEncoding(
		ReadOnlySpan<byte> bytes,
		TextEncodingKind noBomEncoding)
	{
		if (bytes.StartsWith(s_utf32LittleEndianBom) || bytes.StartsWith(s_utf32BigEndianBom))
			throw new DecoderFallbackException("UTF-32 input is not supported.");
		if (bytes.StartsWith(s_utf8Bom))
			return (TextEncodingKind.Utf8, true, s_utf8Bom.Length);
		if (bytes.StartsWith(s_utf16LittleEndianBom))
			return (TextEncodingKind.Utf16LittleEndian, true, s_utf16LittleEndianBom.Length);
		if (bytes.StartsWith(s_utf16BigEndianBom))
			return (TextEncodingKind.Utf16BigEndian, true, s_utf16BigEndianBom.Length);
		return (noBomEncoding, false, 0);
	}

	private static Encoding GetEncoding(TextEncodingKind encoding)
	{
		if (encoding == TextEncodingKind.Windows1252)
			return s_windows1252.Value;

		return encoding switch
		{
			TextEncodingKind.Utf8 => s_utf8,
			TextEncodingKind.Utf16LittleEndian => s_utf16LittleEndian,
			TextEncodingKind.Utf16BigEndian => s_utf16BigEndian,
			_ => throw new ArgumentOutOfRangeException(nameof(encoding))
		};
	}

	private static Encoding CreateWindows1252Encoding()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		return Encoding.GetEncoding(
			1252,
			EncoderFallback.ExceptionFallback,
			DecoderFallback.ExceptionFallback);
	}

	private static byte[] GetPreamble(TextEncodingKind encoding)
	{
		return encoding switch
		{
			TextEncodingKind.Utf8 => s_utf8Bom,
			TextEncodingKind.Utf16LittleEndian => s_utf16LittleEndianBom,
			TextEncodingKind.Utf16BigEndian => s_utf16BigEndianBom,
			_ => throw new ArgumentOutOfRangeException(nameof(encoding))
		};
	}

	private static void RejectUndefinedWindows1252Bytes(ReadOnlySpan<byte> bytes)
	{
		// The five undefined Windows-1252 bytes are looked up through a table so the check is one
		// indexed read per byte instead of a chain of comparisons.
		for (int index = 0; index < bytes.Length; index++)
		{
			if (s_undefinedWindows1252Bytes[bytes[index]])
				throw new DecoderFallbackException($"Undefined Windows-1252 byte 0x{bytes[index]:X2}.");
		}
	}

	private static bool[] CreateUndefinedWindows1252ByteTable()
	{
		bool[] table = new bool[256];
		table[0x81] = true;
		table[0x8D] = true;
		table[0x8F] = true;
		table[0x90] = true;
		table[0x9D] = true;
		return table;
	}
}
