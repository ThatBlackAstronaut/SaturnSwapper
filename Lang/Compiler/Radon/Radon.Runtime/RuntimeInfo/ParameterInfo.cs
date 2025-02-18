using Radon.CodeAnalysis.Emit.Binary.MetadataBinary;

namespace Radon.Runtime.RuntimeInfo;

internal sealed record ParameterInfo
{
    public string Name { get; }
    public TypeInfo Type { get; }
    public MethodInfo Parent { get; }
    public int Ordinal { get; }
    public ParameterInfo(Parameter parameter, Metadata metadata, MethodInfo parent, TypeInfo parentType)
    {
        Name = metadata.Strings.Strings[parameter.Name];
        Type = TypeTracker.Add(metadata.Types.Types[parameter.Type], metadata, parentType);
        Parent = parent;
        Ordinal = parameter.Ordinal;
    }
    
    public override string ToString()
    {
        return $"{Type.ToString(false)} {Name}";
    }
}