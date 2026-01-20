# AI Examples Documentation

This document provides detailed documentation for the .NET 10 examples using the Microsoft Agent Framework.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Project 1: SimpleAgentWithTools](#project-1-simpleagentwithtools)
- [Project 2: WorkflowSequentialPipeline](#project-2-workflowsequentialpipeline)
- [Project 3: CustomerSupportWorkflow](#project-3-customersupportworkflow)
- [Configuration](#configuration)
- [Key Concepts](#key-concepts)
- [Extending the Examples](#extending-the-examples)

---

## Overview

These examples demonstrate core capabilities of the Microsoft Agent Framework:

| Example | Pattern | Key Learning |
|---------|---------|--------------|
| SimpleAgentWithTools | Single agent with tools | Function calling, conversation threads |
| WorkflowSequentialPipeline | Sequential orchestration | Agent chaining, streaming events |
| CustomerSupportWorkflow | Conditional routing | Switch-case workflows, custom executors |

---

## Architecture

### Package Dependencies

```
Microsoft.Agents.AI.OpenAI     - Core agent functionality
Microsoft.Agents.AI.Workflows  - Workflow orchestration
Azure.AI.OpenAI                - Azure OpenAI client
Azure.Identity                 - Azure authentication
Microsoft.Extensions.AI.OpenAI - AI extensions
```

### Core Types

```
AIAgent                 - Base class for all agents
ChatClientAgent         - Agent implementation using IChatClient
AgentThread             - Conversation history container
AgentRunResponse        - Response from agent execution

Workflow                - Compiled workflow graph
WorkflowBuilder         - Fluent API for building workflows
Executor<TIn, TOut>     - Processing unit in workflows
IWorkflowContext        - Execution context for executors
```

---

## Project 1: SimpleAgentWithTools

### Purpose

Demonstrates creating an AI agent with callable function tools that the model can invoke to answer questions.

### Architecture

```
User Query → AIAgent → [Tool Selection] → Function Execution → Response
                ↓
         AgentThread (maintains conversation history)
```

### Key Code Patterns

#### Creating Function Tools

```csharp
[Description("Get the current weather for a specified location.")]
static string GetWeather([Description("The city name")] string location)
{
    // Implementation
    return $"Weather in {location}: Sunny, 22°C";
}
```

- Use `[Description]` attribute on methods and parameters
- The description is passed to the LLM for tool selection
- Return type should be `string` for simple tools

#### Registering Tools with Agent

```csharp
AIAgent agent = chatClient.CreateAIAgent(
    name: "Assistant",
    instructions: "You are a helpful assistant...",
    tools: [
        AIFunctionFactory.Create(GetWeather),
        AIFunctionFactory.Create(GetCurrentTime)
    ]);
```

- `AIFunctionFactory.Create()` converts methods to `AITool` instances
- Tools are passed as a collection to `CreateAIAgent()`

#### Multi-turn Conversations

```csharp
AgentThread thread = agent.GetNewThread();

// First turn
await agent.RunAsync("What's the weather in Seattle?", thread);

// Second turn - context preserved
await agent.RunAsync("What about Tokyo?", thread);
```

- `AgentThread` maintains conversation history
- Pass the same thread to preserve context across turns

### Files

| File | Description |
|------|-------------|
| `Program.cs` | Main application with agent setup and tools |
| `SimpleAgentWithTools.csproj` | Project configuration |
| `README.md` | Project-specific documentation |

---

## Project 2: WorkflowSequentialPipeline

### Purpose

Demonstrates sequential workflow orchestration where multiple agents are chained together, with each agent's output becoming the next agent's input.

### Architecture

```
User Input → Translator → Summarizer → Reviewer → Final Output
                ↓              ↓            ↓
          ChatMessage    ChatMessage   ChatMessage
```

### Key Code Patterns

#### Creating Specialized Agents

```csharp
AIAgent translator = new ChatClientAgent(
    chatClient,
    instructions: "Translate the provided text to French...",
    name: "Translator");

AIAgent summarizer = new ChatClientAgent(
    chatClient,
    instructions: "Summarize the French text in 2-3 sentences...",
    name: "Summarizer");
```

- Use `ChatClientAgent` directly for workflow agents
- Each agent has specialized instructions for its role

#### Building Sequential Workflows

```csharp
Workflow workflow = new WorkflowBuilder(translator)
    .AddEdge(translator, summarizer)
    .AddEdge(summarizer, reviewer)
    .Build();
```

- `WorkflowBuilder` starts with the entry executor
- `AddEdge()` connects executors in sequence
- `Build()` compiles the workflow graph

#### Streaming Execution

```csharp
await using StreamingRun run = await InProcessExecution.StreamAsync(
    workflow,
    new ChatMessage(ChatRole.User, inputText));

// Trigger agent processing
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// Process events
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case AgentRunUpdateEvent agentUpdate:
            Console.Write(agentUpdate.Update.Text);
            break;
        case ExecutorCompletedEvent completed:
            Console.WriteLine($"[{completed.ExecutorId} completed]");
            break;
    }
}
```

- `InProcessExecution.StreamAsync()` starts streaming execution
- `TurnToken` triggers agent processing (required for agents in workflows)
- `WatchStreamAsync()` yields events as they occur

### Workflow Events

| Event Type | Description |
|------------|-------------|
| `AgentRunUpdateEvent` | Streaming text from an agent |
| `ExecutorCompletedEvent` | An executor finished processing |
| `WorkflowOutputEvent` | Final workflow output |

### Files

| File | Description |
|------|-------------|
| `Program.cs` | Workflow setup and streaming execution |
| `WorkflowSequentialPipeline.csproj` | Project configuration |
| `README.md` | Project-specific documentation |

---

## Project 3: CustomerSupportWorkflow

### Purpose

An advanced customer support workflow demonstrating enterprise-grade patterns including Dependency Injection, AI-Powered Triage, Escalation Flow, and Conversation History.

### Architecture

```
                         ┌─────────────────────────────────┐
                         │      DI Container               │
                         │  IChatClient, IEscalation,      │
                         │  IConversationHistory           │
                         └───────────────┬─────────────────┘
                                         │
Customer Query → AI Triage (Structured Output) → [Switch]
                     │                              ├── billing   → BillingAgent ──┐
                     │                              ├── technical → TechAgent ─────┤
                     │                              ├── refund    → RefundAgent ───┤
                     │                              ├── escalate  → Escalation ◀───┤
                     │                              └── default   → GeneralAgent ──┘
                     │
              (RequiresEscalation?)
                     │
                     └──────────────────────────────────────▶ Escalation Handler
```

### Key Features

#### 1. Dependency Injection

```csharp
var services = new ServiceCollection();

// Configure logging
services.AddLogging(builder => builder.AddConsole());

// Register services
services.AddSingleton<IChatClient>(sp => CreateChatClient());
services.AddSingleton<IConversationHistoryService, InMemoryConversationHistoryService>();
services.AddSingleton<IEscalationService, ConsoleEscalationService>();
services.AddSingleton<SupportAgentFactory>();

var serviceProvider = services.BuildServiceProvider();
```

#### 2. AI-Powered Triage with Structured Output

```csharp
// Triage result with full classification
class TriageResult
{
    string Category;        // billing, technical, refund, general
    double Confidence;      // 0.0 - 1.0
    Priority Priority;      // Low, Normal, High, Urgent
    Sentiment Sentiment;    // Positive, Neutral, Frustrated, Angry
    bool RequiresEscalation;
    string? EscalationReason;
}

// Triage agent with structured output
AIAgent triageAgent = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        ChatOptions = new ChatOptions
        {
            Instructions = "Analyze and classify customer queries...",
            ResponseFormat = ChatResponseFormat.ForJsonSchema<TriageResult>()
        }
    });
```

#### 3. Escalation Flow

Escalation can be triggered two ways:

**Automatic (AI Triage):**
- Legal matters mentioned
- Enterprise/contract issues
- Angry customer sentiment
- Security concerns
- Amount > $1000

**Agent-Requested:**
```csharp
// Agents end response with escalation marker
"[ESCALATE: reason for escalation]"

// Workflow detects and routes to escalation
.AddEdge(billingExecutor, escalationExecutor, condition: IsEscalationRequest)
```

#### 4. Conversation History

```csharp
// Service interface
interface IConversationHistoryService
{
    Task AddMessageAsync(string customerId, string role, string content);
    Task<List<ConversationMessage>> GetHistoryAsync(string customerId);
    Task ClearHistoryAsync(string customerId);
}

// Context-aware agent responses
var history = await _historyService.GetHistoryAsync(customerId);
await _agent.RunAsync($"Query: {query}\n\nHistory:\n{historyContext}");
```

### Services

| Service | Interface | Purpose |
|---------|-----------|---------|
| `InMemoryConversationHistoryService` | `IConversationHistoryService` | Per-customer message storage |
| `ConsoleEscalationService` | `IEscalationService` | Ticket creation, supervisor notification |
| `SupportAgentFactory` | - | Creates specialized agents via DI |

### Executors

| Executor | Input → Output | Description |
|----------|----------------|-------------|
| `AITriageExecutor` | `ChatMessage` → `TriageResult` | AI classification with structured output |
| `SupportAgentExecutor` | `TriageResult` → `SupportResponse` | Support with escalation capability |
| `EscalationExecutor` | `object` → (terminal) | Creates tickets, notifies supervisors |

### Files

| File | Description |
|------|-------------|
| `Program.cs` | Full implementation with DI, services, executors |
| `CustomerSupportWorkflow.csproj` | Project with DI packages |
| `README.md` | Detailed documentation |

---

## Configuration

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `USE_AZURE_OPENAI` | No | `false` | Set to `true` for Azure OpenAI |
| `AZURE_OPENAI_ENDPOINT` | If Azure | - | Azure OpenAI resource endpoint |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | No | `gpt-4o-mini` | Model deployment name |
| `OPENAI_API_KEY` | If not Azure | - | OpenAI API key |
| `OPENAI_MODEL` | No | `gpt-4o-mini` | OpenAI model name |

### Azure OpenAI Setup

```bash
export USE_AZURE_OPENAI="true"
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini"

# Authenticate with Azure CLI
az login
```

### OpenAI API Setup

```bash
export OPENAI_API_KEY="sk-..."
export OPENAI_MODEL="gpt-4o-mini"
```

---

## Key Concepts

### Agents vs Executors

| Concept | Description | Use Case |
|---------|-------------|----------|
| **AIAgent** | AI-powered processing unit | Natural language tasks |
| **Executor** | Deterministic processing unit | Data transformation, routing |

### Workflow Patterns

| Pattern | Method | Description |
|---------|--------|-------------|
| Sequential | `AddEdge(A, B)` | A's output flows to B |
| Conditional | `AddSwitch()` | Route based on conditions |
| Parallel | `AddFanOutEdge()` | Send to multiple executors |
| Aggregation | `AddFanInEdge()` | Collect from multiple sources |

### Thread vs Workflow

| Feature | AgentThread | Workflow |
|---------|-------------|----------|
| Scope | Single agent | Multiple executors |
| History | Preserved | Per-executor |
| Use | Multi-turn chat | Complex orchestration |

---

## Extending the Examples

### Adding New Tools

1. Define a static method with `[Description]` attributes
2. Register with `AIFunctionFactory.Create()`
3. Add to agent's tools collection

### Adding Workflow Stages

1. Create new agent or executor
2. Connect with `AddEdge()` in WorkflowBuilder
3. Update `WithOutputFrom()` if producing final output

### Creating Custom Executors

```csharp
internal sealed class MyExecutor() : Executor<InputType, OutputType>("MyExecutor")
{
    public override async ValueTask<OutputType> HandleAsync(
        InputType message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // Process input
        // Optionally use context.SendMessageAsync() for side outputs
        // Return result
    }
}
```

### Adding Parallel Processing

```csharp
var workflow = new WorkflowBuilder(startExecutor)
    .AddFanOutEdge(startExecutor, [agentA, agentB, agentC])
    .AddFanInEdge([agentA, agentB, agentC], aggregator)
    .WithOutputFrom(aggregator)
    .Build();
```

---

## Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| `AZURE_OPENAI_ENDPOINT is not set` | Missing env var | Set the environment variable |
| `Cannot convert ChatClient to IChatClient` | Wrong client type | Use `.AsIChatClient()` for workflows |
| Agent not responding in workflow | Missing TurnToken | Call `run.TrySendMessageAsync(new TurnToken())` |
| Executor not reached | Condition not matching | Check condition function logic |

### Debugging Workflows

```csharp
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    Console.WriteLine($"Event: {evt.GetType().Name}");
    if (evt is ExecutorCompletedEvent completed)
    {
        Console.WriteLine($"  Executor: {completed.ExecutorId}");
        Console.WriteLine($"  Data: {completed.Data}");
    }
}
```
