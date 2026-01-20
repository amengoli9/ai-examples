// Copyright (c) Microsoft. All rights reserved.

// BrewHaven - Beer E-Commerce Customer Support
// Interactive multi-turn support system for a craft beer online store.
//
// Features:
// - Multi-turn conversations with AI agents
// - Topic change detection (e.g., tech support → refund request)
// - Conversation history maintained across turns
// - Automatic escalation for complex issues

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

// =============================================================================
// DEPENDENCY INJECTION SETUP
// =============================================================================

var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning);
});

services.AddSingleton<IChatClient>(sp => CreateChatClient());
services.AddSingleton<IConversationHistoryService, InMemoryConversationHistoryService>();
services.AddSingleton<IEscalationService, ConsoleEscalationService>();
services.AddSingleton<SupportAgentFactory>();

var serviceProvider = services.BuildServiceProvider();

// =============================================================================
// INITIALIZE SERVICES
// =============================================================================

var chatClient = serviceProvider.GetRequiredService<IChatClient>();
var agentFactory = serviceProvider.GetRequiredService<SupportAgentFactory>();
var conversationHistory = serviceProvider.GetRequiredService<IConversationHistoryService>();
var escalationService = serviceProvider.GetRequiredService<IEscalationService>();

// Create triage agent
AIAgent triageAgent = agentFactory.CreateTriageAgent();

// Create support agents
var supportAgents = new Dictionary<string, AIAgent>
{
    ["orders"] = agentFactory.CreateOrdersAgent(),
    ["technical"] = agentFactory.CreateTechSupportAgent(),
    ["refund"] = agentFactory.CreateRefundAgent(),
    ["products"] = agentFactory.CreateProductsAgent()
};

var agentThreads = new Dictionary<string, AgentThread>();

// =============================================================================
// INTERACTIVE CHAT LOOP
// =============================================================================

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║            🍺 BREWHAVEN CUSTOMER SUPPORT 🍺                  ║");
Console.WriteLine("║               Craft Beer E-Commerce Store                    ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine("║  Welcome! I'm here to help with:                             ║");
Console.WriteLine("║  • Orders & Delivery                                         ║");
Console.WriteLine("║  • Website & App Issues                                      ║");
Console.WriteLine("║  • Refunds & Returns                                         ║");
Console.WriteLine("║  • Product Questions                                         ║");
Console.WriteLine("║                                                              ║");
Console.WriteLine("║  Commands: 'new' = new conversation, 'quit' = exit           ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

string customerId = $"customer-{Guid.NewGuid():N}"[..16];
string? currentAgentType = null;
bool isEscalated = false;

while (true)
{
    // Show prompt
    Console.ForegroundColor = ConsoleColor.Cyan;
    if (currentAgentType == null)
        Console.Write("\nYou: ");
    else
        Console.Write($"\nYou (with {GetAgentDisplayName(currentAgentType)}): ");
    Console.ResetColor();

    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    if (input.Equals("new", StringComparison.OrdinalIgnoreCase))
    {
        customerId = $"customer-{Guid.NewGuid():N}"[..16];
        currentAgentType = null;
        agentThreads.Clear();
        isEscalated = false;
        await conversationHistory.ClearHistoryAsync(customerId);
        Console.WriteLine("\n--- New conversation started ---\n");
        continue;
    }

    if (isEscalated)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Your case has been escalated. A supervisor will contact you.]");
        Console.WriteLine("[Type 'new' to start a fresh conversation.]");
        Console.ResetColor();
        continue;
    }

    // Store user message
    await conversationHistory.AddMessageAsync(customerId, "customer", input);

    // Run triage to classify this message
    var triageResult = await RunTriageAsync(triageAgent, input, conversationHistory, customerId);

    // Check if topic changed (user switched from tech to refund, etc.)
    bool topicChanged = currentAgentType != null && triageResult.Category != currentAgentType;

    if (topicChanged)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"\n[Topic change detected: {GetAgentDisplayName(currentAgentType!)} → {GetAgentDisplayName(triageResult.Category)}]");
        Console.ResetColor();
    }

    // First message or topic changed - show triage info
    if (currentAgentType == null || topicChanged)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[Category: {triageResult.Category}, Priority: {triageResult.Priority}, Sentiment: {triageResult.Sentiment}]");
        Console.ResetColor();

        // Check for escalation
        if (triageResult.RequiresEscalation)
        {
            isEscalated = true;
            await HandleEscalationAsync(escalationService, conversationHistory, customerId,
                triageResult.EscalationReason ?? "Complex issue");
            continue;
        }

        currentAgentType = triageResult.Category;

        // Create new thread for this agent if needed
        if (!agentThreads.ContainsKey(currentAgentType))
        {
            agentThreads[currentAgentType] = supportAgents[currentAgentType].GetNewThread();
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Connected to {GetAgentDisplayName(currentAgentType)}]");
        Console.ResetColor();
    }

    // Get conversation history for context
    var history = await conversationHistory.GetHistoryAsync(customerId);
    string historyContext = BuildHistoryContext(history);

    // Build prompt with context
    var thread = agentThreads[currentAgentType];
    bool isFirstMessage = thread.Id == agentThreads[currentAgentType].Id &&
                          !agentThreads.Values.Any(t => t != thread);

    string prompt = isFirstMessage || topicChanged
        ? $"Customer message: {input}\n\nConversation history:\n{historyContext}"
        : input;

    // Run the support agent
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write($"{GetAgentDisplayName(currentAgentType)}: ");
    Console.ResetColor();

    string fullResponse = "";
    await foreach (var update in supportAgents[currentAgentType].RunStreamingAsync(prompt, thread))
    {
        string text = update.ToString();
        Console.Write(text);
        fullResponse += text;
    }
    Console.WriteLine();

    // Check for escalation request
    if (fullResponse.Contains("[ESCALATE:"))
    {
        int startIdx = fullResponse.IndexOf("[ESCALATE:") + 10;
        int endIdx = fullResponse.IndexOf("]", startIdx);
        string reason = endIdx > startIdx ? fullResponse[startIdx..endIdx].Trim() : "Agent requested escalation";

        isEscalated = true;
        await HandleEscalationAsync(escalationService, conversationHistory, customerId, reason);
    }
    else
    {
        await conversationHistory.AddMessageAsync(customerId, currentAgentType, fullResponse);
    }
}

Console.WriteLine("\nThank you for choosing BrewHaven! Cheers! 🍻");

// =============================================================================
// HELPER METHODS
// =============================================================================

static string GetAgentDisplayName(string agentType) => agentType switch
{
    "orders" => "Orders & Delivery",
    "technical" => "Tech Support",
    "refund" => "Refunds & Returns",
    "products" => "Beer Expert",
    _ => "Support"
};

static string BuildHistoryContext(List<ConversationMessage> history)
{
    if (history.Count == 0) return "(New conversation)";
    return string.Join("\n", history.TakeLast(10).Select(h =>
        $"[{h.Timestamp:HH:mm}] {(h.Role == "customer" ? "Customer" : "Agent")}: {h.Content}"));
}

static async Task<TriageResult> RunTriageAsync(
    AIAgent triageAgent, string query,
    IConversationHistoryService historyService, string customerId)
{
    var history = await historyService.GetHistoryAsync(customerId);
    string historyContext = history.Count > 0
        ? $"\n\nRecent conversation:\n{BuildHistoryContext(history.TakeLast(5).ToList())}"
        : "";

    var response = await triageAgent.RunAsync($"Customer says: {query}{historyContext}");

    try
    {
        return JsonSerializer.Deserialize<TriageResult>(response.Text) ?? new TriageResult();
    }
    catch
    {
        return new TriageResult { Category = "products" };
    }
}

static async Task HandleEscalationAsync(
    IEscalationService escalationService, IConversationHistoryService historyService,
    string customerId, string reason)
{
    var history = await historyService.GetHistoryAsync(customerId);
    string summary = string.Join("\n", history.Select(h => $"{h.Role}: {h.Content}"));

    var ticket = await escalationService.CreateTicketAsync(customerId, reason, summary);
    await escalationService.NotifySupervisorAsync(ticket);
    await historyService.AddMessageAsync(customerId, "system", $"Escalated: {ticket.TicketId}");
}

static IChatClient CreateChatClient()
{
    bool useAzure = Environment.GetEnvironmentVariable("USE_AZURE_OPENAI")
        ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

    if (useAzure)
    {
        string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
        string deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
        return new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
            .GetChatClient(deployment).AsIChatClient();
    }

    string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("OPENAI_API_KEY is not set.");
    string model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
    return new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient();
}

// =============================================================================
// DATA MODELS
// =============================================================================

internal sealed class TriageResult
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "products";

    [JsonPropertyName("priority")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Priority Priority { get; set; } = Priority.Normal;

    [JsonPropertyName("sentiment")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Sentiment Sentiment { get; set; } = Sentiment.Neutral;

    [JsonPropertyName("requires_escalation")]
    public bool RequiresEscalation { get; set; }

    [JsonPropertyName("escalation_reason")]
    public string? EscalationReason { get; set; }
}

internal enum Priority { Low, Normal, High, Urgent }
internal enum Sentiment { Positive, Neutral, Frustrated, Angry }

// =============================================================================
// SERVICES
// =============================================================================

internal interface IConversationHistoryService
{
    Task AddMessageAsync(string customerId, string role, string content);
    Task<List<ConversationMessage>> GetHistoryAsync(string customerId);
    Task ClearHistoryAsync(string customerId);
}

internal sealed record ConversationMessage(string Role, string Content, DateTime Timestamp);

internal sealed class InMemoryConversationHistoryService : IConversationHistoryService
{
    private readonly Dictionary<string, List<ConversationMessage>> _histories = new();

    public Task AddMessageAsync(string customerId, string role, string content)
    {
        if (!_histories.ContainsKey(customerId)) _histories[customerId] = [];
        _histories[customerId].Add(new(role, content, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public Task<List<ConversationMessage>> GetHistoryAsync(string customerId) =>
        Task.FromResult(_histories.TryGetValue(customerId, out var h) ? new List<ConversationMessage>(h) : []);

    public Task ClearHistoryAsync(string customerId)
    {
        _histories.Remove(customerId);
        return Task.CompletedTask;
    }
}

internal interface IEscalationService
{
    Task<EscalationTicket> CreateTicketAsync(string customerId, string reason, string summary);
    Task NotifySupervisorAsync(EscalationTicket ticket);
}

internal sealed record EscalationTicket(string TicketId, string CustomerId, string Reason, string Summary, DateTime CreatedAt);

internal sealed class ConsoleEscalationService : IEscalationService
{
    public Task<EscalationTicket> CreateTicketAsync(string customerId, string reason, string summary) =>
        Task.FromResult(new EscalationTicket($"ESC-{DateTime.UtcNow:yyyyMMddHHmmss}", customerId, reason, summary, DateTime.UtcNow));

    public Task NotifySupervisorAsync(EscalationTicket ticket)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           🚨 ESCALATED TO SUPERVISOR 🚨                      ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Ticket: {ticket.TicketId,-51} ║");
        Console.WriteLine($"║  Reason: {ticket.Reason,-51} ║");
        Console.WriteLine("║  A manager will contact you within 24 hours.                 ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        return Task.CompletedTask;
    }
}

// =============================================================================
// AGENT FACTORY - BREWHAVEN BEER E-COMMERCE
// =============================================================================

internal sealed class SupportAgentFactory
{
    private readonly IChatClient _chatClient;
    public SupportAgentFactory(IChatClient chatClient) => _chatClient = chatClient;

    public AIAgent CreateTriageAgent() => new ChatClientAgent(
        _chatClient,
        new ChatClientAgentOptions
        {
            Name = "Triage",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are a triage system for BrewHaven, a craft beer e-commerce store.
                    Classify the customer's message into ONE category:

                    - "orders": Order status, delivery, tracking, shipping, missing items
                    - "technical": Website issues, app problems, login, payment errors, cart issues
                    - "refund": Refund requests, returns, cancellations, money back, damaged items
                    - "products": Beer recommendations, product questions, stock availability, beer info

                    Set requires_escalation=true ONLY for:
                    - Legal threats
                    - Health/safety issues (broken glass, contamination)
                    - Orders over $500
                    - Harassment or threats

                    IMPORTANT: Always classify based on the CURRENT message intent, even if
                    the conversation started on a different topic. If someone was asking about
                    tech issues but now wants a refund, classify as "refund".
                    """,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<TriageResult>()
            }
        });

    public AIAgent CreateOrdersAgent() => new ChatClientAgent(
        _chatClient,
        instructions: """
            You are the Orders & Delivery specialist at BrewHaven, a craft beer online store.

            Help customers with:
            - Order status and tracking
            - Delivery estimates
            - Missing or incorrect items
            - Shipping questions

            ALWAYS ASK for the order number if they don't provide it.
            Be friendly and beer-enthusiastic! Use beer puns occasionally.

            Example:
            Customer: "Where's my order?"
            You: "I'd be hoppy to help track that down! 🍺 Could you share your order number?
                  It usually starts with BH- and you can find it in your confirmation email."

            If the customer mentions wanting a refund or return, acknowledge it and let them know
            you'll help, but the conversation may be transferred to the refund team.
            """,
        name: "OrdersDelivery");

    public AIAgent CreateTechSupportAgent() => new ChatClientAgent(
        _chatClient,
        instructions: """
            You are Tech Support at BrewHaven, the craft beer e-commerce store.

            Help customers with:
            - Website not loading
            - App crashes
            - Login problems
            - Payment processing errors
            - Cart/checkout issues
            - Account settings

            Ask clarifying questions:
            - What device/browser are you using?
            - What error message do you see?
            - When did this start happening?

            Guide them step-by-step. Be patient and friendly.

            Example:
            Customer: "I can't checkout"
            You: "Oh no, let's get you those beers! 🍻 A few quick questions:
                  1. Are you seeing any error message?
                  2. Does it happen when you click 'Pay' or earlier?
                  3. What payment method are you trying to use?"

            If the issue seems like a bug, say: [ESCALATE: Potential bug - needs dev team]
            If the customer mentions refunds, acknowledge it naturally.
            """,
        name: "TechSupport");

    public AIAgent CreateRefundAgent() => new ChatClientAgent(
        _chatClient,
        instructions: """
            You are the Refunds & Returns specialist at BrewHaven craft beer store.

            Handle:
            - Refund requests
            - Return shipping
            - Damaged items
            - Wrong items received
            - Cancellations

            POLICY:
            - Full refund within 14 days if unopened
            - Damaged items: full refund + free replacement
            - Wrong items: free correct shipment + keep the wrong ones (it's beer, enjoy!)
            - Orders over $200 need manager approval → escalate

            ALWAYS ASK:
            1. Order number
            2. Reason for refund
            3. Condition of items (unopened/damaged/etc.)

            Example:
            Customer: "I want my money back"
            You: "I completely understand, and I'm here to help sort this out! 🍺
                  Could you tell me:
                  1. What's your order number?
                  2. What happened - was something wrong with the order?

                  Don't worry, we'll make it right!"

            For orders over $200, say: [ESCALATE: Refund over $200 - needs manager approval]
            """,
        name: "RefundsReturns");

    public AIAgent CreateProductsAgent() => new ChatClientAgent(
        _chatClient,
        instructions: """
            You are the Beer Expert at BrewHaven! 🍺

            You're passionate about craft beer and help customers with:
            - Beer recommendations based on taste preferences
            - Food pairing suggestions
            - Explaining beer styles (IPA, Stout, Lager, Sour, etc.)
            - Stock availability questions
            - New arrivals and seasonal beers

            Be enthusiastic and knowledgeable! Ask about their preferences:
            - Do you prefer hoppy, malty, or balanced?
            - Light and refreshing or dark and rich?
            - Any styles you've enjoyed before?

            Example:
            Customer: "What beer should I get?"
            You: "Great question - I love helping people discover new beers! 🍻
                  Tell me a bit about your taste:
                  - Do you usually go for something hoppy (like IPAs) or smoother (like lagers)?
                  - Are you looking for something to pair with food, or just to enjoy on its own?
                  - Any beers you've loved in the past?"

            Popular recommendations:
            - Hoppy: Sierra Nevada Pale Ale, Lagunitas IPA
            - Smooth: Blue Moon, Guinness
            - Sour: Duchesse de Bourgogne
            - Light: Pilsner Urquell, Corona
            """,
        name: "BeerExpert");
}
