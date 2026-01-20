# Simple Agent with Tools

This sample demonstrates how to create a basic AI agent with function tools using the Microsoft Agent Framework.

## Features

- Create an AI agent with custom function tools
- Support for both Azure OpenAI and OpenAI API
- Multi-turn conversation with conversation history
- Two example tools: `GetWeather` and `GetCurrentTime`

## Prerequisites

- .NET 10.0 SDK
- Either:
  - Azure OpenAI resource with a deployed model, OR
  - OpenAI API key

## Configuration

### Using OpenAI API (default)

```bash
export OPENAI_API_KEY="your-openai-api-key"
export OPENAI_MODEL="gpt-4o-mini"  # Optional, defaults to gpt-4o-mini
```

### Using Azure OpenAI

```bash
export USE_AZURE_OPENAI="true"
export AZURE_OPENAI_ENDPOINT="https://mengoale-talks.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-5-mini"  # Optional, defaults to gpt-4o-mini
```

Azure OpenAI uses `AzureCliCredential` for authentication. Make sure you're logged in with `az login`.

## Running the Sample

```bash
dotnet run
```

## How It Works

1. **Agent Creation**: The agent is created with instructions and function tools using `CreateAIAgent()`.

2. **Function Tools**: Tools are defined as static methods with `[Description]` attributes:
   ```csharp
   [Description("Get the current weather for a specified location.")]
   static string GetWeather([Description("The city name")] string location) => ...
   ```

3. **Tool Registration**: Tools are registered using `AIFunctionFactory.Create()`:
   ```csharp
   tools: [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(GetCurrentTime)]
   ```

4. **Conversation Thread**: An `AgentThread` maintains conversation history across multiple turns.

## Example Output

```
=== Simple Agent with Tools Demo ===
Ask questions about weather or time. Type 'quit' to exit.

User: What's the weather like in Seattle?
Assistant: The weather in Seattle is currently rainy with a temperature of 12°C (54°F).

User: What time is it in Tokyo?
Assistant: The current time in Tokyo (JST) is 2024-01-15 14:30:45.
```
