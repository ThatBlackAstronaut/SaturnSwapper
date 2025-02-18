using System.Runtime.InteropServices;

namespace Radon.CodeAnalysis.Emit.Binary.MetadataBinary;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct MethodTable
{
    public readonly Method[] Methods;
    
    public MethodTable(Method[] methods)
    {
        Methods = methods;
    }
}