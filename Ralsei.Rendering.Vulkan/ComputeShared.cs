using OpenTK.Graphics.Vulkan;
using Ralsei.Core;
using Ralsei.Mathematics;

namespace Ralsei.Rendering.Vulkan;

/// <summary>
/// What every post-processing compute shader has in common: one descriptor set layout (an output
/// image and up to two sampled inputs), a push constant range for the per-pass parameters, and
/// the samplers the passes pick from.
/// </summary>
internal sealed unsafe class ComputeShared : IDisposable
{
    // The minimum size every Vulkan implementation has to support
    public const uint PushConstantSize = 128;

    public const uint OutputBinding = 0;
    public const uint Input0Binding = 1;
    public const uint Input1Binding = 2;

    public VkDescriptorSetLayout SetLayout { get; }
    public VkPipelineLayout PipelineLayout { get; }

    public VkSampler LinearClamp { get; }
    public VkSampler NearestClamp { get; }
    public VkSampler LinearRepeat { get; }
    public VkSampler NearestRepeat { get; }

    private readonly VulkanContext context;

    public ComputeShared(VulkanContext context)
    {
        this.context = context;

        var bindings = stackalloc VkDescriptorSetLayoutBinding[]
        {
            new VkDescriptorSetLayoutBinding
            {
                binding = OutputBinding,
                descriptorType = VkDescriptorType.DescriptorTypeStorageImage,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlagBits.ShaderStageComputeBit
            },
            new VkDescriptorSetLayoutBinding
            {
                binding = Input0Binding,
                descriptorType = VkDescriptorType.DescriptorTypeCombinedImageSampler,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlagBits.ShaderStageComputeBit
            },
            new VkDescriptorSetLayoutBinding
            {
                binding = Input1Binding,
                descriptorType = VkDescriptorType.DescriptorTypeCombinedImageSampler,
                descriptorCount = 1,
                stageFlags = VkShaderStageFlagBits.ShaderStageComputeBit
            }
        };
        var layoutInfo = new VkDescriptorSetLayoutCreateInfo
        {
            sType = VkStructureType.StructureTypeDescriptorSetLayoutCreateInfo,
            bindingCount = 3,
            pBindings = bindings
        };
        VkDescriptorSetLayout setLayout;
        VkUtil.Check(Vk.CreateDescriptorSetLayout(context.Device, &layoutInfo, null, &setLayout), "vkCreateDescriptorSetLayout");
        SetLayout = setLayout;

        var pushRange = new VkPushConstantRange
        {
            stageFlags = VkShaderStageFlagBits.ShaderStageComputeBit,
            offset = 0,
            size = PushConstantSize
        };
        var pipelineLayoutInfo = new VkPipelineLayoutCreateInfo
        {
            sType = VkStructureType.StructureTypePipelineLayoutCreateInfo,
            setLayoutCount = 1,
            pSetLayouts = &setLayout,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &pushRange
        };
        VkPipelineLayout pipelineLayout;
        VkUtil.Check(Vk.CreatePipelineLayout(context.Device, &pipelineLayoutInfo, null, &pipelineLayout), "vkCreatePipelineLayout");
        PipelineLayout = pipelineLayout;

        LinearClamp = CreateSampler(VkFilter.FilterLinear, VkSamplerAddressMode.SamplerAddressModeClampToEdge);
        NearestClamp = CreateSampler(VkFilter.FilterNearest, VkSamplerAddressMode.SamplerAddressModeClampToEdge);
        LinearRepeat = CreateSampler(VkFilter.FilterLinear, VkSamplerAddressMode.SamplerAddressModeRepeat);
        NearestRepeat = CreateSampler(VkFilter.FilterNearest, VkSamplerAddressMode.SamplerAddressModeRepeat);
    }

    public void Dispose()
    {
        var device = context.Device;
        Vk.DestroySampler(device, LinearClamp, null);
        Vk.DestroySampler(device, NearestClamp, null);
        Vk.DestroySampler(device, LinearRepeat, null);
        Vk.DestroySampler(device, NearestRepeat, null);
        Vk.DestroyPipelineLayout(device, PipelineLayout, null);
        Vk.DestroyDescriptorSetLayout(device, SetLayout, null);
    }

    private VkSampler CreateSampler(VkFilter filter, VkSamplerAddressMode addressMode)
    {
        var info = new VkSamplerCreateInfo
        {
            sType = VkStructureType.StructureTypeSamplerCreateInfo,
            magFilter = filter,
            minFilter = filter,
            mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeNearest,
            addressModeU = addressMode,
            addressModeV = addressMode,
            addressModeW = addressMode,
            minLod = 0,
            maxLod = 0,
            borderColor = VkBorderColor.BorderColorFloatTransparentBlack
        };
        VkSampler sampler;
        VkUtil.Check(Vk.CreateSampler(context.Device, &info, null, &sampler), "vkCreateSampler");
        return sampler;
    }
}

/// <summary>
/// A compute pipeline for one post-processing shader.
/// </summary>
internal sealed unsafe class ComputeKernel : IDisposable
{
    private const int GroupSize = 8; // matches local_size_x/y in the shaders

    private readonly VulkanContext context;
    private readonly ComputeShared shared;
    private readonly VkPipeline pipeline;

    /// <param name="shaderName">Name of the shader relative to Shaders/, e.g. "PostProcessing/Glitch".</param>
    public ComputeKernel(VulkanContext context, ComputeShared shared, ResourceLoader loader, string shaderName)
    {
        this.context = context;
        this.shared = shared;

        var module = ShaderUtil.CreateModule(context, loader, $"{shaderName}.comp");
        try
        {
            fixed (byte* entryPoint = "main"u8)
            {
                var createInfo = new VkComputePipelineCreateInfo
                {
                    sType = VkStructureType.StructureTypeComputePipelineCreateInfo,
                    stage = new VkPipelineShaderStageCreateInfo
                    {
                        sType = VkStructureType.StructureTypePipelineShaderStageCreateInfo,
                        stage = VkShaderStageFlagBits.ShaderStageComputeBit,
                        module = module,
                        pName = entryPoint
                    },
                    layout = shared.PipelineLayout
                };
                VkPipeline createdPipeline;
                VkUtil.Check(
                    Vk.CreateComputePipelines(context.Device, default, 1, &createInfo, null, &createdPipeline),
                    $"vkCreateComputePipelines ({shaderName})");
                pipeline = createdPipeline;
            }
        }
        finally
        {
            Vk.DestroyShaderModule(context.Device, module, null);
        }
    }

    public void Dispose()
    {
        Vk.DestroyPipeline(context.Device, pipeline, null);
    }

    /// <summary>
    /// Dispatches enough groups to cover <paramref name="size"/> pixels, then waits for the results to
    /// be visible to whatever compute pass comes next.
    /// </summary>
    public void Run(
        FrameResources frame, Vector2i size, GpuImage output,
        GpuImage? input0 = null, VkSampler sampler0 = default,
        GpuImage? input1 = null, VkSampler sampler1 = default)
    {
        RunCore(frame, size, null, 0, output, input0, sampler0, input1, sampler1);
    }

    /// <inheritdoc cref="Run"/>
    public void Run<TPush>(
        FrameResources frame, Vector2i size, in TPush push, GpuImage output,
        GpuImage? input0 = null, VkSampler sampler0 = default,
        GpuImage? input1 = null, VkSampler sampler1 = default) where TPush : unmanaged
    {
        fixed (TPush* pPush = &push)
            RunCore(frame, size, pPush, (uint) sizeof(TPush), output, input0, sampler0, input1, sampler1);
    }

    private void RunCore(
        FrameResources frame, Vector2i size, void* push, uint pushSize, GpuImage output,
        GpuImage? input0, VkSampler sampler0, GpuImage? input1, VkSampler sampler1)
    {
        if (pushSize > ComputeShared.PushConstantSize)
            throw new InvalidOperationException($"Push constants of {pushSize} bytes do not fit in the {ComputeShared.PushConstantSize} bytes every device supports");

        var cmd = frame.Cmd;
        var set = frame.AllocateDescriptorSet(shared.SetLayout);

        var imageInfos = stackalloc VkDescriptorImageInfo[]
        {
            new VkDescriptorImageInfo { imageView = output.View, imageLayout = VkImageLayout.ImageLayoutGeneral },
            new VkDescriptorImageInfo { sampler = sampler0, imageView = input0?.View ?? default, imageLayout = VkImageLayout.ImageLayoutGeneral },
            new VkDescriptorImageInfo { sampler = sampler1, imageView = input1?.View ?? default, imageLayout = VkImageLayout.ImageLayoutGeneral }
        };

        var writes = stackalloc VkWriteDescriptorSet[3];
        var writeCount = 0u;
        writes[writeCount++] = new VkWriteDescriptorSet
        {
            sType = VkStructureType.StructureTypeWriteDescriptorSet,
            dstSet = set,
            dstBinding = ComputeShared.OutputBinding,
            descriptorCount = 1,
            descriptorType = VkDescriptorType.DescriptorTypeStorageImage,
            pImageInfo = &imageInfos[0]
        };
        if (input0 != null)
        {
            writes[writeCount++] = new VkWriteDescriptorSet
            {
                sType = VkStructureType.StructureTypeWriteDescriptorSet,
                dstSet = set,
                dstBinding = ComputeShared.Input0Binding,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.DescriptorTypeCombinedImageSampler,
                pImageInfo = &imageInfos[1]
            };
        }
        if (input1 != null)
        {
            writes[writeCount++] = new VkWriteDescriptorSet
            {
                sType = VkStructureType.StructureTypeWriteDescriptorSet,
                dstSet = set,
                dstBinding = ComputeShared.Input1Binding,
                descriptorCount = 1,
                descriptorType = VkDescriptorType.DescriptorTypeCombinedImageSampler,
                pImageInfo = &imageInfos[2]
            };
        }
        Vk.UpdateDescriptorSets(context.Device, writeCount, writes, 0, null);

        Vk.CmdBindPipeline(cmd, VkPipelineBindPoint.PipelineBindPointCompute, pipeline);
        Vk.CmdBindDescriptorSets(cmd, VkPipelineBindPoint.PipelineBindPointCompute, shared.PipelineLayout, 0, 1, &set, 0, null);

        // The layout declares the whole range, so push all of it
        var padded = stackalloc byte[(int) ComputeShared.PushConstantSize];
        new Span<byte>(padded, (int) ComputeShared.PushConstantSize).Clear();
        if (pushSize > 0)
            Buffer.MemoryCopy(push, padded, ComputeShared.PushConstantSize, pushSize);
        Vk.CmdPushConstants(cmd, shared.PipelineLayout, VkShaderStageFlagBits.ShaderStageComputeBit, 0, ComputeShared.PushConstantSize, padded);

        Vk.CmdDispatch(cmd, (uint) MathUtil.DivideCeil(size.X, GroupSize), (uint) MathUtil.DivideCeil(size.Y, GroupSize), 1);

        frame.ComputeBarrier();
    }
}
