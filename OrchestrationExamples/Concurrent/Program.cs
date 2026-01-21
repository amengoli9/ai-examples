// Copyright (c) Microsoft. All rights reserved.

// Concurrent Orchestration Example: Insurance Claim Analysis
// This sample demonstrates a concurrent (fan-out/fan-in) workflow where multiple
// specialists analyze an insurance claim simultaneously: Policy Expert, Fraud Detector,
// Damage Assessor, and Compliance Officer - then results are aggregated.

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

namespace Concurrent;

public static class Program
{
    private const string SourceName = "Concurrent.InsuranceClaim";
    private const string ServiceName = "InsuranceClaimAnalysis";

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
        // 3. AGENT DEFINITIONS - Insurance Claim Specialists
        // ============================================================

        // Agent 1: Policy Expert - Verifies coverage
        builder.AddAIAgent("policy-expert",
            """
            You are a Policy Coverage Expert for an insurance company.
            Your role is to analyze insurance claims and determine:
            1. Whether the claim is covered under the policy
            2. What coverage limits apply
            3. Any exclusions that might affect the claim
            4. The deductible amount applicable

            Format your response as:
            **POLICY ANALYSIS**
            - Coverage Status: [Covered/Partially Covered/Not Covered]
            - Applicable Coverage: [type of coverage]
            - Coverage Limit: [amount if determinable]
            - Deductible: [amount if known]
            - Exclusions: [any applicable exclusions]
            - Notes: [additional observations]
            """);

        // Agent 2: Fraud Detector - Analyzes for fraud indicators
        builder.AddAIAgent("fraud-detector",
            """
            You are a Fraud Detection Specialist for an insurance company.
            Your role is to analyze claims for potential fraud indicators:
            1. Look for inconsistencies in the claim details
            2. Identify red flags or suspicious patterns
            3. Assess the likelihood of fraudulent activity
            4. Recommend whether further investigation is needed

            Format your response as:
            **FRAUD ANALYSIS**
            - Risk Level: [Low/Medium/High]
            - Red Flags Identified: [list or "None"]
            - Inconsistencies: [list or "None found"]
            - Investigation Needed: [Yes/No]
            - Recommendation: [proceed/hold for review/investigate]
            """);

        // Agent 3: Damage Assessor - Estimates loss value
        builder.AddAIAgent("damage-assessor",
            """
            You are a Damage Assessment Specialist for an insurance company.
            Your role is to evaluate the claimed damages:
            1. Assess the reported damage or loss
            2. Estimate repair or replacement costs
            3. Determine if the claim amount is reasonable
            4. Identify any salvage value

            Format your response as:
            **DAMAGE ASSESSMENT**
            - Damage Type: [description]
            - Severity: [Minor/Moderate/Severe/Total Loss]
            - Estimated Value: [amount or range]
            - Claim Amount Reasonable: [Yes/High/Low/Needs Verification]
            - Salvage Value: [if applicable]
            - Notes: [additional observations]
            """);

        // Agent 4: Compliance Officer - Ensures regulatory compliance
        builder.AddAIAgent("compliance-officer",
            """
            You are a Compliance Officer for an insurance company.
            Your role is to ensure claim handling meets regulatory requirements:
            1. Verify the claim follows proper procedures
            2. Check for any regulatory concerns
            3. Ensure documentation requirements are met
            4. Identify any compliance risks

            Format your response as:
            **COMPLIANCE REVIEW**
            - Procedural Status: [Compliant/Needs Attention]
            - Documentation: [Complete/Incomplete]
            - Regulatory Concerns: [list or "None"]
            - Timeline Compliance: [Within limits/At risk/Overdue]
            - Recommended Actions: [any required actions]
            """);

        // ============================================================
        // 4. WORKFLOW REGISTRATION - Concurrent Analysis
        // ============================================================
        builder.AddWorkflow("claim-analysis", (sp, workflowName) =>
        {
            var policyExpert = sp.GetRequiredKeyedService<AIAgent>("policy-expert");
            var fraudDetector = sp.GetRequiredKeyedService<AIAgent>("fraud-detector");
            var damageAssessor = sp.GetRequiredKeyedService<AIAgent>("damage-assessor");
            var complianceOfficer = sp.GetRequiredKeyedService<AIAgent>("compliance-officer");

            // Wrap agents with OpenTelemetry
            var agents = new[] { policyExpert, fraudDetector, damageAssessor, complianceOfficer }
                .Select(agent => new OpenTelemetryAgent(agent, SourceName) { EnableSensitiveData = true })
                .Cast<AIAgent>();

            return AgentWorkflowBuilder.BuildConcurrent(workflowName: workflowName, agents: agents);
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
        var urls = app.Urls.Any() ? string.Join(", ", app.Urls) : "https://localhost:5002";

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║     CONCURRENT ORCHESTRATION: Insurance Claim Analysis       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Parallel Analysis by:                                       ║");
        Console.WriteLine("║  • Policy Expert      • Fraud Detector                       ║");
        Console.WriteLine("║  • Damage Assessor    • Compliance Officer                   ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  DevUI: {urls}/devui".PadRight(65) + "║");
        Console.WriteLine($"║  OTLP:  {otlpEndpoint}".PadRight(65) + "║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Open DevUI in your browser to interact with the agents.");
        Console.WriteLine();
        Console.WriteLine("Test examples:");
        Console.WriteLine("  1. \"I need to file a claim for water damage. A pipe burst causing $15,000 damage to floors and walls. Policy HO-12345.\"");
        Console.WriteLine("  2. \"Filing auto insurance claim for a rear-end collision. Other driver was at fault. Damage estimate $8,500.\"");
        Console.WriteLine();

        app.Run();
    }
}
