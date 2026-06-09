# TsCommentify

[![CI / Publish to NuGet](https://github.com/QuinntyneBrown/TsCommentify/actions/workflows/publish.yml/badge.svg)](https://github.com/QuinntyneBrown/TsCommentify/actions/workflows/publish.yml)
[![NuGet Version](https://img.shields.io/nuget/v/TsCommentify.svg)](https://www.nuget.org/packages/TsCommentify/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TsCommentify.svg)](https://www.nuget.org/packages/TsCommentify/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg)](#installation)

A CLI tool that automatically adds JSDoc comments to TypeScript functions and interfaces using best practices.

Parsing is powered by the **official TypeScript compiler** running in a small Node.js sidecar (the same `ts.createSourceFile` AST that drives `tsc` and the editor tooling), so declarations are understood the way the compiler sees them — not approximated with regular expressions. Multi-line signatures, function-typed return types, methods whose `{` sits on the next line, and multi-line generic arrows are all handled correctly.

## Features

- **Compiler-grade parsing**: Uses the real TypeScript AST (via a bundled Node sidecar) to find declarations and extract parameters and return types — including signatures that span multiple lines.
- **Automatic Comment Generation**: Generates JSDoc-style comments for TypeScript declarations, with verb-aware descriptions (e.g. `calculateTotal` → *“Calculates the total.”*).
- **Declaration styles supported**:
  - Regular functions (`function name() {}`)
  - Arrow functions (`const name = () => {}`)
  - Function expressions (`const name = function() {}`)
  - Async functions (`async function name() {}`)
  - Exported functions (`export function name() {}`)
  - Classes (`class Name {}`) and their methods, getters, and setters
  - Interfaces, along with their property and method signatures (e.g. `*.contract.ts` files)
  - Enums (`enum Name {}` and `const enum`), along with their members
  - Type aliases (`type Name = ...`)
- **Type-Aware**: Reads parameter and return types straight from the AST (e.g. `string[]`, `Promise<User>`, `(n: number) => number`).
- **Optional annotation tags**: Flags (`--deprecated`, `--obsolete`, `--internal`, `--public-api`) stamp the matching JSDoc/TSDoc modifier tag onto generated top-level declaration comments (see [Annotation tags](#annotation-tags)).
- **Accurate Comment Detection**: Skips declarations that already have a comment, using the compiler's leading-comment ranges (no false positives from lines that merely start with `*`).
- **Refuses to edit invalid files**: A file with a TypeScript syntax error is reported and skipped rather than risk mis-inserting comments.
- **Graceful fallback**: If Node.js is not available, the tool falls back to a built-in regular-expression parser so it still runs (with reduced accuracy).
- **Batch Processing**: Process single files or entire directories recursively
- **Smart Filtering**: Automatically excludes `node_modules`, `dist`, `.d.ts` files, and test files (`*.spec.ts`, `*.test.ts`)
- **Configurable Ignore Patterns**: Customize which files to ignore via `appsettings.json`

## Requirements

- **.NET 8.0** runtime (the tool itself).
- **Node.js** on your `PATH` (any recent LTS, ≥ 18) for the high-accuracy TypeScript-AST engine. The required `typescript` package is bundled with the tool — you do **not** need a `node_modules` or a `tsconfig.json` in your project. If Node is missing, the tool automatically falls back to the regex parser.

You can force a specific engine with the `TSCOMMENTIFY_PARSER` environment variable (`sidecar`, `regex`, or `auto` — the default), or via `"Parser": { "Engine": "..." }` in `appsettings.json`.

## Installation

Install as a global .NET tool:

```bash
dotnet tool install --global TsCommentify
```

Or build from source:

```bash
dotnet build
dotnet pack src/TsCommentify.Cli/TsCommentify.Cli.csproj
dotnet tool install --global --add-source ./src/TsCommentify.Cli/bin/Debug TsCommentify
```

## Usage

### Process a single TypeScript file

```bash
tc path/to/file.ts
```

### Process an entire directory

```bash
tc path/to/project
```

The tool will recursively scan all `.ts` and `.tsx` files in the directory and add comments to functions that don't have them.

### Annotation tags

Optional flags add a JSDoc/TSDoc modifier tag to every generated **top-level declaration** comment — functions, classes, interfaces, enums, and type aliases. The tags are *not* applied to members (methods, properties, enum members): annotating the declaration already covers its surface.

| Flag | Tag added |
|------|-----------|
| `--deprecated` | `@deprecated` |
| `--obsolete` | `@obsolete` |
| `--internal` | `@internal` |
| `--public-api` (alias `--publicApi`) | `@publicApi` |

The flags are combinable, and the tags are emitted in a fixed, canonical order (`@deprecated`, `@obsolete`, `@internal`, `@publicApi`) regardless of the order you pass them. They are placed immediately after the description and before any `@param`/`@returns` documentation. Declarations that already have a comment are skipped, so re-running never duplicates a tag.

```bash
tc src/legacy --deprecated --internal
```

```typescript
/**
 * User Service.
 *
 * @deprecated
 * @internal
 */
export class UserService {
  /**
   * Gets the user.
   *
   * @param {string} id - The id.
   * @returns {User} The user.
   */
  getUser(id: string): User { return null as any; }
}
```

### Configuration

You can customize the ignore patterns by creating an `appsettings.json` file in the directory where you run the command:

```json
{
  "FileProcessor": {
    "IgnorePatterns": [
      "*.spec.ts",
      "*.test.ts",
      "*.mock.ts"
    ]
  }
}
```

By default, the tool ignores `*.spec.ts` and `*.test.ts` files. You can override this by providing your own list of patterns in the configuration file. Patterns support wildcards (`*` and `?`).

## Example

### Before

```typescript
function calculateTotal(price: number, quantity: number): number {
  return price * quantity;
}

const processData = (data: string[]) => {
  return data.map(item => item.toUpperCase());
};
```

### After

```typescript
/**
 * Calculates the total.
 *
 * @param {number} price - The price.
 * @param {number} quantity - The quantity.
 *
 * @returns {number} The number.
 */
function calculateTotal(price: number, quantity: number): number {
  return price * quantity;
}

/**
 * Processes the data.
 *
 * @param {string[]} data - The data.
 */
const processData = (data: string[]) => {
  return data.map(item => item.toUpperCase());
};
```

### Interfaces

Running `tc foo.contract.ts` documents interfaces and their members.

#### Before

```typescript
export interface Foo {
  id: string;
  getDisplayName(): string;
}
```

#### After

```typescript
/**
 * Foo.
 */
export interface Foo {
  /**
   * Id.
   */
  id: string;
  /**
   * Gets the display name.
   *
   * @returns {string} The string.
   */
  getDisplayName(): string;
}
```

### Classes, enums, and type aliases

The same probing covers class and enum declarations (and their members) plus type aliases.

#### Before

```typescript
export class UserService {
  getUser(id: string): User { return null as any; }
}

export enum ActivityTone {
  Default,
  Outdoor,
}

export type Tone = 'default' | 'outdoor';
```

#### After

```typescript
/**
 * User Service.
 */
export class UserService {
  /**
   * Gets the user.
   *
   * @param {string} id - The id.
   * @returns {User} The user.
   */
  getUser(id: string): User { return null as any; }
}

/**
 * Activity Tone.
 */
export enum ActivityTone {
  /**
   * Default.
   */
  Default,
  /**
   * Outdoor.
   */
  Outdoor,
}

/**
 * Tone.
 */
export type Tone = 'default' | 'outdoor';
```

> A single-line enum such as `enum Direction { Up, Down }` documents only the enum itself — its members share the declaration line, so they can't take a line-above comment without rewriting the source.

## Best Practices

The tool follows JSDoc best practices:

1. **JSDoc Format**: Uses standard `/** */` comment blocks
2. **Function Description**: Generates readable descriptions from function names
3. **Parameter Documentation**: Documents each parameter with type and description
4. **Return Documentation**: Documents return types when specified
5. **Proper Indentation**: Maintains the original code indentation

## Development

### Prerequisites

- .NET 8.0 SDK or later
- Node.js + npm (the build restores the sidecar's `typescript` dependency via `npm ci`; `node` is also used to run the AST engine and the end-to-end tests)

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test
```

### Test Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Current test coverage: **79.1%** (exceeds 80% on business logic)

### Continuous Integration & Releases

The [`CI / Publish to NuGet`](.github/workflows/publish.yml) GitHub Actions workflow runs on every push and pull request to `main`:

- **Pull requests**: build and run the test suite (Release) — no publish.
- **Pushes to `main`**: build, test, pack, then push the package to [NuGet.org](https://www.nuget.org/packages/TsCommentify/).

Publishing is idempotent (`--skip-duplicate`), so a release happens only when `<Version>` in `src/TsCommentify.Cli/TsCommentify.Cli.csproj` is bumped. The NuGet API key is stored in the `NUGET_API_KEY` repository secret.

### Project Structure

```
TsCommentify/
├── .github/
│   └── workflows/
│       └── publish.yml                    # CI: build/test on PRs, publish to NuGet on push to main
├── src/
│   └── TsCommentify.Cli/
│       ├── Program.cs                    # CLI entry point + parser selection
│       ├── Configuration/
│       │   └── FileProcessorOptions.cs   # Bound ignore-pattern options
│       ├── sidecar/
│       │   ├── sidecar.js                # Node/TypeScript-AST sidecar (JSON-RPC over stdio)
│       │   ├── package.json              # Pins the bundled `typescript` dependency
│       │   └── package-lock.json         # Locked sidecar dependency tree (restored via `npm ci`)
│       └── Services/
│           ├── SidecarTypeScriptParser.cs # AST parser (primary) — drives the sidecar
│           ├── SidecarClient.cs           # stdio JSON-RPC transport to node
│           ├── TypeScriptParser.cs        # Regex parser (fallback when node is absent)
│           ├── CommentGenerator.cs        # Generates JSDoc comments
│           ├── FileProcessor.cs           # Orchestrates processing
│           └── I*.cs                       # Service interfaces (DI contracts)
└── tests/
    └── TsCommentify.Tests/
        ├── Services/                      # Unit tests
        └── Integration/                   # Integration tests (end-to-end via the sidecar)
```

## Technologies

- **C# / .NET 8.0**: Core runtime and CLI host
- **Node.js + TypeScript compiler API**: A bundled sidecar (`sidecar/sidecar.js`) parses TypeScript with the real `ts` AST and talks to the host over newline-delimited JSON-RPC on stdio
- **System.CommandLine**: Command-line interface
- **Microsoft.Extensions.DependencyInjection**: Dependency injection
- **Microsoft.Extensions.Logging**: Structured logging
- **Microsoft.Extensions.Configuration**: Configuration management

## Contributing

Contributions are welcome! Please read the [Contributing Guide](CONTRIBUTING.md) to get started.

## Security

To report a security vulnerability, please review our [Security Policy](SECURITY.md). Please do not report security issues through public GitHub issues.

## License

This project is licensed under the MIT License — see the [LICENSE.md](LICENSE.md) file for details.

## Author

Quinntyne Brown