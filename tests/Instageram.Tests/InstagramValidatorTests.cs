using Xunit;

namespace Instageram.Tests;

public class InstagramValidatorTests
{
    [Theory]
    [InlineData("https://www.instagram.com/example/", "example")]
    [InlineData("https://instagram.com/example", "example")]
    [InlineData("http://instagram.com/example.page/", "example.page")]
    [InlineData("instagram.com/example", "example")]
    [InlineData("https://www.instagram.com/_user.name_1/", "_user.name_1")]
    [InlineData("https://www.instagram.com/example/?hl=fa", "example")]
    public void Accepts_valid_profile_urls(string input, string expected)
    {
        var accepted = InstagramValidator.TryGetUsername(input, out var username, out var message);

        Assert.True(accepted, message);
        Assert.Equal(expected, username);
    }

    [Theory]
    [InlineData("", "empty input")]
    [InlineData("   ", "blank input")]
    [InlineData("https://example.com/someone/", "wrong host")]
    [InlineData("https://www.instagram.com/", "no username in the path")]
    [InlineData("https://www.instagram.com/name with spaces/", "invalid characters")]
    [InlineData("https://www.instagram.com/this-name-is-far-too-long-to-be-real/", "too long")]
    public void Rejects_invalid_input_with_a_message(string input, string because)
    {
        var accepted = InstagramValidator.TryGetUsername(input, out _, out var message);

        Assert.False(accepted, because);
        Assert.False(string.IsNullOrWhiteSpace(message), "a reason must be reported");
    }
}
