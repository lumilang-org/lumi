using LLVMSharp.Interop;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Semantics;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
    // Builds the aggregate element by element; each element is converted to its tuple slot type.
    private LLVMValueRef GenerateTupleLiteral(TupleLiteralNode tuple)
    {
        var tupleType = _semanticModel.GetExpressionType(tuple);
        var elementTypes = TupleTypes.Elements(tupleType);
        var llvmType = ResolveType(tupleType);

        var aggregate = LLVMValueRef.CreateConstNull(llvmType);
        for (var i = 0; i < tuple.Elements.Count; i++)
        {
            var value = CastInitializer(
                GenerateExpression(tuple.Elements[i]),
                tuple.Elements[i],
                ResolveType(elementTypes[i]),
                elementTypes[i],
                "tuple_element");

            aggregate = _builder.BuildInsertValue(aggregate, value, (uint)i, "tuple");
        }

        return aggregate;
    }

    // The initializer is evaluated once; each named leaf copies its element into a new local.
    private void GenerateTupleDestructuring(TupleDestructuringNode node)
    {
        var aggregate = GenerateExpression(node.Initializer);
        BindTuplePattern(node.Pattern, aggregate);
    }

    private void BindTuplePattern(TuplePatternNode pattern, LLVMValueRef value)
    {
        if (pattern.IsLeaf)
        {
            if (pattern.Name == "_")
            {
                return;
            }

            var typeName = _semanticModel.GetType(pattern);
            var llvmType = ResolveType(typeName);
            var alloca = _builder.BuildAlloca(llvmType, pattern.Name!);
            _builder.BuildStore(value, alloca);
            _currentScope.Declare(pattern.Name!, new VariableSymbol(typeName, llvmType, alloca));
            return;
        }

        for (var i = 0; i < pattern.Elements!.Count; i++)
        {
            var element = pattern.Elements[i];

            // Skip extraction for ignored elements.
            if (element.IsLeaf && element.Name == "_")
            {
                continue;
            }

            BindTuplePattern(element, _builder.BuildExtractValue(value, (uint)i, "tuple.element"));
        }
    }
}
