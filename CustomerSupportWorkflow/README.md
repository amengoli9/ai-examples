# Interactive Customer Support System

An interactive multi-turn customer support chat system. Talk to AI support agents who will ask clarifying questions to understand and resolve your issues.

## Features

- **Interactive Chat**: Real conversation with AI agents
- **Multi-turn Dialogue**: Agents ask follow-up questions, you respond
- **AI-Powered Triage**: Automatically routes you to the right specialist
- **Conversation History**: Context maintained throughout the conversation
- **Escalation**: Complex issues are escalated to human supervisors

## How It Works

```
You: "I have a problem with my bill"
                    ↓
            [AI Triage analyzes]
                    ↓
        [Routes to Billing Support]
                    ↓
Billing Support: "I'm sorry to hear that! Could you tell me which
                  invoice you're referring to? If you have the invoice
                  number or date, that would help me look into this."
                    ↓
You: "Invoice #12345 from last month"
                    ↓
Billing Support: "Thank you! I can see invoice #12345. What seems
                  to be the issue with it - was there an incorrect
                  amount, or something else?"
                    ↓
            [Conversation continues...]
```

## Agents

| Agent | Specialization | Behavior |
|-------|----------------|----------|
| **Billing Support** | Invoices, payments, subscriptions | Asks for invoice numbers, dates, amounts |
| **Tech Support** | Errors, crashes, bugs | Asks about OS, version, error codes |
| **Refund Specialist** | Refunds, cancellations | Asks about reasons, tries to retain |
| **Customer Service** | General inquiries | Handles everything else |

## Running

```bash
# Set your API key
export OPENAI_API_KEY="sk-..."

# Or use Azure OpenAI
export USE_AZURE_OPENAI="true"
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"

# Run the interactive chat
dotnet run
```

## Commands

| Command | Description |
|---------|-------------|
| `new` | Start a fresh conversation |
| `quit` | Exit the program |

## Example Session

```
╔══════════════════════════════════════════════════════════════╗
║          INTERACTIVE CUSTOMER SUPPORT SYSTEM                 ║
╠══════════════════════════════════════════════════════════════╣
║  Talk to our AI support team. They will ask questions        ║
║  to better understand and resolve your issue.                ║
╚══════════════════════════════════════════════════════════════╝

You: My app keeps crashing

[Analyzing your request...]
[Category: technical, Priority: Normal, Sentiment: Neutral]

[Connected to Tech Support]

Tech Support: I'm sorry you're experiencing crashes! Let me help you
troubleshoot. Could you tell me:
1. What operating system are you using?
2. Which version of our app do you have?
3. Does it crash during a specific action, or randomly?

You (talking to Tech Support): Windows 11, version 2.5, crashes when exporting

Tech Support: Thank you for those details! The export crash on Windows 11
with version 2.5 is something I can help with. Let's try a few things:

First, could you try clearing the app cache?
1. Go to Settings > Storage > Clear Cache
2. Restart the app
3. Try the export again

Did that help, or is it still crashing?

You (talking to Tech Support): Still crashing

Tech Support: I see. Let's try one more thing - could you tell me
what file format you're trying to export to? (PDF, CSV, etc.)
Also, is there an error code shown when it crashes?

You (talking to Tech Support): PDF export, error code EXP-500

Tech Support: Error code EXP-500 with PDF exports is a known issue
we're working on. As a workaround, you can:
1. Export to CSV first
2. Then convert to PDF using any online converter

I'll also flag this for our engineering team. Would you like me
to escalate this so you're notified when the fix is released?
```

## Escalation Triggers

The system automatically escalates to a human supervisor when:
- Legal issues or lawsuits are mentioned
- Amounts exceed $1000
- Customer is extremely angry or threatening
- Security breaches are mentioned
- Agent determines the issue is beyond their scope

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Interactive Chat Loop                     │
├─────────────────────────────────────────────────────────────┤
│  User Input → Triage Agent → Route to Specialist            │
│                                    ↓                        │
│              ┌─────────────────────────────────────┐        │
│              │    Support Agent (with thread)      │        │
│              │    - Asks clarifying questions      │        │
│              │    - Maintains conversation context │        │
│              │    - Can escalate if needed         │        │
│              └─────────────────────────────────────┘        │
│                                    ↓                        │
│              Response → Store in History → Display          │
└─────────────────────────────────────────────────────────────┘
```

## Key Components

### Dependency Injection
```csharp
services.AddSingleton<IChatClient>(...);
services.AddSingleton<IConversationHistoryService, InMemoryConversationHistoryService>();
services.AddSingleton<IEscalationService, ConsoleEscalationService>();
services.AddSingleton<SupportAgentFactory>();
```

### Conversation History Service
Maintains per-customer message history for context-aware responses:
```csharp
interface IConversationHistoryService
{
    Task AddMessageAsync(string customerId, string role, string content);
    Task<List<ConversationMessage>> GetHistoryAsync(string customerId);
    Task ClearHistoryAsync(string customerId);
}
```

### Agent Factory
Creates specialized agents with instructions to ask questions:
```csharp
public AIAgent CreateTechSupportAgent() => new ChatClientAgent(
    chatClient,
    instructions: """
        Ask about their environment (OS, browser, version, etc.)
        Request error messages or codes
        Guide them step-by-step through troubleshooting
        Ask if each step worked before moving to the next
        """);
```
