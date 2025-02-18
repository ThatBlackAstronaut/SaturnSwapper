using System.Runtime.InteropServices;

namespace Radon.CodeAnalysis.Emit.Binary.MetadataBinary;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct Field
{
    public readonly BindingFlags Flags;
    public readonly int Name;
    public readonly int Type;
    public readonly int Parent;
    public readonly int Offset;
    
    public Field(BindingFlags flags, int name, int type, int parent, int offset)
    {
        Flags = flags;
        Name = name;
        Type = type;
        Parent = parent;
        Offset = offset;
    }
}