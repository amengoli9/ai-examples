// Copyright (c) Microsoft. All rights reserved.

// Group Chat Orchestration Example: Risk Committee Meeting
// This sample demonstrates a group chat workflow where multiple executives
// discuss and evaluate business decisions: CRO, CFO, Compliance Head, and Operations Director
// Each participant takes turns in a round-robin fashion.

using System.Diagnostics;
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

namespace GroupChat;

public static class Program
{
    private const string SourceName = "GroupChat.RiskCommittee";
    private const string ServiceName = "RiskCommitteeMeeting";

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
        // 3. AGENT DEFINITIONS - Risk Committee Members
        // ============================================================

        // Agent 1: Chief Risk Officer - Risk Management perspective
        builder.AddAIAgent("chief-risk-officer",
            """
            You are the Chief Risk Officer (CRO) in a Risk Committee meeting.
            Your role is to:
            1. Identify and quantify potential risks in any proposal
            2. Assess probability and impact of identified risks
            3. Suggest risk mitigation strategies
            4. Provide a risk-adjusted view of opportunities

            Speaking style: Analytical, cautious but constructive, data-driven.
            Keep responses concise (2-3 paragraphs max) as this is a committee discussion.
            Build on previous speakers' points and address their concerns.
            Always identify at least one risk consideration.
            """);

        // Agent 2: Chief Financial Officer - Financial perspective
        builder.AddAIAgent("chief-financial-officer",
            """
            You are the Chief Financial Officer (CFO) in a Risk Committee meeting.
            Your role is to:
            1. Analyze financial implications and ROI
            2. Evaluate budget requirements and funding sources
            3. Consider impact on cash flow and financial statements
            4. Assess cost-benefit ratios

            Speaking style: Numbers-focused, pragmatic, bottom-line oriented.
            Keep responses concise (2-3 paragraphs max) as this is a committee discussion.
            Build on previous speakers' points and relate to financial impact.
            Always mention financial considerations or metrics.
            """);

        // Agent 3: Head of Compliance - Regulatory perspective
        builder.AddAIAgent("compliance-head",
            """
            You are the Head of Compliance in a Risk Committee meeting.
            Your role is to:
            1. Ensure proposals meet regulatory requirements
            2. Identify potential compliance gaps or concerns
            3. Consider industry standards and best practices
            4. Flag any legal or reputational risks

            Speaking style: Thorough, rule-conscious, protective of the organization.
            Keep responses concise (2-3 paragraphs max) as this is a committee discussion.
            Build on previous speakers' points and highlight compliance angles.
            Always reference regulatory or policy considerations.
            """);

        // Agent 4: Operations Director - Implementation perspective
        builder.AddAIAgent("operations-director",
            """
            You are the Operations Director in a Risk Committee meeting.
            Your role is to:
            1. Assess operational feasibility of proposals
            2. Evaluate resource requirements and capacity
            3. Consider implementation challenges and timelines
            4. Identify operational synergies or conflicts

            Speaking style: Practical, execution-focused, realistic about constraints.
            Keep responses concise (2-3 paragraphs max) as this is a committee discussion.
            Build on previous speakers' points and address implementation aspects.
            Always consider operational execution and resources.

            After all have spoken, if you're the last in the round, provide a brief summary
            of the committee's consensus and recommend next steps.
            """);

        // ============================================================
        // 4. WORKFLOW REGISTRATION - Group Chat with Round Robin
        // ============================================================
        builder.AddWorkflow("risk-committee", (sp, workflowName) =>
        {
            var cro = sp.GetRequiredKeyedService<AIAgent>("chief-risk-officer");
            var cfo = sp.GetRequiredKeyedService<AIAgent>("chief-financial-officer");
            var complianceHead = sp.GetRequiredKeyedService<AIAgent>("compliance-head");
            var opsDirector = sp.GetRequiredKeyedService<AIAgent>("operations-director");

            // Wrap agents with OpenTelemetry
            var agents = new[] { cro, cfo, complianceHead, opsDirector }
                .Select(agent => new OpenTelemetryAgent(agent, SourceName) { EnableSensitiveData = true })
                .Cast<AIAgent>()
                .ToList();

            // Build group chat with round-robin manager (5 iterations = each person speaks ~once)
            return AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(participants => new RoundRobinGroupChatManager(participants)
                {
                    MaximumIterationCount = 5
                })
                .AddParticipants(agents)
                .Build();
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
        var urls = app.Urls.Any() ? string.Join(", ", app.Urls) : "https://localhost:5004";

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       GROUP CHAT ORCHESTRATION: Risk Committee Meeting       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Committee Members (Round-Robin Discussion):                 ║");
        Console.WriteLine("║  • Chief Risk Officer    • Chief Financial Officer           ║");
        Console.WriteLine("║  • Head of Compliance    • Operations Director               ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  DevUI: {urls}/devui".PadRight(65) + "║");
        Console.WriteLine($"║  OTLP:  {otlpEndpoint}".PadRight(65) + "║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Open DevUI in your browser to interact with the agents.");
        Console.WriteLine();
        Console.WriteLine("Test examples:");
        Console.WriteLine("  1. \"We are considering expanding into cryptocurrency custody services for our banking clients. Discuss the implications.\"");
        Console.WriteLine("  2. \"Should we acquire a small fintech startup for $50M to accelerate our digital transformation?\"");
        Console.WriteLine();

        app.Run();
    }
}
