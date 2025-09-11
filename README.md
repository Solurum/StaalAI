# StaalAI (Solurum.StaalAi)

StaalAI is a .NET global tool that coordinates iterative code changes driven by Large Language Models (LLMs), enriched with CI/CD output (“heat”) to improve accuracy and relevance. It operates within a bounded working directory, applying strict safeguards and rules.

- Primary package and CLI: Solurum.StaalAi (command: `StaalAI`)
- Target framework: .NET 8.0
- Purpose: Iteratively generate or update code, tests, and/or documentation, using structured context and CI outputs.

## Projects

- Solurum.StaalAi (tool) — see the detailed guide: [Solurum.StaalAi/README.md](Solurum.StaalAi/README.md)

## Quick Start

1) Install the global tool:

```bash
dotnet tool install -g Solurum.StaalAi
```

2) Configure environment variables (example):

```bash
# PowerShell
$env:StaalOpenApiToken="your-openai-api-key"
$env:StaalOpenApiModel="gpt-4o-mini"

# bash/zsh
export StaalOpenApiToken="your-openai-api-key"
export StaalOpenApiModel="gpt-4o-mini"
```

3) Run help:

```bash
StaalAI --help
```

For full usage, scenarios, and examples, consult [Solurum.StaalAi/README.md](Solurum.StaalAi/README.md).