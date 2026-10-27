using System.Text.Json.Nodes;
using BrutalBreakout.Configuration;
using BrutalBreakout.Editor;
using BrutalBreakout.Presentation;
using Xunit;

public sealed class RenderingWorkspaceTests
{
    [Fact]
    public void BudgetsStayPendingForRelaunchWhilePathAppliesAndInvalidSaveCannotPublish()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var w = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var d = ConfigurationWorkspaceTests.Doc(w, "rendering");
            var startup = RenderingConfiguration.Load(d.Path).Settings;
            using var owner = new ShmupRendering(startup);
            void Commit(ConfigurationWorkspace.Candidate c)
            {
                var next = c.Get<LoadedRenderingConfiguration>("rendering")!.Settings;
                Assert.Equal(startup.Limits.Instances, next.Limits.Instances);
                owner.ApplyPath(next.Path);
            }
            d.Set("limits.instances", startup.Limits.Instances + 1); d.Set("path", "cpuBuiltTriangles");
            w.Process(Commit);
            Assert.Equal(ShmupRenderingPath.CpuBuiltTriangles, owner.Path); Assert.Same(startup.Limits, owner.Limits);
            Assert.Single(w.PendingRelaunchFields); Assert.True(d.Pending); Assert.True(d.Unsaved);
            w.Request(ConfigurationAction.Apply); w.Process(Commit);
            Assert.Single(w.PendingRelaunchFields); Assert.Same(startup.Limits, owner.Limits);
            string before = File.ReadAllText(d.Path);
            d.Set("limits.instances", 0); w.Request(ConfigurationAction.Save); w.Process(Commit);
            Assert.StartsWith("Rejected:", w.Status); Assert.Equal(before, File.ReadAllText(d.Path));
            d.Set("limits.instances", startup.Limits.Instances + 1); w.Request(ConfigurationAction.Save); w.Process(Commit);
            Assert.Equal(startup.Limits.Instances + 1, RenderingConfiguration.Load(d.Path).Settings.Limits.Instances);
            Assert.Same(startup.Limits, owner.Limits); Assert.Single(w.PendingRelaunchFields);
        });
    }
    [Fact]
    public void CircleEditsApplySameImmutablePolicyLiveAndDiscardWithoutSaving()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var w = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var d = ConfigurationWorkspaceTests.Doc(w, "rendering");
            var startup = RenderingConfiguration.Load(d.Path).Settings;
            using var owner = new ShmupRendering(startup);
            string original = File.ReadAllText(d.Path);
            w.Process(_ => { });
            void Commit(ConfigurationWorkspace.Candidate c)
            {
                var next=c.Get<LoadedRenderingConfiguration>("rendering")!.Settings;
                owner.Apply(next); Assert.Same(next.CircleDetail,owner.CircleDetail);
            }
            ConfigurationControls.EditValue(d, new[] { "circleDetail", "mode" }, JsonValue.Create("fixedSideCount"));
            ConfigurationControls.EditValue(d, new[] { "circleDetail", "fixedSides" }, JsonValue.Create(16));
            w.Process(Commit);
            Assert.Equal(16,owner.CircleDetail.FixedSides);Assert.Empty(w.PendingRelaunchFields);
            Assert.Same(startup.Limits,owner.Limits);Assert.Equal(original,File.ReadAllText(d.Path));
            d.Set("circleDetail.fixedSides",9);w.Request(ConfigurationAction.Save);w.Process(Commit);
            Assert.StartsWith("Rejected:",w.Status);Assert.Equal(16,owner.CircleDetail.FixedSides);
            w.Request(ConfigurationAction.Discard);w.Process(Commit);
            Assert.Equal(CrushKit.Brutal.Draw2D.CircleDetailMode.Automatic,owner.CircleDetail.Mode);
            Assert.Equal(original,File.ReadAllText(d.Path));
        });
    }
    [Fact]
    public void ExternalRenderingEditsConflictAndReloadUsesStrictProductionLoader()
    {
        ConfigurationWorkspaceTests.WithCopy(root =>
        {
            var w = new ConfigurationWorkspace(ConfigurationWorkspaceTests.Paths(root), true);
            var d = ConfigurationWorkspaceTests.Doc(w, "rendering");
            d.Set("path", "cpuBuiltTriangles"); w.Process(_ => { });
            var external = JsonNode.Parse(File.ReadAllText(d.Path))!; external["limits"]!["meshCount"] = 256;
            File.WriteAllText(d.Path, external.ToJsonString());
            w.Request(ConfigurationAction.Save); w.Process(_ => { });
            Assert.Contains("Source changed externally", w.Status); Assert.True(d.Unsaved);
            Assert.Equal(256, RenderingConfiguration.Load(d.Path).Settings.Limits.MeshCount);
            File.WriteAllText(d.Path, "{}"); w.Request(ConfigurationAction.Reload); w.Process(_ => { });
            Assert.StartsWith("Rejected:", w.Status); Assert.Equal("cpuBuiltTriangles", d.Draft.GetProperty("path"u8).GetString());
        });
    }
}
