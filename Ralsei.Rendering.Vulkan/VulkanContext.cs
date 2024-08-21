using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics;
using OpenTK.Graphics.Vulkan;
using Ralsei.Platform.Vulkan;

namespace Ralsei.Rendering.Vulkan;

internal enum MemoryUsage
{
    /// <summary>Fast GPU-only memory.</summary>
    DeviceLocal,

    /// <summary>Host-visible memory the CPU writes once and the GPU copies from (staging buffers).</summary>
    Staging,

    /// <summary>
    /// Host-visible memory the CPU writes every frame and the GPU reads directly (per-frame draw data).
    /// On a discrete GPU this is device local memory the CPU can write to (resizable BAR), so that the GPU does not
    /// have to read across the bus. An integrated GPU shares system memory with the CPU anyway, and its device local,
    /// host-visible memory is a carve-out that is very slow to write to from the CPU, so plain host memory is used there.
    /// </summary>
    Dynamic,

    /// <summary>Host-visible, preferably cached memory the CPU reads back from.</summary>
    Readback
}

/// <summary>
/// Owns the Vulkan instance, the logical device and the single queue everything is submitted to,
/// along with the small utilities (memory allocation, one-shot command buffers) the rest of the renderer needs.
/// </summary>
internal sealed unsafe class VulkanContext : IDisposable
{
    private const string ValidationLayerName = "VK_LAYER_KHRONOS_validation";
    private const string ValidationEnvVar = "RALSEI_VULKAN_VALIDATION";
    private const string DeviceEnvVar = "RALSEI_VULKAN_DEVICE";

    public VkInstance Instance { get; private set; }
    public VkSurfaceKHR Surface { get; private set; }
    public VkPhysicalDevice PhysicalDevice { get; private set; }
    public VkDevice Device { get; private set; }
    public VkQueue Queue { get; private set; }
    public uint QueueFamily { get; private set; }

    public bool IsHeadless { get; }
    public string DeviceName { get; private set; } = string.Empty;
    public VkPhysicalDeviceLimits Limits { get; private set; }
    public bool SupportsMultiDrawIndirect { get; private set; }

    /// <summary>
    /// Whether every texture can be in one descriptor array that any draw indexes (descriptor indexing, which is what bindless
    /// textures are in Vulkan): see <see cref="BindlessTextureCapacity"/>.
    /// </summary>
    public bool SupportsBindlessTextures { get; private set; }

    /// <summary>How many textures the descriptor array can hold, if <see cref="SupportsBindlessTextures"/>.</summary>
    public int BindlessTextureCapacity { get; private set; }
    public bool IsDiscreteGpu { get; private set; }
    public VkSampleCountFlagBits MsaaSamples { get; private set; }

    private VkPhysicalDeviceMemoryProperties memoryProperties;
    private VkDebugUtilsMessengerEXT debugMessenger;
    private GCHandle loggerHandle;

    private VkCommandPool immediateCommandPool;
    private VkFence immediateFence;

    private readonly ILogger logger;

    public VulkanContext(IVulkanSurface surface, int requestedSamples, ILogger logger)
    {
        this.logger = logger;
        IsHeadless = surface.IsHeadless;

        VulkanLoader.Init();

        try
        {
            CreateInstance(surface);

            if (!IsHeadless)
            {
                Surface = new VkSurfaceKHR { Handle = surface.CreateSurface(Instance.Handle) };
                if (Surface.Handle == 0)
                    throw new VulkanException("The surface failed to create a VkSurfaceKHR");
            }

            SelectPhysicalDevice(requestedSamples);
            CreateDevice();
            CreateImmediateResources();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Device.Handle != 0)
        {
            Vk.DeviceWaitIdle(Device);

            if (immediateFence.Handle != 0)
                Vk.DestroyFence(Device, immediateFence, null);
            if (immediateCommandPool.Handle != 0)
                Vk.DestroyCommandPool(Device, immediateCommandPool, null);

            Vk.DestroyDevice(Device, null);
            Device = default;
        }

        if (Instance.Handle != 0)
        {
            if (Surface.Handle != 0)
            {
                Vk.DestroySurfaceKHR(Instance, Surface, null);
                Surface = default;
            }

            if (debugMessenger.Handle != 0)
            {
                Vk.DestroyDebugUtilsMessengerEXT(Instance, debugMessenger, null);
                debugMessenger = default;
            }

            Vk.DestroyInstance(Instance, null);
            Instance = default;
        }

        if (loggerHandle.IsAllocated)
            loggerHandle.Free();
    }

    #region Instance

    private void CreateInstance(IVulkanSurface surface)
    {
        var extensions = new List<string>(surface.GetRequiredInstanceExtensions());
        var layers = new List<string>();

        var validationRequested = Environment.GetEnvironmentVariable(ValidationEnvVar) is "1" or "true";
        var validationEnabled = false;
        if (validationRequested)
        {
            if (HasInstanceLayer(ValidationLayerName) && HasInstanceExtension(Vk.ExtDebugUtilsExtensionName))
            {
                layers.Add(ValidationLayerName);
                extensions.Add(Vk.ExtDebugUtilsExtensionName);
                validationEnabled = true;
            }
            else
            {
                logger.LogWarning(
                    "Vulkan validation was requested through {EnvVar}, but {Layer} is not installed",
                    ValidationEnvVar, ValidationLayerName);
            }
        }

        using var extensionNames = new Utf8StringArray(extensions);
        using var layerNames = new Utf8StringArray(layers);

        var appName = (byte*) Marshal.StringToCoTaskMemUTF8("RALSEI");
        try
        {
            var appInfo = new VkApplicationInfo
            {
                sType = VkStructureType.StructureTypeApplicationInfo,
                pApplicationName = appName,
                applicationVersion = 1,
                pEngineName = appName,
                engineVersion = 1,
                apiVersion = Vk.VK_API_VERSION_1_3
            };

            var createInfo = new VkInstanceCreateInfo
            {
                sType = VkStructureType.StructureTypeInstanceCreateInfo,
                pApplicationInfo = &appInfo,
                enabledLayerCount = layerNames.Count,
                ppEnabledLayerNames = layerNames.Pointer,
                enabledExtensionCount = extensionNames.Count,
                ppEnabledExtensionNames = extensionNames.Pointer
            };

            VkInstance instance;
            VkUtil.Check(Vk.CreateInstance(&createInfo, null, &instance), "vkCreateInstance");
            Instance = instance;
        }
        finally
        {
            Marshal.FreeCoTaskMem((IntPtr) appName);
        }

        VKLoader.SetInstance(Instance);

        if (validationEnabled)
        {
            logger.LogInformation("Vulkan validation layers enabled");
            CreateDebugMessenger();
        }
    }

    private static bool HasInstanceLayer(string name)
    {
        uint count;
        Vk.EnumerateInstanceLayerProperties(&count, null);
        var layers = new VkLayerProperties[count];
        fixed (VkLayerProperties* pLayers = layers)
            Vk.EnumerateInstanceLayerProperties(&count, pLayers);

        for (var i = 0; i < count; i++)
            if (VkUtil.ReadString(ref layers[i].layerName, 256) == name)
                return true;
        return false;
    }

    private static bool HasInstanceExtension(string name)
    {
        uint count;
        Vk.EnumerateInstanceExtensionProperties(null, &count, null);
        var extensions = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* pExtensions = extensions)
            Vk.EnumerateInstanceExtensionProperties(null, &count, pExtensions);

        for (var i = 0; i < count; i++)
            if (VkUtil.ReadString(ref extensions[i].extensionName, 256) == name)
                return true;
        return false;
    }

    private void CreateDebugMessenger()
    {
        loggerHandle = GCHandle.Alloc(logger);

        var createInfo = new VkDebugUtilsMessengerCreateInfoEXT
        {
            sType = VkStructureType.StructureTypeDebugUtilsMessengerCreateInfoExt,
            messageSeverity =
                VkDebugUtilsMessageSeverityFlagBitsEXT.DebugUtilsMessageSeverityWarningBitExt |
                VkDebugUtilsMessageSeverityFlagBitsEXT.DebugUtilsMessageSeverityErrorBitExt,
            messageType =
                VkDebugUtilsMessageTypeFlagBitsEXT.DebugUtilsMessageTypeGeneralBitExt |
                VkDebugUtilsMessageTypeFlagBitsEXT.DebugUtilsMessageTypeValidationBitExt |
                VkDebugUtilsMessageTypeFlagBitsEXT.DebugUtilsMessageTypePerformanceBitExt,
            pfnUserCallback = &DebugCallback,
            pUserData = (void*) GCHandle.ToIntPtr(loggerHandle)
        };

        VkDebugUtilsMessengerEXT messenger;
        VkUtil.Check(Vk.CreateDebugUtilsMessengerEXT(Instance, &createInfo, null, &messenger), "vkCreateDebugUtilsMessengerEXT");
        debugMessenger = messenger;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DebugCallback(
        VkDebugUtilsMessageSeverityFlagBitsEXT severity,
        VkDebugUtilsMessageTypeFlagBitsEXT type,
        VkDebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        if (GCHandle.FromIntPtr((IntPtr) userData).Target is not ILogger logger)
            return 0;

        var message = Marshal.PtrToStringUTF8((IntPtr) data->pMessage);
        var level = (severity & VkDebugUtilsMessageSeverityFlagBitsEXT.DebugUtilsMessageSeverityErrorBitExt) != 0
            ? LogLevel.Error
            : LogLevel.Warning;
        logger.Log(level, "[Vulkan {Type}] {Message}", type, message);
        return 0; // VK_FALSE: do not abort the call that triggered the message
    }

    #endregion

    #region Physical device

    private void SelectPhysicalDevice(int requestedSamples)
    {
        uint count;
        VkUtil.Check(Vk.EnumeratePhysicalDevices(Instance, &count, null), "vkEnumeratePhysicalDevices");
        if (count == 0)
            throw new VulkanException("No Vulkan-capable devices were found");

        var devices = new VkPhysicalDevice[count];
        fixed (VkPhysicalDevice* pDevices = devices)
            VkUtil.Check(Vk.EnumeratePhysicalDevices(Instance, &count, pDevices), "vkEnumeratePhysicalDevices");

        // Lets the device be picked by hand, e.g. on laptops with both an integrated and a discrete GPU
        var deviceOverride = Environment.GetEnvironmentVariable(DeviceEnvVar);

        var bestScore = -1;
        for (var i = 0; i < devices.Length; i++)
        {
            var device = devices[i];

            VkPhysicalDeviceProperties properties;
            Vk.GetPhysicalDeviceProperties(device, &properties);
            var name = VkUtil.ReadString(ref properties.deviceName, 256);

            logger.LogInformation("Found Vulkan device {Index}: {Device}", i, name);

            if (!string.IsNullOrEmpty(deviceOverride) &&
                deviceOverride != i.ToString() &&
                !name.Contains(deviceOverride, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryGetDeviceSuitability(device, properties, out var queueFamily, out var rejection))
            {
                logger.LogInformation("Skipping Vulkan device '{Device}': {Reason}", name, rejection);
                continue;
            }

            var score = properties.deviceType switch
            {
                VkPhysicalDeviceType.PhysicalDeviceTypeDiscreteGpu => 1000,
                VkPhysicalDeviceType.PhysicalDeviceTypeIntegratedGpu => 100,
                VkPhysicalDeviceType.PhysicalDeviceTypeVirtualGpu => 50,
                _ => 10
            };

            if (score <= bestScore)
                continue;

            bestScore = score;
            PhysicalDevice = device;
            QueueFamily = queueFamily;
            DeviceName = name;
            Limits = properties.limits;
            IsDiscreteGpu = properties.deviceType == VkPhysicalDeviceType.PhysicalDeviceTypeDiscreteGpu;
        }

        if (PhysicalDevice.Handle == 0)
        {
            if (!string.IsNullOrEmpty(deviceOverride))
                throw new VulkanException($"No suitable Vulkan device matches {DeviceEnvVar}='{deviceOverride}'");

            throw new VulkanException(
                "No Vulkan device supports what the renderer needs (Vulkan 1.3 with dynamic rendering and synchronization2, and presenting to the window), see the log for why each device was skipped");
        }

        VkPhysicalDeviceMemoryProperties memory;
        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, &memory);
        memoryProperties = memory;

        // Highest requested sample count that both the color and depth attachments support
        var supported = Limits.framebufferColorSampleCounts & Limits.framebufferDepthSampleCounts;
        MsaaSamples = VkSampleCountFlagBits.SampleCount1Bit;
        foreach (var candidate in (ReadOnlySpan<VkSampleCountFlagBits>)
                 [VkSampleCountFlagBits.SampleCount2Bit, VkSampleCountFlagBits.SampleCount4Bit, VkSampleCountFlagBits.SampleCount8Bit])
        {
            if ((int) candidate <= requestedSamples && supported.Has(candidate))
                MsaaSamples = candidate;
        }

        logger.LogInformation("Vulkan device: {Device}, {Samples}x MSAA", DeviceName, (int) MsaaSamples);
    }

    private bool TryGetDeviceSuitability(
        VkPhysicalDevice device, in VkPhysicalDeviceProperties properties,
        out uint queueFamily, out string rejection)
    {
        queueFamily = 0;
        rejection = string.Empty;

        if (properties.apiVersion < Vk.VK_API_VERSION_1_3)
        {
            rejection = "does not support Vulkan 1.3";
            return false;
        }

        // Features
        var features11 = new VkPhysicalDeviceVulkan11Features { sType = VkStructureType.StructureTypePhysicalDeviceVulkan11Features };
        var features13 = new VkPhysicalDeviceVulkan13Features
        {
            sType = VkStructureType.StructureTypePhysicalDeviceVulkan13Features,
            pNext = &features11
        };
        var features2 = new VkPhysicalDeviceFeatures2
        {
            sType = VkStructureType.StructureTypePhysicalDeviceFeatures2,
            pNext = &features13
        };
        Vk.GetPhysicalDeviceFeatures2(device, &features2);

        if (features13.dynamicRendering == 0 || features13.synchronization2 == 0)
        {
            rejection = "does not support dynamic rendering and synchronization2";
            return false;
        }

        if (features11.shaderDrawParameters == 0)
        {
            rejection = "does not support shaderDrawParameters";
            return false;
        }

        if (features2.features.shaderStorageImageExtendedFormats == 0)
        {
            rejection = "does not support shaderStorageImageExtendedFormats";
            return false;
        }

        // HDR render targets and post-processing images
        VkFormatProperties formatProperties;
        Vk.GetPhysicalDeviceFormatProperties(device, VkFormat.FormatR16g16b16a16Sfloat, &formatProperties);
        const VkFormatFeatureFlagBits requiredFeatures =
            VkFormatFeatureFlagBits.FormatFeatureSampledImageBit |
            VkFormatFeatureFlagBits.FormatFeatureSampledImageFilterLinearBit |
            VkFormatFeatureFlagBits.FormatFeatureStorageImageBit |
            VkFormatFeatureFlagBits.FormatFeatureColorAttachmentBit |
            VkFormatFeatureFlagBits.FormatFeatureBlitSrcBit |
            VkFormatFeatureFlagBits.FormatFeatureTransferSrcBit;
        if ((formatProperties.optimalTilingFeatures & requiredFeatures) != requiredFeatures)
        {
            rejection = "does not support RGBA16F render targets";
            return false;
        }

        // Swapchain
        if (!IsHeadless && !HasDeviceExtension(device, Vk.KhrSwapchainExtensionName))
        {
            rejection = $"does not support {Vk.KhrSwapchainExtensionName}";
            return false;
        }

        if (!IsHeadless && !CanPresentToSurface(device, out var surfaceProblem))
        {
            rejection = $"can not present to the window ({surfaceProblem})";
            return false;
        }

        // Queue family with graphics, compute and (if there is a window) presentation
        uint familyCount;
        Vk.GetPhysicalDeviceQueueFamilyProperties(device, &familyCount, null);
        var families = new VkQueueFamilyProperties[familyCount];
        fixed (VkQueueFamilyProperties* pFamilies = families)
            Vk.GetPhysicalDeviceQueueFamilyProperties(device, &familyCount, pFamilies);

        const VkQueueFlagBits requiredQueueFlags = VkQueueFlagBits.QueueGraphicsBit | VkQueueFlagBits.QueueComputeBit;
        for (uint i = 0; i < familyCount; i++)
        {
            if ((families[i].queueFlags & requiredQueueFlags) != requiredQueueFlags)
                continue;

            if (!IsHeadless)
            {
                int presentSupported;
                var supportResult = Vk.GetPhysicalDeviceSurfaceSupportKHR(device, i, Surface, &presentSupported);
                if (supportResult != VkResult.Success || presentSupported == 0)
                    continue;
            }

            queueFamily = i;
            return true;
        }

        rejection = "has no queue family that supports graphics, compute and presentation";
        return false;
    }

    /// <summary>
    /// Asks the driver the questions a swapchain depends on. Some drivers claim to support presenting to a window and then
    /// fail these (e.g. an integrated GPU that does not drive the display the window is on), which is better to find out
    /// here, where another device can still be picked, than when the swapchain is created.
    /// </summary>
    private bool CanPresentToSurface(VkPhysicalDevice device, out string problem)
    {
        VkSurfaceCapabilitiesKHR capabilities;
        var result = Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(device, Surface, &capabilities);
        if (result != VkResult.Success)
        {
            problem = $"vkGetPhysicalDeviceSurfaceCapabilitiesKHR failed with {result}";
            return false;
        }

        uint formatCount;
        result = Vk.GetPhysicalDeviceSurfaceFormatsKHR(device, Surface, &formatCount, null);
        if (result != VkResult.Success || formatCount == 0)
        {
            problem = $"vkGetPhysicalDeviceSurfaceFormatsKHR gave {result} with {formatCount} formats";
            return false;
        }

        uint presentModeCount;
        result = Vk.GetPhysicalDeviceSurfacePresentModesKHR(device, Surface, &presentModeCount, null);
        if (result != VkResult.Success || presentModeCount == 0)
        {
            problem = $"vkGetPhysicalDeviceSurfacePresentModesKHR gave {result} with {presentModeCount} present modes";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool HasDeviceExtension(VkPhysicalDevice device, string name)
    {
        uint count;
        Vk.EnumerateDeviceExtensionProperties(device, null, &count, null);
        var extensions = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* pExtensions = extensions)
            Vk.EnumerateDeviceExtensionProperties(device, null, &count, pExtensions);

        for (var i = 0; i < count; i++)
            if (VkUtil.ReadString(ref extensions[i].extensionName, 256) == name)
                return true;
        return false;
    }

    #endregion

    #region Logical device

    // A texture is in the array by its index, which is never very large, and a device that cannot have this many is not worth the trouble
    private const int MaxBindlessTextureCapacity = 8192;
    private const int MinBindlessTextureCapacity = 256;

    private void CreateDevice()
    {
        // Query what the device supports so that optional features can be enabled
        var supported12 = new VkPhysicalDeviceVulkan12Features { sType = VkStructureType.StructureTypePhysicalDeviceVulkan12Features };
        VkPhysicalDeviceFeatures2 supported = new() { sType = VkStructureType.StructureTypePhysicalDeviceFeatures2, pNext = &supported12 };
        Vk.GetPhysicalDeviceFeatures2(PhysicalDevice, &supported);
        SupportsMultiDrawIndirect = supported.features.multiDrawIndirect != 0;

        // Bindless textures need an array of them that is indexed by anything, with slots that nothing is written to, that can be
        // written to while frames that do not use the slot are being drawn
        var properties12 = new VkPhysicalDeviceVulkan12Properties { sType = VkStructureType.StructureTypePhysicalDeviceVulkan12Properties };
        var properties2 = new VkPhysicalDeviceProperties2 { sType = VkStructureType.StructureTypePhysicalDeviceProperties2, pNext = &properties12 };
        Vk.GetPhysicalDeviceProperties2(PhysicalDevice, &properties2);
        var capacity = Math.Min(
            Math.Min(properties12.maxPerStageDescriptorUpdateAfterBindSamplers, properties12.maxPerStageDescriptorUpdateAfterBindSampledImages),
            Math.Min(properties12.maxDescriptorSetUpdateAfterBindSamplers, properties12.maxDescriptorSetUpdateAfterBindSampledImages));
        BindlessTextureCapacity = (int) Math.Min(capacity, MaxBindlessTextureCapacity);
        SupportsBindlessTextures =
            supported12.runtimeDescriptorArray != 0 &&
            supported12.descriptorBindingPartiallyBound != 0 &&
            supported12.descriptorBindingSampledImageUpdateAfterBind != 0 &&
            supported12.shaderSampledImageArrayNonUniformIndexing != 0 &&
            BindlessTextureCapacity >= MinBindlessTextureCapacity;

        var features11 = new VkPhysicalDeviceVulkan11Features
        {
            sType = VkStructureType.StructureTypePhysicalDeviceVulkan11Features,
            shaderDrawParameters = VkUtil.True
        };
        var features12 = new VkPhysicalDeviceVulkan12Features
        {
            sType = VkStructureType.StructureTypePhysicalDeviceVulkan12Features,
            runtimeDescriptorArray = SupportsBindlessTextures ? VkUtil.True : VkUtil.False,
            descriptorBindingPartiallyBound = SupportsBindlessTextures ? VkUtil.True : VkUtil.False,
            descriptorBindingSampledImageUpdateAfterBind = SupportsBindlessTextures ? VkUtil.True : VkUtil.False,
            shaderSampledImageArrayNonUniformIndexing = SupportsBindlessTextures ? VkUtil.True : VkUtil.False
        };
        features11.pNext = &features12;
        var features13 = new VkPhysicalDeviceVulkan13Features
        {
            sType = VkStructureType.StructureTypePhysicalDeviceVulkan13Features,
            pNext = &features11,
            dynamicRendering = VkUtil.True,
            synchronization2 = VkUtil.True
        };
        var features2 = new VkPhysicalDeviceFeatures2
        {
            sType = VkStructureType.StructureTypePhysicalDeviceFeatures2,
            pNext = &features13
        };
        features2.features.shaderStorageImageExtendedFormats = VkUtil.True;
        features2.features.multiDrawIndirect = SupportsMultiDrawIndirect ? VkUtil.True : VkUtil.False;

        var extensions = new List<string>();
        if (!IsHeadless)
            extensions.Add(Vk.KhrSwapchainExtensionName);
        using var extensionNames = new Utf8StringArray(extensions);

        var priority = 1.0f;
        var queueCreateInfo = new VkDeviceQueueCreateInfo
        {
            sType = VkStructureType.StructureTypeDeviceQueueCreateInfo,
            queueFamilyIndex = QueueFamily,
            queueCount = 1,
            pQueuePriorities = &priority
        };

        var createInfo = new VkDeviceCreateInfo
        {
            sType = VkStructureType.StructureTypeDeviceCreateInfo,
            pNext = &features2,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueCreateInfo,
            enabledExtensionCount = extensionNames.Count,
            ppEnabledExtensionNames = extensionNames.Pointer
        };

        VkDevice device;
        VkUtil.Check(Vk.CreateDevice(PhysicalDevice, &createInfo, null, &device), "vkCreateDevice");
        Device = device;

        VkQueue queue;
        Vk.GetDeviceQueue(Device, QueueFamily, 0, &queue);
        Queue = queue;
    }

    private void CreateImmediateResources()
    {
        var poolInfo = new VkCommandPoolCreateInfo
        {
            sType = VkStructureType.StructureTypeCommandPoolCreateInfo,
            flags = VkCommandPoolCreateFlagBits.CommandPoolCreateTransientBit |
                    VkCommandPoolCreateFlagBits.CommandPoolCreateResetCommandBufferBit,
            queueFamilyIndex = QueueFamily
        };
        VkCommandPool pool;
        VkUtil.Check(Vk.CreateCommandPool(Device, &poolInfo, null, &pool), "vkCreateCommandPool");
        immediateCommandPool = pool;

        var fenceInfo = new VkFenceCreateInfo { sType = VkStructureType.StructureTypeFenceCreateInfo };
        VkFence fence;
        VkUtil.Check(Vk.CreateFence(Device, &fenceInfo, null, &fence), "vkCreateFence");
        immediateFence = fence;
    }

    #endregion

    #region Utilities

    public delegate void RecordCommands(VkCommandBuffer cmd);

    /// <summary>
    /// Records and submits a one-off command buffer, and blocks until the GPU has executed it.
    /// Meant for rare operations such as initial image layout transitions.
    /// </summary>
    public void SubmitImmediate(RecordCommands record)
    {
        var allocInfo = new VkCommandBufferAllocateInfo
        {
            sType = VkStructureType.StructureTypeCommandBufferAllocateInfo,
            commandPool = immediateCommandPool,
            level = VkCommandBufferLevel.CommandBufferLevelPrimary,
            commandBufferCount = 1
        };
        VkCommandBuffer cmd;
        VkUtil.Check(Vk.AllocateCommandBuffers(Device, &allocInfo, &cmd), "vkAllocateCommandBuffers");

        try
        {
            var beginInfo = new VkCommandBufferBeginInfo
            {
                sType = VkStructureType.StructureTypeCommandBufferBeginInfo,
                flags = VkCommandBufferUsageFlagBits.CommandBufferUsageOneTimeSubmitBit
            };
            VkUtil.Check(Vk.BeginCommandBuffer(cmd, &beginInfo), "vkBeginCommandBuffer");
            record(cmd);
            VkUtil.Check(Vk.EndCommandBuffer(cmd), "vkEndCommandBuffer");

            var cmdInfo = new VkCommandBufferSubmitInfo
            {
                sType = VkStructureType.StructureTypeCommandBufferSubmitInfo,
                commandBuffer = cmd
            };
            var submitInfo = new VkSubmitInfo2
            {
                sType = VkStructureType.StructureTypeSubmitInfo2,
                commandBufferInfoCount = 1,
                pCommandBufferInfos = &cmdInfo
            };
            var fence = immediateFence;
            VkUtil.Check(Vk.QueueSubmit2(Queue, 1, &submitInfo, fence), "vkQueueSubmit2");
            VkUtil.Check(Vk.WaitForFences(Device, 1, &fence, VkUtil.True, ulong.MaxValue), "vkWaitForFences");
            VkUtil.Check(Vk.ResetFences(Device, 1, &fence), "vkResetFences");
        }
        finally
        {
            Vk.FreeCommandBuffers(Device, immediateCommandPool, 1, &cmd);
        }
    }

    public void WaitIdle()
    {
        VkUtil.Check(Vk.DeviceWaitIdle(Device), "vkDeviceWaitIdle");
    }

    /// <summary>
    /// Allocates device memory for the given requirements, trying the memory types that best fit
    /// <paramref name="usage"/> first.
    /// </summary>
    public VkDeviceMemory AllocateMemory(in VkMemoryRequirements requirements, MemoryUsage usage)
    {
        const VkMemoryPropertyFlagBits deviceLocal = VkMemoryPropertyFlagBits.MemoryPropertyDeviceLocalBit;
        const VkMemoryPropertyFlagBits hostVisible =
            VkMemoryPropertyFlagBits.MemoryPropertyHostVisibleBit | VkMemoryPropertyFlagBits.MemoryPropertyHostCoherentBit;
        const VkMemoryPropertyFlagBits hostCached = VkMemoryPropertyFlagBits.MemoryPropertyHostCachedBit;

        // Each entry is a set of required flags, in order of preference
        ReadOnlySpan<VkMemoryPropertyFlagBits> preferences = usage switch
        {
            MemoryUsage.Staging => [hostVisible, hostVisible | deviceLocal],
            MemoryUsage.Dynamic => IsDiscreteGpu ? [hostVisible | deviceLocal, hostVisible] : [hostVisible, hostVisible | deviceLocal],
            MemoryUsage.Readback => [hostVisible | hostCached, hostVisible],
            _ => [deviceLocal]
        };

        foreach (var required in preferences)
        {
            for (uint i = 0; i < memoryProperties.memoryTypeCount; i++)
            {
                if ((requirements.memoryTypeBits & (1u << (int) i)) == 0)
                    continue;
                if ((memoryProperties.memoryTypes[(int) i].propertyFlags & required) != required)
                    continue;

                var allocInfo = new VkMemoryAllocateInfo
                {
                    sType = VkStructureType.StructureTypeMemoryAllocateInfo,
                    allocationSize = requirements.size,
                    memoryTypeIndex = i
                };
                VkDeviceMemory memory;
                var result = Vk.AllocateMemory(Device, &allocInfo, null, &memory);
                if (result == VkResult.Success)
                    return memory;

                if (result is not (VkResult.ErrorOutOfDeviceMemory or VkResult.ErrorOutOfHostMemory))
                    VkUtil.Check(result, "vkAllocateMemory");
                // Heap is full, try the next candidate
            }
        }

        throw new VulkanException($"Could not allocate {requirements.size} bytes of {usage} memory");
    }

    #endregion
}
