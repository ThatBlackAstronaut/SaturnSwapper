﻿using System.Collections.Immutable;
using Radon.CodeAnalysis.Binding.Analyzers;
using Radon.CodeAnalysis.Syntax;

namespace Radon.CodeAnalysis.Symbols;

public sealed class PrimitiveTemplateSymbol : TypeSymbol
{
    public override string Name { get; }
    public override int Size { get; internal set; }
    internal override TypeBinder? TypeBinder { get; set; }
    public override SymbolKind Kind => SymbolKind.Template;
    public override ImmutableArray<MemberSymbol> Members { get; private protected set; }
    public override AssemblySymbol? ParentAssembly { get; }
    public override ImmutableArray<SyntaxKind> Modifiers { get; }
    public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }
    public ImmutableArray<TypeSymbol> TypeArguments { get; }
    public TemplateSymbol Template { get; }

    public PrimitiveTemplateSymbol(TemplateSymbol template, ImmutableArray<TypeSymbol> typeArguments)
    {
        Name = template.Name;
        Members = template.Members;
        ParentAssembly = template.ParentAssembly;
        Size = template.Size;
        TypeBinder = template.TypeBinder;
        Modifiers = template.Modifiers;
        TypeParameters = template.TypeParameters;
        TypeArguments = typeArguments;
        Template = template;
    }
    
    public override string ToString()
    {
        return Name;
    }
}