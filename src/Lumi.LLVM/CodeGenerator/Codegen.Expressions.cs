using System.Text;
using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.Structs;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
    private LLVMValueRef GenerateExpression(ExpressionNode expression)
    {
        return expression switch
        {
            LiteralNode<string> num => LLVMValueRef.CreateConstInt(
                ResolveType(_semanticModel.GetExpressionType(expression)),
                ulong.Parse(num.Value)),
            LiteralNode<double> floating => LLVMValueRef.CreateConstReal(
                ResolveType(_semanticModel.GetExpressionType(expression)),
                floating.Value),
            IdentifierNode bare when _semanticModel.IsEnumConstruction(bare) =>
                GenerateEnumConstruction(bare, bare.Name, []),
            FunctionCallExpressionNode bareCall when _semanticModel.IsEnumConstruction(bareCall) =>
                GenerateEnumConstruction(bareCall, bareCall.Name, bareCall.Arguments),
            IdentifierNode id => LoadVariable(id.Name),
            BinaryExpressionNode binary => GenerateBinary(binary),
            UnaryExpressionNode unary => GenerateUnary(unary),
            UpdateExpressionNode update => GenerateUpdate(update),
            AddressOfExpressionNode addressOf => ResolveTargetAddress(addressOf.Operand).Pointer,
            DereferenceExpressionNode dereference => GenerateDereference(dereference),
            CastExpressionNode cast => GenerateCast(cast),
            SizeOfExpressionNode sizeOf => LLVMValueRef.CreateConstInt(
                _usizeType, _dataLayout.ABISizeOfType(ResolveType(_semanticModel.GetMangledName(sizeOf)))),
            AlignOfExpressionNode alignOf => LLVMValueRef.CreateConstInt(
                _usizeType, _dataLayout.ABIAlignmentOfType(ResolveType(_semanticModel.GetMangledName(alignOf)))),
            LiteralNode<char> character => LLVMValueRef.CreateConstInt(_context.Int8Type, character.Value),
            StringLiteralNode str => GenerateStringLiteral(str),
            // Pointers are opaque, so one null constant serves every pointer type.
            NilLiteralNode => LLVMValueRef.CreateConstPointerNull(ResolveType(PointerTypes.To(TypeConst.U8))),
            LiteralNode<bool> boolean => LLVMValueRef.CreateConstInt(_context.Int1Type, (ulong)(boolean.Value ? 1 : 0)),
            TupleLiteralNode tuple => GenerateTupleLiteral(tuple),
            EnumStructLiteralNode enumLiteral => GenerateEnumStructConstruction(enumLiteral),
            MethodCallExpressionNode { } enumCall when _semanticModel.IsEnumConstruction(enumCall) =>
                GenerateEnumConstruction(enumCall, enumCall.MethodName, enumCall.Arguments),
            MemberAccessNode enumUnit when _semanticModel.IsEnumConstruction(enumUnit) =>
                GenerateEnumConstruction(enumUnit, enumUnit.MemberName, []),
            StructLiteralNode structLiteral => GenerateStructLiteral(structLiteral),
            ArrayLiteralNode arrayLiteral => GenerateArrayLiteral(arrayLiteral),
            IndexExpressionNode indexExpression => GenerateIndexExpression(indexExpression),
            RangeIndexExpressionNode range => GenerateRangeIndex(range),
            FunctionCallExpressionNode functionCall => GenerateFunctionCall(functionCall),
            MethodCallExpressionNode methodCall => GenerateMethodCall(methodCall),
            MemberAccessNode member => GenerateMemberAccess(member),
            _ => throw new Exception("Unknown IR generation node target.")
        };
    }

    // A string value is a pointer to a NUL-terminated, UTF-8 byte array held in a
    // private constant global.
    private LLVMValueRef GenerateStringLiteral(StringLiteralNode literal)
    {
        if (_stringLiterals.TryGetValue(literal.Value, out var existing))
        {
            return existing;
        }

        var bytes = Encoding.UTF8.GetBytes(literal.Value);
        var elements = new LLVMValueRef[bytes.Length + 1];

        for (var i = 0; i < bytes.Length; i++)
        {
            elements[i] = LLVMValueRef.CreateConstInt(_context.Int8Type, bytes[i]);
        }

        elements[^1] = LLVMValueRef.CreateConstInt(_context.Int8Type, 0);

        var initializer = LLVMValueRef.CreateConstArray(_context.Int8Type, elements);
        var global = _module.AddGlobal(initializer.TypeOf, $".str.{_stringLiterals.Count}");
        global.Initializer = initializer;
        global.IsGlobalConstant = true;
        global.Linkage = LLVMLinkage.LLVMPrivateLinkage;
        global.UnnamedAddress = LLVMUnnamedAddr.LLVMGlobalUnnamedAddr;

        _stringLiterals[literal.Value] = global;
        return global;
    }

    private LLVMValueRef GenerateStructLiteral(StructLiteralNode literal)
    {
        var typeName = _semanticModel.GetExpressionType(literal);
        var mangledName = _semanticModel.GetMangledName(literal);
        // If it's a struct literal, the expression type might be the original name, but the layout is stored under mangled name.
        // Actually TypeChecker.CheckStructLiteral sets the expression type.
        // Let's try mangled name first, then typeName.
        if (!_structLayouts.TryGetValue(mangledName, out var structLayout) &&
            !_structLayouts.TryGetValue(typeName, out structLayout))
        {
            throw new Exception($"Codegen Error: Struct layout for '{typeName}' (mangled: '{mangledName}') not found.");
        }

        var alloca = _builder.BuildAlloca(structLayout.StructType, "struct_literal");
        EmitStructLiteralFields(literal, alloca, structLayout);
        return _builder.BuildLoad2(structLayout.StructType, alloca, "struct_literal_load");
    }

    private LLVMValueRef GenerateArrayLiteral(ArrayLiteralNode literal)
    {
        var typeName = _semanticModel.GetExpressionType(literal);
        var llvmType = ResolveType(typeName);
        var alloca = _builder.BuildAlloca(llvmType, "array_literal");

        EmitArrayLiteralElements(literal, alloca, llvmType);

        return _builder.BuildLoad2(llvmType, alloca, "array_literal_load");
    }

    private void EmitArrayLiteralElements(ArrayLiteralNode literal, LLVMValueRef arrayPtr, LLVMTypeRef arrayType)
    {
        var elementType = arrayType.ElementType;
        var literalType = _semanticModel.GetExpressionType(literal);

        // Elements are converted to the literal's element type (an array element given to `T[][N]`
        // becomes a slice).
        var elementLumiTypeName = SliceTypes.IsFixedArray(literalType) ? SliceTypes.ArrayElement(literalType) : "";
        for (var i = 0; i < literal.Elements.Count; i++)
        {
            var elementValue = GenerateExpression(literal.Elements[i]);
            var elementLumiType = _semanticModel.GetExpressionType(literal.Elements[i]);

            var indices = new[]
            {
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                LLVMValueRef.CreateConstInt(_context.Int32Type, (uint)i)
            };

            var elementPtr = _builder.BuildGEP2(arrayType, arrayPtr, indices, $"array_literal_idx_{i}");

            // Handle type mismatch (e.g., constant promotion)
            elementValue = CastInitializer(
                elementValue,
                literal.Elements[i],
                elementType,
                elementLumiTypeName,
                $"array_literal_init_{i}");

            _builder.BuildStore(elementValue, elementPtr);
        }
    }

    private LLVMValueRef GenerateCast(CastExpressionNode cast)
    {
        var value = GenerateExpression(cast.Operand);
        var sourceType = _semanticModel.GetExpressionType(cast.Operand);
        var targetType = _semanticModel.GetExpressionType(cast);

        // A string is just a pointer to NUL-terminated bytes, so *u8 <-> string changes nothing.
        if (targetType == TypeConst.String && PointerTypes.IsPointer(sourceType)
            || sourceType == TypeConst.String && PointerTypes.IsPointer(targetType))
        {
            return value;
        }

        if (targetType == TypeConst.String && SliceTypes.IsSlice(sourceType))
        {
            return SliceToString(value);
        }

        // Pointers are opaque, so pointer <-> pointer and identity casts need no instruction.
        if (sourceType == targetType || PointerTypes.IsPointer(targetType) && IsPointerOrNil(sourceType))
        {
            return value;
        }

        if (PointerTypes.IsPointer(sourceType))
        {
            return _builder.BuildPtrToInt(value, ResolveType(targetType), "ptrtoint");
        }

        if (PointerTypes.IsPointer(targetType))
        {
            return _builder.BuildIntToPtr(value, ResolveType(targetType), "inttoptr");
        }

        return _llvmTypeHelper.ConvertInteger(value, sourceType, targetType);
    }

    private LLVMValueRef GenerateDereference(DereferenceExpressionNode dereference)
    {
        var (pointer, _, pointeeType) = ResolveTargetAddress(dereference);
        return _builder.BuildLoad2(pointeeType, pointer, "deref");
    }

    private LLVMValueRef GenerateIndexExpression(IndexExpressionNode indexExpr)
    {
        var (ptr, _, elementType) = ResolveTargetAddress(indexExpr);
        return _builder.BuildLoad2(elementType, ptr, "index_load");
    }

    // Whether the expression names memory (a variable, field, element or dereference).
    private bool IsStoredLocation(ExpressionNode expression)
    {
        return expression switch
        {
            IdentifierNode or DereferenceExpressionNode or IndexExpressionNode => true,
            MemberAccessNode member => PointerTypes.IsPointer(_semanticModel.GetExpressionType(member.Target))
                                       || IsStoredLocation(member.Target),
            _ => false
        };
    }

    private LLVMValueRef GenerateMemberAccess(MemberAccessNode member)
    {
        // An element of a tuple that is not stored anywhere (pair().0) is extracted by value.
        var receiverType = _semanticModel.GetExpressionType(member.Target);
        if (member.MemberName == "len" && (SliceTypes.IsSlice(receiverType) || SliceTypes.IsFixedArray(receiverType)))
        {
            return GenerateLength(member, receiverType);
        }

        if (TupleTypes.IsTuple(receiverType) && !IsStoredLocation(member.Target))
        {
            return _builder.BuildExtractValue(
                GenerateExpression(member.Target),
                uint.Parse(member.MemberName),
                "tuple.element");
        }

        var (fieldPtr, _, fieldLlvmType) = ResolveTargetAddress(member);
        return _builder.BuildLoad2(fieldLlvmType, fieldPtr, $"load_{member.MemberName}");
    }

    // len(string) -> i32, lowered to strlen (which returns i64).
    private LLVMValueRef GenerateBuiltinLen(FunctionCallExpressionNode call)
    {
        var text = GenerateExpression(call.Arguments[0]);
        var length = WriterUtils.EmitStringLength(text, _module, _context, _builder);

        return _builder.BuildTrunc(length, _context.Int32Type, "len");
    }

    private LLVMValueRef GenerateFunctionCall(
        FunctionCallExpressionNode functionCall)
    {
        if (_semanticModel.IsBuiltinCall(functionCall))
        {
            return GenerateBuiltinLen(functionCall);
        }

        var mangledName = _semanticModel.GetMangledName(functionCall);
        if (!_functions.TryGetValue(mangledName, out var functionInfo))
        {
            throw new Exception(
                $"Codegen Error: Function '{functionCall.Name}' (mangled: '{mangledName}') was not declared.");
        }

        if (!_semanticModel.Functions.TryGet(mangledName, out var signature))
        {
            throw new Exception(
                $"Codegen Error: Function '{functionCall.Name}' (mangled: '{mangledName}') was not found in SemanticModel.");
        }

        var arguments = new LLVMValueRef[functionCall.Arguments.Count];
        for (var i = 0; i < functionCall.Arguments.Count; i++)
        {
            var argExpr = functionCall.Arguments[i];
            var argValue = GenerateExpression(argExpr);
            var targetTypeName = signature.ParameterTypes[i];
            var targetType = ResolveType(targetTypeName);

            arguments[i] = CastInitializer(
                argValue,
                argExpr,
                targetType,
                targetTypeName,
                $"arg_{i}");
        }

        var returnType = functionInfo.FunctionType.ReturnType;

        var name = returnType.Kind == LLVMTypeKind.LLVMVoidTypeKind
            ? string.Empty
            : "calltmp";

        return _builder.BuildCall2(
            functionInfo.FunctionType,
            functionInfo.Function,
            arguments,
            name);
    }

    // receiver.method(args) / Struct.function(args): a plain call to <Struct>__method with the
    // receiver, adjusted to the type of `self`, as the first argument.
    private LLVMValueRef GenerateMethodCall(MethodCallExpressionNode call)
    {
        if (_semanticModel.IsSliceGet(call))
        {
            return GenerateElementGet(call);
        }

        var mangledName = _semanticModel.GetMangledName(call);
        var functionInfo = _functions[mangledName];
        _semanticModel.Functions.TryGet(mangledName, out var signature);

        var receiver = _semanticModel.GetReceiver(call);
        var arguments = new List<LLVMValueRef>();

        switch (receiver)
        {
            case CallReceiver.Value:
                arguments.Add(GenerateExpression(call.Target));
                break;
            case CallReceiver.AddressOf:
                arguments.Add(ResolveTargetAddress(call.Target).Pointer);
                break;
            case CallReceiver.Dereference:
                var structType = signature!.ParameterTypes[0];
                arguments.Add(_builder.BuildLoad2(
                    ResolveType(structType),
                    GenerateExpression(call.Target),
                    "self"));
                break;
        }

        var offset = arguments.Count;
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var targetTypeName = signature!.ParameterTypes[i + offset];

            arguments.Add(CastInitializer(
                GenerateExpression(call.Arguments[i]),
                call.Arguments[i],
                ResolveType(targetTypeName),
                targetTypeName,
                $"arg_{i}"));
        }

        var name = functionInfo.FunctionType.ReturnType.Kind == LLVMTypeKind.LLVMVoidTypeKind
            ? string.Empty
            : "calltmp";

        return _builder.BuildCall2(
            functionInfo.FunctionType,
            functionInfo.Function,
            arguments.ToArray(),
            name);
    }

    private LLVMValueRef LoadVariable(string name)
    {
        var variable = _currentScope.ResolveVariable(name);

        return _builder.BuildLoad2(
            variable.LLVMType,
            variable.Allocation,
            name);
    }

    private void EmitStructLiteralFields(
        StructLiteralNode literal,
        LLVMValueRef structAlloca,
        StructLayoutInfo layout)
    {
        foreach (var init in literal.Initializers)
        {
            if (!layout.FieldsByName.TryGetValue(init.FieldName, out var field))
            {
                throw new InvalidOperationException(
                    $"Unknown field '{init.FieldName}' on struct '{layout.Name}'.");
            }

            var fieldValue = GenerateExpression(init.Value);
            fieldValue = CastInitializer(
                fieldValue,
                init.Value,
                field.LlvmType,
                field.LumiType,
                $"field_{layout.Name}_{field.Name}");

            var fieldPtr = _builder.BuildStructGEP2(
                layout.StructType,
                structAlloca,
                (uint)field.Index,
                $"{layout.Name}.{field.Name}.ptr");

            _builder.BuildStore(fieldValue, fieldPtr);
        }
    }

    private (LLVMValueRef Pointer, string LumiType, LLVMTypeRef LlvmType) ResolveTargetAddress(
        ExpressionNode target)
    {
        switch (target)
        {
            case IdentifierNode id:
            {
                var variable = _currentScope.ResolveVariable(id.Name);
                return (variable.Allocation, variable.TypeName, variable.LLVMType);
            }

            case MemberAccessNode member:
            {
                LLVMValueRef basePtr;
                string baseLumiType;

                // p.field on a *Struct: the pointer's value is the struct's address.
                var receiverType = _semanticModel.GetExpressionType(member.Target);
                if (PointerTypes.IsPointer(receiverType))
                {
                    basePtr = GenerateExpression(member.Target);
                    baseLumiType = PointerTypes.Pointee(receiverType);
                }
                else
                {
                    (basePtr, baseLumiType, _) = ResolveTargetAddress(member.Target);
                }

                if (TupleTypes.IsTuple(baseLumiType))
                {
                    var index = uint.Parse(member.MemberName);
                    var elementType = TupleTypes.Elements(baseLumiType)[(int)index];

                    return (
                        _builder.BuildStructGEP2(ResolveType(baseLumiType), basePtr, index, $"tuple.{index}.ptr"),
                        elementType,
                        ResolveType(elementType));
                }

                if (!_structLayouts.TryGetValue(baseLumiType, out var layout))
                {
                    throw new InvalidOperationException(
                        $"Type '{baseLumiType}' is not a struct.");
                }

                if (!layout.FieldsByName.TryGetValue(member.MemberName, out var field))
                {
                    throw new InvalidOperationException(
                        $"Struct '{baseLumiType}' has no field '{member.MemberName}'.");
                }

                var fieldPtr = _builder.BuildStructGEP2(
                    layout.StructType,
                    basePtr,
                    (uint)field.Index,
                    $"{baseLumiType}.{field.Name}.ptr");

                return (fieldPtr, field.LumiType, field.LlvmType);
            }

            case DereferenceExpressionNode dereference:
            {
                // The operand is a pointer value; it is the address of the pointee.
                var pointer = GenerateExpression(dereference.Operand);
                var pointeeTypeName = _semanticModel.GetExpressionType(dereference);

                return (pointer, pointeeTypeName, ResolveType(pointeeTypeName));
            }

            case IndexExpressionNode indexExpr
                when _semanticModel.GetExpressionType(indexExpr.Target) == TypeConst.String:
            {
                // A string is a pointer to bytes: load it, then index the bytes.
                var stringPtr = GenerateExpression(indexExpr.Target);
                var index = _llvmTypeHelper.ConvertInteger(
                    GenerateExpression(indexExpr.Index),
                    _semanticModel.GetExpressionType(indexExpr.Index),
                    TypeConst.I64);

                var bytePtr = _builder.BuildInBoundsGEP2(
                    _context.Int8Type,
                    stringPtr,
                    new[] { index },
                    "str_index_ptr");

                return (bytePtr, TypeConst.Char, _context.Int8Type);
            }

            case IndexExpressionNode indexExpr
                when PointerTypes.IsPointer(_semanticModel.GetExpressionType(indexExpr.Target)):
            {
                // p[i] on a *T: step i elements of T from the pointer's value.
                var pointer = GenerateExpression(indexExpr.Target);
                var elementTypeName = _semanticModel.GetExpressionType(indexExpr);
                var index = _llvmTypeHelper.ConvertInteger(
                    GenerateExpression(indexExpr.Index),
                    _semanticModel.GetExpressionType(indexExpr.Index),
                    TypeConst.I64);

                var elementType = ResolveType(elementTypeName);
                var elementPtr = _builder.BuildGEP2(elementType, pointer, new[] { index }, "ptr_index_ptr");

                return (elementPtr, elementTypeName, elementType);
            }

            case IndexExpressionNode indexExpr
                when SliceTypes.IsSlice(_semanticModel.GetExpressionType(indexExpr.Target)):
            {
                // s[i] on a slice: bounds-checked step from the view's element pointer.
                var slice = GenerateExpression(indexExpr.Target);
                var elementPointer = _builder.BuildExtractValue(slice, 0, "slice.ptr");
                var count = _builder.BuildExtractValue(slice, 1, "slice.len");
                var index = WidenIndex(
                    GenerateExpression(indexExpr.Index),
                    _semanticModel.GetExpressionType(indexExpr.Index));

                // Unsigned comparison also rejects a negative index.
                var inBounds = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULT, index, count, "slice.index.ok");
                EmitAbortUnless(inBounds, "slice.index");

                var elementTypeName = _semanticModel.GetExpressionType(indexExpr);
                var elementType = ResolveType(elementTypeName);
                var address = _builder.BuildGEP2(elementType, elementPointer, new[] { index }, "slice.index.ptr");

                return (address, elementTypeName, elementType);
            }

            case IndexExpressionNode indexExpr:
            {
                var (basePtr, baseLumiType, baseLlvmType) = ResolveTargetAddress(indexExpr.Target);

                var indexValue = GenerateExpression(indexExpr.Index);

                var indices = new[]
                {
                    LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                    indexValue
                };

                var elementPtr = _builder.BuildGEP2(
                    baseLlvmType,
                    basePtr,
                    indices,
                    "array_index_ptr");

                var elementType = baseLlvmType.ElementType;
                var elementLumiType = _semanticModel.GetExpressionType(indexExpr);

                return (elementPtr, elementLumiType, elementType);
            }

            default:
                throw new NotSupportedException(
                    $"Unsupported member/assignment target '{target.GetType().Name}'.");
        }
    }
}
