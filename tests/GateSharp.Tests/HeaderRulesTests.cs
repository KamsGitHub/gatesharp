using GateSharp.Proxy;

namespace GateSharp.Tests;

public sealed class HeaderRulesTests
{
    [Theory]
    [InlineData("x-foo", "x-foo", true)]
    [InlineData("close, X-Foo", "x-foo", true)]
    [InlineData(" keep-alive ,x-foo ", "X-FOO", true)]
    [InlineData("x-foobar", "x-foo", false)]
    [InlineData("", "x-foo", false)]
    [InlineData(",,", "x-foo", false)]
    public void Connection_nomination_matches_whole_tokens_case_insensitively(string connection, string name, bool expected)
    {
        Assert.Equal(expected, HeaderRules.IsNominatedBy(connection, name));
    }
}
