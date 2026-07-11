using System;
using System.Collections.Generic;
using System.IO;
using Cleanerer.Services;

namespace Cleanerer.Tests;

/// <summary>
/// Covers <see cref="RulesService"/> persistence: a full round-trip of a rule list (every field), and
/// the "never throw" fallbacks (missing file, corrupt JSON → empty list). Each test runs against a
/// throwaway temp directory.
/// </summary>
public class RulesServiceTests : IDisposable
{
    private readonly string _dir;

    public RulesServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "Cleanerer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private RulesService NewService() => new RulesService(_dir);

    // Fixed Ids so two calls produce value-equal lists (record equality includes Id), letting a
    // save-in-one-service / load-in-another comparison succeed.
    private static readonly Guid Id1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Id2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Id3 = new("33333333-3333-3333-3333-333333333333");

    private static List<ProcessRule> SampleRules() => new()
    {
        new ProcessRule { Id = Id1, MatchName = "chrome", ThresholdMb = 2048, Action = RuleAction.Trim, Enabled = true },
        new ProcessRule { Id = Id2, MatchName = "lsass", ThresholdMb = 100, Action = RuleAction.Kill, Enabled = false },
        new ProcessRule { Id = Id3, MatchName = "*", ThresholdMb = 4096, Action = RuleAction.Kill, Enabled = true },
    };

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        List<ProcessRule> original = SampleRules();

        NewService().Save(original);
        IReadOnlyList<ProcessRule> reloaded = NewService().Load();

        Assert.Equal(original, reloaded); // record value equality compares Id + all fields
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(NewService().Load());
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_dir, "rules.json"), "{ not valid json ]]");

        Assert.Empty(NewService().Load());
    }

    [Fact]
    public void Constructor_LoadsCurrentFromDisk()
    {
        NewService().Save(SampleRules());

        RulesService fresh = NewService();

        Assert.Equal(SampleRules(), fresh.Current);
    }

    [Fact]
    public void Save_RaisesRulesChangedWithNewValues()
    {
        RulesService service = NewService();
        IReadOnlyList<ProcessRule>? received = null;
        service.RulesChanged += (_, r) => received = r;

        List<ProcessRule> toSave = SampleRules();
        service.Save(toSave);

        Assert.Equal(toSave, received);
        Assert.Equal(toSave, service.Current);
    }

    [Fact]
    public void Save_EmptyList_RoundTrips()
    {
        NewService().Save(new List<ProcessRule>());

        Assert.Empty(NewService().Load());
    }
}
