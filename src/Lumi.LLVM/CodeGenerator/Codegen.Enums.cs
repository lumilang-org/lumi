using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Ast.Statements;
using Lumi.Core.Semantics;
using Lumi.LLVM.Structs;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
    private readonly Dictionary<string, EnumLayoutInfo> _enumLayouts = new();
    private readonly HashSet<string> _enumBodiesDone = new();

    // Creates the named (still opaque) LLVM type of every enum so other types can refer to it.
    private void DeclareEnums(ProgramNode program)
    {
        foreach (var enumDecl in program.Enums)
        {
            var name = _semanticModel.GetMangledName(enumDecl);
            _enumLayouts[name] = new EnumLayoutInfo(
                name,
                _context.CreateNamedStruct(name),
                enumDecl,
                new Dictionary<string, EnumVariantLayout>());
        }
    }

    private void DefineEnums()
    {
        foreach (var name in _enumLayouts.Keys.ToList())
        {
            EnsureEnumBody(name);
        }
    }

    private void EnsureEnumBody(string name)
    {
        if (!_enumBodiesDone.Add(name))
        {
            return;
        }

        var layout = _enumLayouts[name];
        var variants = new Dictionary<string, EnumVariantLayout>();
        ulong maxSize = 0;
        uint maxAlign = 1;

        for (var tag = 0; tag < layout.Declaration.Variants.Count; tag++)
        {
            var variant = layout.Declaration.Variants[tag];
            var fieldTypes = variant.Fields.Select(f => _semanticModel.GetType(f)).ToList();

            foreach (var fieldType in fieldTypes)
            {
                EnsureSized(fieldType);
            }

            var payload = LLVMTypeRef.CreateStruct(
                fieldTypes.Select(ResolveType).ToArray(), false);

            if (fieldTypes.Count > 0)
            {
                maxSize = Math.Max(maxSize, _dataLayout.ABISizeOfType(payload));
                maxAlign = Math.Max(maxAlign, _dataLayout.ABIAlignmentOfType(payload));
            }

            variants[variant.Name] = new EnumVariantLayout(
                variant.Name,
                tag,
                variant.Kind,
                variant.Fields.Select(f => f.Name).ToList(),
                fieldTypes,
                payload);
        }

        var unit = _context.GetIntType(maxAlign * 8);
        var count = (uint)((maxSize + maxAlign - 1) / maxAlign);

        layout.Type.StructSetBody(
            [_context.Int32Type, LLVMTypeRef.CreateArray(unit, count)], false);

        _enumLayouts[name] = layout with { Variants = variants };
    }

    // A by-value field needs its type's size, so nested enums get their body first.
    private void EnsureSized(string typeName)
    {
        if (typeName.EndsWith(']'))
        {
            EnsureSized(typeName[..typeName.LastIndexOf('[')]);
        }
        else if (_enumLayouts.ContainsKey(typeName))
        {
            EnsureEnumBody(typeName);
        }
        else if (_structLayouts.TryGetValue(typeName, out var structLayout))
        {
            foreach (var field in structLayout.Fields)
            {
                if (!PointerTypes.IsPointer(field.LumiType))
                {
                    EnsureSized(field.LumiType);
                }
            }
        }
    }

    private LLVMValueRef GenerateEnumConstruction(
        ASTNode node,
        string variantName,
        IReadOnlyList<ExpressionNode> values)
    {
        var layout = _enumLayouts[_semanticModel.GetMangledName(node)];
        var variant = layout.Variants[variantName];

        var slot = _builder.BuildAlloca(layout.Type, "enum");
        _builder.BuildStore(
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)variant.Tag),
            _builder.BuildStructGEP2(layout.Type, slot, 0, "enum.tag"));

        if (values.Count > 0)
        {
            var payload = _builder.BuildStructGEP2(layout.Type, slot, 1, "enum.payload");

            for (var i = 0; i < values.Count; i++)
            {
                var fieldType = variant.FieldTypes[i];
                var value = CastInitializer(
                    GenerateExpression(values[i]),
                    values[i],
                    ResolveType(fieldType),
                    fieldType,
                    "variant_field");

                _builder.BuildStore(
                    value,
                    _builder.BuildStructGEP2(variant.PayloadType, payload, (uint)i, "enum.field"));
            }
        }

        return _builder.BuildLoad2(layout.Type, slot, "enum.value");
    }

    private LLVMValueRef GenerateEnumStructConstruction(EnumStructLiteralNode literal)
    {
        var layout = _enumLayouts[_semanticModel.GetMangledName(literal)];
        var variant = layout.Variants[literal.VariantName];

        var ordered = variant.FieldNames
            .Select(name => literal.Initializers.First(init => init.FieldName == name).Value)
            .ToList();

        return GenerateEnumConstruction(literal, literal.VariantName, ordered);
    }

    private static string PatternVariantName(ExpressionNode pattern)
    {
        return pattern switch
        {
            MemberAccessNode access => access.MemberName,
            MethodCallExpressionNode call => call.MethodName,
            EnumStructLiteralNode literal => literal.VariantName,
            _ => throw new InvalidOperationException("Not an enum pattern.")
        };
    }

    private void GenerateEnumSwitch(SwitchStatementNode switchStmt, string enumName)
    {
        var layout = _enumLayouts[enumName];
        var function = _builder.InsertBlock.Parent;

        // Evaluate once, then dispatch on the tag.
        var slot = _builder.BuildAlloca(layout.Type, "switch.enum");
        _builder.BuildStore(GenerateExpression(switchStmt.Subject), slot);
        var tag = _builder.BuildLoad2(
            _context.Int32Type,
            _builder.BuildStructGEP2(layout.Type, slot, 0, "switch.tag.ptr"),
            "switch.tag");

        var armBlocks = switchStmt.Arms
            .Select((_, i) => _context.AppendBasicBlock(function, $"switch.arm.{i}"))
            .ToList();
        var defaultBlock = switchStmt.DefaultBody is not null
            ? _context.AppendBasicBlock(function, "switch.default")
            : default;
        var endBlock = _context.AppendBasicBlock(function, "switch.end");

        var switchInst = _builder.BuildSwitch(
            tag,
            switchStmt.DefaultBody is not null ? defaultBlock : endBlock,
            (uint)switchStmt.Arms.Sum(arm => arm.Patterns.Count));

        for (var i = 0; i < switchStmt.Arms.Count; i++)
        {
            foreach (var pattern in switchStmt.Arms[i].Patterns)
            {
                var variant = layout.Variants[PatternVariantName(pattern)];
                switchInst.AddCase(
                    LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)variant.Tag),
                    armBlocks[i]);
            }
        }

        var continueTarget = _loopTargets.Count > 0 ? _loopTargets.Peek().ContinueTarget : endBlock;
        _loopTargets.Push(new LoopTargets(endBlock, continueTarget));

        try
        {
            for (var i = 0; i < switchStmt.Arms.Count; i++)
            {
                var arm = switchStmt.Arms[i];
                _builder.PositionAtEnd(armBlocks[i]);

                _currentScope = _currentScope.EnterScope();
                try
                {
                    if (arm.Patterns.Count == 1)
                    {
                        BindEnumPayload(layout, slot, arm.Patterns[0]);
                    }

                    GenerateStatements(arm.Body);
                }
                finally
                {
                    _currentScope = _currentScope.ExitScope();
                }

                if (_builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    _builder.BuildBr(endBlock);
                }
            }

            if (switchStmt.DefaultBody is not null)
            {
                _builder.PositionAtEnd(defaultBlock);
                GenerateScopedStatements(switchStmt.DefaultBody);

                if (_builder.InsertBlock.Terminator.Handle == IntPtr.Zero)
                {
                    _builder.BuildBr(endBlock);
                }
            }
        }
        finally
        {
            _loopTargets.Pop();
        }

        _builder.PositionAtEnd(endBlock);
    }

    // Copies each bound payload field into a fresh local of the arm.
    private void BindEnumPayload(EnumLayoutInfo layout, LLVMValueRef slot, ExpressionNode pattern)
    {
        var variant = layout.Variants[PatternVariantName(pattern)];
        var bindings = new List<(string Variable, int Field)>();

        switch (pattern)
        {
            case MethodCallExpressionNode call:
                for (var i = 0; i < call.Arguments.Count; i++)
                {
                    bindings.Add((((IdentifierNode)call.Arguments[i]).Name, i));
                }

                break;
            case EnumStructLiteralNode literal:
                foreach (var init in literal.Initializers)
                {
                    bindings.Add((((IdentifierNode)init.Value).Name, variant.FieldNames.ToList().IndexOf(init.FieldName)));
                }

                break;
        }

        if (bindings.Count == 0)
        {
            return;
        }

        var payload = _builder.BuildStructGEP2(layout.Type, slot, 1, "bind.payload");

        foreach (var (variable, field) in bindings)
        {
            if (variable == "_")
            {
                continue;
            }

            var typeName = variant.FieldTypes[field];
            var llvmType = ResolveType(typeName);
            var value = _builder.BuildLoad2(
                llvmType,
                _builder.BuildStructGEP2(variant.PayloadType, payload, (uint)field, $"{variable}.field"),
                $"{variable}.value");

            var alloca = _builder.BuildAlloca(llvmType, variable);
            _builder.BuildStore(value, alloca);
            _currentScope.Declare(variable, new VariableSymbol(typeName, llvmType, alloca));
        }
    }
}
