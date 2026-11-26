using LLVMSharp.Interop;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;
using Lumi.Core.Semantics;

namespace Lumi.LLVM.CodeGenerator;

public sealed partial class Codegen
{
    // { ptr, i64 }: element pointer at field 0, element count at field 1.
    private LLVMTypeRef SliceStructType()
    {
        return _context.GetStructType(
            [LLVMTypeRef.CreatePointer(_context.Int8Type, 0), _context.Int64Type], false);
    }

    private LLVMValueRef MakeSlice(LLVMValueRef pointer, LLVMValueRef length)
    {
        var slice = LLVMValueRef.CreateConstNull(SliceStructType());
        slice = _builder.BuildInsertValue(slice, pointer, 0, "slice.ptr");
        return _builder.BuildInsertValue(slice, length, 1, "slice.len");
    }

    // Address of an array held by `expression`: the storage itself when the expression names
    // one, otherwise a temporary copy.
    private LLVMValueRef ArrayAddress(LLVMValueRef value, ExpressionNode expression)
    {
        if (expression is IdentifierNode or MemberAccessNode or IndexExpressionNode or DereferenceExpressionNode)
        {
            return ResolveTargetAddress(expression).Pointer;
        }

        var temporary = _builder.BuildAlloca(value.TypeOf, "slice.temp");
        _builder.BuildStore(value, temporary);
        return temporary;
    }

    private LLVMValueRef ArrayToSlice(LLVMValueRef arrayValue, ExpressionNode expression)
    {
        var arrayType = _semanticModel.GetExpressionType(expression);
        var length = LLVMValueRef.CreateConstInt(_context.Int64Type, (ulong)SliceTypes.ArrayLength(arrayType));
        return MakeSlice(ArrayAddress(arrayValue, expression), length);
    }

    // `.len` of a slice or fixed array, as i64.
    private LLVMValueRef GenerateLength(MemberAccessNode member, string receiverType)
    {
        if (SliceTypes.IsSlice(receiverType))
        {
            return _builder.BuildExtractValue(GenerateExpression(member.Target), 1, "slice.len");
        }

        // Evaluate a target with side effects (a call); a plain variable needs no load.
        if (member.Target is not (IdentifierNode or MemberAccessNode or IndexExpressionNode))
        {
            GenerateExpression(member.Target);
        }

        return LLVMValueRef.CreateConstInt(_context.Int64Type, (ulong)SliceTypes.ArrayLength(receiverType));
    }

    // `.get(i)`: Option<T>.Some(element) when 0 <= i < len, otherwise Option<T>.None.
    private LLVMValueRef GenerateElementGet(MethodCallExpressionNode call)
    {
        var receiverType = _semanticModel.GetExpressionType(call.Target);
        var optionName = _semanticModel.GetMangledName(call);
        var layout = _enumLayouts[optionName];

        LLVMValueRef elements;
        LLVMValueRef length;
        string elementTypeName;

        var receiver = GenerateExpression(call.Target);

        if (SliceTypes.IsSlice(receiverType))
        {
            elementTypeName = SliceTypes.Element(receiverType);
            elements = _builder.BuildExtractValue(receiver, 0, "get.ptr");
            length = _builder.BuildExtractValue(receiver, 1, "get.len");
        }
        else
        {
            elementTypeName = SliceTypes.ArrayElement(receiverType);
            elements = ArrayAddress(receiver, call.Target);
            length = LLVMValueRef.CreateConstInt(_context.Int64Type, (ulong)SliceTypes.ArrayLength(receiverType));
        }

        var elementType = ResolveType(elementTypeName);
        var index = GenerateExpression(call.Arguments[0]);
        var indexTypeName = _semanticModel.GetExpressionType(call.Arguments[0]);
        index = WidenIndex(index, indexTypeName);

        // One unsigned comparison covers both a negative index and one past the end.
        var inBounds = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULT, index, length, "get.inbounds");

        var slot = _builder.BuildAlloca(layout.Type, "get.option");
        var function = _builder.InsertBlock.Parent;
        var someBlock = function.AppendBasicBlock("get.some");
        var noneBlock = function.AppendBasicBlock("get.none");
        var endBlock = function.AppendBasicBlock("get.end");
        _builder.BuildCondBr(inBounds, someBlock, noneBlock);

        _builder.PositionAtEnd(someBlock);
        var some = layout.Variants["Some"];
        _builder.BuildStore(
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)some.Tag),
            _builder.BuildStructGEP2(layout.Type, slot, 0, "get.tag"));
        var elementPointer = _builder.BuildGEP2(elementType, elements, new[] { index }, "get.element.ptr");
        var element = _builder.BuildLoad2(elementType, elementPointer, "get.element");
        var payload = _builder.BuildStructGEP2(layout.Type, slot, 1, "get.payload");
        _builder.BuildStore(element, _builder.BuildStructGEP2(some.PayloadType, payload, 0, "get.field"));
        _builder.BuildBr(endBlock);

        _builder.PositionAtEnd(noneBlock);
        _builder.BuildStore(
            LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)layout.Variants["None"].Tag),
            _builder.BuildStructGEP2(layout.Type, slot, 0, "get.tag"));
        _builder.BuildBr(endBlock);

        _builder.PositionAtEnd(endBlock);
        return _builder.BuildLoad2(layout.Type, slot, "get.value");
    }

    // `s[start..end]`: bounds-checked view of elements start..end of a slice or array. An invalid
    // range terminates the program, like an invalid shift amount.
    private LLVMValueRef GenerateRangeIndex(RangeIndexExpressionNode range)
    {
        var receiverType = _semanticModel.GetExpressionType(range.Target);
        var receiver = GenerateExpression(range.Target);

        LLVMValueRef elements;
        LLVMValueRef length;
        string elementTypeName;

        if (SliceTypes.IsSlice(receiverType))
        {
            elementTypeName = SliceTypes.Element(receiverType);
            elements = _builder.BuildExtractValue(receiver, 0, "range.ptr");
            length = _builder.BuildExtractValue(receiver, 1, "range.len");
        }
        else
        {
            elementTypeName = SliceTypes.ArrayElement(receiverType);
            elements = ArrayAddress(receiver, range.Target);
            length = LLVMValueRef.CreateConstInt(_context.Int64Type, (ulong)SliceTypes.ArrayLength(receiverType));
        }

        var start = range.Start is null
            ? LLVMValueRef.CreateConstInt(_context.Int64Type, 0)
            : WidenIndex(GenerateExpression(range.Start), _semanticModel.GetExpressionType(range.Start));
        var end = range.End is null
            ? length
            : WidenIndex(GenerateExpression(range.End), _semanticModel.GetExpressionType(range.End));

        // Unsigned comparisons reject a negative bound as a huge value: end <= len and start <= end
        // together mean 0 <= start <= end <= len.
        var endInBounds = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULE, end, length, "range.end.ok");
        var startBeforeEnd = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULE, start, end, "range.start.ok");
        var valid = _builder.BuildAnd(endInBounds, startBeforeEnd, "range.valid");

        EmitAbortUnless(valid, "range");

        var elementType = ResolveType(elementTypeName);
        var subPointer = _builder.BuildGEP2(elementType, elements, new[] { start }, "range.sub.ptr");
        var subLength = _builder.BuildSub(end, start, "range.sub.len");
        return MakeSlice(subPointer, subLength);
    }

    // Continues in a fresh block when `condition` holds; otherwise terminates the program.
    private void EmitAbortUnless(LLVMValueRef condition, string name)
    {
        var function = _builder.InsertBlock.Parent;
        var okBlock = function.AppendBasicBlock($"{name}.ok");
        var failBlock = function.AppendBasicBlock($"{name}.fail");
        _builder.BuildCondBr(condition, okBlock, failBlock);

        _builder.PositionAtEnd(failBlock);
        EmitAbort();

        _builder.PositionAtEnd(okBlock);
    }

    private void EmitAbort()
    {
        var abortType = LLVMTypeRef.CreateFunction(_context.VoidType, []);
        var abort = _module.GetNamedFunction("abort");
        if (abort.Handle == IntPtr.Zero)
        {
            abort = _module.AddFunction("abort", abortType);
        }

        _builder.BuildCall2(abortType, abort, []);
        _builder.BuildUnreachable();
    }

    private LLVMValueRef WidenIndex(LLVMValueRef index, string indexTypeName)
    {
        if (index.TypeOf.IntWidth >= 64)
        {
            return index;
        }

        return indexTypeName.StartsWith('u')
            ? _builder.BuildZExt(index, _context.Int64Type, "get.index")
            : _builder.BuildSExt(index, _context.Int64Type, "get.index");
    }
}
