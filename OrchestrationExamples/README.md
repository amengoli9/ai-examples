# Orchestration Examples

.NET examples demonstrating different orchestration patterns in the Microsoft Agent Framework with real-world banking and insurance business scenarios.

## Features

Each example includes:
- **Dependency Injection (DI)** - Using `Microsoft.Extensions.DependencyInjection`
- **OpenTelemetry** - Traces and metrics to Aspire Dashboard or Azure Monitor
- **Console Output** - Interactive console interface with streaming responses
- **DevUI** - Web-based UI for testing agents at `/devui`

## Examples

| Project | Orchestration Type | Scenario | Agents |
|---------|-------------------|----------|--------|
| [Sequential](./Sequential/) | Pipeline | Loan Application Processing | Document Collector → Credit Analyst → Risk Assessor → Loan Officer |
| [Concurrent](./Concurrent/) | Fan-out/Fan-in | Insurance Claim Analysis | Policy Expert, Fraud Detector, Damage Assessor, Compliance Officer (parallel) |
| [GroupChat](./GroupChat/) | Round-Robin Discussion | Risk Committee Meeting | CRO, CFO, Compliance Head, Operations Director |
| [Handoffs](./Handoffs/) | Dynamic Routing | Banking Customer Service | Triage → Account Services, Loan Services, Investment Advisor, Fraud Support |

## Prerequisites

1. **.NET 9.0 SDK** or later
2. **Azure OpenAI** deployment with a chat model (e.g., `gpt-4o-mini`)
3. **Azure CLI** authenticated (`az login`)

## Configuration

Set the following environment variables:

```bash
# Required
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini"

# Optional - OpenTelemetry
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"
export APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=..."
```

Or use `appsettings.json` / `appsettings.Development.json` in each project.

## Running the Examples

### Option 1: Run with DevUI (recommended for testing)

```bash
# From the solution directory
dotnet run --project Sequential

# Access DevUI at: https://localhost:5001/devui
```

### Option 2: Run all projects

```bash
# Build all
dotnet build

# Run individual projects
dotnet run --project Sequential
dotnet run --project Concurrent
dotnet run --project GroupChat
dotnet run --project Handoffs
```

## Observability with Aspire Dashboard

To visualize telemetry data, run the Aspire Dashboard:

```bash
docker run -d \
  -p 18888:18888 \
  -p 4317:18889 \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

- **Dashboard**: http://localhost:18888
- **OTLP Endpoint**: http://localhost:4317 (gRPC) or http://localhost:4318 (HTTP)

## Project Structure

```
OrchestrationExamples/
├── OrchestrationExamples.sln
├── Directory.Build.props          # Shared MSBuild properties
├── README.md
├── PLAN.md                        # Detailed planning document
├── Sequential/
│   ├── Sequential.csproj
│   └── Program.cs                 # Loan Application Pipeline
├── Concurrent/
│   ├── Concurrent.csproj
│   └── Program.cs                 # Insurance Claim Analysis
├── GroupChat/
│   ├── GroupChat.csproj
│   └── Program.cs                 # Risk Committee Meeting
└── Handoffs/
    ├── Handoffs.csproj
    └── Program.cs                 # Banking Customer Service
```

## Architecture

### Common Pattern

Each example follows this architecture:

1. **OpenTelemetry Setup** - Configures tracing, metrics, and logging
2. **DI Registration** - Registers `IChatClient` and agents via `WebApplicationBuilder`
3. **Agent Definitions** - Business-specific agent personas with clear instructions
4. **Workflow Registration** - Builds the orchestration pattern using `AgentWorkflowBuilder`
5. **DevUI/API** - Maps endpoints for web UI and OpenAI-compatible API
6. **Console Output** - Displays startup information and usage examples

### Key Classes

- `AgentWorkflowBuilder` - Factory for building orchestration patterns
- `OpenTelemetryAgent` - Wrapper that adds telemetry to any agent
- `RoundRobinGroupChatManager` - Turn management for group chats
- `WebApplicationBuilder.AddAIAgent()` - DI extension for registering agents
- `WebApplicationBuilder.AddWorkflow()` - DI extension for registering workflows

## Sample Interactions

### Sequential (Loan Application)
```
User: I want to apply for a $50,000 home improvement loan. I've had a stable job
      for 5 years with an income of $80,000/year.
```

### Concurrent (Insurance Claim)
```
User: I need to file a claim for water damage. A pipe burst and caused $15,000
      in damage to my floors and walls. Policy number: HO-12345.
```

### Group Chat (Risk Committee)
```
User: We are considering expanding into cryptocurrency custody services for our
      banking clients. What are the key considerations?
```

### Handoffs (Customer Service)
```
User: I noticed a charge I don't recognize on my account for $500.
```

## License

See the main repository license.
