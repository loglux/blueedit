using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// The video cards Vulkan sees, asked the way llama.cpp's Vulkan build asks: the loader (vulkan-1.dll,
/// libvulkan.so.1) is given an instance and lists its physical devices. The loader alone proves nothing: Windows
/// Sandbox has vulkan-1.dll from its host and no device under it (measured). A device of type CPU (llvmpipe,
/// SwiftShader) is software drawing, not a video card. Run in a process of its own (<c>be --vulkan-devices</c>), so a
/// driver that misbehaves cannot take the editor down, and its libraries are not left loaded in it.
/// </summary>
static unsafe class VideoCards
{
    /// <summary>A device as Vulkan describes it: its name, type (0 other, 1 integrated, 2 discrete, 3 virtual, 4 CPU)
    /// and memory of its own in MB (its largest device-local heap; for an integrated card, a share of the computer's).</summary>
    public readonly record struct Device(string Name, int Type, int MemoryMb = 0)
    {
        public bool IsGpu => Type is 1 or 2 or 3;
        /// <summary>A card with memory of its own, which a model can be put in (an integrated one borrows the
        /// computer's and is about as slow as the processor for a model).</summary>
        public bool IsDedicated => Type is 2 or 3;
        public override string ToString() => $"{Type} {MemoryMb} {Name}";
    }

    [StructLayout(LayoutKind.Sequential)]
    struct InstanceCreateInfo
    {
        public int SType;                                   // VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1
        public IntPtr Next;
        public int Flags;
        public IntPtr ApplicationInfo;
        public int LayerCount;
        public IntPtr LayerNames;
        public int ExtensionCount;
        public IntPtr ExtensionNames;
    }

    /// <summary>The devices; none without a Vulkan loader or when it gives no instance (no driver at all).</summary>
    public static List<Device> Vulkan()
    {
        if (!NativeLibrary.TryLoad(OperatingSystem.IsWindows() ? "vulkan-1.dll" : "libvulkan.so.1", out var lib)) return [];
        try
        {
            var create = (delegate* unmanaged<InstanceCreateInfo*, IntPtr, IntPtr*, int>)NativeLibrary.GetExport(lib, "vkCreateInstance");
            var destroy = (delegate* unmanaged<IntPtr, IntPtr, void>)NativeLibrary.GetExport(lib, "vkDestroyInstance");
            var enumerate = (delegate* unmanaged<IntPtr, uint*, IntPtr*, int>)NativeLibrary.GetExport(lib, "vkEnumeratePhysicalDevices");
            var properties = (delegate* unmanaged<IntPtr, byte*, void>)NativeLibrary.GetExport(lib, "vkGetPhysicalDeviceProperties");
            var memory = (delegate* unmanaged<IntPtr, byte*, void>)NativeLibrary.GetExport(lib, "vkGetPhysicalDeviceMemoryProperties");
            var info = new InstanceCreateInfo { SType = 1 };
            IntPtr instance;
            if (create(&info, IntPtr.Zero, &instance) != 0) return [];
            try
            {
                uint count = 0;
                if (enumerate(instance, &count, null) != 0) return [];
                var handles = new IntPtr[count];
                fixed (IntPtr* h = handles) if (count > 0 && enumerate(instance, &count, h) < 0) return [];
                var devices = new List<Device>();
                // VkPhysicalDeviceProperties: four uint32, the type, then the name (256 bytes, UTF-8); 824 bytes in all
                var p = stackalloc byte[1024];
                // VkPhysicalDeviceMemoryProperties: a count and 32 types (8 bytes each), then at 260 a count and at 264
                // 16 heaps of 16 bytes: the size (uint64) and flags, 1 = device-local; 520 bytes in all
                var m = stackalloc byte[1024];
                foreach (var d in handles.Take((int)count))
                {
                    properties(d, p);
                    var name = new ReadOnlySpan<byte>(p + 20, 256);
                    memory(d, m);
                    long largest = 0;
                    for (int i = 0; i < Math.Min(16, *(int*)(m + 260)); i++)
                        if ((*(int*)(m + 264 + 16 * i + 8) & 1) != 0) largest = Math.Max(largest, *(long*)(m + 264 + 16 * i));
                    devices.Add(new Device(Encoding.UTF8.GetString(name[..Math.Max(0, name.IndexOf((byte)0))]), *(int*)(p + 16), (int)(largest >> 20)));
                }
                return devices;
            }
            finally { destroy(instance, IntPtr.Zero); }
        }
        catch (EntryPointNotFoundException) { return []; }
        finally { NativeLibrary.Free(lib); }
    }

    /// <summary>What <c>be --vulkan-devices</c> prints: a line "type memoryMB name" a device, "none" without any.</summary>
    public static string Print(List<Device> devices) => devices.Count == 0 ? "none" : string.Join('\n', devices);

    /// <summary>That output back as devices; null when it is not that (another program answered).</summary>
    internal static List<Device>? Parse(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines is ["none"]) return [];
        if (lines.Length == 0) return null;
        var devices = new List<Device>();
        foreach (var l in lines)
        {
            if (l.Split(' ', 3) is not [var t, var mb, var name] || !int.TryParse(t, out int type) || !int.TryParse(mb, out int memory)) return null;
            devices.Add(new Device(name, type, memory));
        }
        return devices;
    }
}
