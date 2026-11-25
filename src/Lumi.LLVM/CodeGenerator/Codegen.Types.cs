using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Declarations;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.Structs;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
    private LLVMTypeRef ResolveType(string typeName)
    {
        if (typeName.Contains('[') && typeName.EndsWith(']'))
        {
            var lastBracket = typeName.LastIndexOf('[');
            var elementTypeName = typeName.Substring(0, lastBracket);
            var lengthStr = typeName.Substring(lastBracket + 1, typeName.Length - lastBracket - 2);
            var length = uint.Parse(lengthStr);
            var elementType = ResolveType(elementTypeName);
            return LLVMTypeRef.CreateArray(elementType, length);
        }

        // Pointers are opaque; the pointee is tracked through Lumi type names. Not resolving
        // the pointee here also keeps self-referential structs (*Node inside Node) valid.
        if (PointerTypes.IsPointer(typeName))
        {
            return LLVMTypeRef.CreatePointer(_context.Int8Type, 0);
        }

        if (_enumLayouts.TryGetValue(typeName, out var enumLayout))
        {
            return enumLayout.Type;
        }

        if (_structLayouts.TryGetValue(typeName, out var structLayout))
        {
            return structLayout.StructType;
        }

        // Try mangled name
        var mangled = NameMangler.Qualified(typeName);
        if (_structLayouts.TryGetValue(mangled, out structLayout))
        {
            return structLayout.StructType;
        }

        if (typeName == TypeConst.Usize)
        {
            return _usizeType;
        }

        return typeName switch
        {
            TypeConst.I8 or TypeConst.U8 => _context.Int8Type,
            TypeConst.I16 or TypeConst.U16 => _context.Int16Type,
            TypeConst.I32 or TypeConst.U32 => _context.Int32Type,
            TypeConst.I64 or TypeConst.U64 => _context.Int64Type,

            TypeConst.Char => _context.Int8Type,
            TypeConst.String => LLVMTypeRef.CreatePointer(_context.Int8Type, 0),
            TypeConst.Void => _context.VoidType,
            TypeConst.Bool => _context.Int1Type,

            TypeConst.F16 => _context.HalfType,
            TypeConst.F32 => _context.FloatType,
            TypeConst.F64 => _context.DoubleType,

            _ => throw new NotSupportedException(
                $"Unsupported backend data type: {typeName}")
        };
    }

    private LLVMValueRef CastInteger(
        LLVMValueRef value,
        LLVMTypeRef targetType,
        string name)
    {
        var sourceType = value.TypeOf;

        if (sourceType.Kind != LLVMTypeKind.LLVMIntegerTypeKind ||
            targetType.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (sourceType == targetType)
            {
                return value;
            }

            throw new NotSupportedException(
                $"Cannot convert {sourceType} to {targetType}.");
        }

        if (sourceType.IntWidth == targetType.IntWidth)
        {
            return value;
        }

        if (sourceType.IntWidth < targetType.IntWidth)
        {
            return _builder.BuildSExt(
                value,
                targetType,
                name);
        }

        return _builder.BuildTrunc(
            value,
            targetType,
            name);
    }

    private LLVMValueRef CastInitializer(
        LLVMValueRef value,
        ExpressionNode expression,
        LLVMTypeRef targetType,
        string targetTypeName,
        string name)
    {
        var sourceType = value.TypeOf;

        if (sourceType.Kind != LLVMTypeKind.LLVMIntegerTypeKind ||
            targetType.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (sourceType == targetType)
            {
                return value;
            }

            throw new NotSupportedException(
                $"Cannot convert initializer to '{targetTypeName}'.");
        }

        var sourceWidth = sourceType.IntWidth;
        var targetWidth = targetType.IntWidth;

        if (sourceWidth == targetWidth)
        {
            return value;
        }

        if (sourceWidth > targetWidth)
        {
            return _builder.BuildTrunc(value, targetType, name);
        }

        // Integer literals currently represent non-negative values.
        // Their magnitude is preserved by zero extension.
        if (expression is LiteralNode<string>)
        {
            return _builder.BuildZExt(value, targetType, name);
        }

        // For variables, preserve their declared signedness.

        if (expression is IdentifierNode identifier)
        {
            var variable = _currentScope.ResolveVariable(identifier.Name);

            return variable.IsUnsignedIntegerType()
                ? _builder.BuildZExt(value, targetType, name)
                : _builder.BuildSExt(value, targetType, name);
        }

        // Fallback for integer expressions.
        return _builder.BuildSExt(value, targetType, name);
    }

    private void RegisterStructs(ProgramNode program)
    {
        var namedTypes = new Dictionary<string, LLVMTypeRef>();

        foreach (var structNode in program.Structs)
        {
            var mangledName = _semanticModel.GetMangledName(structNode);
            var llvmStruct = _context.CreateNamedStruct(mangledName);
            namedTypes[mangledName] = llvmStruct;
        }

        // Store partial/opaque types first so nested struct fields can resolve them
        foreach (var structNode in program.Structs)
        {
            var mangledName = _semanticModel.GetMangledName(structNode);
            var fieldsByName = new Dictionary<string, StructFieldLayout>();
            var orderedFields = new List<StructFieldLayout>();

            for (var i = 0; i < structNode.Fields.Count; i++)
            {
                var field = structNode.Fields[i];
                var layout = new StructFieldLayout(i, field.Name, field.Type, default);
                orderedFields.Add(layout);
                fieldsByName[field.Name] = layout;
            }

            _structLayouts[mangledName] = new StructLayoutInfo(
                mangledName,
                namedTypes[mangledName],
                orderedFields,
                fieldsByName);
        }

        // Set struct bodies now that every struct type exists
        foreach (var structNode in program.Structs)
        {
            var mangledName = _semanticModel.GetMangledName(structNode);
            var info = _structLayouts[mangledName];
            var fieldLlvmTypes = new LLVMTypeRef[structNode.Fields.Count];
            var updatedOrdered = new List<StructFieldLayout>(structNode.Fields.Count);
            var updatedByName = new Dictionary<string, StructFieldLayout>(structNode.Fields.Count);

            for (var i = 0; i < structNode.Fields.Count; i++)
            {
                var field = structNode.Fields[i];
                var fieldLlvmType = ResolveType(field.Type);
                fieldLlvmTypes[i] = fieldLlvmType;

                var layout = new StructFieldLayout(i, field.Name, field.Type, fieldLlvmType);
                updatedOrdered.Add(layout);
                updatedByName[field.Name] = layout;
            }

            info.StructType.StructSetBody(fieldLlvmTypes, false);

            _structLayouts[mangledName] = new StructLayoutInfo(
                mangledName,
                info.StructType,
                updatedOrdered,
                updatedByName);
        }
    }
}
