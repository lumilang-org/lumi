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
| `byte` | Unsigned Integer | Alias of `u8` |
| `i8`, `i16`, `i32`, `i64` | Signed Integer | Fixed-width 2's complement integers (8 to 64 bits) |
| `u8`, `u16`, `u32`, `u64` | Unsigned Integer | Fixed-width unsigned integers (8 to 64 bits) |
| `f16`, `f32`, `f64` | Floating Point | IEEE 754 half, single, and double precision floats |
| `bool` | Boolean | `true` or `false` (1-bit integer in LLVM) |
| `char` | Character | 8-bit character literal (e.g., `'a'`, `'\n'`), see 2.2 |
| `string` | String | Immutable text literal (e.g., `"hello"`), see 2.2 |
| `void` | Void | Indicates a function returns no value |

### 2.2 Characters and Strings

A character literal is a single character in single quotes and has type `char` (one byte). A string literal is written in double quotes and has type `string`. Both support the escape sequences `\n`, `\r`, `\t`, `\\`, `\'`, `\"` and `\0`. Literals cannot span lines. Empty (`''`) or multi-character (`'ab'`) character literals, unterminated literals and unknown escapes are compile errors.

```text
char newline = '\n'
string greeting = "Hello\n"
string[2] names = ["Ada", "Grace"]
```

A `string` can be used anywhere a type is accepted: variables, parameters, return types, struct fields and arrays (`string[3]`). It is a pointer to immutable, NUL-terminated UTF-8 bytes (so `\0` ends the text for `print`, `println`, `len` and `==`).

| Operation | Result | Notes |
| :--- | :---: | :--- |
| `s[i]` | `char` | Byte at index `i` (any integer type). Not bounds-checked at run time; a constant negative index is a compile error. Strings are immutable, so `s[i] = c` is rejected. |
| `len(s)` | `i32` | Number of bytes before the terminating NUL. A user-declared function named `len` takes precedence. |
| `a == b`, `a != b` | `bool` | Compares contents (not addresses). `<`, `<=`, `>`, `>=`, `+` and other operators are rejected. |
| `c == 'a'`, `c != 'a'` | `bool` | `char` supports equality only. |

```text
string s = "banana"
u32 n = 0
for i32 i = 0; i < len(s); i = i + 1 {
    if (s[i] == 'a') { n = n + 1 }
}
```

### 2.3 Aggregate Types (`struct`)

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

### 6.3 Increment and Decrement

`++` and `--` add or subtract 1 and store the result back. They work as a prefix or postfix operator, as a statement, or inside a larger expression:

| Form | Value of the expression |
| :--- | :--- |
| `++x`, `--x` | The **new** value |
| `x++`, `x--` | The **previous** value |

```text
i32 i = 5
i32 a = i++      // a = 5, i = 6
i32 b = ++i      // b = 7, i = 7
for i32 j = 0; j < 3; j++ { }
counts[idx++]--  // idx is evaluated once
```

* **Operand:** must be an integer (`i8`..`i64`, `u8`..`u64`, `byte`) held in a variable, parameter, struct field or array element (`x`, `p.x`, `arr[i]`, `arr[i].n`). Literals, calls and parenthesised expressions (`1++`, `(x + 1)++`, `f()++`) are rejected, as are `bool`, `char`, `string`, floats, structs and arrays. Every variable is mutable; string elements (`s[i]++`) are not.
* **Type:** the expression has the operand's type. Unsigned and fixed-width values wrap on overflow.
* **Line rule:** statements have no terminator, so `++`/`--` directly after an operand **on the same line** is always the postfix operator. Start a prefix update on its own line, for example `x = 1` followed by a new line `++y`.

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

`expr` may be an integer, float, `bool`, `char` or `string`.

---

## 9. Standard Library

Standard modules live in the `std/` folder, which is copied next to the compiler, so `import std::string` works from any directory. `std::string` provides non-allocating helpers:

| Function | Description |
| :--- | :--- |
| `empty() -> string`, `newline() -> string` | The strings `""` and `"\n"` |
| `identity(string value) -> string` | Returns `value` |
| `print_line(string value) -> void` | Prints `value` and a newline |
| `is_empty(string s) -> bool` | `len(s) == 0` |
| `equals(string a, string b) -> bool` | `a == b` |
| `count_char(string s, char target) -> i32` | Number of occurrences of `target` |
| `index_of(string s, char target) -> i32` | Index of the first `target`, or `-1` |
| `starts_with_char(string s, char ch) -> bool` | `s` is non-empty and `s[0] == ch` |
| `ends_with_char(string s, char ch) -> bool` | `s` is non-empty and its last byte is `ch` |

```text
import std::string
func main() -> void {
    println(std::string::count_char("banana", 'a'))
}
```

---

## 10. Execution Model & v0.1 Limitations

* **Memory Allocation:** All local variables and struct instances are allocated on the stack via LLVM `alloca`.
* **No Dynamic Heap / GC:** Pointer allocation and heap operations are out of scope for v0.1.
* **Single-File Scope:** Multi-module linkage, imports, generics, and traits will be introduced in subsequent compiler releases.