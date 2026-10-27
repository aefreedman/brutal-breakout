using System.Text.Json.Nodes;
using BrutalBreakout.Editor;
using CrushKit.Brutal.ImGui;
using Xunit;

public sealed class InspectorPolishConfigurationTests
{
    private static string Path => ConfigurationWorkspaceTests.Paths()["inspector"];
    [Fact]
    public void RequiredThemeAndFeedbackObjectsValidateBeforeNativeStartupAndAreRetained()
    {
        var loaded = InspectorConfiguration.Load(Path);
        using var inspector = new ImGuiInspector(loaded.Settings, null, null, loaded.Theme);
        var feedback = new ConfigurationFeedback(loaded.Feedback, loaded.Theme);
        var controls = new ConfigurationControls(ConfigurationWorkspaceTests.Create(), loaded.ConfigurationWindow, loaded.Theme);
        var gameplay = new GameplayControls(loaded.GameplayWindow, loaded.Theme);
        Assert.Same(loaded.Theme, inspector.Theme); Assert.Same(loaded.Theme, controls.Theme);
        Assert.Same(loaded.Theme, gameplay.Theme); Assert.Same(loaded.Theme, feedback.Theme);
        Assert.Same(loaded.Feedback, feedback.Settings);
        Assert.Equal(4, loaded.Feedback.Capacity); Assert.Equal(2048, loaded.Feedback.MaxTextCharacters);
        Assert.True(loaded.Theme.Colors.Text.X > loaded.Theme.Colors.MutedText.X);
    }
    [Theory]
    [InlineData("theme")]
    [InlineData("feedback")]
    public void NewRootObjectsAreRequiredAndCannotBeNull(string property)
    {
        Mutate(n => n.Remove(property)); Mutate(n => n[property] = null);
    }
    [Fact]
    public void ColorsMetricsAndFeedbackRejectMissingUnknownAndInvalidValues()
    {
        Mutate(n => n["theme"]!["colors"]!.AsObject().Remove("text"));
        Mutate(n => n["theme"]!["colors"]!["text"] = new JsonArray(1, 1, 1));
        Mutate(n => n["theme"]!["colors"]!["text"] = new JsonArray(2, 1, 1, 1));
        Mutate(n => n["theme"]!["colors"]!["unknown"] = 1);
        Mutate(n => n["theme"]!["metrics"]!["scrollbarEm"] = 0);
        Mutate(n => n["theme"]!["metrics"]!["disabledAlpha"] = 2);
        Mutate(n => n["feedback"]!["capacity"] = 0);
        Mutate(n => n["feedback"]!["maxTextCharacters"] = 127);
        Mutate(n => n["feedback"]!["successSeconds"] = 0);
        Mutate(n => n["feedback"]!.AsObject().Remove("informationSeconds"));
    }
    private static void Mutate(Action<JsonObject> mutate)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path))!.AsObject(); mutate(node);
        Assert.Throws<InvalidDataException>(() => InspectorConfiguration.Load(Path, node.ToJsonString()));
    }
}
