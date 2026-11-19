# Lumi Language Reference (v0.1)

Lumi is a statically typed systems programming language designed around explicit typing, stack-allocated values, lexical scoping, and a clean LLVM compilation pipeline.

---

## 1. Comments & Syntax Rules

* **Identifiers:** Case-sensitive ASCII character sequences starting with a letter or underscore `[a-zA-Z_][a-zA-Z0-9_]*`.
* **Whitespace:** Spaces, tabs, and line breaks separate tokens and are otherwise ignored outside character and string literals.
* **Declarations:** Functions and struct definitions are top-level constructs.

---

## 2. Types & Type System

### 2.1 Primitive Scalar Types

| Type Name | Category | Description / Size |
| :--- | :--- | :--- |
| `i8`, `i16`, `i32`, `i64` | Signed Integer | Fixed-width 2's complement integers (8 to 64 bits) |
| `u8`, `u16`, `u32`, `u64` | Unsigned Integer | Fixed-width unsigned integers (8 to 64 bits) |
| `f16`, `f32`, `f64` | Floating Point | IEEE 754 half, single, and double precision floats |
| `bool` | Boolean | `true` or `false` (1-bit integer in LLVM) |
| `char` | Character | 8-bit ASCII character literal (e.g., `'a'`) |
| `void` | Void | Indicates a function returns no value |

### 2.2 Aggregate Types (`struct`)

Structs group named fields into stack-allocated data structures. Struct declarations occur at the top level of a file.

```text
struct Vec {
    u32 x
    u32 y
}
```

---

## 3. Variables & Scope

### 3.1 Variable Declarations

Local variables are declared using explicit typing, a variable name, and an initial value:

```text
u16 counter = 20
Vec p = Vec { x: 10, y: 20 }
```

### 3.2 Assignment & Mutation

Variables and struct fields can be reassigned using the `=` assignment operator:

```text
counter = 25
p.x = p.x + 5
```

Lexical scoping applies to all variables declared inside blocks (`{ ... }`), `if` branches, and loop bodies.

---

## 4. Struct Literals & Member Access

### 4.1 Struct Literals

Struct instances are created with field initializer syntax. All declared fields must be initialized:

```text
Vec position = Vec { x: 100, y: 200 }
```

### 4.2 Member Access

Fields are accessed using the dot (`.`) operator:

```text
u32 total = position.x + position.y
```

Nested member access is fully supported for composite types:

```text
struct Line {
    Vec start
    Vec end
}

u32 startX = line.start.x
```

---

## 5. Functions

Functions are defined at the top level using the `func` keyword, parameter lists, a return type arrow (`->`), and a block body:

```text
func add(u32 a, u32 b) -> u32 {
    return a + b
}

func logMessage() -> void {
    println('k')
}
```

* **Parameters:** Typed explicitly as `Type name`. Multiple parameters are comma-separated.
* **Return Values:** Non-`void` functions must explicitly execute a `return` statement on all code paths.

---

## 6. Expressions & Operators

### 6.1 Unary Operators

| Operator | Target Type | Description |
| :---: | :--- | :--- |
| `-` | Signed integers, Floats | Arithmetic negation |
| `!` | `bool` | Logical NOT |
| `~` | Integers | Bitwise NOT |

### 6.2 Binary Operators

| Operator Category | Operators | Description |
| :--- | :---: | :--- |
| **Arithmetic** | `+`, `-`, `*`, `/` | Addition, subtraction, multiplication, division |
| **Comparison** | `==`, `!=`, `<`, `<=`, `>`, `>=` | Equality and relational comparisons returning `bool` |
| **Logical** | `&&`, `||` | Short-circuiting logical operations on `bool` |
| **Bitwise** | `&`, `\|`, `^` | Bitwise AND, OR, XOR on integers |
| **Shift** | `<<`, `>>` | Left shift and arithmetic/logical right shift |

---

## 7. Control Flow

### 7.1 Conditionals (`if` / `else`)

Condition expressions must evaluate to `bool`.

```text
if p.x > 10 {
    println(1)
} else if p.x == 10 {
    println(0)
} else {
    println(2)
}
```

### 7.2 `while` Loops

Executes statements while a boolean condition remains `true`:

```text
u32 i = 0
while i < 10 {
    i = i + 1
}
```

### 7.3 `for` Loops

Consists of an optional initializer, condition, and update statement separated by semicolons:

```text
for u32 i = 0; i < 10; i = i + 1 {
    println(i)
}
```

### 7.4 Loop Jump Statements

* `break`: Immediately exits the innermost loop.
* `continue`: Skips the remainder of the current loop iteration and advances to the update/condition evaluation.

---

## 8. Built-in Statements

Lumi includes built-in output functions for development:

* `print(expr)`: Prints the value of `expr` without a trailing newline.
* `println(expr)`: Prints the value of `expr` followed by a system newline.

---

## 9. Execution Model & v0.1 Limitations

* **Memory Allocation:** All local variables and struct instances are allocated on the stack via LLVM `alloca`.
* **No Dynamic Heap / GC:** Pointer allocation and heap operations are out of scope for v0.1.
* **Single-File Scope:** Multi-module linkage, imports, generics, and traits will be introduced in subsequent compiler releases.