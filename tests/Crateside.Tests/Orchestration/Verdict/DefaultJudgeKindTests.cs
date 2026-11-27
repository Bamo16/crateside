using Crateside.Core.Orchestration.Verdict;
using Microsoft.Extensions.Configuration;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class DefaultJudgeKindTests
{
    private sealed class Install(bool isInstalled) : IClaudeInstall
    {
        public bool IsInstalled { get; } = isInstalled;
    }

    private static JudgeOptions Configured(bool installed, string? kind)
    {
        var options = new JudgeOptions();

        if (kind is not null)
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Kind"] = kind })
                .Build()
                .Bind(options);

        new DefaultJudgeKind(new Install(installed)).PostConfigure(null, options);

        return options;
    }

    [Fact]
    public void An_unset_kind_is_Claude_Code_when_it_is_installed()
    {
        var options = Configured(installed: true, kind: null);

        Assert.Equal(JudgeKind.ClaudeCode, options.Kind);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void An_unset_kind_is_none_when_Claude_Code_is_not_installed()
    {
        var options = Configured(installed: false, kind: null);

        Assert.Equal(JudgeKind.None, options.Kind);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void A_kind_that_is_set_stands_whatever_is_installed()
    {
        Assert.Equal(JudgeKind.None, Configured(installed: true, kind: "None").Kind);
        Assert.Equal(JudgeKind.ClaudeCode, Configured(installed: false, kind: "ClaudeCode").Kind);
    }

    [Fact]
    public void With_no_judge_nothing_spends_the_Claude_quota()
    {
        Assert.False(Configured(installed: false, kind: null).SpendsClaudeQuota);
        Assert.True(Configured(installed: true, kind: null).SpendsClaudeQuota);
    }
}
