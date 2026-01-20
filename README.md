# AI Examples

.NET 10 examples using the [Microsoft Agent Framework](https://github.com/microsoft/agent-framework).

## Examples

| Project | Description |
|---------|-------------|
| [SimpleAgentWithTools](./SimpleAgentWithTools/) | Basic AI agent with function tools (weather, time) |
| [WorkflowSequentialPipeline](./WorkflowSequentialPipeline/) | Sequential agent chain: Translator → Summarizer → Reviewer |
| [CustomerSupportWorkflow](./CustomerSupportWorkflow/) | Customer support with triage-based routing to specialized agents |

## Prerequisites

- .NET 10.0 SDK
- Either:
  - Azure OpenAI resource with a deployed model, OR
  - OpenAI API key

## Quick Start

### 1. Clone and navigate

```bash
cd ai-examples
```

### 2. Configure credentials

**Using OpenAI API:**
```bash
export OPENAI_API_KEY="your-openai-api-key"
```

**Using Azure OpenAI:**
```bash
export USE_AZURE_OPENAI="true"
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini"
```

### 3. Run an example

```bash
# Simple agent with tools
dotnet run --project SimpleAgentWithTools

# Sequential workflow pipeline
dotnet run --project WorkflowSequentialPipeline

# Customer support workflow
dotnet run --project CustomerSupportWorkflow
```

## Building All Projects

```bash
dotnet build
```

## Documentation

See [docs/DOCUMENTATION.md](./docs/DOCUMENTATION.md) for detailed documentation including:
- Architecture and key concepts
- Code patterns and examples
- Configuration options
- Troubleshooting guide

## Package References

These examples use the following NuGet packages:

- `Microsoft.Agents.AI.OpenAI` - Core agent functionality with OpenAI support
- `Microsoft.Agents.AI.Workflows` - Workflow orchestration (for workflow examples)
- `Azure.Identity` - Azure authentication
