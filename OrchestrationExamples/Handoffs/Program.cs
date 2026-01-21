// Copyright (c) Microsoft. All rights reserved.

// Handoffs Orchestration Example: Banking Customer Service
// This sample demonstrates a handoffs workflow where customer inquiries are
// intelligently routed between specialists: Triage Agent routes to Account Services,
// Loan Services, Investment Advisor, or Fraud Support based on customer needs.

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

namespace Handoffs;

public static class Program
{
    private const string SourceName = "Handoffs.BankingService";
    private const string ServiceName = "BankingCustomerService";

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
        // 3. AGENT DEFINITIONS - Banking Customer Service Team
        // ============================================================

        // Agent 1: Triage Agent - Initial contact and routing
        builder.AddAIAgent("triage-agent",
            """
            You are the Triage Agent for a bank's customer service center.
            Your role is to:
            1. Greet customers warmly and professionally
            2. Understand their inquiry or issue
            3. Route them to the appropriate specialist

            IMPORTANT: You MUST handoff to a specialist. Analyze the customer's need and handoff to:
            - account-services: For balance inquiries, transfers, card issues, account updates
            - loan-services: For loan applications, payments, refinancing, mortgage questions
            - investment-advisor: For investment products, portfolio questions, retirement planning
            - fraud-support: For suspicious activity, unauthorized transactions, security concerns

            Keep your initial response brief and always handoff to the appropriate specialist.
            Example: "I understand you have a question about [topic]. Let me connect you with our specialist."
            """);

        // Agent 2: Account Services - General account support
        builder.AddAIAgent("account-services",
            """
            You are an Account Services Specialist at a bank.
            You handle:
            - Balance inquiries and account statements
            - Fund transfers between accounts
            - Debit/credit card issues (lost, stolen, limits)
            - Account updates (address, contact info)
            - Setting up automatic payments
            - Account fees and charges questions

            Be helpful and thorough. After assisting, ask if there's anything else.
            If the customer has a different type of question, handoff back to triage-agent.
            """);

        // Agent 3: Loan Services - Lending support
        builder.AddAIAgent("loan-services",
            """
            You are a Loan Services Specialist at a bank.
            You handle:
            - Personal loan applications and inquiries
            - Mortgage questions and applications
            - Auto loan information
            - Payment schedules and due dates
            - Refinancing options
            - Loan payoff information

            Be informative and guide customers through their lending needs.
            If the customer has a different type of question, handoff back to triage-agent.
            """);

        // Agent 4: Investment Advisor - Wealth management
        builder.AddAIAgent("investment-advisor",
            """
            You are an Investment Advisor at a bank.
            You handle:
            - Investment product information (CDs, mutual funds, stocks)
            - Portfolio questions and performance
            - Retirement planning (IRA, 401k rollovers)
            - Risk assessment discussions
            - Market information and guidance
            - Wealth management services

            Provide educational information while noting that specific advice requires a consultation.
            If the customer has a different type of question, handoff back to triage-agent.
            """);

        // Agent 5: Fraud Support - Security team
        builder.AddAIAgent("fraud-support",
            """
            You are a Fraud Support Specialist at a bank.
            You handle:
            - Reporting suspicious account activity
            - Unauthorized transaction disputes
            - Identity theft concerns
            - Account security measures
            - Temporary account locks
            - Security alert notifications

            Treat all fraud concerns seriously and with urgency. Reassure the customer.
            Guide them through security steps and documentation needed.
            If the customer has a different type of question, handoff back to triage-agent.
            """);

        // ============================================================
        // 4. WORKFLOW REGISTRATION - Handoffs Pattern
        // ============================================================
        builder.AddWorkflow("customer-service", (sp, workflowName) =>
        {
            var triageAgent = sp.GetRequiredKeyedService<AIAgent>("triage-agent");
            var accountServices = sp.GetRequiredKeyedService<AIAgent>("account-services");
            var loanServices = sp.GetRequiredKeyedService<AIAgent>("loan-services");
            var investmentAdvisor = sp.GetRequiredKeyedService<AIAgent>("investment-advisor");
            var fraudSupport = sp.GetRequiredKeyedService<AIAgent>("fraud-support");

            // Wrap all agents with OpenTelemetry
            var wrappedTriage = new OpenTelemetryAgent(triageAgent, SourceName) { EnableSensitiveData = true };
            var wrappedAccount = new OpenTelemetryAgent(accountServices, SourceName) { EnableSensitiveData = true };
            var wrappedLoan = new OpenTelemetryAgent(loanServices, SourceName) { EnableSensitiveData = true };
            var wrappedInvestment = new OpenTelemetryAgent(investmentAdvisor, SourceName) { EnableSensitiveData = true };
            var wrappedFraud = new OpenTelemetryAgent(fraudSupport, SourceName) { EnableSensitiveData = true };

            var specialists = new AIAgent[] { wrappedAccount, wrappedLoan, wrappedInvestment, wrappedFraud };

            // Build handoffs workflow: Triage can hand off to any specialist,
            // and specialists can hand back to triage
            return AgentWorkflowBuilder
                .CreateHandoffBuilderWith(wrappedTriage)
                .WithHandoffs(wrappedTriage, specialists)
                .WithHandoffs(specialists, wrappedTriage)
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
        var urls = app.Urls.Any() ? string.Join(", ", app.Urls) : "https://localhost:5006";

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       HANDOFFS ORCHESTRATION: Banking Customer Service       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Triage Agent routes to specialists:                         ║");
        Console.WriteLine("║  • Account Services    • Loan Services                       ║");
        Console.WriteLine("║  • Investment Advisor  • Fraud Support                       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  DevUI: {urls}/devui".PadRight(65) + "║");
        Console.WriteLine($"║  OTLP:  {otlpEndpoint}".PadRight(65) + "║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Open DevUI in your browser to interact with the agents.");
        Console.WriteLine();
        Console.WriteLine("Test examples:");
        Console.WriteLine("  1. \"I noticed a $500 charge on my account that I don't recognize. It says 'AMZN Digital' from yesterday.\"");
        Console.WriteLine("  2. \"I want to refinance my mortgage to get a lower interest rate. Current rate is 6.5% on a $300,000 loan.\"");
        Console.WriteLine();

        app.Run();
    }
}
