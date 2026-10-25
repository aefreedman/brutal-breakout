using System.Text;
using Brutal;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Brutal.Numerics;
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
    private OverlayPixels? _debugPixels;
    private ImGuiContextPtr _uiContext;
    private bool _hasUiContext, _uiFrame;
    private GlfwBackend? _uiPlatform;
    private VulkanBackend? _uiRenderer;
    private OverlayFrame? _overlayFrame;
    private bool _inspectorVisible;
    private readonly OverlayCaptureTracker _captureTracker = new();
    private readonly Dictionary<OverlayPixels, OverlayTexture> _overlayTextures = [];
    private readonly List<BufferEx> _overlayStaging = [];
    private VkSampler _overlaySampler;
    private int _swapchainGeneration;
    private int _recordedLabels;
    private bool _recordedImage, _recordedInspector;
    private int _recordedUiIndices;
    public OverlayPresentationEvidence OverlayEvidence { get; private set; }

    /// <summary>Call once with the validated startup settings; native allocation is deferred to BeginOverlayFrame.</summary>
    public void ConfigureOverlay(OverlaySettings settings)
    {
        CheckOverlayThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_overlaySettings != null) throw new InvalidOperationException("Overlay is startup-only.");
        _overlaySettings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>After OS events, before game input routing. Explicit visibility supports hidden acceptance without input injection.</summary>
    public OverlayCapture BeginOverlayFrame(bool? inspectorVisible = null)
    {
        CheckOverlayThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        CancelOverlayFrame();
        var settings = _overlaySettings ?? throw new InvalidOperationException("Configure overlay first.");
        if (!settings.Enabled) return _captureTracker.Update(false, false, false);
        var size = _window.FramebufferSize;
        if (_window.IsIconified || size.X <= 0 || size.Y <= 0) return _captureTracker.Update(false, false, false);
        if (_recreate || size.X != _extent.Width || size.Y != _extent.Height) RecreateSwapchain(size);
        ResourceLifetime.OnFailure(EnsureOverlay, DisposeOverlay);
        _inspectorVisible = inspectorVisible ?? settings.DebugVisible;
        ImGui.SetCurrentContext(_uiContext);
        _uiRenderer!.NewFrame(); _uiPlatform!.NewFrame(); ImGui.NewFrame(); _uiFrame = true;
        return _captureTracker.Update(_inspectorVisible, ImGui.GetIO().WantCaptureMouse, ImGui.GetIO().WantCaptureKeyboard);
    }

    /// <summary>Supply current presentation facts after advancing simulation, before Draw. Snapshot owned by this frame.</summary>
    public void SetOverlayFrame(OverlayFrame frame)
    {
        CheckOverlayThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        var settings = _overlaySettings ?? throw new InvalidOperationException("Configure overlay first.");
        ValidateOverlayFrame(frame, settings);
        if (_uiFrame) _overlayFrame = new(frame.Labels.ToArray(), frame.Diagnostics);
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

    private void EnsureOverlay()
    {
        var s = _overlaySettings!;
        if (!_hasUiContext)
        {
            _overlayText = new OverlayText(s);
            _debugPixels = OverlayPixels.LoadPng(s.ImagePath, s.MaxTextureDimension, s.MaxEncodedImageBytes);
            _uiContext = ImGui.CreateContext();
            if (_uiContext.IsNull()) throw new InvalidOperationException("ImGui context creation failed.");
            _hasUiContext = true;
            ImGui.GetStyle().FontSizeBase = s.FontSize;
            var io = ImGui.GetIO(); io.IniFilename = default; io.LogFilename = default;
            io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
            _uiPlatform = new GlfwBackend(_window, GlfwBackend.ClientApi.Vulkan);
            _overlaySampler = _device!.CreateSampler(new VkSamplerCreateInfo
            {
                MagFilter = VkFilter.Linear, MinFilter = VkFilter.Linear, MipmapMode = VkSamplerMipmapMode.Nearest,
                AddressModeU = VkSamplerAddressMode.ClampToEdge, AddressModeV = VkSamplerAddressMode.ClampToEdge,
                AddressModeW = VkSamplerAddressMode.ClampToEdge, MaxLod = 0,
            }, null);
        }
        ImGui.SetCurrentContext(_uiContext);
        _uiRenderer ??= new VulkanBackend(new VulkanBackend.CreateInfo
        {
            Device = (DeviceEx)_device!, GraphicsQueue = _queue, RenderPass = _renderPass, SubPass = 0,
            MinImageCount = Math.Max(2, _images.Length), ImageCount = _images.Length, SampleCount = Samples,
            DescriptorPoolSize = checked(s.CacheEntries + s.MaxLabels + 2),
        });
    }

    // Called only after the frame fence and before beginning the render pass.
    private void PrepareOverlay()
    {
        _recordedLabels = 0; _recordedImage = false; _recordedInspector = false; _recordedUiIndices = 0;
        foreach (var staging in _overlayStaging) staging.Dispose();
        _overlayStaging.Clear();
        if (!_uiFrame) return;
        if (_overlayFrame == null) { CancelOverlayFrame(); return; }
        var s = _overlaySettings!;
        ImGui.SetCurrentContext(_uiContext);
        // Resolve the complete active set before eviction, so no descriptor referenced by this frame is freed.
        var pixels = _overlayFrame.Labels.Select(l => _overlayText!.Get(l.Text)).ToArray();
        var active = pixels.Append(_debugPixels!).ToHashSet();
        if (_overlayTextures.Count + active.Count(p => !_overlayTextures.ContainsKey(p)) > s.CacheEntries + 1)
            foreach (var key in _overlayTextures.Keys.Where(p => !active.Contains(p)).ToArray()) ReleaseOverlayTexture(key);
        var io = ImGui.GetIO(); var scale = io.DisplayFramebufferScale;
        var draw = ImGui.GetBackgroundDrawList();
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i]; var c = _overlayFrame.Labels[i].Color;
            var min = new float2(s.Left / scale.X, (s.Top + i * s.LineSpacing) / scale.Y);
            var max = min + new float2(p.Width / scale.X, p.Height / scale.Y);
            if (!p.HasCoverage || c.A <= 0 || !Visible(min, max, io.DisplaySize)) continue;
            // Palette is premultiplied-linear; stock backend expects straight-linear color and SRC_ALPHA blending.
            var tint = new ImColor8(ToByte(c.R / c.A), ToByte(c.G / c.A), ToByte(c.B / c.A), ToByte(c.A));
            draw.AddImage(GetOverlayTexture(p), min, max, null, null, tint); _recordedLabels++;
        }
        if (_inspectorVisible)
        {
            ImGui.SetNextWindowPos(new float2(s.DebugLeft / scale.X, s.DebugTop / scale.Y), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new float2(s.DebugWidth / scale.X, s.DebugHeight / scale.Y), ImGuiCond.Always);
            if (ImGui.Begin("Diagnostics (read only)"u8, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                var d = _overlayFrame.Diagnostics;
                ImGui.Text(Encoding.UTF8.GetBytes($"{d.State}\nBalls {d.Balls}  Bricks {d.Bricks}\nFrame {d.FrameMilliseconds:F2} ms\nShape frames {d.PresentedShapeFrames}\nVertices {d.ShapeVertices}  Indices {d.ShapeIndices}\nOverlay frames {OverlayEvidence.PresentedFrames}\nText cache {_overlayText!.Count}, rasterizations {_overlayText.Rasterizations}, hits {_overlayText.CacheHits}"));
                var min = ImGui.GetCursorScreenPos();
                var available = ImGui.GetContentRegionAvail();
                var imageSize = new float2(s.DebugImageSize / scale.X, s.DebugImageSize / scale.Y);
                if (_debugPixels!.HasCoverage && available.X > 0 && available.Y > 0 && Visible(min, min + imageSize, io.DisplaySize))
                {
                    ImGui.Image(GetOverlayTexture(_debugPixels!), imageSize); _recordedImage = true;
                }
                _recordedInspector = Visible(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), io.DisplaySize);
            }
            ImGui.End();
        }
        ImGui.Render(); _uiFrame = false;
        _recordedUiIndices = ImGui.GetDrawData().TotalIdxCount;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    private static bool Visible(float2 min, float2 max, float2 size) => max.X > 0 && max.Y > 0 && min.X < size.X && min.Y < size.Y && max.X > min.X && max.Y > min.Y;

    private void RecordOverlay()
    {
        if (_recordedUiIndices > 0) _uiRenderer!.RenderDrawData(ImGui.GetDrawData(), new CommandBuffer(_device!, _command));
    }

    private void CountOverlayPresentation()
    {
        if (_recordedUiIndices <= 0) return;
        var old = OverlayEvidence;
        OverlayEvidence = new(old.PresentedFrames + 1, old.TextFrames + (_recordedLabels > 0 ? 1 : 0),
            old.ImageFrames + (_recordedImage ? 1 : 0), old.InspectorFrames + (_recordedInspector ? 1 : 0),
            old.LabelQuads + _recordedLabels, old.DrawIndices + _recordedUiIndices, _extent.Width, _extent.Height, _swapchainGeneration);
    }

    private void CancelOverlayFrame()
    {
        if (_uiFrame) { _uiFrame = false; ImGui.SetCurrentContext(_uiContext); ImGui.EndFrame(); }
        _overlayFrame = null;
    }
    private void CheckOverlayThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Overlay is main-thread owned.");
    }

    // Device-idle lifecycle boundary: descriptors and pipeline die before compatible render pass is released.
    private void ReleaseOverlaySwapchain()
    {
        List<Action> releases = [CancelOverlayFrame];
        if (_hasUiContext) ImGui.SetCurrentContext(_uiContext);
        foreach (var key in _overlayTextures.Keys.ToArray()) releases.Add(() => ReleaseOverlayTexture(key));
        foreach (var staging in _overlayStaging) releases.Add(staging.Dispose);
        _overlayStaging.Clear();
        var backend = _uiRenderer;
        if (backend != null) releases.Add(backend.Dispose);
        try { ResourceLifetime.ReleaseAll(releases.ToArray()); }
        finally { _uiRenderer = null; }
    }
    private void DisposeOverlay()
    {
        List<Action> releases = [ReleaseOverlaySwapchain];
        if (_uiPlatform != null) releases.Add(_uiPlatform.Dispose);
        if (!_overlaySampler.IsNull()) releases.Add(() => _device!.DestroySampler(_overlaySampler, null));
        if (_hasUiContext) releases.Add(() => ImGui.DestroyContext(_uiContext));
        if (_overlayText != null) releases.Add(_overlayText.Dispose);
        try { ResourceLifetime.ReleaseAll(releases.ToArray()); }
        finally { _uiPlatform = null; _overlaySampler = default; _hasUiContext = false; _overlayText = null; }
    }

    private sealed record OverlayTexture(ImageEx Image, VkImageView View, ImTextureRef Reference);
    private void ReleaseOverlayTexture(OverlayPixels pixels)
    {
        var texture = _overlayTextures[pixels]; _overlayTextures.Remove(pixels);
        ResourceLifetime.ReleaseAll(() => _uiRenderer!.RemoveTexture(texture.Reference),
            () => _device!.DestroyImageView(texture.View, null), texture.Image.Dispose);
    }

    private ImTextureRef GetOverlayTexture(OverlayPixels pixels)
    {
        if (_overlayTextures.TryGetValue(pixels, out var existing)) return existing.Reference;
        ImageEx image = default; VkImageView view = default; BufferEx staging = default;
        try
        {
            image = _allocator!.CreateImage(new ImageEx.CreateInfo
            {
                Name = "", ImageType = VkImageType._2D, ImageFormat = VkFormat.R8G8B8A8UNorm,
                ImageExtent = new VkExtent3D { Width = pixels.Width, Height = pixels.Height, Depth = 1 },
                ImageMipLevels = 1, ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit,
                ImageTiling = VkImageTiling.Optimal, ImageUsage = VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.SampledBit,
                ImageSharingMode = VkSharingMode.Exclusive, AllocPreference = MemoryPreference.PreferGpu,
            });
            UploadBuffer(ref staging, pixels.Rgba.Span, VkBufferUsageFlags.TransferSrcBit);
            var range = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 };
            var barrier = new VkImageMemoryBarrier
            {
                OldLayout = VkImageLayout.Undefined, NewLayout = VkImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED, DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED,
                Image = image.VkImage, SubresourceRange = range, DstAccessMask = VkAccessFlags.TransferWriteBit,
            };
            ref var api = ref _device!.PInvoke;
            api.vkCmdPipelineBarrier.Invoke(_command, VkPipelineStageFlags.TopOfPipeBit, VkPipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &barrier);
            var copy = new VkBufferImageCopy
            {
                ImageSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
                ImageExtent = image.Extent,
            };
            api.vkCmdCopyBufferToImage.Invoke(_command, staging.VkBuffer, image.VkImage, VkImageLayout.TransferDstOptimal, 1, &copy);
            barrier.OldLayout = VkImageLayout.TransferDstOptimal; barrier.NewLayout = VkImageLayout.ShaderReadOnlyOptimal;
            barrier.SrcAccessMask = VkAccessFlags.TransferWriteBit; barrier.DstAccessMask = VkAccessFlags.ShaderReadBit;
            api.vkCmdPipelineBarrier.Invoke(_command, VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, &barrier);
            view = CreateColorView(image.VkImage, VkFormat.R8G8B8A8UNorm);
            var reference = _uiRenderer!.AddTexture(_overlaySampler, view, VkImageLayout.ShaderReadOnlyOptimal);
            _overlayTextures.Add(pixels, new(image, view, reference)); _overlayStaging.Add(staging);
            return reference;
        }
        catch
        {
            if (!view.IsNull()) _device!.DestroyImageView(view, null);
            if (!image.IsNull()) image.Dispose(); if (!staging.IsNull()) staging.Dispose();
            throw;
        }
    }
}
