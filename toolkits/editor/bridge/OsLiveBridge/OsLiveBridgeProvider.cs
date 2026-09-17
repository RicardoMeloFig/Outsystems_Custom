// OsLiveBridgeProvider - the IDiscoverable hook SS actually instantiates at startup.
// Mirrors ServiceStudio.Plugin.SampleBlocks.SampleBlockProvider (subclass of
// ToolEntriesProvider<Self>), which the heap probe confirmed is instantiated. SS
// discovers IDiscoverable ToolEntriesProvider subclasses, instantiates them, and
// registers them in its HashSet<ToolEntriesProvider> - so this ctor runs at startup
// and boots the pipe server. GetToolEntries returns empty (no toolbox pollution).
using System.Collections.Generic;
using System.Linq;
using ServiceStudio.PluginAPI;
using ServiceStudio.PluginAPI.ToolEntries;

namespace OsLiveBridge;

internal sealed class OsLiveBridgeProvider : ToolEntriesProvider<OsLiveBridgeProvider>
{
    public OsLiveBridgeProvider()
    {
        BridgeHost.Log("OsLiveBridgeProvider ctor");
        BridgeHost.EnsureStarted();
    }

    public override IEnumerable<IToolEntry> GetToolEntries(IWorkspace workspace)
    {
        // Note: workspace.OwnerESpace gives the active module (IWorkspace -> IESpace),
        // but accessing it pulls in OutSystems.Model.V1 at compile time. The bridge
        // reaches modules via PluginProvider.ModelServices.LoadedESpaces instead, so we
        // don't touch OwnerESpace here. Return empty (no toolbox pollution).
        try { BridgeHost.Log("GetToolEntries called"); } catch { }
        return Enumerable.Empty<IToolEntry>();
    }

    public override void CleanupToolEntriesCache(IWorkspace workspace)
    {
        // no-op
    }
}
