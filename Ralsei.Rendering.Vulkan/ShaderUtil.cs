using OpenTK.Graphics.Vulkan;
using Ralsei.Core;

namespace Ralsei.Rendering.Vulkan;

internal static unsafe class ShaderUtil
{
    /// <summary>
    /// Creates a shader module from the embedded SPIR-V at Shaders/{name}.spv.
    /// </summary>
    public static VkShaderModule CreateModule(VulkanContext context, ResourceLoader loader, string name)
    {
        var code = loader.ReadResourceBytes($"Shaders/{name}.spv");
        if (code is null)
            throw new InvalidOperationException($"Could not load compiled shader for '{name}'");
        if (code.Length % sizeof(uint) != 0)
            throw new InvalidOperationException($"Compiled shader for '{name}' is not valid SPIR-V");

        fixed (byte* pCode = code)
        {
            var createInfo = new VkShaderModuleCreateInfo
            {
                sType = VkStructureType.StructureTypeShaderModuleCreateInfo,
                codeSize = (nuint) code.Length,
                pCode = (uint*) pCode
            };
            VkShaderModule module;
            VkUtil.Check(Vk.CreateShaderModule(context.Device, &createInfo, null, &module), $"vkCreateShaderModule ({name})");
            return module;
        }
    }
}
