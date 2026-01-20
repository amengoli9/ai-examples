# Workflow Sequential Pipeline

This sample demonstrates a sequential workflow pipeline where multiple AI agents are chained together. Each agent's output becomes the input for the next agent in the pipeline.

## Pipeline Architecture

```
User Input → Translator → Summarizer → Reviewer → Final Output
                ↓              ↓            ↓
          (French text)  (French summary)  (English + Quality Review)
```

## Agents

1. **Translator**: Converts input text to French
2. **Summarizer**: Creates a 2-3 sentence summary in French
3. **Reviewer**: Translates back to English and provides quality assessment

## Features

- Sequential agent orchestration using `WorkflowBuilder`
- Streaming output from each agent
- Event-based progress tracking
- Support for both Azure OpenAI and OpenAI API

## Prerequisites

- .NET 10.0 SDK
- Either:
  - Azure OpenAI resource with a deployed model, OR
  - OpenAI API key

## Configuration

### Using OpenAI API (default)

```bash
export OPENAI_API_KEY="your-openai-api-key"
export OPENAI_MODEL="gpt-4o-mini"  # Optional
```

### Using Azure OpenAI

```bash
export USE_AZURE_OPENAI="true"
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini"  # Optional
```

## Running the Sample

```bash
dotnet run
```

## How It Works

1. **Workflow Construction**: Agents are connected using `WorkflowBuilder`:
   ```csharp
   Workflow workflow = new WorkflowBuilder(translator)
       .AddEdge(translator, summarizer)
       .AddEdge(summarizer, reviewer)
       .Build();
   ```

2. **Streaming Execution**: The workflow streams results using `InProcessExecution.StreamAsync()`:
   ```csharp
   await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, message);
   await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
   ```

3. **Event Processing**: Events are processed as they arrive:
   - `AgentRunUpdateEvent`: Streaming text from agents
   - `ExecutorCompletedEvent`: Agent finished processing
   - `WorkflowOutputEvent`: Final workflow output

## Example Output

```
=== Sequential Workflow Pipeline Demo ===
Pipeline: User Input -> Translator (French) -> Summarizer -> Reviewer

Input Text:
The Microsoft Agent Framework is a comprehensive multi-language framework...

--- Processing through pipeline ---

Le Microsoft Agent Framework est un framework multi-langage complet...

[Translator completed]

Le framework Microsoft Agent est une solution complète pour créer des agents IA...

[Summarizer completed]

[English Translation]
The Microsoft Agent Framework is a complete solution for creating AI agents...

[Quality Assessment]
The summary accurately captures the main points...

[Reviewer completed]

=== Pipeline Complete ===
```
