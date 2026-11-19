# Lumi

A statically typed systems programming language and native compiler built in C# and .NET on top of **LLVM** (`LLVMSharp.Interop`), focused on explicit types, stack-based values, and a clean, readable compiler pipeline.

## v0.1 Features

- **Primitive Types:** Fixed-width signed integers (`i8`, `i16`, `i32`, `i64`), unsigned integers (`u8`, `u16`, `u32`, `u64`), floating-point types (`f16`, `f32`, `f64`), `bool`, `char`, and `void`.
- **Structs:** Top-level `struct` declarations, struct literals (`Vec { x: 10, y: 20 }`), field access (`.`), and field mutation (`p.x = ...`).
- **Functions & Control Flow:** Typed functions (`func name(params) -> ReturnType`), `if` / `else`, `while`, `for`, `break`, `continue`, and `return`.
- **Diagnostics & Built-ins:** Source-span diagnostics (`DiagnosticBag`) and `print` / `println` statements.

## Build And Run

```bash
dotnet build
dotnet run --project Lumi.Cli -- path/to/program.lumi
```

## Example

```text
struct Vec {
    u32 x
    u32 y
}

func main() -> void {
    Vec p = Vec { x: 10, y: 20 }
    p.x = p.x + 5
    println(p.x + p.y)
}
```

## Compiler Pipeline

1. **Lexer** (`Lumi.Core.Syntax`) — Converts source text into tokens with line/column spans and reports lexical errors.
2. **Parser** (`Lumi.Core.Syntax`) — Builds the AST (`ProgramNode`, `StructDeclarationNode`, `FunctionDeclarationNode`, statements, and expressions) with top-level error recovery.
3. **Semantic Analysis** (`Lumi.Core.Semantics`) — Registers functions and structs, manages lexical scopes (`SymbolTable`), checks types and literal bounds (`TypeChecker`), and populates `SemanticModel`.
4. **LLVM Codegen** (`Lumi.LLVM.CodeGenerator`) — Lowers AST nodes and `SemanticModel` types into LLVM IR via `LLVMSharp.Interop` and verifies the module.

## Status & Roadmap (`v0.1` -> `v0.2`)

- **v0.1 (Current):** Core primitives, control flow, functions, structs, semantic type checking, and LLVM IR codegen.
- **Next up:** Struct function parameters/returns, nested structs, fixed-size arrays, a test suite (`Lumi.Tests`), and sample programs under `examples/`.
- **Later:** Generics, modules/imports, and richer optimization/tooling support.

## License

MIT