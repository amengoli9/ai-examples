# Human-in-the-Loop Workflow

A sample application demonstrating Human-in-the-Loop (HITL) approval workflows using:
- **Backend**: .NET 9 with Microsoft Agent Framework + AG-UI protocol
- **Frontend**: Next.js with CopilotKit
- **LLM Provider**: Azure OpenAI (with DefaultAzureCredential)

## Architecture

```
┌─────────────────────┐         ┌─────────────────────┐
│   Next.js Frontend  │  HTTP   │   .NET Backend      │
│   (CopilotKit)      │◄───────►│   (AG-UI Protocol)  │
│                     │   SSE   │                     │
│  - CopilotChat      │         │  - AIAgent          │
│  - Approval Badge   │         │  - MapAGUI endpoint │
│  - Message History  │         │  - Approval Tool    │
└─────────────────────┘         └─────────────────────┘
```

## Prerequisites

1. **.NET 9 SDK**: [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
2. **Node.js 18+**: [Download](https://nodejs.org/)
3. **Azure OpenAI Resource**: With a deployed GPT-4o model
4. **Azure CLI** (optional): For DefaultAzureCredential authentication

## Setup

### 1. Configure Azure OpenAI

Set the required environment variables:

```bash
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o"
```

Make sure you're authenticated to Azure:
```bash
az login
```

### 2. Start the Backend

```bash
cd backend
dotnet restore
dotnet run
```

The backend will start on `http://localhost:5000`.

### 3. Start the Frontend

```bash
cd frontend
npm install
npm run dev
```

The frontend will start on `http://localhost:3000`.

## Usage

1. Open `http://localhost:3000` in your browser
2. Ask the assistant to perform a task, for example:
   - "Please execute a backup of the database"
   - "Run the deployment script for staging"
   - "Clean up temporary files"
3. The assistant will request your approval before executing
4. Click **Approve** or **Reject** in the approval badge
5. The assistant will respond based on your decision

## How It Works

### Human-in-the-Loop Flow

1. User sends a message requesting an action
2. The agent decides to call the `ExecuteCommand` tool
3. The tool is wrapped with `ApprovalRequiredAIFunction` which triggers approval
4. Backend emits a `request_approval` tool call via AG-UI protocol
5. Frontend receives the tool call and renders the ApprovalBadge
6. User clicks Approve/Reject
7. The `respond()` function sends the decision back to the agent
8. Agent continues or cancels based on the decision

### Key Components

- **ServerFunctionApprovalAgent**: Wraps the base agent to handle approval requests
- **ApprovalBadge**: React component using `useCopilotAction` with `renderAndWait`
- **ChatHistory**: Persists conversation to localStorage

## Project Structure

```
hitl-workflow/
├── backend/
│   ├── Program.cs                    # Main entry, AG-UI endpoint
│   ├── Agents/
│   │   └── ServerFunctionApprovalAgent.cs  # Approval handling agent
│   ├── Models/
│   │   └── AgentState.cs             # State models
│   └── hitl-workflow.csproj
│
└── frontend/
    ├── app/
    │   ├── layout.tsx                # CopilotKit provider
    │   ├── page.tsx                  # Main page with chat
    │   └── api/copilotkit/
    │       └── route.ts              # Runtime endpoint
    ├── components/
    │   ├── ApprovalBadge.tsx         # HITL approval UI
    │   └── ChatHistory.tsx           # Conversation history
    └── package.json
```

## Troubleshooting

### Backend fails to start
- Ensure `AZURE_OPENAI_ENDPOINT` and `AZURE_OPENAI_DEPLOYMENT_NAME` are set
- Verify you're authenticated with Azure (`az login`)
- Check that the deployment exists and is accessible

### Frontend can't connect to backend
- Ensure the backend is running on port 5000
- Check CORS settings if running on different ports
- Verify `BACKEND_URL` in `.env.local`

### Approval badge doesn't appear
- Check browser console for errors
- Ensure the agent is using the approval-required tool
- Verify the AG-UI protocol is being used correctly
