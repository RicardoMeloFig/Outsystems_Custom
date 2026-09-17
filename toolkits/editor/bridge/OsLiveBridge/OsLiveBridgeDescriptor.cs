// OsLiveBridge - Service Studio plugin (descriptor) that starts a named-pipe bridge
// server inside the SS process. Discovered/loaded by SS because the assembly carries
// [assembly: Plugin(typeof(OsLiveBridgeDescriptor))] and lives in Plugins\ServiceStudio\.
// SS instantiates the descriptor (subclass of AbstractPluginDescriptor, mirroring
// DietAutomationPluginDescriptor); its constructor starts the background pipe server
// (idempotent). The pipe server then exposes the live IESpace model to an external
// MCP client (outsystems-omleditor live mode).
using OutSystems.Model.Plugins;
using ServiceStudio.PluginAPI;

[assembly: Plugin(typeof(OsLiveBridge.OsLiveBridgeDescriptor))]

namespace OsLiveBridge;

// sealed + bare subclass mirrors DietAutomationPluginDescriptor; AbstractPluginDescriptor
// supplies default GetGuid/GetName. We only add a ctor that boots the bridge.
internal sealed class OsLiveBridgeDescriptor : AbstractPluginDescriptor
{
    public OsLiveBridgeDescriptor()
    {
        BridgeHost.Log("OsLiveBridgeDescriptor ctor");
        BridgeHost.EnsureStarted();
    }
}

// Module initializer: runs when SS first executes any code in this assembly
// (e.g. when it instantiates the provider below). Belt-and-suspenders alongside
// the provider ctor.
internal static class ModuleInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        BridgeHost.Log("module initializer");
        BridgeHost.EnsureStarted();
    }
}
