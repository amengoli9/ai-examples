// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;
using Azure.Identity;
using HitlWorkflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddHttpClient().AddLogging();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Add(HitlJsonContext.Default));
builder.Services.AddAGUI();

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

WebApplication app = builder.Build();

app.UseCors();

// Get configuration
string endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set. Set it as an environment variable.");
string deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set. Set it as an environment variable.");

// Define approval-required tool - executes a command after user approval
[Description("Execute a command or task. This requires user approval before execution.")]
static string ExecuteCommand(
    [Description("The command or task to execute")] string command,
    [Description("Description of what this command will do")] string description)
{
    // Simulate command execution
    return $"Successfully executed: {command}\nResult: Task completed - {description}";
}

// Get JsonSerializerOptions
var jsonOptions = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value;

// Create approval-required tool
#pragma warning disable MEAI001 // Type is for evaluation purposes only
AITool[] tools = [new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ExecuteCommand))];
#pragma warning restore MEAI001

// Create base agent with Azure OpenAI
ChatClient openAIChatClient = new AzureOpenAIClient(
        new Uri(endpoint),
        new DefaultAzureCredential())
    .GetChatClient(deploymentName);

ChatClientAgent baseAgent = openAIChatClient.AsIChatClient().CreateAIAgent(
    name: "WorkflowAssistant",
    instructions: """
        You are a helpful workflow assistant that can execute tasks and commands for the user.
        When the user asks you to perform an action or execute a task, use the ExecuteCommand tool.
        Always explain what you're about to do before requesting approval.
        Be helpful and provide clear descriptions of what each command will accomplish.
        """,
    tools: tools);

// Wrap with ServerFunctionApprovalAgent for HITL
var agent = new ServerFunctionApprovalAgent(baseAgent, jsonOptions.SerializerOptions);

// Map the AG-UI endpoint
app.MapAGUI("/", agent);

await app.RunAsync();
