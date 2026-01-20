// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates creating a simple AI agent with function tools.
// The agent can call GetWeather and GetCurrentTime functions to answer user questions.
// Supports both Azure OpenAI and OpenAI API configurations.

using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

// Configure the chat client based on environment variables
ChatClient chatClient = GetChatClient();

// Create an agent with function tools
AIAgent agent = chatClient.CreateAIAgent(
    name: "Assistant",
    instructions: "You are a helpful assistant with access to weather and time information. " +
                  "Use the available tools to answer questions about weather conditions and current time.",
    tools: [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(GetCurrentTime)]);

// Create a thread for multi-turn conversation
AgentThread thread = agent.GetNewThread();

Console.WriteLine("=== Simple Agent with Tools Demo ===");
Console.WriteLine("Ask questions about weather or time. Type 'quit' to exit.\n");

// Demo queries
string[] demoQueries =
[
    "What's the weather like in Seattle?",
    "What time is it in Tokyo?",
    "Compare the weather in London and Paris"
];

foreach (string query in demoQueries)
{
    Console.WriteLine($"User: {query}");
    AgentRunResponse response = await agent.RunAsync(query, thread);
    Console.WriteLine($"Assistant: {response}\n");
}

// Interactive mode
Console.WriteLine("--- Interactive Mode ---");
while (true)
{
    Console.Write("You: ");
    string? input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    AgentRunResponse response = await agent.RunAsync(input, thread);
    Console.WriteLine($"Assistant: {response}\n");
}

// Function tools
[Description("Get the current weather for a specified location.")]
static string GetWeather([Description("The city name to get weather for")] string location)
{
    // Simulated weather data - in a real application, this would call a weather API
    var weatherData = new Dictionary<string, (string condition, int tempC)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Seattle"] = ("Rainy", 12),
        ["London"] = ("Cloudy", 15),
        ["Paris"] = ("Sunny", 22),
        ["Tokyo"] = ("Clear", 18),
        ["New York"] = ("Partly Cloudy", 20),
        ["Sydney"] = ("Warm", 28)
    };

    if (weatherData.TryGetValue(location, out var weather))
    {
        return $"Weather in {location}: {weather.condition}, {weather.tempC}°C ({weather.tempC * 9 / 5 + 32}°F)";
    }

    return $"Weather in {location}: Mild conditions, approximately 18°C (64°F)";
}

[Description("Get the current time in a specified timezone.")]
static string GetCurrentTime([Description("The timezone name (e.g., UTC, PST, EST, JST, GMT)")] string timezone)
{
    // Timezone offset mappings
    var timezoneOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["UTC"] = 0,
        ["GMT"] = 0,
        ["PST"] = -8,
        ["PDT"] = -7,
        ["EST"] = -5,
        ["EDT"] = -4,
        ["CST"] = -6,
        ["CDT"] = -5,
        ["MST"] = -7,
        ["MDT"] = -6,
        ["JST"] = 9,
        ["CET"] = 1,
        ["CEST"] = 2,
        ["AEST"] = 10,
        ["AEDT"] = 11
    };

    DateTime utcNow = DateTime.UtcNow;

    if (timezoneOffsets.TryGetValue(timezone.ToUpperInvariant(), out int offset))
    {
        DateTime localTime = utcNow.AddHours(offset);
        return $"Current time in {timezone.ToUpperInvariant()}: {localTime:yyyy-MM-dd HH:mm:ss}";
    }

    return $"Current time in UTC: {utcNow:yyyy-MM-dd HH:mm:ss} (timezone '{timezone}' not recognized, showing UTC)";
}

// Helper to configure chat client
static ChatClient GetChatClient()
{
    bool useAzure = Environment.GetEnvironmentVariable("USE_AZURE_OPENAI")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

    if (useAzure)
    {
        string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT environment variable is not set.");
        string deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

        Console.WriteLine($"Using Azure OpenAI: {endpoint} (deployment: {deploymentName})");

        return new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
            .GetChatClient(deploymentName);
    }
    else
    {
        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set. Set USE_AZURE_OPENAI=true to use Azure OpenAI instead.");
        string model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";

        Console.WriteLine($"Using OpenAI API (model: {model})");

        return new OpenAIClient(apiKey)
            .GetChatClient(model);
    }
}
