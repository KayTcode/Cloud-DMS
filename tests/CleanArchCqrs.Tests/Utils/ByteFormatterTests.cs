using CleanArchCqrs.Application.Common.Utils;
using FluentAssertions;
using Xunit;

namespace CleanArchCqrs.Tests.Utils;

public class ByteFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(5368709120, "5 GB")]
    [InlineData(1099511627776, "1 TB")]
    public void Format_ShouldReturnCorrectHumanReadableRepresentation(long bytes, string expected)
    {
        var result = ByteFormatter.Format(bytes);
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_ShouldHandleNegativeBytes()
    {
        var result = ByteFormatter.Format(-1024);
        result.Should().Be("-1 KB");
    }
}
