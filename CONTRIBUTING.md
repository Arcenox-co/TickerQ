# Contributing to TickerQ

We welcome contributions to TickerQ! Before you start, please read through this guide.

## Contributor License Agreement (CLA)

All contributors must sign our [Contributor License Agreement](CLA.md) before their pull request can be merged. This is a one-time process handled automatically via [CLA Assistant](https://cla-assistant.io/) when you open your first pull request.

**Why?** TickerQ includes source-available commercial package families as well as components and historical artifacts under MIT and/or Apache 2.0. The CLA expressly allows Arcenox LLC to distribute contributions under open-source or proprietary terms while preserving the license attached to each immutable released artifact.

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

By contributing, you grant the rights stated in the [CLA](CLA.md). Inclusion does not promise a particular outbound license: the applicable terms are the license embedded in the relevant immutable artifact. See the repository [license guide](LICENSE.md), [commercial agreement](licenses/COMMERCIAL.md), and [preserved open-source terms](licenses/OPEN-SOURCE.md).
