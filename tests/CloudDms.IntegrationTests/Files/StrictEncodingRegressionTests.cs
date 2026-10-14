using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Infrastructure.Search;
using CleanArchCqrs.Infrastructure.Search.Extraction;
using Microsoft.Extensions.Options;
namespace CloudDms.IntegrationTests.Files;
public sealed class StrictEncodingRegressionTests
{
    [Theory]
    [InlineData("utf8", false)] [InlineData("utf8", true)]
    [InlineData("utf16le", false)] [InlineData("utf16be", false)]
    [InlineData("utf32le", false)] [InlineData("utf32be", false)]
    public async Task ValidBomTextRoundTrips(string kind, bool noBom)
    {
        Encoding encoding = kind switch { "utf16le" => new UnicodeEncoding(false,true,true), "utf16be" => new UnicodeEncoding(true,true,true), "utf32le" => new UTF32Encoding(false,true,true), "utf32be" => new UTF32Encoding(true,true,true), _ => new UTF8Encoding(!noBom,true) };
        const string expected = "Tiếng Việt 日本語 😀";
        using var stream = new MemoryStream(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray());
        var result = await new FileTextExtractor(Options.Create(new DocumentIndexOptions())).ExtractAsync(stream,"sample.txt",default);
        Assert.Equal(expected, Assert.Single(result.Sections).Text);
    }
    public static IEnumerable<object[]> InvalidBytes => new byte[][] {
        [0xff,0xfe,0x00], [0xfe,0xff,0x00], [0xff,0xfe,0x00,0xd8], [0xfe,0xff,0xd8,0x00],
        [0xff,0xfe,0x00,0x00,0x00], [0x00,0x00,0xfe,0xff,0x00], [0xef,0xbb,0xbf,0xc3,0x28]
    }.Select(b => new object[] { b });
    [Theory] [MemberData(nameof(InvalidBytes))]
    public async Task MalformedBomAndSurrogatesHaveNoText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var result = await new FileTextExtractor(Options.Create(new DocumentIndexOptions())).ExtractAsync(stream,"sample.txt",default);
        Assert.Empty(result.Sections); Assert.Equal("UNSUPPORTED_ENCODING", result.Reason);
    }
}
