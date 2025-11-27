using System.Runtime.CompilerServices;
using System.Xml;

using DotNetXtensions.Mini;

namespace DotNetXtensions.Html;

/// <summary>
/// `HtmlTag` is a high-performance, low-allocation HTML tag parser for opening HTML tags.
/// Call / instantiate this when you already are processing HTML and already have a position that
/// looks like an opening tag (or whitespace before it), and you want to have all the dirty work
/// of parsing it after that point -- up to the close of the opening tag -- handled for you.
/// Thus allowing you to access the tag's name, attributes, positional info (start and end, etc).
/// As an end-result: `HtmlTag` represents a parsed HTML opening tag with its name, attributes,
/// and positional information. Optimized for high-performance parsing with minimal memory allocations.
/// </summary>
/// <remarks>
/// This parser handles standard HTML tags including:
/// <list type="bullet">
/// <item><description>Self-closing tags (e.g., <c><![CDATA[<img />]]></c>, <c><![CDATA[<br/>]]></c>)</description></item>
/// <item><description>Tags with quoted attributes (e.g., <c><![CDATA[<div class="red">]]></c>)</description></item>
/// <item><description>UGLY input (e.g., <c><![CDATA[<div class   = "   red" foo =bar>]]></c>)</description></item>
/// <item><description>Tags with unquoted attributes (e.g., <c><![CDATA[<input type=text>]]></c>)</description></item>
/// <item><description>Boolean attributes (e.g., <c><![CDATA[<input disabled>]]></c>)</description></item>
/// <item><description>Mixed quote styles (single and double quotes)</description></item>
/// </list>
/// </remarks>
public class HtmlTag
{
	// === PARSING STATE FIELDS ===

	/// <summary>Current parsing position.</summary>
	private int _currPos;

	/// <summary>
	/// Final valid position for parsing (excludes the closing '&gt;' and optional '/').
	/// </summary>
	private int _endPosition;

	/// <summary>
	/// Effective search length. After initialization, this represents the length from 
	/// the start of the tag to one position BEFORE the closing '&gt;' character.
	/// The closing '&gt;' is intentionally excluded to simplify parsing operations.
	/// This is an internal optimization value, not meant for external consumption.
	/// </summary>
	private int _searchLength;


	// === PUBLIC PROPERTIES ===

	/// <summary>
	/// Gets the name of the HTML tag (e.g., "div", "img", "p").
	/// Tag names are validated to conform to XML naming conventions.
	/// </summary>
	public string TagName { get; set; }

	/// <summary>
	/// Gets the dictionary of attribute name-value pairs.
	/// Returns null if the tag has no attributes.
	/// Boolean attributes (without values) are stored with empty string values.
	/// If duplicate attributes exist, the last one wins.
	/// </summary>
	public Dictionary<string, string> Attributes { get; set; }

	/// <summary>
	/// Gets the zero-based index in the input string where the tag starts (at the <c><![CDATA[<]]></c> character).
	/// </summary>
	public int TagStartIndex { get; private set; }

	/// <summary>
	/// Gets the total length of the complete tag, including the opening <c><![CDATA[<]]></c> and closing <c><![CDATA[>]]></c> characters.
	/// For self-closed tags, this includes the <c>/</c> character.
	/// </summary>
	public int TagLength { get; private set; }

	/// <summary>
	/// Gets whether this tag is self-closed (ends with <c><![CDATA[/>]]></c>).
	/// Examples: <c><![CDATA[<br/>]]></c>, <c><![CDATA[<img />]]></c>, <c><![CDATA[<input type="text"/>]]></c>
	/// </summary>
	public bool IsSelfClosed { get; private set; }

	/// <summary>
	/// Gets the zero-based index where the tag's inner content starts 
	/// (one character after the opening <c><![CDATA[<]]></c>).
	/// </summary>
	public int InnerTagStartIndex => TagStartIndex + 1;

	/// <summary>
	/// Gets the length of the tag's inner content, excluding the opening <c><![CDATA[<]]></c> 
	/// and closing <c><![CDATA[>]]></c> (and optional <c>/</c> for self-closed tags).
	/// </summary>
	public int InnerTagLength { get; private set; }


	// === COMPUTED PROPERTIES ===

	/// <summary>
	/// Gets whether this tag has no attributes. Equivalent to checking if Attributes is null or empty.
	/// </summary>
	public bool NoAtts => Attributes.IsNulle();

	/// <summary>
	/// Indicates whether parsing has reached the end of the searchable tag content.
	/// </summary>
	private bool EndReached => _currPos > _endPosition;


	// === PUBLIC API ===

	/// <summary>
	/// Parses an HTML opening tag from the input string.
	/// </summary>
	/// <param name="inputHtml">The HTML string containing the tag to parse. Must not be null.</param>
	/// <param name="startIndex">The zero-based index where the tag starts (should point to <c><![CDATA[<]]></c>). Default is 0.</param>
	/// <param name="findTagEnd">
	/// True (default) to search for the closing <c><![CDATA[>]]></c> of the tag starting from <paramref name="startIndex"/>.
	/// False when the caller guarantees that <paramref name="inputHtml"/> ends exactly at the tag's closing <c><![CDATA[>]]></c>,
	/// thereby avoiding the search overhead.
	/// </param>
	/// <returns>
	/// True if the tag was successfully parsed; false if the input is malformed or doesn't represent a valid HTML tag.
	/// </returns>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="inputHtml"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="startIndex"/> is negative or beyond the string length.</exception>
	/// <remarks>
	/// This method is designed to be reusable - the same HtmlTag instance can be used to parse multiple tags.
	/// </remarks>
	public bool Parse(string inputHtml, int startIndex = 0, bool findTagEnd = true)
	{
		if(inputHtml == null) throw new ArgumentNullException(nameof(inputHtml));
		if(startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex));

		return Parse(inputHtml.AsSpan(), startIndex, findTagEnd);
	}

	/// <summary>
	/// Parses an HTML opening tag from the input span.
	/// This is the primary parsing method - the string overload delegates to this.
	/// </summary>
	/// <param name="htmlSpan">The HTML span containing the tag to parse.</param>
	/// <param name="startIndex">The zero-based index where the tag starts (should point to <c><![CDATA[<]]></c>). Default is 0.</param>
	/// <param name="findTagEnd">
	/// True (default) to search for the closing <c><![CDATA[>]]></c> of the tag starting from <paramref name="startIndex"/>.
	/// False when the caller guarantees that <paramref name="htmlSpan"/> ends exactly at the tag's closing <c><![CDATA[>]]></c>,
	/// thereby avoiding the search overhead.
	/// </param>
	/// <returns>
	/// True if the tag was successfully parsed; false if the input is malformed or doesn't represent a valid HTML tag.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="startIndex"/> is negative or beyond the span length.</exception>
	/// <remarks>
	/// <para>
	/// This method is designed to be reusable - the same HtmlTag instance can be used to parse multiple tags.
	/// </para>
	/// <para>
	/// This overload is ideal for high-performance scenarios where the caller already has a span,
	/// avoiding the overhead of creating a span from a string.
	/// </para>
	/// </remarks>
	public bool Parse(ReadOnlySpan<char> htmlSpan, int startIndex = 0, bool findTagEnd = true)
	{
		if(startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex));

		if(!Initialize(startIndex, findTagEnd, htmlSpan))
			return false;

		if(!TryParseTagNameAtCurr(htmlSpan))
			return false;

		if(EndReached)
			return true;

		// After tag name, there must be whitespace before attributes (or end of tag)
		if(!XmlConvert.IsWhitespaceChar(htmlSpan[_currPos++]))
			return false;

		// Parse all attributes
		while(!EndReached) {
			if(!TryFindAndParseNextAttr(htmlSpan))
				return false;
		}

		return true;
	}


	// === INITIALIZATION ===

	/// <summary>
	/// Initializes parsing state and validates the basic structure of the tag.
	/// Sets up position markers and calculates tag boundaries.
	/// </summary>
	/// <param name="startIndex">Starting position in the HTML.</param>
	/// <param name="findTagEnd">Whether to search for the closing bracket.</param>
	/// <param name="htmlSpan">Span view of the HTML for character access.</param>
	private bool Initialize(int startIndex, bool findTagEnd, ReadOnlySpan<char> htmlSpan)
	{
		if(startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex));

		_searchLength = htmlSpan.Length;
		TagStartIndex = _currPos = startIndex;

		// Reset state for reuse
		TagName = null;
		Attributes?.Clear();

		// Minimum valid tag: "<p>" (3 chars)
		if((_searchLength - _currPos) < 3)
			return false;

		if(_currPos >= _searchLength)
			throw new ArgumentOutOfRangeException(nameof(startIndex));

		// Must start with '<'
		if(htmlSpan[_currPos] != '<')
			return false;

		// Find or use the closing '>' position
		int endIdx = findTagEnd
			? htmlSpan.Slice(TagStartIndex).IndexOf('>')
			: _searchLength - 1;

		if(endIdx < 0)
			return false;

		// IndexOf returns position relative to slice start, so add TagStartIndex back
		if(findTagEnd)
			endIdx += TagStartIndex;

		// Calculate total tag length (including '<' and '>')
		// +1 because: position 0 to position 2 = 3 characters (0, 1, 2)
		TagLength = endIdx - TagStartIndex + 1;

		// Validate basic structure
		if(endIdx < 2
			|| endIdx <= TagStartIndex
			|| TagLength < 2
			|| htmlSpan[endIdx] != '>')
			return false;

		// Move back to exclude the closing '>' from our search space
		endIdx--;

		// Check for self-closing tag
		IsSelfClosed = htmlSpan[endIdx] == '/';
		if(IsSelfClosed)
			endIdx--; // Also exclude the '/' from our search space

		_endPosition = endIdx;
		_searchLength = _endPosition + 1;

		// Move past the opening '<' - all subsequent parsing starts from the tag name
		_currPos++;

		// Calculate the length of content between '<' and '>' (excluding both)
		InnerTagLength = (_endPosition - _currPos) + 1;
		if(InnerTagLength < 1)
			return false;

		return true;
	}


	// === TAG NAME PARSING ===

	/// <summary>
	/// Parses and validates the tag name from the current position.
	/// Tag name must conform to XML naming rules.
	/// </summary>
	private bool TryParseTagNameAtCurr(ReadOnlySpan<char> htmlSpan)
	{
		// Minimum: "<p" (end '>' already excluded from search length)
		if(_searchLength < 2)
			return false;

		int nameLength = CountToNextWhitespaceOrEnd(htmlSpan);
		if(nameLength < 1)
			return false;

		// Extract tag name using span to avoid allocation initially
		ReadOnlySpan<char> tagNameSpan = htmlSpan.Slice(_currPos, nameLength);

		// Validate the name before allocating a string
		if(!ValidateTagName(tagNameSpan))
			return false;

		// Only allocate string after validation
		TagName = new string(tagNameSpan);
		_currPos += nameLength;

		return true;
	}

	/// <summary>
	/// Validates that the tag name conforms to XML naming conventions.
	/// Optimized with a fast path for common ASCII alphanumeric names.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool ValidateTagName(ReadOnlySpan<char> name)
	{
		if(name.Length == 0)
			return false;

		// Fast path: Most HTML tags are simple ASCII alphanumeric (possibly with dash/underscore)
		// This avoids the heavier XmlConvert validation for ~99% of real-world tags
		if(name[0].IsAsciiLetter() &&
			IsAsciiAlphaNumeric(name, allowDash: true, allowUnderscore: true)) {
			return true;
		}

		// Slow path: Use XML validation for custom/special tag names
		try {
			XmlConvert.VerifyName(new string(name));
			return true;
		}
		catch {
			return false;
		}
	}


	// === ATTRIBUTE PARSING ===

	/// <summary>
	/// A STAR OF THE SHOW: working in tandem with `TryParseAttrAtIndicatedPos`, which this
	/// calls in multiple discovered scenarios:
	/// 
	/// Searching from currPos while skiping any leading whitespace, looks for a NEXT attribute.
	/// Returns true if found (and added), else false if none found. Handles attributes of ANY kind,
	/// ie whether a `=` was found or not, etc. Returns false if end of tag reached or invalid input
	/// encountered.
	/// <para />
	/// This is the primary attribute discovery method that scans for attribute boundaries.
	/// </remarks>
	private bool TryFindAndParseNextAttr(ReadOnlySpan<char> htmlSpan)
	{
		SkipWhitespaceOrEnd(htmlSpan);
		if(EndReached)
			return true;

		int attrStartPos = _currPos;

		// Scan through the attribute name
		for(; _currPos < _searchLength; _currPos++) {

			char c = htmlSpan[_currPos];

			// Fast check: Most attribute name chars are alphanumeric or dash
			// This is faster than the whitespace check, so test it first
			if(!c.IsAsciiLetterOrDigit() && c != '-') {

				// Found '=' - this attribute has a value
				if(c == '=') {
					return TryParseAttrAtIndicatedPos(attrStartPos, _currPos - attrStartPos, isBooleanAttribute: false, htmlSpan);
				}

				// Found whitespace - might be boolean or might have '=' after whitespace
				if(XmlConvert.IsWhitespaceChar(c)) {
					int whitespaceCount = SkipWhitespaceOrEnd(htmlSpan);
					bool isBooleanAttribute = EndReached || htmlSpan[_currPos] != '=';

					// Adjust position if this is indeed a boolean attribute
					if(whitespaceCount > 0 && isBooleanAttribute)
						_currPos--;

					return TryParseAttrAtIndicatedPos(attrStartPos, _currPos - attrStartPos, isBooleanAttribute, htmlSpan);
				}

				// Not alphanumeric, not dash, not '=', not whitespace
				// Must be a valid XML name character (e.g., ':', '_', Unicode letters)
				if(!XmlConvert.IsNCNameChar(c) && c != ':') {
					return false; // Invalid character in attribute name
				}
			}
		}

		// Reached end of tag without finding '=' or whitespace
		// This is a boolean attribute at the end of the tag (e.g., "<input disabled/>")
		return TryParseAttrAtIndicatedPos(attrStartPos, _currPos - attrStartPos, isBooleanAttribute: true, htmlSpan);
	}

	/// <summary>
	/// Another star of the show: working in tandem with it's caller `TryFindAndParseNextAttr` --
	/// 
	/// Tries to fully parse and add an attribute at specified pos, where caller has already
	/// critically determined: 1. if it's a boolean attribute or not, and 2. has determined
	/// the name boundaries, while skipping past any whitespace and after the '=' if applicable.
	/// </summary>
	/// <param name="nameStartPos">Starting position of the attribute name.</param>
	/// <param name="nameLength">Length of the attribute name.</param>
	/// <param name="isBooleanAttribute">
	/// True if this is a boolean/valueless attribute (no <c>=</c> sign found, OR <c>=</c> found
	/// but no value follows).
	/// <para />
	/// [UPDATE: dude, I forget if `=` with no value if we're calling that
	/// boolean value... code-wise and test wise things are working but would like to verify...]
	/// </param>
	/// <param name="htmlSpan">The HTML span being parsed.</param>
	/// <returns>True if attr (+ possible value) found / added</returns>
	/// <remarks>
	/// This method assumes the caller has already:
	/// <list type="bullet">
	/// <item><description>Located the attribute name boundaries (start position and length)</description></item>
	/// <item><description>Determined whether it's a boolean attribute (by checking for <c>=</c> sign)</description></item>
	/// <item><description>Positioned <see cref="_currPos"/> appropriately for value parsing</description></item>
	/// </list>
	/// The method validates the attribute name, parses any value (if not boolean), and adds it to the dictionary.
	/// </remarks>
	private bool TryParseAttrAtIndicatedPos(
		int nameStartPos,
		int nameLength,
		bool isBooleanAttribute,
		ReadOnlySpan<char> htmlSpan)
	{
		if(nameLength < 1)
			return false;

		// Extract and validate attribute name
		ReadOnlySpan<char> nameSpan = htmlSpan.Slice(nameStartPos, nameLength);
		string attrName = new string(nameSpan).NullIfEmptyTrimmed();

		if(attrName.IsNulle())
			return false;

		// Validate first character of attribute name
		// (Must be letter or underscore, not digit or dash)
		if(!attrName[0].IsAsciiLetter() &&
			!XmlConvert.IsStartNCNameChar(attrName[0]))
			return false;

		char quoteChar = default;

		// If not already determined to be boolean, check what follows the '='
		if(!isBooleanAttribute)
			isBooleanAttribute = ProcessEqualsSignAndDetermineIfBoolean(ref quoteChar, htmlSpan);

		// Boolean attribute - store with empty string value
		if(isBooleanAttribute) {
			AddAttribute(attrName, null);
			_currPos++;
			return true;
		}

		// Has a value - parse it
		bool isUnquotedValue = quoteChar == ' '; // Space is our convention for unquoted
		if(!isUnquotedValue)
			_currPos++; // Move past the opening quote

		string attrValue;
		bool success = isUnquotedValue
			? ParseUnquotedAttributeValue(out attrValue, htmlSpan)
			: ParseQuotedAttributeValue(quoteChar, out attrValue, htmlSpan);

		if(!success)
			return false;

		AddAttribute(attrName, attrValue);
		return true;
	}

	/// <summary>
	/// Parses a quoted attribute value (enclosed in " or ').
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool ParseQuotedAttributeValue(char quoteChar, out string value, ReadOnlySpan<char> htmlSpan)
	{
		value = null;

		if(quoteChar != '"' && quoteChar != '\'')
			throw new ArgumentException($"Invalid quote character: '{quoteChar}'", nameof(quoteChar));

		int lengthToClosingQuote = CountToCharFromCurrentPosition(quoteChar, htmlSpan);
		if(lengthToClosingQuote < 0)
			return false; // No closing quote found

		// Extract value (or use empty string if zero-length)
		value = lengthToClosingQuote == 0
			? ""
			: new string(htmlSpan.Slice(_currPos, lengthToClosingQuote));

		_currPos += lengthToClosingQuote + 1; // +1 to move past the closing quote
		return true;
	}

	/// <summary>
	/// Parses an unquoted attribute value (terminated by whitespace or end of tag).
	/// <para>Example: <c><![CDATA[<input type=text>]]></c></para>
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool ParseUnquotedAttributeValue(out string value, ReadOnlySpan<char> htmlSpan)
	{
		int lengthToWhitespace = CountToNextWhitespaceOrEnd(htmlSpan);

		if(lengthToWhitespace < 1) {
			// Either no whitespace found (end of tag) or immediate whitespace
			// Both cases result in empty value: <div attr=> or <div attr= >
			value = "";
			return true;
		}

		value = new string(htmlSpan.Slice(_currPos, lengthToWhitespace));
		_currPos += lengthToWhitespace;
		return true;
	}

	/// <summary>
	/// CALL THIS ONLY WHEN CURRPOS *IS* `=`. VERIFIES AND THROWS IF NOT!
	/// Processes the '=' character at _currPos and determines if the attribute is actually boolean
	/// (has '=' but no value follows).
	/// Note: "boolean attribute" is indeed the technical name for attributes that have no value.
	/// Note as well: docs say that attribute names with a trailing `=` but NO value following
	/// (ie with no quotes, just an `=` and then end or next attribute) are INVALID HTML, even
	/// though in practise it's common and browsers handle. So we do too, and we DO treat that
	/// as a boolean attribute.
	/// </summary>
	/// <param name="quoteChar">
	/// Output parameter that receives the quote character (<c>"</c>, <c>'</c>), or <c>' '</c> for unquoted values.
	/// </param>
	/// <param name="htmlSpan"></param>
	/// <returns>True if this is a boolean attribute; false if it has a value.</returns>
	private bool ProcessEqualsSignAndDetermineIfBoolean(ref char quoteChar, ReadOnlySpan<char> htmlSpan)
	{
		if(htmlSpan[_currPos] != '=')
			throw new InvalidOperationException("Current position must be at '=' character");

		_currPos++;
		if(EndReached)
			return true; // "<div attr=/>" - boolean

		quoteChar = htmlSpan[_currPos];

		// Has opening quote - definitely has a value
		if(quoteChar == '"' || quoteChar == '\'')
			return false;

		// Skip any whitespace after '='
		int whitespaceCount = SkipWhitespaceOrEnd(htmlSpan);
		if(EndReached)
			return true; // "<div attr= />" - boolean

		quoteChar = htmlSpan[_currPos];

		// After whitespace, found a quote - has a value
		if(quoteChar == '"' || quoteChar == '\'')
			return false;

		// Had '=' then whitespace then non-quote - this is boolean
		// Example: "<div attr= >" or "<div attr= attr2=..."
		if(whitespaceCount > 0) {
			_currPos--;
			return true;
		}

		// Had '=' with no whitespace and no quote - unquoted value
		// Example: "<div attr=value>"
		quoteChar = ' '; // Convention: space indicates "any whitespace terminates"
		return false;
	}

	/// <summary>
	/// Adds an attribute to the Attributes dictionary.
	/// Initializes the dictionary if this is the first attribute. (PERF! doesn't
	/// alloc dict if no attributes!) If a duplicate attribute name exists,
	/// the new value OVERWRITES the old one!
	/// <para />
	/// Note this claim: "Modern browsers universally treat the last occurrence of a duplicate
	/// attribute as the winner." So we must do too. LAST ONE IN WINS.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void AddAttribute(string name, string value)
	{
		Attributes ??= new Dictionary<string, string>(capacity: 4);
		Attributes[name] = value; // OVERWRITES any earlier duplicate!
	}


	// === NAVIGATION HELPER METHODS ===
	// Note: "Count" methods do NOT alter position, while "Skip" methods advance position

	/// <summary>
	/// Counts characters from the current position until a whitespace character is encountered or end is reached.
	/// Does NOT modify the current position, but reads it.
	/// </summary>
	/// <returns>The number of non-whitespace characters from current position.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int CountToNextWhitespaceOrEnd(ReadOnlySpan<char> span)
	{
		int i = _currPos;

		for(; i < _searchLength; i++) {
			if(XmlConvert.IsWhitespaceChar(span[i]))
				break;
		}

		return i - _currPos;
	}

	/// <summary>
	/// Counts characters from the current position until the specified character is found.
	/// Does not modify the current position.
	/// </summary>
	/// <param name="targetChar">The character to search for.</param>
	/// <returns>
	/// The number of characters from current position to the target character (excluding the target),
	/// or -1 if the character is not found.
	/// </returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int CountToCharFromCurrentPosition(char targetChar, ReadOnlySpan<char> span)
	{
		int i = _currPos;

		for(; i < _searchLength; i++) {
			if(span[i] == targetChar)
				return i - _currPos;
		}

		return -1;
	}

	/// <summary>
	/// ADVANCES current position past all whitespace chars.
	/// Stops at the first non-whitespace character or when end is reached.
	/// </summary>
	/// <returns>The number of whitespace characters skipped.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int SkipWhitespaceOrEnd(ReadOnlySpan<char> span)
	{
		int whitespaceCount = 0;

		for(; _currPos < _searchLength; _currPos++, whitespaceCount++) {
			if(!XmlConvert.IsWhitespaceChar(span[_currPos]))
				break;
		}

		return whitespaceCount;
	}


	// === VALIDATION HELPER METHODS ===

	/// <summary>
	/// Determines whether all characters in the span are ASCII letters or digits (a-z, A-Z, 0-9),
	/// optionally allowing underscores and dashes.
	/// </summary>
	/// <param name="span">The character span to validate.</param>
	/// <param name="allowUnderscore">Whether to allow underscore '_' characters.</param>
	/// <param name="allowDash">Whether to allow dash '-' characters.</param>
	/// <returns>True if all characters meet the criteria; otherwise false.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsAsciiAlphaNumeric(
		ReadOnlySpan<char> span,
		bool allowUnderscore = false,
		bool allowDash = false)
	{
		for(int i = 0; i < span.Length; i++) {
			char c = span[i];

			if(!c.IsAsciiLetterOrDigit()) {
				if(c == '_' && allowUnderscore)
					continue;
				if(c == '-' && allowDash)
					continue;

				return false;
			}
		}

		return true;
	}
}
