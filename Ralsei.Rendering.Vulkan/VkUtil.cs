using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.Vulkan;

namespace Ralsei.Rendering.Vulkan;

public sealed class VulkanException(string message, VkResult? result = null) : Exception(message)
{
    public VkResult? Result { get; } = result;
}

internal static unsafe class VkUtil
{
    // VkBool32 is exposed as a plain int by the bindings
    public const int True = 1;
    public const int False = 0;

    public static void Check(VkResult result, string operation)
    {
        if (result != VkResult.Success)
            throw new VulkanException($"{operation} failed with {result}", result);
    }

    public static bool Has(this VkSampleCountFlagBits flags, VkSampleCountFlagBits flag)
        => (flags & flag) == flag;

    /// <summary>
    /// Reads a null-terminated UTF-8 string out of a fixed-size inline array (e.g. VkPhysicalDeviceProperties.deviceName).
    /// </summary>
    public static string ReadString<TArray>(ref TArray array, int length) where TArray : struct
    {
        var bytes = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<TArray, byte>(ref array), length);
        var end = bytes.IndexOf((byte) 0);
        return System.Text.Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }

    public static VkImageSubresourceRange ColorRange()
        => new()
        {
            aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
            baseMipLevel = 0,
            levelCount = 1,
            baseArrayLayer = 0,
            layerCount = 1
        };

    public static VkImageSubresourceLayers ColorLayers()
        => new()
        {
            aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
            mipLevel = 0,
            baseArrayLayer = 0,
            layerCount = 1
        };

    public static VkMemoryBarrier2 MemoryBarrier(
        VkPipelineStageFlagBits2 srcStage, VkAccessFlagBits2 srcAccess,
        VkPipelineStageFlagBits2 dstStage, VkAccessFlagBits2 dstAccess)
        => new()
        {
            sType = VkStructureType.StructureTypeMemoryBarrier2,
            srcStageMask = srcStage,
            srcAccessMask = srcAccess,
            dstStageMask = dstStage,
            dstAccessMask = dstAccess
        };

    public static VkImageMemoryBarrier2 ImageBarrier(
        VkImage image,
        VkImageLayout oldLayout, VkImageLayout newLayout,
        VkPipelineStageFlagBits2 srcStage, VkAccessFlagBits2 srcAccess,
        VkPipelineStageFlagBits2 dstStage, VkAccessFlagBits2 dstAccess,
        VkImageAspectFlagBits aspect = VkImageAspectFlagBits.ImageAspectColorBit)
        => new()
        {
            sType = VkStructureType.StructureTypeImageMemoryBarrier2,
            srcStageMask = srcStage,
            srcAccessMask = srcAccess,
            dstStageMask = dstStage,
            dstAccessMask = dstAccess,
            oldLayout = oldLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            dstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            image = image,
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = aspect,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1
            }
        };

    public static void PipelineBarrier(VkCommandBuffer cmd, in VkMemoryBarrier2 barrier)
    {
        fixed (VkMemoryBarrier2* pBarrier = &barrier)
        {
            var dependency = new VkDependencyInfo
            {
                sType = VkStructureType.StructureTypeDependencyInfo,
                memoryBarrierCount = 1,
                pMemoryBarriers = pBarrier
            };
            Vk.CmdPipelineBarrier2(cmd, &dependency);
        }
    }

    public static void PipelineBarrier(VkCommandBuffer cmd, in VkImageMemoryBarrier2 barrier)
    {
        fixed (VkImageMemoryBarrier2* pBarrier = &barrier)
        {
            var dependency = new VkDependencyInfo
            {
                sType = VkStructureType.StructureTypeDependencyInfo,
                imageMemoryBarrierCount = 1,
                pImageMemoryBarriers = pBarrier
            };
            Vk.CmdPipelineBarrier2(cmd, &dependency);
        }
    }

    public static void PipelineBarrier(VkCommandBuffer cmd, in VkMemoryBarrier2 memoryBarrier, ReadOnlySpan<VkImageMemoryBarrier2> imageBarriers)
    {
        fixed (VkMemoryBarrier2* pMemoryBarrier = &memoryBarrier)
        fixed (VkImageMemoryBarrier2* pImageBarriers = imageBarriers)
        {
            var dependency = new VkDependencyInfo
            {
                sType = VkStructureType.StructureTypeDependencyInfo,
                memoryBarrierCount = 1,
                pMemoryBarriers = pMemoryBarrier,
                imageMemoryBarrierCount = (uint) imageBarriers.Length,
                pImageMemoryBarriers = pImageBarriers
            };
            Vk.CmdPipelineBarrier2(cmd, &dependency);
        }
    }
}

/// <summary>
/// A native array of null-terminated UTF-8 strings, as expected by e.g. ppEnabledExtensionNames.
/// </summary>
internal sealed unsafe class Utf8StringArray : IDisposable
{
    public byte** Pointer { get; }
    public uint Count { get; }

    public Utf8StringArray(IReadOnlyList<string> strings)
    {
        Count = (uint) strings.Count;
        Pointer = (byte**) NativeMemory.AllocZeroed((nuint) Math.Max(strings.Count, 1), (nuint) sizeof(byte*));
        for (var i = 0; i < strings.Count; i++)
            Pointer[i] = (byte*) Marshal.StringToCoTaskMemUTF8(strings[i]);
    }

    public void Dispose()
    {
        for (var i = 0; i < Count; i++)
            Marshal.FreeCoTaskMem((IntPtr) Pointer[i]);
        NativeMemory.Free(Pointer);
    }
}
