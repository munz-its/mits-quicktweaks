using MunzQuickTweaks.Core.Safety;
using Xunit;

namespace MunzQuickTweaks.Tests;

internal sealed class FakeRestorePoints : IRestorePointService
{
    public ProtectionState State { get; set; } = ProtectionState.Enabled;

    public bool CreateFails { get; set; }

    public bool EnableFails { get; set; }

    public int Created { get; private set; }

    public ProtectionState GetProtectionState() => State;

    public OperationResult EnableProtection()
    {
        if (EnableFails)
        {
            return OperationResult.Fail("enable kaputt");
        }

        State = ProtectionState.Enabled;
        return OperationResult.Ok;
    }

    public OperationResult Create(string description)
    {
        if (CreateFails || State != ProtectionState.Enabled)
        {
            return OperationResult.Fail("create kaputt");
        }

        Created++;
        return OperationResult.Ok;
    }

    public IReadOnlyList<RestorePointInfo> List() => Array.Empty<RestorePointInfo>();
}

internal sealed class FakePrompt : ISafetyPrompt
{
    public bool? EnableAnswer { get; set; } = true;

    public bool ContinueAnswer { get; set; }

    public int EnableAsked { get; private set; }

    public List<string> ContinueReasons { get; } = new();

    public List<string> Stages { get; } = new();

    public bool? AskEnableProtection()
    {
        EnableAsked++;
        return EnableAnswer;
    }

    public bool AskContinueWithoutRestorePoint(string reason)
    {
        ContinueReasons.Add(reason);
        return ContinueAnswer;
    }

    public void ReportProgress(string stage) => Stages.Add(stage);
}

public sealed class RestorePointGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-gate-" + Guid.NewGuid().ToString("N"));
    private readonly FakeRestorePoints _rp = new();
    private readonly FakePrompt _prompt = new();

    private HistoryStore History => new(Path.Combine(_dir, "history.jsonl"));

    private RestorePointGate CreateGate() => new(_rp, History);

    [Fact]
    public void Creates_exactly_one_point_per_session()
    {
        var gate = CreateGate();

        Assert.Equal(GateResult.Proceed, gate.Ensure(_prompt));
        Assert.Equal(GateResult.Proceed, gate.Ensure(_prompt));
        Assert.Equal(GateResult.Proceed, gate.Ensure(_prompt));

        Assert.Equal(1, _rp.Created);
        Assert.Empty(_prompt.ContinueReasons);
    }

    [Fact]
    public void Disabled_protection_is_enabled_on_yes_and_point_created()
    {
        _rp.State = ProtectionState.Disabled;

        Assert.Equal(GateResult.Proceed, CreateGate().Ensure(_prompt));
        Assert.Equal(1, _prompt.EnableAsked);
        Assert.Equal(1, _rp.Created);
        Assert.Equal(new[] { "enable", "create" }, _prompt.Stages);
    }

    [Fact]
    public void Disabled_protection_and_no_continues_without_point_and_asks_only_once()
    {
        _rp.State = ProtectionState.Disabled;
        _prompt.EnableAnswer = false;
        var gate = CreateGate();

        Assert.Equal(GateResult.Proceed, gate.Ensure(_prompt));
        Assert.Equal(GateResult.Proceed, gate.Ensure(_prompt));
        Assert.Equal(1, _prompt.EnableAsked);
        Assert.Equal(0, _rp.Created);
    }

    [Fact]
    public void Cancel_stops_and_asks_again_next_time()
    {
        _rp.State = ProtectionState.Disabled;
        _prompt.EnableAnswer = null;
        var gate = CreateGate();

        Assert.Equal(GateResult.Cancelled, gate.Ensure(_prompt));
        Assert.False(gate.SettledThisSession);
        gate.Ensure(_prompt);
        Assert.Equal(2, _prompt.EnableAsked);
    }

    [Theory]
    [InlineData(true, GateResult.Proceed)]
    [InlineData(false, GateResult.Cancelled)]
    public void Failed_creation_asks_user(bool continueAnswer, GateResult expected)
    {
        _rp.CreateFails = true;
        _prompt.ContinueAnswer = continueAnswer;

        Assert.Equal(expected, CreateGate().Ensure(_prompt));
        Assert.Equal("create kaputt", Assert.Single(_prompt.ContinueReasons));
    }

    [Fact]
    public void Policy_block_asks_without_trying_to_enable()
    {
        _rp.State = ProtectionState.BlockedByPolicy;
        _prompt.ContinueAnswer = true;

        Assert.Equal(GateResult.Proceed, CreateGate().Ensure(_prompt));
        Assert.Equal(0, _prompt.EnableAsked);
        Assert.Equal("policy", Assert.Single(_prompt.ContinueReasons));
    }

    [Fact]
    public void Every_step_is_written_to_the_history()
    {
        _rp.State = ProtectionState.Disabled;
        CreateGate().Ensure(_prompt);

        var actions = History.Latest(10).Select(h => h.Action).ToList();
        Assert.Equal(new[] { HistoryAction.RestorePointCreated, HistoryAction.ProtectionEnabled }, actions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-hist-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "history.jsonl");

    [Fact]
    public void Latest_returns_newest_first_and_respects_count()
    {
        var store = new HistoryStore(FilePath);
        for (var i = 0; i < 5; i++)
        {
            store.Add(HistoryAction.Applied, $"t.{i}", true);
        }

        var latest = store.Latest(3);
        Assert.Equal(new[] { "t.4", "t.3", "t.2" }, latest.Select(l => l.TweakId));
    }

    [Fact]
    public void Broken_line_is_skipped_not_fatal()
    {
        var store = new HistoryStore(FilePath);
        store.Add(HistoryAction.Applied, "t.a", true);
        File.AppendAllText(FilePath, "{ kaputt" + Environment.NewLine);
        store.Add(HistoryAction.Reverted, "t.b", false, "Fehler");

        var latest = store.Latest(10);
        Assert.Equal(2, latest.Count);
        Assert.False(latest[0].Success);
    }

    [Fact]
    public void Missing_file_gives_empty_history()
    {
        Assert.Empty(new HistoryStore(FilePath).Latest(10));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public sealed class GateExceptionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mqt-gatex-" + Guid.NewGuid().ToString("N"));

    private sealed class ExplodingRestorePoints : IRestorePointService
    {
        public ProtectionState GetProtectionState() => ProtectionState.Enabled;

        public OperationResult EnableProtection() => OperationResult.Ok;

        public OperationResult Create(string description) => throw new System.Runtime.InteropServices.COMException();

        public IReadOnlyList<RestorePointInfo> List() => Array.Empty<RestorePointInfo>();
    }

    [Theory]
    [InlineData(true, GateResult.Proceed)]
    [InlineData(false, GateResult.Cancelled)]
    public void Unexpected_exception_asks_user_instead_of_crashing(bool answer, GateResult expected)
    {
        var prompt = new FakePrompt { ContinueAnswer = answer };
        var gate = new RestorePointGate(new ExplodingRestorePoints(), new HistoryStore(Path.Combine(_dir, "h.jsonl")));

        Assert.Equal(expected, gate.Ensure(prompt));
        Assert.Contains("COMException", Assert.Single(prompt.ContinueReasons));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
