using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;

/// <summary>
/// P/Invoke bindings for libwayland-client and wlr-layer-shell protocol.
/// These compile on any platform but resolve at runtime only on Linux with Wayland.
/// </summary>
internal static class WaylandInterop
{
    private const string LibWaylandClient = "libwayland-client.so.0";

    // --- wayland-client Core ---

    [DllImport(LibWaylandClient)]
    public static extern IntPtr wl_display_connect(string? name);

    [DllImport(LibWaylandClient)]
    public static extern void wl_display_disconnect(IntPtr display);

    [DllImport(LibWaylandClient)]
    public static extern int wl_display_roundtrip(IntPtr display);

    [DllImport(LibWaylandClient)]
    public static extern int wl_display_dispatch(IntPtr display);

    [DllImport(LibWaylandClient)]
    public static extern IntPtr wl_display_get_registry(IntPtr display);

    // --- wl_registry ---

    [DllImport(LibWaylandClient)]
    public static extern int wl_registry_add_listener(
        IntPtr registry,
        ref WlRegistryListener listener,
        IntPtr data);

    // --- wl_compositor ---

    [DllImport(LibWaylandClient)]
    public static extern IntPtr wl_compositor_create_surface(IntPtr compositor);

    [DllImport(LibWaylandClient)]
    public static extern IntPtr wl_compositor_create_region(IntPtr compositor);

    // --- wl_region ---

    [DllImport(LibWaylandClient)]
    public static extern void wl_region_add(IntPtr region, int x, int y, int width, int height);

    [DllImport(LibWaylandClient)]
    public static extern void wl_region_subtract(IntPtr region, int x, int y, int width, int height);

    [DllImport(LibWaylandClient)]
    public static extern void wl_region_destroy(IntPtr region);

    // --- wl_surface ---

    [DllImport(LibWaylandClient)]
    public static extern void wl_surface_set_input_region(IntPtr surface, IntPtr region);

    [DllImport(LibWaylandClient)]
    public static extern void wl_surface_commit(IntPtr surface);

    [DllImport(LibWaylandClient)]
    public static extern void wl_surface_destroy(IntPtr surface);

    // --- wl_proxy (generic) ---

    [DllImport(LibWaylandClient)]
    public static extern void wl_proxy_destroy(IntPtr proxy);

    [DllImport(LibWaylandClient)]
    public static extern IntPtr wl_proxy_marshal_constructor(
        IntPtr proxy,
        uint opcode,
        IntPtr @interface,
        IntPtr[] args);

    [DllImport(LibWaylandClient, EntryPoint = "wl_proxy_marshal_flags")]
    public static extern IntPtr wl_registry_bind(
        IntPtr registry,
        uint name,
        ref WlInterface @interface,
        uint version,
        uint flags);

    // --- Interface descriptors for binding ---

    [StructLayout(LayoutKind.Sequential)]
    public struct WlInterface
    {
        public IntPtr Name;           // const char*
        public int Version;
        public int MethodCount;
        public IntPtr Methods;        // const struct wl_message*
        public int EventCount;
        public IntPtr Events;         // const struct wl_message*
    }

    // Pre-built interface structs for binding (populated at runtime via GetInterfacePointer)
    private static IntPtr _wlCompositorInterfacePtr;
    private static IntPtr _wlSurfaceInterfacePtr;
    private static IntPtr _wlRegionInterfacePtr;

    /// <summary>
    /// Gets a pointer to the wl_compositor_interface symbol from libwayland-client.
    /// </summary>
    [DllImport(LibWaylandClient)]
    private static extern IntPtr wl_compositor_interface_get();

    /// <summary>
    /// Binds to wl_compositor from the registry.
    /// </summary>
    public static IntPtr BindCompositor(IntPtr registry, uint name, uint version)
    {
        // Create a minimal interface descriptor for binding
        var iface = new WlInterface();
        var nameBytes = System.Text.Encoding.UTF8.GetBytes("wl_compositor\0");
        var nameHandle = System.Runtime.InteropServices.GCHandle.Alloc(nameBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            iface.Name = nameHandle.AddrOfPinnedObject();
            iface.Version = (int)version;
            iface.MethodCount = 2;  // create_surface, create_region
            iface.Methods = IntPtr.Zero;
            iface.EventCount = 0;
            iface.Events = IntPtr.Zero;

            return wl_registry_bind(registry, name, ref iface, version, 0);
        }
        finally
        {
            nameHandle.Free();
        }
    }

    // --- Registry Listener Delegate ---

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RegistryGlobalHandler(
        IntPtr data,
        IntPtr registry,
        uint name,
        [MarshalAs(UnmanagedType.LPStr)] string @interface,
        uint version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RegistryGlobalRemoveHandler(
        IntPtr data,
        IntPtr registry,
        uint name);

    [StructLayout(LayoutKind.Sequential)]
    public struct WlRegistryListener
    {
        public RegistryGlobalHandler Global;
        public RegistryGlobalRemoveHandler GlobalRemove;
    }
}

/// <summary>
/// P/Invoke bindings for wlr-layer-shell-unstable-v1 protocol (sway, Hyprland, wlroots).
/// This protocol is not part of core Wayland and requires a wlroots-based compositor.
/// </summary>
internal static class WlrLayerShellInterop
{
    // The wlr-layer-shell protocol doesn't have a separate library - it's a Wayland protocol
    // that we access through the registry. These constants define the protocol interface.

    /// <summary>Layer shell interface name for registry binding.</summary>
    public const string ZWLR_LAYER_SHELL_V1 = "zwlr_layer_shell_v1";

    /// <summary>Layer surface interface name.</summary>
    public const string ZWLR_LAYER_SURFACE_V1 = "zwlr_layer_surface_v1";

    /// <summary>
    /// Layer values for zwlr_layer_shell_v1::get_layer_surface.
    /// Higher layers are rendered above lower layers.
    /// </summary>
    public enum Layer : uint
    {
        /// <summary>Desktop background layer.</summary>
        Background = 0,
        /// <summary>Desktop widgets, dock panels below windows.</summary>
        Bottom = 1,
        /// <summary>Panels, docks, etc. above normal windows.</summary>
        Top = 2,
        /// <summary>Overlay layer: notifications, OSD, always on top.</summary>
        Overlay = 3
    }

    /// <summary>
    /// Anchor edge flags for zwlr_layer_surface_v1::set_anchor.
    /// Combine with bitwise OR to anchor to multiple edges.
    /// </summary>
    [Flags]
    public enum Anchor : uint
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8,
        /// <summary>Anchor to all edges (fullscreen on output).</summary>
        All = Top | Bottom | Left | Right
    }

    /// <summary>
    /// Keyboard interactivity modes for zwlr_layer_surface_v1::set_keyboard_interactivity.
    /// </summary>
    public enum KeyboardInteractivity : uint
    {
        /// <summary>No keyboard focus.</summary>
        None = 0,
        /// <summary>Exclusive keyboard focus (grabs keyboard).</summary>
        Exclusive = 1,
        /// <summary>On-demand keyboard focus.</summary>
        OnDemand = 2
    }

    // Protocol opcodes for zwlr_layer_shell_v1
    public const uint GET_LAYER_SURFACE = 0;

    // Protocol opcodes for zwlr_layer_surface_v1
    public const uint SET_SIZE = 0;
    public const uint SET_ANCHOR = 1;
    public const uint SET_EXCLUSIVE_ZONE = 2;
    public const uint SET_MARGIN = 3;
    public const uint SET_KEYBOARD_INTERACTIVITY = 4;
    public const uint GET_POPUP = 5;
    public const uint ACK_CONFIGURE = 6;
    public const uint DESTROY = 7;
    public const uint SET_LAYER = 8;
}
