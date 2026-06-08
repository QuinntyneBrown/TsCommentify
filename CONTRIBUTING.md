# Contributing to TsCommentify

First off, thank you for taking the time to contribute! 🎉

The following is a set of guidelines for contributing to TsCommentify. These are mostly guidelines, not rules — use your best judgment, and feel free to propose changes to this document in a pull request.

## Code of Conduct

This project and everyone participating in it is expected to be respectful and considerate. Please be kind, assume good intent, and keep discussions constructive.

## How Can I Contribute?

### Reporting Bugs

Before creating a bug report, please check the [existing issues](https://github.com/QuinntyneBrown/TsCommentify/issues) to avoid duplicates. When filing a bug report, include:

- A clear and descriptive title
- The exact steps to reproduce the problem
- A minimal TypeScript sample that triggers the issue (input and expected/actual output)
- The version of TsCommentify and the .NET SDK you are using
- Your operating system

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When suggesting an enhancement, describe the current behavior, the behavior you'd like to see, and why it would be useful.

### Pull Requests

1. **Fork** the repository and create your branch from `main`.
2. **Make your changes**, following the coding guidelines below.
3. **Add tests** for any new behavior, and make sure existing tests pass.
4. **Update documentation** (including the README) if your change affects usage.
5. **Open a pull request** with a clear description of the problem and solution.

## Development Setup

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Build

```bash
dotnet build
```

### Run Tests

```bash
dotnet test
```

### Test Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Try It Locally

```bash
dotnet pack src/TsCommentify.Cli/TsCommentify.Cli.csproj
dotnet tool install --global --add-source ./src/TsCommentify.Cli/bin/Debug TsCommentify
```

## Coding Guidelines

- Follow the existing code style and conventions used throughout the project.
- Keep changes focused — one logical change per pull request.
- Write tests for new functionality and bug fixes.
- Ensure `dotnet build` and `dotnet test` succeed before submitting.
- Use clear, descriptive commit messages.

## Project Structure

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

## License

By contributing to TsCommentify, you agree that your contributions will be licensed under the [MIT License](LICENSE.md).
