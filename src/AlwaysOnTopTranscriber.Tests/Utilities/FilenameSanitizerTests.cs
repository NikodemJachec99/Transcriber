using AlwaysOnTopTranscriber.Core.Utilities;

namespace AlwaysOnTopTranscriber.Tests.Utilities;

public sealed class FilenameSanitizerTests
{
    [Theory]
    [InlineData("my:session?name", "my_session_name")]
    [InlineData("CON", "CON_file")]
    [InlineData("   ", "session")]
    public void Sanitize_NormalizesUnsafeWindowsNames(string input, string expected)
    {
        var actual = FilenameSanitizer.Sanitize(input);
        Assert.Equal(expected, actual);
    }
}
