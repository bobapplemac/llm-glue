using LlmGlue.Operations;

namespace LlmGlue.Tests;

public sealed class CliHelpTests
{
    [Fact]
    public void HelpDocumentsCommandsAndConfigSearch()
    {
        Assert.Contains("help, --help, /?, -h", CliCommands.HelpText, StringComparison.Ordinal);
        Assert.Contains("config preview opencode", CliCommands.HelpText, StringComparison.Ordinal);
        Assert.Contains("llm-glue.toml", CliCommands.HelpText, StringComparison.Ordinal);
        Assert.Contains("update rollback", CliCommands.HelpText, StringComparison.Ordinal);
    }
}
