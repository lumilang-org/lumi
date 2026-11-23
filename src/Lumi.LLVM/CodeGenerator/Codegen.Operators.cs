using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Semantics;
using Lumi.Core.Syntax;
using Lumi.LLVM.Utils;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
    // ++/--: resolve the address once, load, add/subtract 1, store back. A prefix update
    // yields the new value and a postfix update the previous one; statements ignore it.
    private LLVMValueRef GenerateUpdate(UpdateExpressionNode update)
    {
        var (address, _, valueType) = ResolveTargetAddress(update.Target);

        var current = _builder.BuildLoad2(valueType, address, "update.old");
        var one = LLVMValueRef.CreateConstInt(valueType, 1);

        var updated = update.Operator == TypeConst.Increment
            ? _builder.BuildAdd(current, one, "update.inc")
            : _builder.BuildSub(current, one, "update.dec");

        _builder.BuildStore(updated, address);

        return update.IsPrefix ? updated : current;
    }

    private LLVMValueRef GenerateUnary(UnaryExpressionNode unary)
    {
        var operand = GenerateExpression(unary.Operand);

        return unary.Operator switch
        {
            "-" when operand.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind =>
                _builder.BuildNeg(operand, "negtmp"),

            "-" when operand.TypeOf.Kind is
                    LLVMTypeKind.LLVMHalfTypeKind
                    or LLVMTypeKind.LLVMFloatTypeKind
                    or LLVMTypeKind.LLVMDoubleTypeKind =>
                _builder.BuildFNeg(operand, "fnegtmp"),

            "!" when operand.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind =>
                _builder.BuildNot(operand, "logical.not"),

            "~" when operand.TypeOf.Kind == LLVMTypeKind.LLVMIntegerTypeKind =>
                _builder.BuildNot(operand, "bitwise.not"),

            "-" => throw new NotSupportedException(
                "Unary '-' requires a numeric operand."),

            _ => throw new NotSupportedException(
                $"Unsupported unary operator: '{unary.Operator}'.")
        };
    }

    private LLVMValueRef GenerateBinary(BinaryExpressionNode binary)
    {
        if (binary.Operator is TypeConst.LogicalAnd or TypeConst.LogicalOr)
        {
            return GenerateLogicalBinary(binary);
        }

        var left = GenerateExpression(binary.Left);
        var right = GenerateExpression(binary.Right);

        if (TypeChecker.IsShiftOperator(binary.Operator))
        {
            return GenerateShift(binary, left, right);
        }

        if (_semanticModel.GetExpressionType(binary.Left) == TypeConst.String
            && _semanticModel.GetExpressionType(binary.Right) == TypeConst.String
            && binary.Operator is TypeConst.EqualsEquals or TypeConst.BangEquals)
        {
            var comparison = WriterUtils.EmitStringCompare(left, right, _module, _context, _builder);

            return _builder.BuildICmp(
                binary.Operator == TypeConst.EqualsEquals
                    ? LLVMIntPredicate.LLVMIntEQ
                    : LLVMIntPredicate.LLVMIntNE,
                comparison,
                LLVMValueRef.CreateConstInt(_context.Int32Type, 0),
                "str.cmptmp");
        }

        if (IsPointerOrNil(_semanticModel.GetExpressionType(binary.Left))
            && IsPointerOrNil(_semanticModel.GetExpressionType(binary.Right))
            && binary.Operator is TypeConst.EqualsEquals or TypeConst.BangEquals)
        {
            return _builder.BuildICmp(
                binary.Operator == TypeConst.EqualsEquals
                    ? LLVMIntPredicate.LLVMIntEQ
                    : LLVMIntPredicate.LLVMIntNE,
                left,
                right,
                "ptr.cmptmp");
        }

        if (binary.Operator is TypeConst.Plus or TypeConst.Minus
            && (PointerTypes.IsPointer(_semanticModel.GetExpressionType(binary.Left))
                || PointerTypes.IsPointer(_semanticModel.GetExpressionType(binary.Right))))
        {
            return GeneratePointerOffset(binary, left, right);
        }

        if (left.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind
            || right.TypeOf.Kind == LLVMTypeKind.LLVMPointerTypeKind)
        {
            throw new NotSupportedException(
                "Pointer arithmetic is not supported.");
        }

        var leftType = _semanticModel.GetExpressionType(binary.Left);
        var rightType = _semanticModel.GetExpressionType(binary.Right);

        // Characters are bytes; the type checker only allows (in)equality on them.
        if (leftType == TypeConst.Char && rightType == TypeConst.Char
            && binary.Operator is TypeConst.EqualsEquals or TypeConst.BangEquals)
        {
            return _builder.BuildICmp(
                binary.Operator == TypeConst.EqualsEquals
                    ? LLVMIntPredicate.LLVMIntEQ
                    : LLVMIntPredicate.LLVMIntNE,
                left,
                right,
                "char.cmptmp");
        }

        if (leftType == TypeConst.Bool && rightType == TypeConst.Bool)
        {
            if (binary.Operator is TypeConst.EqualsEquals or TypeConst.BangEquals)
            {
                var predicate = binary.Operator switch
                {
                    TypeConst.EqualsEquals => LLVMIntPredicate.LLVMIntEQ,
                    TypeConst.BangEquals => LLVMIntPredicate.LLVMIntNE,

                    _ => throw new NotSupportedException(
                        $"Unsupported boolean operator: {binary.Operator}")
                };

                return _builder.BuildICmp(
                    predicate,
                    left,
                    right,
                    "bool.cmptmp");
            }

            throw new NotSupportedException(
                $"Operator '{binary.Operator}' is not supported for boolean operands.");
        }

        if (IsFloatingPoint(left.TypeOf.Kind)
            || IsFloatingPoint(right.TypeOf.Kind))
        {
            if (!IsFloatingPoint(left.TypeOf.Kind)
                || !IsFloatingPoint(right.TypeOf.Kind)
                || left.TypeOf != right.TypeOf)
            {
                throw CreateIncompatibleOperandsException(
                    leftType,
                    rightType);
            }

            if (IsComparisonOperator(binary.Operator))
            {
                var predicate = binary.Operator switch
                {
                    TypeConst.EqualsEquals =>
                        LLVMRealPredicate.LLVMRealOEQ,

                    TypeConst.BangEquals =>
                        LLVMRealPredicate.LLVMRealUNE,

                    TypeConst.LessThan =>
                        LLVMRealPredicate.LLVMRealOLT,

                    TypeConst.LessThanOrEquals =>
                        LLVMRealPredicate.LLVMRealOLE,

                    TypeConst.GreaterThan =>
                        LLVMRealPredicate.LLVMRealOGT,

                    TypeConst.GreaterThanOrEquals =>
                        LLVMRealPredicate.LLVMRealOGE,

                    _ => throw new NotSupportedException(
                        $"Unsupported floating-point comparison: {binary.Operator}")
                };

                return _builder.BuildFCmp(
                    predicate,
                    left,
                    right,
                    "fcmp");
            }

            return binary.Operator switch
            {
                TypeConst.Plus =>
                    _builder.BuildFAdd(left, right, "faddtmp"),

                TypeConst.Minus =>
                    _builder.BuildFSub(left, right, "fsubtmp"),

                TypeConst.Star =>
                    _builder.BuildFMul(left, right, "fmultmp"),

                TypeConst.Slash =>
                    _builder.BuildFDiv(left, right, "fdivtmp"),

                _ => throw new NotSupportedException(
                    $"Unsupported floating-point operator: {binary.Operator}")
            };
        }

        if (left.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind
            || right.TypeOf.Kind != LLVMTypeKind.LLVMIntegerTypeKind)
        {
            throw new NotSupportedException(
                $"Binary operator '{binary.Operator}' requires numeric operands.");
        }

        var promoted = PromoteIntegerOperands(
            left,
            right,
            leftType,
            rightType);

        left = promoted.Left;
        right = promoted.Right;

        var commonType = promoted.CommonType;

        if (IsComparisonOperator(binary.Operator))
        {
            var predicate = binary.Operator switch
            {
                TypeConst.EqualsEquals =>
                    LLVMIntPredicate.LLVMIntEQ,

                TypeConst.BangEquals =>
                    LLVMIntPredicate.LLVMIntNE,

                TypeConst.LessThan =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntULT
                        : LLVMIntPredicate.LLVMIntSLT,

                TypeConst.LessThanOrEquals =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntULE
                        : LLVMIntPredicate.LLVMIntSLE,

                TypeConst.GreaterThan =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntUGT
                        : LLVMIntPredicate.LLVMIntSGT,

                TypeConst.GreaterThanOrEquals =>
                    TypeChecker.IsUnsignedInteger(commonType)
                        ? LLVMIntPredicate.LLVMIntUGE
                        : LLVMIntPredicate.LLVMIntSGE,

                _ => throw new NotSupportedException(
                    $"Unsupported integer comparison: {binary.Operator}")
            };

            return _builder.BuildICmp(
                predicate,
                left,
                right,
                "cmptmp");
        }

        return binary.Operator switch
        {
            TypeConst.Plus =>
                _builder.BuildAdd(left, right, "addtmp"),

            TypeConst.Minus =>
                _builder.BuildSub(left, right, "subtmp"),

            TypeConst.Star =>
                _builder.BuildMul(left, right, "multmp"),

            // Bitwise operators
            TypeConst.Ampersand => _builder.BuildAnd(left, right, "bitwise.and"),
            TypeConst.Pipe => _builder.BuildOr(left, right, "bitwise.or"),
            TypeConst.Caret => _builder.BuildXor(left, right, "bitwise.xor"),

            TypeConst.ShiftLeft => _builder.BuildShl(left, right, "shift.left"),
            TypeConst.ShiftRight when IsUnsignedIntegerType(binary.Left) =>
                _builder.BuildLShr(left, right, "shift.right"),

            TypeConst.ShiftRight => _builder.BuildAShr(left, right, "shift.right"),

            TypeConst.Slash =>
                TypeChecker.IsUnsignedInteger(commonType)
                    ? _builder.BuildUDiv(left, right, "divtmp")
                    : _builder.BuildSDiv(left, right, "divtmp"),

            _ => throw new NotSupportedException(
                $"Unsupported integer operator: {binary.Operator}")
        };
    }

    private static bool IsPointerOrNil(string type)
    {
        return type == TypeConst.Nil || PointerTypes.IsPointer(type);
    }

    // ptr + n, n + ptr, ptr - n: advance by n elements of the pointee type.
    private LLVMValueRef GeneratePointerOffset(
        BinaryExpressionNode binary,
        LLVMValueRef left,
        LLVMValueRef right)
    {
        var leftType = _semanticModel.GetExpressionType(binary.Left);
        var rightType = _semanticModel.GetExpressionType(binary.Right);
        var pointerOnLeft = PointerTypes.IsPointer(leftType);

        var pointer = pointerOnLeft ? left : right;
        var pointerType = pointerOnLeft ? leftType : rightType;
        var offset = pointerOnLeft ? right : left;
        var offsetType = pointerOnLeft ? rightType : leftType;

        // Signed offsets are sign-extended and unsigned ones zero-extended to 64 bits.
        var index = _llvmTypeHelper.ConvertInteger(offset, offsetType, TypeConst.I64);

        if (binary.Operator == TypeConst.Minus)
        {
            index = _builder.BuildNeg(index, "ptr.negoffset");
        }

        return _builder.BuildGEP2(
            ResolveType(PointerTypes.Pointee(pointerType)),
            pointer,
            new[] { index },
            "ptr.offset");
    }

    private bool IsUnsignedIntegerType(ExpressionNode expression)
    {
        var type = _semanticModel.GetExpressionType(expression);
        return TypeChecker.IsUnsignedInteger(type);
    }

    private LLVMValueRef GenerateLogicalBinary(BinaryExpressionNode binary)
    {
        var isAnd = binary.Operator == TypeConst.LogicalAnd;

        var left = GenerateExpression(binary.Left);
        var leftEndBlock = _builder.InsertBlock;

        var function = leftEndBlock.Parent;

        var rightBlock = _context.AppendBasicBlock(
            function,
            isAnd ? "and.rhs" : "or.rhs");

        var mergeBlock = _context.AppendBasicBlock(
            function,
            isAnd ? "and.end" : "or.end");

        if (isAnd)
        {
            _builder.BuildCondBr(left, rightBlock, mergeBlock);
        }
        else
        {
            _builder.BuildCondBr(left, mergeBlock, rightBlock);
        }

        _builder.PositionAtEnd(rightBlock);

        var right = GenerateExpression(binary.Right);
        var rightEndBlock = _builder.InsertBlock;

        if (!HasTerminator(rightEndBlock))
        {
            _builder.BuildBr(mergeBlock);
        }

        _builder.PositionAtEnd(mergeBlock);

        var shortCircuitValue = LLVMValueRef.CreateConstInt(
            _context.Int1Type,
            isAnd ? 0UL : 1UL);

        var phi = _builder.BuildPhi(_context.Int1Type, "logical.result");

        phi.AddIncoming(
            [shortCircuitValue, right],
            [leftEndBlock, rightEndBlock],
            2);

        return phi;
    }

    private (LLVMValueRef Left, LLVMValueRef Right, string CommonType) PromoteIntegerOperands(
        LLVMValueRef left,
        LLVMValueRef right,
        string leftType,
        string rightType)
    {
        var commonType = TypePromoter.GetCommonType(
            leftType,
            rightType);

        if (commonType is null)
        {
            throw CreateIncompatibleOperandsException(
                leftType,
                rightType);
        }

        left = _llvmTypeHelper.ConvertInteger(
            left,
            leftType,
            commonType);

        right = _llvmTypeHelper.ConvertInteger(
            right,
            rightType,
            commonType);

        return (left, right, commonType);
    }

    private static bool IsFloatingPoint(LLVMTypeKind kind)
    {
        return kind is LLVMTypeKind.LLVMHalfTypeKind
            or LLVMTypeKind.LLVMFloatTypeKind
            or LLVMTypeKind.LLVMDoubleTypeKind;
    }

    private static bool IsComparisonOperator(string op)
    {
        return op is TypeConst.EqualsEquals
            or TypeConst.BangEquals
            or TypeConst.LessThan
            or TypeConst.LessThanOrEquals
            or TypeConst.GreaterThan
            or TypeConst.GreaterThanOrEquals;
    }

    private static InvalidOperationException CreateIncompatibleOperandsException(
        string leftType,
        string rightType)
    {
        return new InvalidOperationException(
            $"Codegen received incompatible operand types: " +
            $"'{leftType}' and '{rightType}'.");
    }

    private LLVMValueRef GenerateShift(
        BinaryExpressionNode binary,
        LLVMValueRef left,
        LLVMValueRef right)
    {
        var leftType = _semanticModel.GetExpressionType(binary.Left);
        var rightType = _semanticModel.GetExpressionType(binary.Right);

        var bitWidth = left.TypeOf.IntWidth;
        var isUnsignedShiftAmount =
            TypeChecker.IsUnsignedInteger(rightType);

        var function = _builder.InsertBlock.Parent;

        var validBlock = _context.AppendBasicBlock(function, "shift.valid");
        var invalidBlock = _context.AppendBasicBlock(function, "shift.invalid");

        // Reject negative shift amounts for signed integers.
        LLVMValueRef isValid;

        if (isUnsignedShiftAmount)
        {
            isValid = _builder.BuildICmp(
                LLVMIntPredicate.LLVMIntULT,
                right,
                LLVMValueRef.CreateConstInt(right.TypeOf, bitWidth),
                "shift.amount.valid");
        }
        else
        {
            var isNonNegative = _builder.BuildICmp(
                LLVMIntPredicate.LLVMIntSGE,
                right,
                LLVMValueRef.CreateConstInt(right.TypeOf, 0),
                "shift.amount.nonnegative");

            var isBelowWidth = _builder.BuildICmp(
                LLVMIntPredicate.LLVMIntULT,
                right,
                LLVMValueRef.CreateConstInt(right.TypeOf, bitWidth),
                "shift.amount.below.width");

            isValid = _builder.BuildAnd(
                isNonNegative,
                isBelowWidth,
                "shift.amount.valid");
        }

        _builder.BuildCondBr(isValid, validBlock, invalidBlock);

        // Invalid shifts terminate the program.
        _builder.PositionAtEnd(invalidBlock);
        _builder.BuildCall2(
            LLVMTypeRef.CreateFunction(_context.VoidType, []),
            _module.AddFunction("abort", LLVMTypeRef.CreateFunction(_context.VoidType, [])),
            []);

        _builder.BuildUnreachable();

        // Generate the actual shift only on the valid path.
        _builder.PositionAtEnd(validBlock);

        return binary.Operator switch
        {
            TypeConst.ShiftLeft =>
                _builder.BuildShl(left, right, "shift.left"),

            TypeConst.ShiftRight when IsUnsignedIntegerType(binary.Left) =>
                _builder.BuildLShr(left, right, "shift.right"),

            TypeConst.ShiftRight =>
                _builder.BuildAShr(left, right, "shift.right"),

            _ => throw new NotSupportedException(
                $"Unsupported shift operator: '{binary.Operator}'.")
        };
    }
}
