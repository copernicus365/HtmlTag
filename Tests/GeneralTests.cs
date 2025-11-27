namespace Tests;

public class GeneralTests
{
	[Fact]
	public void BasicUsage()
	{
		// Parse a simple tag
		var tag = new HtmlTag();
		bool parsed = tag.Parse("<div class='container'>");

		True(parsed);
		Equal("div", tag.TagName);
		Equal("container", tag.Attributes["class"]);
	}

	[Fact]
	public void ParsingWithAttributes()
	{
		var tag = new HtmlTag();
		bool parsed = tag.Parse("<img src='photo.jpg' alt='My Photo' width=800 />");

		True(parsed);
		Equal("img", tag.TagName);
		True(tag.IsSelfClosed);
		Equal(3, tag.Attributes.Count);
		Equal("photo.jpg", tag.Attributes["src"]);
		Equal("My Photo", tag.Attributes["alt"]);
		Equal("800", tag.Attributes["width"]);
	}

	[Fact]
	public void HighPerformanceSpanAPI()
	{
		ReadOnlySpan<char> html = "<div class='test'>".AsSpan();
		var tag = new HtmlTag();

		bool parsed = tag.Parse(html, startIndex: 0);

		True(parsed);
		Equal("div", tag.TagName);
		Equal("test", tag.Attributes["class"]);
	}

	[Fact]
	public void ParsingFromWithinLargerDocument()
	{
		string html = "<html><body><div id='main'>Content</div></body></html>";
		var tag = new HtmlTag();

		// Find and parse the div tag starting at position 12
		bool parsed = tag.Parse(html, startIndex: 12);

		True(parsed);
		Equal("div", tag.TagName);
		Equal(12, tag.TagStartIndex);
		Equal(15, tag.TagLength);
		Equal("main", tag.Attributes["id"]);
	}

	[Fact]
	public void BooleanAttributes()
	{
		var tag = new HtmlTag();
		bool parsed = tag.Parse("<input type=text disabled required>");

		True(parsed);
		Equal("text", tag.Attributes["type"]);
		Null(tag.Attributes["disabled"]);
		Null(tag.Attributes["required"]);
	}

	[Fact]
	public void ComplexRealWorldExample()
	{
		var tag = new HtmlTag();
		string html = @"<img 
	 class='thumb-image' 
	 data-image='https://example.com/photo.png' 
	 data-image-dimensions=1920x1080 
	 data-image-focal-point='0.5,0.5' 
	 alt='My Photo' 
	 src='https://example.com/photo.png?format=1200w' 
	 data-load=false />";

		bool parsed = tag.Parse(html);

		True(parsed);
		Equal("img", tag.TagName);
		True(tag.IsSelfClosed);
		Equal(7, tag.Attributes.Count);
		Equal("thumb-image", tag.Attributes["class"]);
		Equal("https://example.com/photo.png", tag.Attributes["data-image"]);
		Equal("1920x1080", tag.Attributes["data-image-dimensions"]);
		Equal("0.5,0.5", tag.Attributes["data-image-focal-point"]);
		Equal("My Photo", tag.Attributes["alt"]);
		Equal("https://example.com/photo.png?format=1200w", tag.Attributes["src"]);
		Equal("false", tag.Attributes["data-load"]);
	}

	/// <summary>
	/// NOTE! This is a dubious example, as HtmlTag is designed to parse a single tag at a time,
	/// not to stream through and correctly find all tags. For that matter, it doesn't handle nested
	/// tags and so forth. In any case... This still works
	/// </summary>
	[Fact]
	public void ParsingMultipleTags()
	{
		string html = "<div><p>Hello</p>  \t<img src='pic.jpg'/></div>";
		var tag = new HtmlTag();
		int pos = 0;
		List<(string name, int position)> foundTags = [];

		while(pos < html.Length) {
			if(html[pos] == '<') {
				if(tag.Parse(html, startIndex: pos)) {
					foundTags.Add((tag.TagName, tag.TagStartIndex));
					pos = tag.TagStartIndex + tag.TagLength;
					continue;
				}
			}
			pos++;
		}

		Equal(3, foundTags.Count);
		Equal(("div", 0), foundTags[0]);
		Equal(("p", 5), foundTags[1]);
		Equal(("img", 20), foundTags[2]);
	}

	[Fact]
	public void HandlingMalformedExtraSpacesAroundEquals()
	{
		var tag = new HtmlTag();

		// Extra spaces around equals
		bool parsed1 = tag.Parse("<div class  =  'test'>");
		True(parsed1);
		Equal("test", tag.Attributes["class"]);
	}

	[Fact]
	public void NoQuoteValues()
	{
		var tag = new HtmlTag();

		// Unquoted attribute values
		bool parsed2 = tag.Parse("<img width=800 height=600>");
		True(parsed2);
		Equal("800", tag.Attributes["width"]);
		Equal("600", tag.Attributes["height"]);
	}

	[Fact]
	public void DuplicateAttributes()
	{
		var tag = new HtmlTag();
		bool parsed = tag.Parse("<div class='first' class='second'>");

		True(parsed);
		Equal("second", tag.Attributes["class"]); // Last one wins
	}

	[Fact]
	public void EmptyAttributeValues()
	{
		var tag = new HtmlTag();
		bool parsed = tag.Parse("<div title='' data-test>");

		True(parsed);
		Equal("", tag.Attributes["title"]);
		Null(tag.Attributes["data-test"]);
		Equal(2, tag.Attributes.Count);
	}

	[Fact]
	public void SpecialCharactersInAttributes()
	{
		var tag = new HtmlTag();
		bool parsed = tag.Parse(@"<div data-json='{""key"":""value""}'>");

		True(parsed);
		// Attributes are NOT unescaped - returned as-is
		Equal(@"{""key"":""value""}", tag.Attributes["data-json"]);
	}
}