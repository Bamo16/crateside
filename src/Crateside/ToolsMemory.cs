using Crateside.Components;
using Crateside.Core.Pairing;

namespace Crateside;

/// <summary>The last tool used and the key picked in it, shared between the tools until a restart.</summary>
public sealed class ToolsMemory
{
    public ToolsFrame.Tool LastTool { get; set; } = ToolsFrame.Tool.Partners;

    public MusicKey? Key { get; set; }
}
