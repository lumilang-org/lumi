using Lumi.Core.Diagnostics;

namespace Lumi.Core.Ast.Expressions;

/// <summary>The <c>nil</c> literal: a null pointer, assignable to any pointer type.</summary>
public sealed class NilLiteralNode(SourceSpan span) : ExpressionNode(span);
