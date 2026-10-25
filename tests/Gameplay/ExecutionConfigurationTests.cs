using System.Text.Json;
using BrutalBreakout.Configuration;
using Xunit;

public class ExecutionConfigurationTests
{
    private const string Valid = """
        {"workerCount":2,"maxExpectedConcurrentJobs":64,"physicsWorkerCount":2}
        """;

    private static void WithFile(string json, Action<string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "breakout-execution-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(path, json); check(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShippedBudgetsLoadFromCopiedJsonAndAreSharedUnchanged()
    {
        var loaded = ExecutionConfiguration.LoadForMode([], true, AppContext.BaseDirectory, Path.GetTempPath());
        Assert.NotNull(loaded);
        using var json = JsonDocument.Parse(File.ReadAllText(loaded.Path));
        Assert.Equal(json.RootElement.GetProperty("workerCount"u8).GetInt32(), loaded.Settings.Jobs.WorkerCount);
        Assert.Equal(json.RootElement.GetProperty("physicsWorkerCount"u8).GetInt32(), loaded.Settings.PhysicsWorkerCount);
        Assert.Equal(json.RootElement.GetProperty("maxExpectedConcurrentJobs"u8).GetInt32(), loaded.Settings.Jobs.MaxExpectedConcurrentJobs);
        using var simulation = new BrutalBreakout.Simulation.BallSimulation(TestSettings.Baseline, TestSettings.Gameplay, loaded.Settings);
        Assert.Same(loaded.Settings, simulation.Execution);
    }

    [Theory]
    [InlineData("workerCount")]
    [InlineData("maxExpectedConcurrentJobs")]
    [InlineData("physicsWorkerCount")]
    public void EveryFieldIsRequired(string omitted)
    {
        using var doc = JsonDocument.Parse(Valid);
        var json = JsonSerializer.Serialize(doc.RootElement.EnumerateObject()
            .Where(p => p.Name != omitted).ToDictionary(p => p.Name, p => p.Value));
        Reject(json);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("{\"workerCount\":2,\"maxExpectedConcurrentJobs\":64,\"physicsWorkerCount\":2,\"extra\":1}")]
    [InlineData("{\"workerCount\":2,\"maxExpectedConcurrentJobs\":64,\"physicsWorkerCount\":2,\"workerCount\":3}")]
    [InlineData("{\"workerCount\":2,\"maxExpectedConcurrentJobs\":64,\"physicsWorkerCount\":2,\"worker\\u0043ount\":3}")]
    [InlineData("{\"workerCount\":0,\"maxExpectedConcurrentJobs\":64,\"physicsWorkerCount\":2}")]
    [InlineData("{\"workerCount\":2,\"maxExpectedConcurrentJobs\":0,\"physicsWorkerCount\":2}")]
    [InlineData("{\"workerCount\":2,\"maxExpectedConcurrentJobs\":64,\"physicsWorkerCount\":65}")]
    [InlineData("{\"workerCount\":\"2\",\"maxExpectedConcurrentJobs\":64,\"physicsWorkerCount\":2}")]
    [InlineData("{\"workerCount\":2,\"maxExpectedConcurrentJobs\":64,\"physicsWorkerCount\":null}")]
    public void InvalidDocumentsRejectWithPathContext(string json) => Reject(json);

    private static void Reject(string json) => WithFile(json, path =>
    {
        var error = Assert.Throws<InvalidDataException>(() => ExecutionConfiguration.Load(path));
        Assert.Contains(path, error.Message);
    });

    [Fact]
    public void GallerySkipsImplicitFileButValidatesExplicitOverrides()
    {
        Assert.Null(ExecutionConfiguration.LoadForMode([], false, "missing", Path.GetTempPath()));
        WithFile(Valid, path => Assert.NotNull(ExecutionConfiguration.LoadForMode(
            ["--execution-config", path], false, "missing", Path.GetTempPath())));
        Assert.Throws<ArgumentException>(() => ExecutionConfiguration.LoadForMode(
            ["--execution-config"], false, "missing", Path.GetTempPath()));
    }
}
