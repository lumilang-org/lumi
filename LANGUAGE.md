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
| `usize` | Unsigned Integer | Pointer-sized unsigned integer: as wide as a pointer on the target (64 bits on 64-bit targets), used for sizes and indices |
| `i8`, `i16`, `i32`, `i64` | Signed Integer | Fixed-width 2's complement integers (8 to 64 bits) |
| `u8`, `u16`, `u32`, `u64` | Unsigned Integer | Fixed-width unsigned integers (8 to 64 bits) |
| `f16`, `f32`, `f64` | Floating Point | IEEE 754 half, single, and double precision floats |
| `bool` | Boolean | `true` or `false` (1-bit integer in LLVM) |
| `char` | Character | 8-bit character literal (e.g., `'a'`, `'\n'`), see 2.2 |
| `string` | String | Immutable text literal (e.g., `"hello"`), see 2.2 |
| `void` | Void | Indicates a function returns no value |

`usize` is a distinct type with the same unsigned semantics as `u64`: it is zero-extended, compared with unsigned predicates (`<`, `<=`, `>`, `>=`), divided with unsigned division and shifted right logically, and wraps on overflow (`usize max = zero - 1`). It supports every integer operator, `++`/`--`, indexing, pointers (`*usize`), struct fields and arrays (`usize[N]`). Like the other integer types it must match exactly in assignments, arguments and returns, and it never takes a sign (`-n` and `usize x = -1` are errors). In arithmetic and comparisons it combines with `usize` and with the narrower unsigned types `u8`, `u16`, `u32` (which widen to `usize`), but not with `u64` or any signed type; convert with an explicit value of the right type instead. Compile-time range checks and the operator rules assume a 64-bit `usize`.

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

### 2.4 Pointers

`*T` is a pointer to a value of type `T`; pointers nest (`**i32`) and can point to any type, including structs, arrays, strings and other pointers. They are allowed everywhere a type is: variables, parameters, return types (`-> *i32`) and struct fields (a struct may hold a pointer to itself).

```text
i32 x = 1
*i32 p = &x        // address-of
*p = 2             // write through the pointer
i32 y = *p + 1     // read through the pointer

struct Node { i32 value *Node next }
func bump(*i32 n) -> void { *n = *n + 1 }
```

* **`&expr`** yields `*T` for an `expr` of type `T`. `expr` must be a variable, parameter, struct field, array element or dereference (`&x`, `&p.x`, `&arr[1]`, `&*p`). Literals, call results and arithmetic (`&5`, `&f()`, `&(x + 1)`) are rejected. `&s[i]` on a string gives a `*char` into the string's read-only bytes: read it, never write through it.
* **`*expr`** requires a pointer `*T` and yields `T`. It is also an assignment target (`*p = v`, `**pp = v`) and works with `++`/`--` (`(*p)++`).
* **Structs and arrays:** there is no automatic dereference. Use `(*p).x` and `(*a)[i]`. A pointer to an array is written `*(i32[3])` (this is also the type of `&arr`); `*i32[3]` means an array of three `*i32`.
* **`nil`:** the null pointer. It can be assigned, passed, returned or used as a struct field or array element wherever a `*T` is expected, and compared with `==`/`!=` against any pointer (either operand order, or against another `nil`). It is not a type of its own: `i32 x = nil`, `bool b = x == nil` for a non-pointer `x`, `nil < p`, `nil + 1`, `*nil` and `print(nil)` are errors.
* **Comparison:** pointers of the same type can be compared with `==` and `!=` (address equality). There is no pointer ordering. `print`/`println` on a pointer print its address, not the pointee (`0x0` for `nil`).
* **Arithmetic:** `p + n`, `n + p` and `p - n` (with `n` any integer type, negative values included) advance the pointer by `n` elements of the pointee type, so `*i32 + 1` moves 4 bytes and `*u8 + 1` moves 1. There is no pointer subtraction (`p - q`) and no bounds checking; going outside the pointed-to object is undefined behavior.
* **Limits:** there is no pointer to `void`; a pointer is not tracked after creation, so keeping one past the lifetime of the variable it points to (for example returning `&local`) is undefined behavior. Pointers cannot be converted between types.
* **Parsing note:** statements have no terminator, so a `*` that starts a new line **and** whose line contains an `=` begins a new statement (`*p = 1`) instead of continuing the previous line as a multiplication. A `(` on a new line is never a call.

---

Member access also works through a struct pointer: `p.field` is `(*p).field`, for reads and assignments (`p.inner.v = 1`). Only one pointer level is dereferenced automatically.

## 3. Variables & Scope

### 3.1 Variable Declarations

Local variables are declared using explicit typing, a variable name, and an initial value:

```text
u16 counter = 20
Vec p = Vec { x: 10, y: 20 }
```

`let` infers the type from the initializer: `let x = 20` (`i32`), `let s = "Sam"`, `let p = raw as *Entity`, `let n = sizeof(Entity)` (`usize`). An initializer is required, and `nil` or a `void` call can't be inferred. `let x: i64 = 5` gives an explicit type, which is checked like any declaration.

### 3.2 Assignment & Mutation

Variables and struct fields can be reassigned using the `=` assignment operator:

```text
counter = 25
p.x = p.x + 5
```

Lexical scoping applies to all variables declared inside blocks (`{ ... }`), `if` branches, and loop bodies.

---

### 2.3.1 Functions inside structs

A `func` can be declared inside a struct body next to the fields. It does not change the struct's size or layout.

```
struct Counter {
    i32 value

    func new(i32 start) -> Counter { return Counter { value: start } }   // associated function
    func bump(*Counter self, i32 by) -> void { self.value = self.value + by }   // instance method
}

Counter c = Counter.new(1)
c.bump(2)
```

- A first parameter named `self` of type `S` or `*S` makes an instance method, called as `value.method(args)`. Anything else named `self` (wrong type, or not first) is `LUMI1002`.
- Without `self` it is an associated function, called as `Struct.function(args)`.
- The receiver adapts to `self`: a struct variable passed to `*S self` has its address taken (it must be addressable, so not a temporary), and a `*S` passed to `S self` is dereferenced.
- Each function is emitted as `<MangledStruct>__<name>`, e.g. `std_mem_Arena__reset`.

### 7.4 `switch`

```
switch score {
    10, 9 => println(1)
    8 => { i32 bonus = 2 println(bonus) }
    default => println(0)
}
```

- The subject is evaluated once and must be an integer, `bool` or `char`.
- Cases are constant integer, `char` or `bool` literals (negative integers allowed) that fit the subject type; a repeated value is `LUMI1017`, other misuse is `LUMI1018`.
- An arm is one statement or a block with its own scope. There is no fall-through: after an arm the program continues after the `switch`. `break` leaves the `switch`; `continue` applies to the enclosing loop.
- A `switch` with a `default` where every arm returns counts as returning.

### 2.3.2 Visibility (`pub`)

Everything is private to its module unless marked `pub`: top-level `func`, `extern func` and `struct`,
struct fields, and functions inside structs.

```
module shapes
pub struct Account {
    i32 balance          // private field
    pub i32 id
    pub func open(i32 id) -> Account { return Account { balance: 0, id: id } }
}
```

- Inside the defining module (including the struct's own functions) everything is accessible.
- From another module only `pub` items are: structs, fields (`p.f` read and write), instance methods,
  associated functions (`Struct.f()`) and plain functions. Violations are `LUMI1016`.
- A struct literal outside its module needs a `pub` struct whose fields are **all** `pub`; otherwise
  provide a `pub` associated function. Layout (`sizeof`, `alignof`) ignores visibility.
- Non-`pub` functions other than `main` get internal linkage in the generated object file.

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

### 6.4 Casts

`expr as Type` converts explicitly. It binds tighter than any binary operator but looser than prefix operators, calls, `.` and `[]`: `*p as u64` is `(*p) as u64`, and `alloc(8) as *u64` needs no parentheses.

Allowed: identity, pointer to any pointer (`*u8 as *Point`), integer to integer (truncates or sign/zero-extends by the source type), and pointer to/from `usize`. Everything else (structs by value, `bool`, `string`, float) is `LUMI1002`.

### 6.5 `sizeof` and `alignof`

`sizeof(Type)` and `alignof(Type)` are `usize` constants computed from the target's data layout (struct padding included), e.g. `arena_alloc(&arena, sizeof(Pair), alignof(Pair))`. `Type` can be any non-`void` type: primitives, `*T`, structs and arrays.

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

### 7.3.1 `for ... in` Loops

`for item in array { ... }` runs the body once per element of a fixed-size array, in order. `item` has the element type, is scoped to the loop, and is a copy: assigning to it does not change the array. The iterable can be a variable, field, element or an array literal; anything else (`i32`, `string`, pointers) is a `LUMI1002` error. `break` and `continue` work as in other loops.

### 7.4 Loop Jump Statements

* `break`: Immediately exits the innermost loop.
* `continue`: Skips the remainder of the current loop iteration and advances to the update/condition evaluation.

---

## 8. Built-in Statements

Lumi includes built-in output functions for development:

* `print(expr)`: Prints the value of `expr` without a trailing newline.
* `println(expr)`: Prints the value of `expr` followed by a system newline.

Unsigned integers (`u8`..`u64`, `usize`) print as unsigned values. `expr` may be an integer, float, `bool`, `char` or `string`. A pointer (`*T`, any pointee) prints its address as `0x` followed by lowercase hex digits (for example `0x16fdff2ac`); the pointee is not read. The value differs from run to run.

---

## 9. Standard Library

Standard modules live in the `std/` folder, which is copied next to the compiler, so `import std::string` works from any directory. `std::mem` provides a bump-pointer `Arena` on top of `extern` `malloc`/`free`: `arena_init(*Arena, usize) -> bool`, `arena_alloc(*Arena, usize size, usize align) -> *u8` (returns `nil` when the request does not fit or `align` is not a power of two), `arena_reset` and `arena_destroy`. `arena.buf` is `nil` whenever the arena owns no memory.

`std::string` provides non-allocating helpers:

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