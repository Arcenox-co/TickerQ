# Contributing to TickerQ

We welcome contributions to TickerQ! Before you start, please read through this guide.

## Contributor License Agreement (CLA)

All contributors must sign our [Contributor License Agreement](CLA.md) before their pull request can be merged. This is a one-time process handled automatically via [CLA Assistant](https://cla-assistant.io/) when you open your first pull request.

**Why?** TickerQ is dual-licensed under Apache 2.0 and MIT. The CLA ensures that all contributions can be distributed under these licenses and that the project can continue to evolve.

## How to Contribute

1. **Fork** the repository
2. **Create a branch** from `main` for your changes
3. **Make your changes** and ensure the build passes
4. **Open a pull request** against `main`
5. **Sign the CLA** when prompted by the CLA Assistant bot

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or .NET 8/9 depending on your target)
- PostgreSQL, SQL Server, SQLite, or MySQL (for integration tests)
- [Node.js 18+](https://nodejs.org/) (for the Node.js SDK)

### Building

```bash
dotnet build src/src.sln
```

### Running Tests

```bash
dotnet test src/src.sln
```

## Guidelines

- Follow existing code style and conventions
- Add tests for new functionality
- Keep pull requests focused — one feature or fix per PR
- Write clear commit messages

## Reporting Issues

Use [GitHub Issues](https://github.com/Arcenox-co/TickerQ/issues) to report bugs or request features. Include steps to reproduce, expected behavior, and actual behavior.

## License

By contributing, you agree that your contributions will be licensed under the project's [dual license (Apache 2.0 / MIT)](LICENSE), subject to the terms of the [CLA](CLA.md).
