// Copyright (c) Microsoft. All rights reserved.

// Triage Orchestration Example: IT Support Ticket Routing
// This sample demonstrates a custom triage workflow where support tickets are
// intelligently routed to specialized executors based on ticket analysis.
// Pattern: Triage Agent analyzes → Routes to appropriate Executor → Response

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Triage;

public static class Program
{
    private const string SourceName = "Triage.ITSupport";
    private const string ServiceName = "ITSupportTriageService";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ============================================================
        // 1. OPENTELEMETRY CONFIGURATION
        // ============================================================
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
        var applicationInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(ServiceName, serviceVersion: "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName,
                ["service.instance.id"] = Environment.MachineName
            });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddSource(SourceName)
                    .AddSource("Microsoft.Agents.AI.*")
                    .AddSource("Microsoft.Extensions.AI.*")
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));

                if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
                {
                    tracing.AddAzureMonitorTraceExporter(options =>
                        options.ConnectionString = applicationInsightsConnectionString);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddMeter(SourceName)
                    .AddMeter("Microsoft.Agents.AI.*")
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            });

        // ============================================================
        // 2. AZURE OPENAI CLIENT SETUP WITH DI
        // ============================================================
        var endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]
            ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
        var deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";

        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            return new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
                .GetChatClient(deploymentName)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
                .Build();
        });

        // ============================================================
        // 3. AGENT DEFINITIONS - IT Support Specialists
        // ============================================================

        // Triage Agent - Analyzes and classifies tickets
        builder.AddAIAgent("triage-analyzer",
            """
            You are an IT Support Triage Analyzer.
            Your role is to analyze incoming support tickets and classify them into categories.

            Analyze the ticket and respond with a JSON object containing:
            {
                "category": "network|hardware|software|security|general",
                "priority": "low|medium|high|critical",
                "summary": "brief summary of the issue",
                "reasoning": "why you chose this category"
            }

            Categories:
            - network: Network connectivity, VPN, WiFi, DNS issues
            - hardware: Physical device problems, printers, monitors, peripherals
            - software: Application errors, installation, updates, crashes
            - security: Security concerns, suspicious activity, access issues
            - general: General inquiries, how-to questions, other issues
            """);

        // Network Specialist
        builder.AddAIAgent("network-specialist",
            """
            You are a Network Specialist in IT Support.
            You handle:
            - Network connectivity issues
            - VPN configuration and troubleshooting
            - WiFi problems
            - DNS resolution issues
            - Firewall and proxy settings

            Provide clear, step-by-step troubleshooting instructions.
            Always ask for relevant details if needed (IP address, network name, error messages).
            End with a resolution summary or escalation recommendation.
            """);

        // Hardware Specialist
        builder.AddAIAgent("hardware-specialist",
            """
            You are a Hardware Specialist in IT Support.
            You handle:
            - Computer hardware issues (desktop, laptop)
            - Printer problems
            - Monitor and display issues
            - Peripheral devices (keyboard, mouse, docking stations)
            - Hardware upgrades and replacements

            Provide practical troubleshooting steps.
            Determine if the issue requires physical intervention or can be resolved remotely.
            Include warranty and replacement information when relevant.
            """);

        // Software Specialist
        builder.AddAIAgent("software-specialist",
            """
            You are a Software Specialist in IT Support.
            You handle:
            - Application installation and configuration
            - Software crashes and errors
            - Operating system issues
            - Updates and patches
            - License and activation problems

            Provide detailed troubleshooting steps with commands when applicable.
            Consider compatibility issues and system requirements.
            Suggest workarounds when immediate fixes aren't available.
            """);

        // Security Specialist
        builder.AddAIAgent("security-specialist",
            """
            You are a Security Specialist in IT Support.
            You handle:
            - Security incidents and suspicious activity
            - Password and access issues
            - Malware and virus concerns
            - Phishing attempts
            - Compliance and security policy questions

            Treat all security concerns with appropriate urgency.
            Provide immediate protective actions when needed.
            Follow incident response procedures and document thoroughly.
            Escalate critical security incidents immediately.
            """);

        // General Support
        builder.AddAIAgent("general-support",
            """
            You are a General IT Support Specialist.
            You handle:
            - General IT inquiries
            - How-to questions
            - Service requests
            - Information requests
            - Issues that don't fit other categories

            Be helpful and informative.
            Direct users to appropriate resources when available.
            Create tickets for complex requests that need follow-up.
            """);

        // ============================================================
        // 4. WORKFLOW REGISTRATION - Custom Triage Pattern
        // ============================================================
        builder.AddWorkflow("it-support-triage", (sp, workflowName) =>
        {
            var chatClient = sp.GetRequiredService<IChatClient>();

            // Get all agents
            var triageAnalyzer = sp.GetRequiredKeyedService<AIAgent>("triage-analyzer");
            var networkSpecialist = sp.GetRequiredKeyedService<AIAgent>("network-specialist");
            var hardwareSpecialist = sp.GetRequiredKeyedService<AIAgent>("hardware-specialist");
            var softwareSpecialist = sp.GetRequiredKeyedService<AIAgent>("software-specialist");
            var securitySpecialist = sp.GetRequiredKeyedService<AIAgent>("security-specialist");
            var generalSupport = sp.GetRequiredKeyedService<AIAgent>("general-support");

            // Wrap agents with OpenTelemetry
            var wrappedTriage = new OpenTelemetryAgent(triageAnalyzer, SourceName) { EnableSensitiveData = true };
            var wrappedNetwork = new OpenTelemetryAgent(networkSpecialist, SourceName) { EnableSensitiveData = true };
            var wrappedHardware = new OpenTelemetryAgent(hardwareSpecialist, SourceName) { EnableSensitiveData = true };
            var wrappedSoftware = new OpenTelemetryAgent(softwareSpecialist, SourceName) { EnableSensitiveData = true };
            var wrappedSecurity = new OpenTelemetryAgent(securitySpecialist, SourceName) { EnableSensitiveData = true };
            var wrappedGeneral = new OpenTelemetryAgent(generalSupport, SourceName) { EnableSensitiveData = true };

            // Create custom executors
            var triageExecutor = new TriageAnalysisExecutor(wrappedTriage, chatClient);
            var networkExecutor = new SpecialistExecutor("NetworkSpecialist", wrappedNetwork);
            var hardwareExecutor = new SpecialistExecutor("HardwareSpecialist", wrappedHardware);
            var softwareExecutor = new SpecialistExecutor("SoftwareSpecialist", wrappedSoftware);
            var securityExecutor = new SpecialistExecutor("SecuritySpecialist", wrappedSecurity);
            var generalExecutor = new SpecialistExecutor("GeneralSupport", wrappedGeneral);
            var outputExecutor = new TriageOutputExecutor();

            // Build the workflow with conditional routing
            var workflowBuilder = new WorkflowBuilder(triageExecutor);

            // Add fan-out edge from triage to specialists with routing logic
            workflowBuilder.AddFanOutEdge<TriageResult>(
                triageExecutor,
                [networkExecutor, hardwareExecutor, softwareExecutor, securityExecutor, generalExecutor],
                GetTriageRouter()
            );

            // Connect all specialists to the output executor
            workflowBuilder.AddEdge(networkExecutor, outputExecutor);
            workflowBuilder.AddEdge(hardwareExecutor, outputExecutor);
            workflowBuilder.AddEdge(softwareExecutor, outputExecutor);
            workflowBuilder.AddEdge(securityExecutor, outputExecutor);
            workflowBuilder.AddEdge(generalExecutor, outputExecutor);

            // Set output from the output executor
            workflowBuilder.WithOutputFrom(outputExecutor);
            workflowBuilder.WithName(workflowName);

            return workflowBuilder.Build();
        }).AddAsAIAgent();

        // ============================================================
        // 5. DEVUI AND API CONFIGURATION
        // ============================================================
        builder.Services.AddOpenAIResponses();
        builder.Services.AddOpenAIConversations();

        var app = builder.Build();

        app.MapOpenAIResponses();
        app.MapOpenAIConversations();
        app.MapDevUI();

        // ============================================================
        // 6. CONSOLE OUTPUT
        // ============================================================
        var urls = app.Urls.Count > 0 ? string.Join(", ", app.Urls) : "https://localhost:5007";

        Console.WriteLine();
        Console.WriteLine("===============================================================");
        Console.WriteLine("        TRIAGE ORCHESTRATION: IT Support Ticket Routing        ");
        Console.WriteLine("===============================================================");
        Console.WriteLine();
        Console.WriteLine("  Custom Workflow Pattern:");
        Console.WriteLine("  User Request → Triage Analyzer → Routes to Specialist");
        Console.WriteLine();
        Console.WriteLine("  Available Specialists:");
        Console.WriteLine("  * Network Specialist    - VPN, WiFi, DNS issues");
        Console.WriteLine("  * Hardware Specialist   - Devices, printers, peripherals");
        Console.WriteLine("  * Software Specialist   - Apps, OS, installations");
        Console.WriteLine("  * Security Specialist   - Security incidents, access");
        Console.WriteLine("  * General Support       - Other inquiries");
        Console.WriteLine();
        Console.WriteLine($"  DevUI: {urls}/devui");
        Console.WriteLine($"  OTLP:  {otlpEndpoint}");
        Console.WriteLine();
        Console.WriteLine("===============================================================");
        Console.WriteLine();
        Console.WriteLine("Open DevUI in your browser to interact with the triage system.");
        Console.WriteLine();
        Console.WriteLine("Test examples:");
        Console.WriteLine("  1. \"I can't connect to the VPN from home. It says connection timed out.\"");
        Console.WriteLine("  2. \"My laptop screen is flickering and sometimes goes black.\"");
        Console.WriteLine("  3. \"Excel keeps crashing when I open large spreadsheets.\"");
        Console.WriteLine("  4. \"I received a suspicious email asking for my password.\"");
        Console.WriteLine();

        app.Run();
    }

    /// <summary>
    /// Creates a router function for triage-based routing to specialists.
    /// </summary>
    private static Func<TriageResult?, int, IEnumerable<int>> GetTriageRouter()
    {
        return (triageResult, targetCount) =>
        {
            if (triageResult is null)
            {
                return [4]; // Default to general support
            }

            return triageResult.Category.ToLowerInvariant() switch
            {
                "network" => [0],
                "hardware" => [1],
                "software" => [2],
                "security" => [3],
                _ => [4] // general
            };
        };
    }
}

// ============================================================
// CUSTOM EXECUTOR CLASSES
// ============================================================

/// <summary>
/// Result of the triage analysis.
/// </summary>
public sealed class TriageResult
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "general";

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "medium";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonIgnore]
    public string OriginalRequest { get; set; } = string.Empty;

    [JsonIgnore]
    public string TicketId { get; set; } = string.Empty;
}

/// <summary>
/// Response from a specialist.
/// </summary>
public sealed class SpecialistResponse
{
    public string TicketId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string Specialist { get; set; } = string.Empty;
}

/// <summary>
/// Executor that performs triage analysis on incoming tickets.
/// </summary>
internal sealed class TriageAnalysisExecutor : Executor<ChatMessage, TriageResult>
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AIAgent _triageAgent;
    private readonly IChatClient _chatClient;

    public TriageAnalysisExecutor(AIAgent triageAgent, IChatClient chatClient)
        : base("TriageAnalysisExecutor")
    {
        _triageAgent = triageAgent;
        _chatClient = chatClient;
    }

    public override async ValueTask<TriageResult> HandleAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // Generate ticket ID
        var ticketId = $"TKT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        // Analyze the ticket using the triage agent
        var response = await _triageAgent.RunAsync(message, cancellationToken: cancellationToken);

        // Parse the JSON response
        TriageResult? result = null;
        try
        {
            // Try to extract JSON from the response
            var responseText = response.Text;
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = responseText[jsonStart..(jsonEnd + 1)];
                result = JsonSerializer.Deserialize<TriageResult>(jsonContent, s_jsonOptions);
            }
        }
        catch
        {
            // If parsing fails, create a default result
        }

        result ??= new TriageResult
        {
            Category = "general",
            Priority = "medium",
            Summary = "Unable to categorize automatically",
            Reasoning = "Failed to parse triage response"
        };

        result.OriginalRequest = message.Text;
        result.TicketId = ticketId;

        // Store ticket info in workflow state
        await context.QueueStateUpdateAsync(ticketId, result, scopeName: "Tickets", cancellationToken);

        // Emit triage event
        await context.AddEventAsync(
            new WorkflowInfoEvent($"[TRIAGE] Ticket {ticketId} classified as {result.Category.ToUpperInvariant()} (Priority: {result.Priority})"),
            cancellationToken);

        return result;
    }
}

/// <summary>
/// Generic executor for specialist agents.
/// </summary>
internal sealed class SpecialistExecutor : Executor<TriageResult, SpecialistResponse>
{
    private readonly AIAgent _specialistAgent;
    private readonly string _specialistName;

    public SpecialistExecutor(string name, AIAgent specialistAgent)
        : base(name)
    {
        _specialistAgent = specialistAgent;
        _specialistName = name;
    }

    public override async ValueTask<SpecialistResponse> HandleAsync(
        TriageResult triageResult,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // Create a prompt that includes the triage context
        var contextualPrompt = $"""
            Ticket ID: {triageResult.TicketId}
            Priority: {triageResult.Priority}
            Category: {triageResult.Category}
            Summary: {triageResult.Summary}

            Original Request:
            {triageResult.OriginalRequest}

            Please provide a helpful response to resolve this issue.
            """;

        // Get response from the specialist agent
        var response = await _specialistAgent.RunAsync(contextualPrompt, cancellationToken: cancellationToken);

        return new SpecialistResponse
        {
            TicketId = triageResult.TicketId,
            Category = triageResult.Category,
            Priority = triageResult.Priority,
            Response = response.Text,
            Specialist = _specialistName
        };
    }
}

/// <summary>
/// Output executor that formats the final response.
/// </summary>
internal sealed class TriageOutputExecutor : Executor<SpecialistResponse>
{
    public TriageOutputExecutor() : base("TriageOutputExecutor")
    {
    }

    public override async ValueTask HandleAsync(
        SpecialistResponse response,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var formattedOutput = $"""
            ═══════════════════════════════════════════════════════════
            TICKET: {response.TicketId}
            CATEGORY: {response.Category.ToUpperInvariant()}
            PRIORITY: {response.Priority.ToUpperInvariant()}
            HANDLED BY: {response.Specialist}
            ═══════════════════════════════════════════════════════════

            {response.Response}

            ───────────────────────────────────────────────────────────
            If this doesn't resolve your issue, please reply with more
            details or request escalation.
            ═══════════════════════════════════════════════════════════
            """;

        await context.YieldOutputAsync(formattedOutput, cancellationToken);
    }
}

/// <summary>
/// Custom event for workflow information.
/// </summary>
internal sealed class WorkflowInfoEvent(string message) : WorkflowEvent(message) { }
