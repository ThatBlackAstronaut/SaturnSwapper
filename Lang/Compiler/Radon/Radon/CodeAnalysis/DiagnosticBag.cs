using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Radon.CodeAnalysis.Binding.Semantics;
using Radon.CodeAnalysis.Symbols;
using Radon.CodeAnalysis.Syntax;
using Radon.CodeAnalysis.Syntax.Nodes;
using Radon.CodeAnalysis.Text;

namespace Radon.CodeAnalysis;

internal sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics;
    private readonly bool _disabled;
    private bool _block;

    public int Count => _diagnostics.Count;
    
    public DiagnosticBag(bool disabled = false)
    {
        _diagnostics = new List<Diagnostic>();
        _disabled = disabled;
    }

    public IEnumerator<Diagnostic> GetEnumerator()
    {
        return _diagnostics.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public override string ToString()
    {
        return $"Errors: {Count}";
    }

    public void Block()
    {
        _block = true;
    }

    public void Unblock()
    {
        _block = false;
    }

    private static string GetText(SyntaxKind kind)
    {
        if (kind == SyntaxKind.EndOfFileToken)
        {
            return "end of file";
        }
        
        if (kind.Text == null)
        {
            return kind.ToString().ToLower().Replace("token", string.Empty);
        }
        
        return kind.Text;
    }
    
    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        if (_disabled || _block)
        {
            return;
        }
        
        _diagnostics.AddRange(diagnostics);
    }

    private static string? GetMethod()
    {
        var stackTrace = new StackTrace();
        var frame = stackTrace.GetFrame(3); // Skip this method, and the caller of this method.
        if (frame is null)
        {
            return null;
        }
        
        var method = frame.GetMethod();
        if (method is null)
        {
            return null;
        }
        
        var sb = new StringBuilder();
        // Something like: [Namespcae]Type.Method(Parameters)
        // E.g: [Radon.CodeAnalysis.Binding.Semantics]TypeBinder.Bind(SyntaxNode, Object[])
        sb.Append('[');
        sb.Append(method.DeclaringType?.Namespace);
        sb.Append(']');
        sb.Append(method.DeclaringType?.Name);
        sb.Append('.');
        sb.Append(method.Name);
        sb.Append('(');
        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            sb.Append(parameter.ParameterType.Name);
            if (i != parameters.Length - 1)
            {
                sb.Append(", ");
            }
        }
        
        sb.Append(')');
        return sb.ToString();
    }
    
    private void ReportError(TextLocation location, string message, ErrorCode code)
    {
        if (_disabled || _block)
        {
            return;
        }

        var methodString = GetMethod();
        var diagnostic = Diagnostic.Error(location, message, code, methodString);
        _diagnostics.Add(diagnostic);
    }
    
    private void ReportWarning(TextLocation location, string message, ErrorCode code)
    {
        if (_disabled || _block)
        {
            return;
        }
        
        var methodString = GetMethod();
        var diagnostic = Diagnostic.Warning(location, message, code, methodString);
        _diagnostics.Add(diagnostic);
    }

    public void ReportUnterminatedMultiLineComment(TextLocation location)
    {
        const string message = "Unterminated multi-line comment.";
        ReportError(location, message, ErrorCode.UnterminatedMultiLineComment);
    }

    public void ReportInvalidNumber(TextLocation location, string text)
    {
        var message = $"The number '{text}' is not a valid number.";
        ReportError(location, message, ErrorCode.InvalidNumber);
    }

    public void ReportUnterminatedString(TextLocation location)
    {
        const string message = "Unterminated string literal.";
        ReportError(location, message, ErrorCode.UnterminatedString);
    }

    public void ReportUnexpectedCharacter(TextLocation location, char character)
    {
        var message = $"Unexpected character '{character}'.";
        ReportError(location, message, ErrorCode.UnexpectedCharacter);
    }

    private static string? BuildExpected(ImmutableArray<SyntaxKind> expectedKinds)
    {
        if (expectedKinds.Length == 0)
        {
            return null;
        }
        
        var sb = new StringBuilder();
        var dynamicList = new List<SyntaxKind>();
        SyntaxKind? lastKind = null;
        for (var i = 0; i < expectedKinds.Length; i++)
        {
            var expectedKind = expectedKinds[i];
            if (expectedKind.Text == null)
            {
                dynamicList.Add(expectedKind);
                continue;
            }
            
            sb.Append('\'');
            sb.Append(GetText(expectedKind));
            sb.Append('\'');
            // Check if it's the last element.
            if (i < expectedKinds.Length - 1)
            {
                sb.Append(", ");
            }
            else
            {
                lastKind = expectedKind;
            }
        }

        if (lastKind == null)
        {
            return null;
        }
        
        if (dynamicList.Count > 0)
        {
            sb.Append(lastKind.Text);
            for (var i = 0; i < dynamicList.Count; i++)
            {
                var expectedKind = dynamicList[i];
                var name = GetText(expectedKind);
                var indefiniteArticle = "aeiou".Contains(name[0]) ? "an" : "a";
                var expectedKindText = $"{indefiniteArticle} {name}";
                sb.Append(expectedKindText);
                // Check if it's the second last element.
                sb.Append(i < dynamicList.Count - 2 ? ", " : " or ");
            }
        }
        else
        {
            sb.Append(" or ");
            sb.Append('\'');
            sb.Append(GetText(lastKind));
            sb.Append('\'');
        }
        
        return sb.ToString();
    }
    
    public void ReportExpectedToken(TextLocation location, SyntaxKind kind)
    {
        var expectedKindText = GetText(kind);
        var message = $"Expected '{expectedKindText}'.";
        ReportError(location, message, ErrorCode.ExpectedToken);
    }
    
    public void ReportUnexpectedToken(TextLocation location, string text)
    {
        var message = $"Unexpected token '{text}'.";
        ReportError(location, message, ErrorCode.UnexpectedToken);
    }
    
    public void ReportUnexpectedToken(TextLocation location, string text, SyntaxKind expectedKind)
    {
        var expectedKindText = expectedKind.Text;
        var isDynamic = expectedKindText == null;
        if (expectedKindText == null)
        {
            var name = GetText(expectedKind);
            var indefiniteArticle = "aeiou".Contains(name[0]) ? "an" : "a";
            expectedKindText = $"{indefiniteArticle} {name}";
        }

        var message = isDynamic ? $"Unexpected token '{text}', expected {expectedKindText}." 
                                     : $"Unexpected token '{text}', expected '{expectedKindText}'.";
        
        ReportError(location, message, ErrorCode.UnexpectedToken);
    }

    public void ReportUnexpectedToken(TextLocation location, SyntaxKind kind, ImmutableArray<SyntaxKind> expectedKinds)
    {
        var sb = new StringBuilder();
        sb.Append("Unexpected token '");
        sb.Append(GetText(kind));
        sb.Append("', expected: ");
        var expected = BuildExpected(expectedKinds);
        if (expected == null)
        {
            sb.Clear();
            sb.Append("Unexpected token");
        }
        else
        {
            sb.Append(expected);
        }
        
        sb.Append('.');
        var message = sb.ToString();
        ReportError(location, message, ErrorCode.UnexpectedToken);
    }

    public void ReportInternalCompilerError(Exception e)
    {
#if DEBUG
        var exceptionMessage = e.ToString();
#else
        var exceptionMessage = e.Message;
#endif
        var message = $"Internal compiler error: '{exceptionMessage}'";
        ReportError(TextLocation.Empty, message, ErrorCode.InternalError);
    }

    public void ReportUnfinishedDirective(TextLocation location, ImmutableArray<SyntaxKind> kinds)
    {
        var sb = new StringBuilder();
        sb.Append("Unfinished directive, expected: ");
        var expected = BuildExpected(kinds);
        if (expected == null)
        {
            sb.Clear();
            sb.Append("Unfinished directive");
        }
        else
        {
            sb.Append(expected);
        }
        
        sb.Append('.');
        var message = sb.ToString();
        ReportError(location, message, ErrorCode.UnfinishedDirective);
    }

    public void ReportIncludePathDoesNotExist(TextLocation location, string includePath)
    {
        var message = $"Include path '{includePath}' does not exist.";
        ReportError(location, message, ErrorCode.IncludePathDoesNotExist);
    }

    public void ReportInvalidTypeDeclaration(TextLocation location, string keywordText)
    {
        var message = $"Invalid type declaration '{keywordText}'.";
        ReportError(location, message, ErrorCode.InvalidTypeDeclaration);
    }

    public void ReportInvalidTypeStart(TextLocation location, string text)
    {
        var message = $"Invalid type start '{text}'. Expected type modifier or type keyword.";
        ReportError(location, message, ErrorCode.InvalidTypeStart);
    }

    public void ReportCannotGoUpDirectory()
    {
        const string message = "Cannot go up directory.";
        ReportError(TextLocation.Empty, message, ErrorCode.InvalidIncludePath);
    }

    public void ReportDuplicateInclude(TextLocation location, string path)
    {
        var message = $"Duplicate include '{path}'.";
        ReportError(location, message, ErrorCode.DuplicateInclude);
    }

    public void ReportCircularInclude(TextLocation location, string path)
    {
        var message = $"Circular include '{path}'.";
        ReportError(location, message, ErrorCode.CircularInclude);
    }

    public void ReportNullScope(TextLocation location)
    {
        const string message = "Null scope.";
        ReportError(location, message, ErrorCode.InternalError);
    }

    public void ReportSymbolAlreadyDeclared(TextLocation location, string name)
    {
        var message = $"Symbol '{name}' is already declared.";
        ReportError(location, message, ErrorCode.SymbolAlreadyDeclared);
    }

    public void ReportUnresolvedSymbol(TextLocation location, string name)
    {
        var message = $"Unresolved symbol '{name}'.";
        ReportError(location, message, ErrorCode.UnresolvedSymbol);
    }

    public void ReportMultipleProgramUnits(TextLocation location)
    {
        const string message = "Multiple program units.";
        ReportError(location, message, ErrorCode.MultipleProgramUnits);
    }

    public void ReportConstructorNameMismatch(TextLocation location, string name, object text)
    {
        var message = $"Constructor name '{name}' does not match type name '{text}'.";
        ReportError(location, message, ErrorCode.ConstructorNameMismatch);
    }

    public void ReportUndefinedType(TextLocation location, string text)
    {
        var message = $"Undefined type '{text}'.";
        ReportError(location, message, ErrorCode.UndefinedType);
    }

    public void ReportUndefinedBinaryOperator(TextLocation location, string text, TypeSymbol left, TypeSymbol right)
    {
        var message = $"Binary operator '{text}' is not defined for types '{left}' and '{right}'.";
        ReportError(location, message, ErrorCode.UndefinedBinaryOperator);
    }

    public void ReportInvalidImportPathType(TextLocation location, TypeSymbol type)
    {
        var message = $"Invalid import path type '{type}'.";
        ReportError(location, message, ErrorCode.InvalidImportPathType);
    }

    public void ReportCannotInvoke(TextLocation location, BoundNodeKind kind)
    {
        var message = $"Cannot invoke expressions of type '{kind}'.";
        ReportError(location, message, ErrorCode.CannotInvokeExpression);
    }

    public void ReportNullLiteral(TextLocation location)
    {
        const string message = "Null literal.";
        ReportError(location, message, ErrorCode.NullLiteral);
    }

    public void ReportInvalidLiteralExpression(TextLocation location, string text)
    {
        var message = $"Invalid literal expression '{text}'.";
        ReportError(location, message, ErrorCode.InvalidLiteralExpression);
    }

    public void ReportUndefinedMember(TextLocation location, string name, TypeSymbol type)
    {
        var message = $"Undefined member '{name}' for type '{type}'.";
        ReportError(location, message, ErrorCode.UndefinedMember);
    }

    public void ReportThisExpressionOutsideOfMethod(TextLocation location)
    {
        const string message = "Use of 'this' expression outside of method.";
        ReportError(location, message, ErrorCode.ThisExpressionOutsideOfMethod);
    }

    public void ReportThisExpressionInStaticMethod(TextLocation location)
    {
        const string message = "Use of 'this' expression in static method.";
        ReportError(location, message, ErrorCode.ThisExpressionInStaticMethod);
    }

    public void ReportUndefinedUnaryOperator(TextLocation location, string text, TypeSymbol type)
    {
        var message = $"Unary operator '{text}' is not defined for type '{type}'.";
        ReportError(location, message, ErrorCode.UndefinedUnaryOperator);
    }

    public void ReportCannotConvert(TextLocation location, TypeSymbol expressionType, TypeSymbol type)
    {
        var message = $"Cannot convert expression of type '{expressionType}' to type '{type}'.";
        ReportError(location, message, ErrorCode.CannotConvert);
    }

    public void ReportCannotConvertSourceType(TextLocation location, TypeSymbol literalType, TypeSymbol type)
    {
        var message = $"Cannot convert source type '{literalType}' to type '{type}'.";
        ReportError(location, message, ErrorCode.CannotConvertSourceType);
    }

    public void ReportCannotConvertImplicitly(TextLocation location, TypeSymbol expressionType, TypeSymbol type)
    {
        var message = $"Cannot implicitly convert expression of type '{expressionType}' to type '{type}'.";
        ReportError(location, message, ErrorCode.CannotConvertImplicitly);
    }

    public void ReportCycleInStructLayout(TextLocation location, string type)
    {
        var message = $"Cycle in struct layout for type '{type}'.";
        ReportError(location, message, ErrorCode.CycleInStructLayout);
    }

    public void ReportEnumMemberMustHaveConstantValue(TextLocation location, string name)
    {
        var message = $"Enum member '{name}' must have constant value.";
        ReportError(location, message, ErrorCode.EnumMemberMustHaveConstantValue);
    }

    public void ReportIncorrectNumberOfTypeArguments(TextLocation location, string method, int length, int provided)
    {
        var message = $"Incorrect number of type arguments for method '{method}'. Expected {length}, provided {provided}.";
        ReportError(location, message, ErrorCode.IncorrectNumberOfTypeArguments);
    }

    public void ReportIncorrectNumberOfArguments(TextLocation location, string method, int parametersLength, int argumentsCount)
    {
        var message = $"Incorrect number of arguments for method '{method}'. Expected {parametersLength}, provided {argumentsCount}.";
        ReportError(location, message, ErrorCode.IncorrectNumberOfArguments);
    }

    public void ReportUnresolvedMethod(TextLocation location, string name, ImmutableArray<TypeSymbol> parameterTypes)
    {
        var parameterTypesString = $"parameter types '{string.Join(", ", parameterTypes)}'";
        if (parameterTypesString.Length == 0)
        {
            parameterTypesString = "no parameters";
        }
        
        var message = $"Unresolved method '{name}' with {parameterTypesString}.";
        ReportError(location, message, ErrorCode.UnresolvedMethod);
    }

    public void ReportAmbiguousMethodCall<TMethodSymbol>(TextLocation location, ImmutableArray<TMethodSymbol> ambiguousCalls) 
        where TMethodSymbol : AbstractMethodSymbol
    {
        // example: The call is ambiguous between the following methods: 'void Program.M(float, int)' and 'void Program.M(int, float)'
        var sb = new StringBuilder();
        sb.Append("The call is ambiguous between the following methods: ");
        for (var i = 0; i < ambiguousCalls.Length; i++)
        {
            var method = ambiguousCalls[i];
            sb.Append($"'{method.Type} {method.ParentType}.{method.Name}(");
            for (var j = 0; j < method.Parameters.Length; j++)
            {
                var parameter = method.Parameters[j];
                sb.Append($"{parameter.Type}");
                if (j != method.Parameters.Length - 1)
                {
                    sb.Append(", ");
                }
            }
            
            sb.Append(")'");
            if (i != ambiguousCalls.Length - 1)
            {
                sb.Append(" and ");
            }
        }
        
        ReportError(location, sb.ToString(), ErrorCode.AmbiguousMethodCall);
    }

    public void ReportCannotInstantiateNonStruct(TextLocation location, string name)
    {
        var message = $"Cannot instantiate non-struct '{name}'.";
        ReportError(location, message, ErrorCode.CannotInstantiateNonStruct);
    }
    
    public void ReportCouldNotBindTemplateMethod(TextLocation location, string name)
    {
        var message = $"Could not bind template method '{name}'.";
        ReportError(location, message, ErrorCode.CouldNotBindTemplateMethod);
    }
    
    public void ReportCannotInstantiateNonArray(TextLocation location, string name)
    {
        var message = $"Cannot instantiate non-array '{name}'.";
        ReportError(location, message, ErrorCode.CannotInstantiateNonArray);
    }

    public void ReportIndexMustBeInteger(TextLocation location)
    {
        const string message = "Index must be integer.";
        ReportError(location, message, ErrorCode.IndexMustBeInteger);
    }

    public void ReportCannotIndexNonArray(TextLocation location, string name)
    {
        var message = $"Cannot index non-array '{name}'.";
        ReportError(location, message, ErrorCode.CannotIndexNonArray);
    }

    public void ReportArrayMustHaveSize(TextLocation location)
    {
        const string message = "Array initializer must have size specified.";
        ReportError(location, message, ErrorCode.ArrayMustHaveSize);
    }

    public void ReportCannotReturnExpressionFromVoidMethod(TextLocation location)
    {
        const string message = "Cannot return an expression from a void method.";
        ReportError(location, message, ErrorCode.CannotReturnExpressionFromVoidMethod);
    }

    public void ReportMustReturnExpressionFromNonVoidMethod(TextLocation location)
    {
        const string message = "Must return an expression from a non-void method.";
        ReportError(location, message, ErrorCode.MustReturnExpressionFromNonVoidMethod);
    }

    public void ReportSignKeyMustBeString(TextLocation location)
    {
        const string message = "Sign key must be of type 'string'.";
        ReportError(location, message, ErrorCode.SignKeyMustBeString);
    }

    public void ReportSignValueMustBeStringBoolOrNumeric(TextLocation location)
    {
        const string message = "Sign value must be of type 'string', 'bool', or any numeric type.";
        ReportError(location, message, ErrorCode.SignValueMustBeStringBoolOrNumeric);
    }

    public void ReportNullConstantValue(TextLocation location)
    {
        const string message = "Constant value is null.";
        ReportError(location, message, ErrorCode.NullConstantValue);
    }

    public void ReportCannotHaveBothPublicAndPrivateModifier(TextLocation location)
    {
        const string message = "Cannot have both public and private modifier.";
        ReportError(location, message, ErrorCode.CannotHaveBothPublicAndPrivateModifier);
    }

    public void ReportDuplicateModifier(TextLocation location, SyntaxKind modifier)
    {
        var message = $"Duplicate modifier '{modifier.Text}'.";
        ReportError(location, message, ErrorCode.DuplicateModifier);
    }

    public void ReportCannotAccessNonPublicMember(TextLocation location, string name)
    {
        var message = $"Cannot access non-public member '{name}'.";
        ReportError(location, message, ErrorCode.CannotAccessNonPublicMember);
    }
}