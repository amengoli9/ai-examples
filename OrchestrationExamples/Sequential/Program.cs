// Copyright (c) Microsoft. All rights reserved.

// Sequential Orchestration Example: Loan Application Pipeline
// This sample demonstrates a sequential workflow where a loan application
// flows through multiple processing stages: Document Collection → Credit Analysis → Risk Assessment → Final Decision

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

namespace Sequential;

public static class Program
{
    private const string SourceName = "Sequential.LoanPipeline";
    private const string ServiceName = "LoanApplicationPipeline";

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
        // 3. AGENT DEFINITIONS - Loan Application Pipeline
        // ============================================================

        // Agent 1: Document Collector - Validates application completeness
        builder.AddAIAgent("document-collector",
            """
            You are a Document Collector for a bank's loan department.
            Your role is to:
            1. Review the loan application for completeness
            2. Identify any missing documents or information
            3. Summarize the applicant's basic information
            4. Pass a structured summary to the next stage

            Always be professional and thorough. Format your response as:
            **Application Summary:**
            - Applicant: [name if provided]
            - Loan Amount Requested: [amount]
            - Loan Purpose: [purpose]
            - Documents Status: [complete/incomplete]
            - Notes: [any observations]
            """);

        // Agent 2: Credit Analyst - Analyzes credit history
        builder.AddAIAgent("credit-analyst",
            """
            You are a Credit Analyst at a bank.
            Based on the application summary from the Document Collector, your role is to:
            1. Evaluate the credit-worthiness indicators mentioned
            2. Assess the applicant's financial stability
            3. Identify any credit concerns or red flags
            4. Provide a credit assessment score (Excellent/Good/Fair/Poor)

            Format your response as:
            **Credit Analysis:**
            - Credit Assessment: [Excellent/Good/Fair/Poor]
            - Key Factors: [list factors]
            - Concerns: [any concerns or "None identified"]
            - Recommendation: [proceed/caution/review needed]
            """);

        // Agent 3: Risk Assessor - Evaluates overall loan risk
        builder.AddAIAgent("risk-assessor",
            """
            You are a Risk Assessment Officer at a bank.
            Based on the previous analyses, your role is to:
            1. Evaluate the overall risk level of the loan
            2. Consider debt-to-income implications
            3. Assess collateral adequacy if mentioned
            4. Calculate a risk score and category

            Format your response as:
            **Risk Assessment:**
            - Risk Level: [Low/Medium/High/Very High]
            - Risk Score: [1-10, where 10 is highest risk]
            - Key Risk Factors: [list factors]
            - Mitigation Suggestions: [if applicable]
            - Proceed to Approval: [Yes/With Conditions/No]
            """);

        // Agent 4: Loan Officer - Makes final decision
        builder.AddAIAgent("loan-officer",
            """
            You are a Senior Loan Officer making the final decision.
            Based on all previous analyses (Document, Credit, Risk), your role is to:
            1. Review all assessments comprehensively
            2. Make a final approval/denial decision
            3. If approved, specify terms and conditions
            4. Provide clear reasoning for the decision

            Format your response as:
            **FINAL LOAN DECISION:**
            ================================
            Decision: [APPROVED/APPROVED WITH CONDITIONS/DENIED]

            Reasoning: [explanation]

            Terms (if approved):
            - Interest Rate Recommendation: [rate]
            - Loan Term: [term]
            - Special Conditions: [any conditions]

            Next Steps: [what the applicant should do]
            ================================
            """);

        // ============================================================
        // 4. WORKFLOW REGISTRATION - Sequential Pipeline
        // ============================================================
        builder.AddWorkflow("loan-pipeline", (sp, workflowName) =>
        {
            var documentCollector = sp.GetRequiredKeyedService<AIAgent>("document-collector");
            var creditAnalyst = sp.GetRequiredKeyedService<AIAgent>("credit-analyst");
            var riskAssessor = sp.GetRequiredKeyedService<AIAgent>("risk-assessor");
            var loanOfficer = sp.GetRequiredKeyedService<AIAgent>("loan-officer");

            // Wrap agents with OpenTelemetry
            var agents = new[] { documentCollector, creditAnalyst, riskAssessor, loanOfficer }
                .Select(agent => new OpenTelemetryAgent(agent, SourceName) { EnableSensitiveData = true })
                .Cast<AIAgent>();

            return AgentWorkflowBuilder.BuildSequential(workflowName: workflowName, agents: agents);
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
        var urls = app.Urls.Any() ? string.Join(", ", app.Urls) : "https://localhost:5001";

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║      SEQUENTIAL ORCHESTRATION: Loan Application Pipeline     ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Pipeline Stages:                                            ║");
        Console.WriteLine("║  1. Document Collector → 2. Credit Analyst                   ║");
        Console.WriteLine("║  3. Risk Assessor     → 4. Loan Officer                      ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  DevUI: {urls}/devui".PadRight(65) + "║");
        Console.WriteLine($"║  OTLP:  {otlpEndpoint}".PadRight(65) + "║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Open DevUI in your browser to interact with the agents.");
        Console.WriteLine();
        Console.WriteLine("Test examples:");
        Console.WriteLine("  1. \"I want to apply for a $50,000 home improvement loan. I have a stable job for 5 years, income $80,000/year.\"");
        Console.WriteLine("  2. \"Requesting a $200,000 mortgage for a first home purchase. Credit score 720, down payment 20%, self-employed.\"");
        Console.WriteLine();

        app.Run();
    }
}
