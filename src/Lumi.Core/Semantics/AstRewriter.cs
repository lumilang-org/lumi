using System.Collections;
using System.Reflection;
using Lumi.Core.Ast;
using Lumi.Core.Ast.Expressions;

namespace Lumi.Core.Semantics;

// Deep-copies or rewrites an AST subtree by walking its fields. Every string reachable from a
// node (type names, identifiers, operators) goes through `map`; literal nodes are left alone so
// string contents are never touched.
public static class AstRewriter
{
    public delegate string StringMap(ASTNode owner, string text);

    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static T Clone<T>(T node, StringMap map) where T : ASTNode
    {
        return (T)VisitNode(node, clone: true, map);
    }

    public static void Rewrite(ASTNode node, StringMap map)
    {
        VisitNode(node, clone: false, map);
    }

    public static void SetProperty(ASTNode node, string property, object? value)
    {
        var field = FindField(node.GetType(), $"<{property}>k__BackingField")
                    ?? throw new InvalidOperationException($"No property '{property}' on {node.GetType().Name}.");
        field.SetValue(node, value);
    }

    private static bool IsOpaque(ASTNode node)
    {
        return node is LiteralNode or StringLiteralNode;
    }

    private static ASTNode VisitNode(ASTNode node, bool clone, StringMap map)
    {
        var target = clone ? (ASTNode)MemberwiseCloneMethod.Invoke(node, null)! : node;

        if (IsOpaque(node))
        {
            return target;
        }

        for (var type = node.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                var value = field.GetValue(node);
                var visited = VisitValue(value, field.FieldType, clone, map, node);

                if (!ReferenceEquals(visited, value))
                {
                    field.SetValue(target, visited);
                }
            }
        }

        return target;
    }

    private static object? VisitValue(object? value, Type declaredType, bool clone, StringMap map, ASTNode owner)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return map(owner, text);
            case ASTNode child:
                return VisitNode(child, clone, map);
            case IEnumerable sequence when TryGetElementType(declaredType, value.GetType(), out var elementType):
            {
                var items = new List<object?>();
                foreach (var item in sequence)
                {
                    items.Add(VisitValue(item, elementType, clone, map, owner));
                }

                if (declaredType.IsArray)
                {
                    var array = Array.CreateInstance(elementType, items.Count);
                    for (var i = 0; i < items.Count; i++)
                    {
                        array.SetValue(items[i], i);
                    }

                    return array;
                }

                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                foreach (var item in items)
                {
                    list.Add(item);
                }

                return list;
            }
            default:
                return value;
        }
    }

    private static bool TryGetElementType(Type declaredType, Type runtimeType, out Type elementType)
    {
        elementType = typeof(object);

        if (runtimeType.IsArray)
        {
            elementType = runtimeType.GetElementType()!;
            return true;
        }

        foreach (var candidate in runtimeType.GetInterfaces().Append(runtimeType))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = candidate.GetGenericArguments()[0];

                // Only collections that a List<T> can stand in for are rebuilt.
                return declaredType.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType)) ||
                       declaredType.IsArray;
            }
        }

        return false;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var field = t.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }
}
