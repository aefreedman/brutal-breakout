using System.Text;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;
using Brutal.TextureApi.Abstractions;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using BrutalBreakout.Configuration;
using BrutalBreakout.Presentation.Overlay;

namespace BrutalBreakout.Rendering;

internal sealed unsafe partial class VulkanRenderer
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private OverlaySettings? _overlaySettings;
    private OverlayText? _overlayText;
    private GenericTexture? _debugPixels, _previewPixels;
    private OverlayGpuTexture? _previewGpu;
    private ImTextureRef _previewReference;
    private bool _hasPreviewReference;
    private ImGuiContextPtr _uiContext;
    private bool _hasUiContext, _uiFrame, _overlayActiveFrame;
    private GlfwBackend? _uiPlatform;
    private VulkanBackend? _uiRenderer;
    private OverlayFrame? _overlayFrame;
    private bool _inspectorVisible;
    private readonly OverlayCaptureTracker _captureTracker = new();
    private readonly Dictionary<string, OverlayGpuTexture> _overlayTextures = new(StringComparer.Ordinal);
    private readonly List<BufferEx> _overlayStaging = [];
    private int _swapchainGeneration, _recordedLabels, _recordedLabelIndices, _recordedUiIndices;
    private bool _recordedImage, _recordedInspector;
    public OverlayPresentationEvidence OverlayEvidence { get; private set; }

    public void ConfigureOverlay(OverlaySettings settings)
    {
        CheckOverlayThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_overlaySettings != null) throw new InvalidOperationException("Overlay is startup-only.");
        _overlaySettings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>Before input routing. HUD coordinates/font size are physical framebuffer pixels, independent of ImGui/DPI.</summary>
    public OverlayCapture BeginOverlayFrame(bool? inspectorVisible = null)
    {
        CheckOverlayThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        CancelOverlayFrame();
        var settings = _overlaySettings ?? throw new InvalidOperationException("Configure overlay first.");
        if (!settings.Enabled) return _captureTracker.Update(false, false, false);
        _inspectorVisible = inspectorVisible ?? settings.DebugVisible;
        if (!_inspectorVisible && _hasUiContext)
        {
            // Remove callbacks/context immediately on close, even if minimized, after their last GPU use.
            Check(_device!.WaitForFences([_frameFence], true, -1), "Wait for inspector retirement");
            ReleaseInspector();
        }
        var size = _window.FramebufferSize;
        if (_window.IsIconified || size.X <= 0 || size.Y <= 0) return _captureTracker.Update(false, false, false);
        if (_recreate || size.X != _extent.Width || size.Y != _extent.Height) RecreateSwapchain(size);
        ResourceLifetime.OnFailure(EnsureGameOverlay, DisposeOverlay);
        if (_inspectorVisible)
        {
            ResourceLifetime.OnFailure(EnsureInspector, ReleaseInspector);
            ImGui.SetCurrentContext(_uiContext);
            _uiRenderer!.NewFrame(); _uiPlatform!.NewFrame();
            ImGui.GetStyle().FontSizeBase = settings.FontSize / ImGui.GetIO().DisplayFramebufferScale.Y;
            ImGui.NewFrame(); _uiFrame = true;
        }
        _overlayActiveFrame = true;
        return _captureTracker.Update(_inspectorVisible,
            _uiFrame && ImGui.GetIO().WantCaptureMouse, _uiFrame && ImGui.GetIO().WantCaptureKeyboard);
    }

    public void SetOverlayFrame(OverlayFrame frame)
    {
        CheckOverlayThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = _overlaySettings ?? throw new InvalidOperationException("Configure overlay first.");
        ValidateOverlayFrame(frame, settings);
        if (_overlayActiveFrame) _overlayFrame = new(frame.Labels.ToArray(), frame.Diagnostics);
    }

    private static void ValidateOverlayFrame(OverlayFrame frame, OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(frame.Labels); ArgumentNullException.ThrowIfNull(frame.Diagnostics);
        if (frame.Labels.Count > settings.MaxLabels) throw new ArgumentException("Too many overlay labels.");
        foreach (var label in frame.Labels)
        {
            if (label == null || string.IsNullOrEmpty(label.Text) || label.Text.Length > settings.MaxLabelCharacters || label.Text.Any(char.IsControl))
                throw new ArgumentException("Invalid overlay label.");
            var c = label.Color;
            if (!float.IsFinite(c.R) || !float.IsFinite(c.G) || !float.IsFinite(c.B) || !float.IsFinite(c.A) || c.A < 0 || c.A > 1 ||
                c.R < 0 || c.G < 0 || c.B < 0 || c.R > c.A || c.G > c.A || c.B > c.A) throw new ArgumentException("Expected premultiplied-linear palette tint.");
        }
        var d = frame.Diagnostics;
        if (d.State == null || d.State.Length > settings.MaxLabelCharacters || d.State.Any(char.IsControl) ||
            !double.IsFinite(d.FrameMilliseconds) || d.FrameMilliseconds < 0 || d.Balls < 0 || d.Bricks < 0 ||
            d.PresentedShapeFrames < 0 || d.ShapeVertices < 0 || d.ShapeIndices < 0) throw new ArgumentException("Invalid diagnostic facts.");
    }

    private void EnsureInspector()
    {
        var s = _overlaySettings!;
        // Decode once through BRUTAL.Texture/Stb. The sole derived preview is straight-linear for stock ImGui.
        _debugPixels ??= OverlayPixels.LoadPng(s.ImagePath, s.MaxTextureDimension, s.MaxEncodedImageBytes);
        _previewPixels ??= OverlayPixels.CreateInspectorPreview(_debugPixels);
        if (!_hasUiContext)
        {
            _uiContext = ImGui.CreateContext();
            if (_uiContext.IsNull()) throw new InvalidOperationException("ImGui context creation failed.");
            _hasUiContext = true;
            var io = ImGui.GetIO(); io.IniFilename = default; io.LogFilename = default;
            io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
            _uiPlatform = new GlfwBackend(_window, GlfwBackend.ClientApi.Vulkan);
        }
        ImGui.SetCurrentContext(_uiContext);
        _uiRenderer ??= new VulkanBackend(new VulkanBackend.CreateInfo
        {
            Device = (DeviceEx)_device!, GraphicsQueue = _queue, RenderPass = _renderPass, SubPass = 0,
            MinImageCount = Math.Max(2, _images.Length), ImageCount = _images.Length, SampleCount = Samples,
            DescriptorPoolSize = 2, // One diagnostic image plus the backend font atlas; not a tunable cache.
        });
    }

    // After the shared fence; staging copies and barriers precede the render pass.
    private void PrepareOverlay()
    {
        _recordedLabels = _recordedLabelIndices = _recordedUiIndices = 0;
        _recordedImage = _recordedInspector = false;
        var retiredStaging = _overlayStaging.ToArray(); _overlayStaging.Clear();
        ResourceLifetime.ReleaseAll(retiredStaging.Select(b => (Action)b.Dispose).ToArray());
        _overlayDraws.Clear(); _overlayBatch?.Clear();
        if (!_overlayActiveFrame) return;
        if (_overlayFrame == null) { CancelOverlayFrame(); return; }
        var s = _overlaySettings!;
        var active = _overlayFrame.Labels.Select(l => l.Text).ToHashSet(StringComparer.Ordinal);
        if (_overlayTextures.Count + active.Count(t => !_overlayTextures.ContainsKey(t)) > s.CacheEntries)
            foreach (var key in _overlayTextures.Keys.Where(t => !active.Contains(t)).ToArray()) ReleaseOverlayTexture(key);
        for (int i = 0; i < _overlayFrame.Labels.Count; i++)
        {
            var label = _overlayFrame.Labels[i];
            if (!_overlayTextures.TryGetValue(label.Text, out var texture))
            {
                // Borrowed GenericTexture is copied to GPU staging immediately, before the next cache access can evict it.
                texture = UploadOverlayTexture(_overlayText!.Get(label.Text), gameDescriptor: true);
                try { _overlayTextures.Add(label.Text, texture); }
                catch { ReleaseGpuTexture(texture); throw; }
            }
            int first = _overlayBatch!.Indices.Length;
            if (texture.HasCoverage && _overlayBatch.TryAdd(s.Left, s.Top + i * s.LineSpacing,
                texture.Width, texture.Height, _extent.Width, _extent.Height, label.Color))
                _overlayDraws.Add((first, texture.Set));
        }
        UploadBuffer(ref _overlayVertices, _overlayBatch!.Vertices, VkBufferUsageFlags.VertexBufferBit);
        UploadBuffer(ref _overlayIndices, _overlayBatch.Indices, VkBufferUsageFlags.IndexBufferBit);
        if (_uiFrame) PrepareInspector();
        _overlayActiveFrame = false;
    }

    private void PrepareInspector()
    {
        ImGui.SetCurrentContext(_uiContext);
        var s = _overlaySettings!; var io = ImGui.GetIO(); var scale = io.DisplayFramebufferScale;
        // Inspector settings are also physical pixels; convert only at the local ImGui boundary.
        ImGui.SetNextWindowPos(new float2(s.DebugLeft / scale.X, s.DebugTop / scale.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(s.DebugWidth / scale.X, s.DebugHeight / scale.Y), ImGuiCond.Always);
        if (ImGui.Begin("Diagnostics (read only)"u8, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
        {
            var d = _overlayFrame!.Diagnostics;
            ImGui.Text(Encoding.UTF8.GetBytes($"{d.State}\nBalls {d.Balls}  Bricks {d.Bricks}\nFrame {d.FrameMilliseconds:F2} ms\nShape frames {d.PresentedShapeFrames}\nVertices {d.ShapeVertices}  Indices {d.ShapeIndices}\nOverlay frames {OverlayEvidence.PresentedFrames}\nText cache {_overlayText!.Count}, rasterizations {_overlayText.Rasterizations}, hits {_overlayText.CacheHits}"));
            var min = ImGui.GetCursorScreenPos(); var available = ImGui.GetContentRegionAvail();
            var imageSize = new float2(s.DebugImageSize / scale.X, s.DebugImageSize / scale.Y);
            if (OverlayPixels.HasCoverage(_previewPixels!) && available.X > 0 && available.Y > 0 && Visible(min, min + imageSize, io.DisplaySize))
            {
                if (!_hasPreviewReference)
                {
                    _previewGpu = UploadOverlayTexture(_previewPixels!, gameDescriptor: false);
                    _previewReference = _uiRenderer!.AddTexture(_overlaySampler, _previewGpu.View.VkImageView, VkImageLayout.ShaderReadOnlyOptimal);
                    _hasPreviewReference = true;
                }
                var drawList = ImGui.GetWindowDrawList(); int before = drawList.IdxBuffer.Count;
                ImGui.Image(_previewReference, imageSize);
                _recordedImage = drawList.IdxBuffer.Count > before; // Actual image indices, not just a requested widget.
            }
            _recordedInspector = Visible(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), io.DisplaySize);
        }
        ImGui.End(); ImGui.Render(); _uiFrame = false;
        _recordedUiIndices = ImGui.GetDrawData().TotalIdxCount;
    }

    private static bool Visible(float2 min, float2 max, float2 size) => max.X > 0 && max.Y > 0 && min.X < size.X && min.Y < size.Y && max.X > min.X && max.Y > min.Y;

    private void CountOverlayPresentation()
    {
        int indices = _recordedLabelIndices + _recordedUiIndices;
        if (indices <= 0) return;
        var old = OverlayEvidence;
        OverlayEvidence = new(old.PresentedFrames + 1, old.TextFrames + (_recordedLabels > 0 ? 1 : 0),
            old.ImageFrames + (_recordedImage && _recordedUiIndices > 0 ? 1 : 0), old.InspectorFrames + (_recordedInspector && _recordedUiIndices > 0 ? 1 : 0),
            old.LabelQuads + _recordedLabels, old.DrawIndices + indices, _extent.Width, _extent.Height, _swapchainGeneration);
    }

    private void CancelOverlayFrame()
    {
        if (_uiFrame) { _uiFrame = false; ImGui.SetCurrentContext(_uiContext); ImGui.EndFrame(); }
        _overlayFrame = null; _overlayActiveFrame = false;
    }
    private void CheckOverlayThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Overlay is main-thread owned.");
    }

    private void ReleaseInspector()
    {
        List<Action> releases = [];
        if (_hasUiContext) ImGui.SetCurrentContext(_uiContext);
        if (_uiFrame) { _uiFrame = false; releases.Add(ImGui.EndFrame); }
        if (_hasPreviewReference) { _hasPreviewReference = false; releases.Add(() => _uiRenderer!.RemoveTexture(_previewReference)); }
        if (_previewGpu != null) { var gpu = _previewGpu; _previewGpu = null; releases.Add(() => ReleaseGpuTexture(gpu)); }
        if (_uiRenderer != null) releases.Add(_uiRenderer.Dispose);
        if (_uiPlatform != null) releases.Add(_uiPlatform.Dispose);
        if (_hasUiContext) releases.Add(() => ImGui.DestroyContext(_uiContext));
        try { ResourceLifetime.ReleaseAll(releases.ToArray()); }
        finally { _uiRenderer = null; _uiPlatform = null; _hasUiContext = false; }
    }

    // Device-idle swapchain boundary, before destroying the render pass.
    private void ReleaseOverlaySwapchain()
    {
        List<Action> releases = [CancelOverlayFrame, ReleaseInspector];
        foreach (var key in _overlayTextures.Keys.ToArray()) releases.Add(() => ReleaseOverlayTexture(key));
        foreach (var staging in _overlayStaging) releases.Add(staging.Dispose); _overlayStaging.Clear();
        if (!_overlayPipeline.IsNull()) { var pipeline = _overlayPipeline; _overlayPipeline = default; releases.Add(() => _device!.DestroyPipeline(pipeline, null)); }
        ResourceLifetime.ReleaseAll(releases.ToArray());
    }

    private void DisposeOverlay()
    {
        List<Action> releases = [ReleaseOverlaySwapchain, DisposeOverlayGpu];
        if (_overlayText != null) { var text = _overlayText; _overlayText = null; releases.Add(text.Dispose); }
        if (_debugPixels != null) { var texture = _debugPixels; _debugPixels = null; releases.Add(texture.Destroy); }
        if (_previewPixels != null) { var texture = _previewPixels; _previewPixels = null; releases.Add(texture.Destroy); }
        ResourceLifetime.ReleaseAll(releases.ToArray());
    }
}
