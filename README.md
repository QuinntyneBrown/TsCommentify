# TsCommentify

[![NuGet Version](https://img.shields.io/nuget/v/TsCommentify.svg)](https://www.nuget.org/packages/TsCommentify/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/TsCommentify.svg)](https://www.nuget.org/packages/TsCommentify/)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.md)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey.svg)](#installation)

A CLI tool that automatically adds JSDoc comments to TypeScript functions and interfaces using best practices.

## Features

- **Automatic Comment Generation**: Automatically generates JSDoc-style comments for TypeScript functions and interfaces
- **Smart Parsing**: Supports multiple function declaration styles:
  - Regular functions (`function name() {}`)
  - Arrow functions (`const name = () => {}`)
  - Function expressions (`const name = function() {}`)
  - Async functions (`async function name() {}`)
  - Exported functions (`export function name() {}`)
  - Class methods, getters, and setters
- **Interface Support**: Documents interface declarations along with their property and method signatures (e.g. `*.contract.ts` files)
- **Type-Aware**: Recognizes TypeScript type annotations for parameters and return types
- **Comment Detection**: Skips functions, interfaces, and members that already have comments
- **Batch Processing**: Process single files or entire directories recursively
- **Smart Filtering**: Automatically excludes `node_modules`, `dist`, `.d.ts` files, and test files (`*.spec.ts`, `*.test.ts`)
- **Configurable Ignore Patterns**: Customize which files to ignore via `appsettings.json`

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
 * Calculate Total.
 *
 * @param {number} price - The price
 * @param {number} quantity - The quantity
 * @returns {number} The result of the operation
 */
function calculateTotal(price: number, quantity: number): number {
  return price * quantity;
}

/**
 * Process Data.
 *
 * @param {any} data - The data
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
   * Get Display Name.
   *
   * @returns {string} The result of the operation
   */
  getDisplayName(): string;
}
```

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

### Project Structure

```
TsCommentify/
├── src/
│   └── TsCommentify.Cli/
│       ├── Program.cs              # CLI entry point
│       └── Services/
│           ├── TypeScriptParser.cs # Parses TS files
│           ├── CommentGenerator.cs # Generates comments
│           └── FileProcessor.cs    # Orchestrates processing
└── tests/
    └── TsCommentify.Tests/
        ├── Services/               # Unit tests
        └── Integration/            # Integration tests
```

## Technologies

- **C# / .NET 8.0**: Core runtime
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