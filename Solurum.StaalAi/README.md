# StaalAI (Solurum.StaalAi)

StaalAI is a .NET global tool designed to iteratively run and combine LLM-driven changes with CI/CD outputs to achieve accurate code generation and updates. It works within a clearly defined working directory and enforces strict safety and formatting rules.

- Tool command name: `StaalAI`
- Target framework: .NET 8.0
- Packaging: .NET Global Tool
- Logging: Serilog (configurable minimum level)

## Features

- Iterative LLM-assisted changes constrained to a designated working directory.
- Incorporates build/test/code-analysis/pipeline outputs (“heat”) to guide improvements.
- Strict, deterministic YAML protocol between the LLM and the tool to avoid ambiguity.
- Edit modes to limit the scope of changes (All, OnlyCode, OnlyTests, OnlyDocumentation).

## Installation

Prerequisites:
- .NET SDK 8.0 or later installed.

Install:
```bash
dotnet tool install -g Solurum.StaalAi
```

Update:
```bash
dotnet tool update -g Solurum.StaalAi
```

Uninstall:
```bash
dotnet tool uninstall -g Solurum.StaalAi
```

Verify:
```bash
StaalAI --help
```

## Configuration

The tool requires an OpenAI API token and model name, provided via environment variables:

- `StaalOpenApiToken`: Your OpenAI API key.
- `StaalOpenApiModel`: The OpenAI model id (for example: `gpt-4o-mini`).

Examples:
```bash
# PowerShell
$env:StaalOpenApiToken="your-openai-api-key"
$env:StaalOpenApiModel="gpt-4o-mini"

# bash/zsh
export StaalOpenApiToken="your-openai-api-key"
export StaalOpenApiModel="gpt-4o-mini"
```

Notes:
- Secrets can also be provided via user secrets for local testing (the tool wires configuration with user secrets).
- Network access is required to communicate with the OpenAI API.

## Logging

Global options:
- `--minimum-log-level <level>`: Sets the minimum log level. Defaults to `Information`.
  - Accepted values map to Serilog LogEventLevel: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`.
- `--debug`: Convenience switch that forces `Debug` logging (hidden option).

Examples:
```bash
StaalAI --minimum-log-level Debug --help
StaalAI --minimum-log-level Warning generate -pf "/abs/prompt.txt" -wd "/abs/repo"
```

## Commands

### generate

Generates or updates code, tests, and/or documentation by running an LLM conversation guided by your prompt and your repository context.

Usage:
```bash
StaalAI generate -pf <prompt-file> -wd <working-directory> [-em <EditMode>]
```

Required options:
- `-pf`, `--prompt-file`       Path to a text file containing your request prompt.
- `-wd`, `--working-directory` Absolute path to the root folder where all changes are allowed.

Optional options:
- `-em`, `--ai-edit-mode`      One of:
  - `All` (default)
  - `OnlyCode`
  - `OnlyTests`
  - `OnlyDocumentation`

Behavior:
- Builds a complete system prompt including:
  - Strict unbreakable rules.
  - Your request prompt contents.
  - A listing of files under the specified working directory.
  - Hints derived from any present “heat” folders.
- Initiates a deterministic YAML-based conversation with the LLM to perform changes strictly within the working directory.
- Expects environment variables `StaalOpenApiToken` and `StaalOpenApiModel` to be set.

Examples:
```bash
# Minimal run (Linux/macOS)
export StaalOpenApiToken="your-openai-api-key"
export StaalOpenApiModel="gpt-4o-mini"
StaalAI generate -pf "/home/user/prompt.txt" -wd "/home/user/repo"

# Minimal run (Windows PowerShell)
$env:StaalOpenApiToken="your-openai-api-key"
$env:StaalOpenApiModel="gpt-4o-mini"
StaalAI generate -pf "C:\Users\me\prompt.txt" -wd "C:\dev\repo"

# Documentation-only changes
StaalAI generate -pf "/abs/prompt.md" -wd "/abs/repo" -em OnlyDocumentation

# Code-only changes with debug logging
StaalAI --minimum-log-level Debug generate -pf "/abs/prompt.txt" -wd "/abs/repo" -em OnlyCode
```

### add-heat

Adds log files and other outputs from builds, tests, code analysis, or pipelines under a designated “heat group”. The LLM uses these to prioritize and guide changes.

Usage:
```bash
StaalAI add-heat -hf <heat-file> -wd <working-directory> [-hg <heat-group>]
```

Required options:
- `-hf`, `--heat-file`         Path to a file containing output to include (for example, a build log or test results file).
- `-wd`, `--working-directory` Absolute path to the repository root folder.

Optional options:
- `-hg`, `--heat-group`        Name of the logical group. Defaults to `latest`.

Behavior:
- Copies the specified file into:
  ```
  <working-directory>/.heat/<heat-group>/<original-file-name>
  ```
- The prompt engine looks for the following well-known groups (directories) and will highlight them if present:
  - `build`          (e.g., compiler output, build logs)
  - `tests`          (e.g., test results)
  - `codeanalysis`   (e.g., static analysis results)
  - `pipelineoutput` (e.g., CI/CD workflow logs)
  - `latest`         (default catch-all group)

Examples:
```bash
# Add a dotnet build log to the "build" group
StaalAI add-heat -hf "/abs/logs/dotnet-build.log" -wd "/abs/repo" -hg build

# Add test results to the "tests" group
StaalAI add-heat -hf "/abs/logs/dotnet-test.log" -wd "/abs/repo" -hg tests

# Add miscellaneous output to the default group "latest"
StaalAI add-heat -hf "/abs/output/analysis.txt" -wd "/abs/repo"
```

## Working Directory and Safety Rules

- All changes are restricted to the path provided via `-wd/--working-directory`.
- Files under `.git`, `.github`, or `.heat` are never modified by the LLM.
- Files named `carbon.staal.txt` or `iron.staal.txt` must not be altered; they may define AI-specific rules to follow.
- The tool will always read `iron.staal.txt` where present and prefer the closest file in the directory hierarchy when rules conflict.
- The tool never changes IDE/editor configuration files.

You can add your own `iron.staal.txt` to scope and guide the AI for your repository. Example rule themes include limiting threading complexity, framework constraints, and preferred patterns.

## Exit Codes

- `0`   Ok
- `1`   Fail
- `-1`  UnexpectedException
- `-2`  NotImplemented

## Tips

- Keep prompts concise, goal-oriented, and include acceptance criteria.
- Deposit relevant logs using `add-heat` before running `generate` to improve quality.
- Start with a narrower `--minimum-log-level` (e.g., `Information` or `Warning`) and switch to `Debug` during troubleshooting.

## Help

```bash
StaalAI --help
StaalAI generate --help
StaalAI add-heat --help
```

## License

This project is licensed under the terms described in the repository’s LICENSE file.