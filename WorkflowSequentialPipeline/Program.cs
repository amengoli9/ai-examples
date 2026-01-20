// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates a sequential workflow pipeline using the Microsoft Agent Framework.
// The workflow chains three AI agents: Translator -> Summarizer -> Reviewer
// Each agent's output flows as input to the next agent in the pipeline.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;

// Configure the chat client
IChatClient chatClient = GetChatClient();

// Create specialized agents for the pipeline
AIAgent translator = new ChatClientAgent(
    chatClient,
    instructions: """
        You are a professional translator. Translate the provided text to French.
        Maintain the original meaning and tone. Output only the French translation.
        """,
    name: "Translator");

AIAgent summarizer = new ChatClientAgent(
    chatClient,
    instructions: """
        You are a summarization expert. Take the French text provided and create a concise
        summary in 2-3 sentences. Keep the summary in French.
        """,
    name: "Summarizer");

AIAgent reviewer = new ChatClientAgent(
    chatClient,
    instructions: """
        You are a quality reviewer. Review the French summary provided and:
        1. Translate it back to English
        2. Provide a brief quality assessment (accuracy, clarity, completeness)
        Format your response as:
        [English Translation]
        <the translation>

        [Quality Assessment]
        <your assessment>
        """,
    name: "Reviewer");

// Build the sequential workflow pipeline
Workflow workflow = new WorkflowBuilder(translator)
    .AddEdge(translator, summarizer)
    .AddEdge(summarizer, reviewer)
    .Build();

Console.WriteLine("=== Sequential Workflow Pipeline Demo ===");
Console.WriteLine("Pipeline: User Input -> Translator (French) -> Summarizer -> Reviewer\n");

// Sample text to process
string inputText = """
    The Microsoft Agent Framework is a comprehensive multi-language framework for building,
    orchestrating, and deploying AI agents. It supports both .NET and Python implementations
    and provides everything from simple chat agents to complex multi-agent workflows with
    graph-based orchestration. Key features include streaming responses, checkpointing,
    human-in-the-loop capabilities, and time-travel debugging.
    """;

Console.WriteLine("Input Text:");
Console.WriteLine(inputText);
Console.WriteLine("\n--- Processing through pipeline ---\n");

// Execute the workflow with streaming
await using StreamingRun run = await InProcessExecution.StreamAsync(
    workflow,
    new ChatMessage(ChatRole.User, inputText));

// Send the turn token to trigger agent processing
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// Stream and display results from each agent
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case AgentRunUpdateEvent agentUpdate:
            // Show streaming output from agents
            Console.Write(agentUpdate.Update.Text);
            break;

        case ExecutorCompletedEvent executorCompleted:
            // Show when each executor completes
            Console.WriteLine($"\n\n[{executorCompleted.ExecutorId} completed]\n");
            break;

        case WorkflowOutputEvent workflowOutput:
            // Final workflow output
            Console.WriteLine("\n=== Final Output ===");
            Console.WriteLine(workflowOutput.Data);
            break;
    }
}

Console.WriteLine("\n=== Pipeline Complete ===");

// Helper to configure chat client
static IChatClient GetChatClient()
{
    bool useAzure = Environment.GetEnvironmentVariable("USE_AZURE_OPENAI")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

    if (useAzure)
    {
        string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT environment variable is not set.");
        string deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

        Console.WriteLine($"Using Azure OpenAI: {endpoint} (deployment: {deploymentName})\n");

        return new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
            .GetChatClient(deploymentName)
            .AsIChatClient();
    }
    else
    {
        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set. Set USE_AZURE_OPENAI=true to use Azure OpenAI instead.");
        string model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

        Console.WriteLine($"Using OpenAI API (model: {model})\n");

        return new OpenAIClient(apiKey)
            .GetChatClient(model)
            .AsIChatClient();
    }
}
