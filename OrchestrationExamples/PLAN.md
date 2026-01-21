# Orchestration Examples Plan

## Overview

This project contains .NET examples for each orchestration type in the Microsoft Agent Framework, focused on real-world business scenarios in banking and customer service:

1. **Sequential** - Loan Application Processing Pipeline
2. **Concurrent** - Insurance Claim Multi-Expert Analysis
3. **Group Chat** - Risk Committee Meeting
4. **Handoffs** - Banking Customer Service Center

## Features in Each Example

Each example includes:
- **Dependency Injection (DI)** - Using `Microsoft.Extensions.DependencyInjection`
- **OpenTelemetry** - Traces, metrics, and logging to Aspire Dashboard
- **Console Output** - Interactive console interface with streaming responses
- **DevUI** - Web-based UI for testing agents at `/devui`

## Project Structure

```
OrchestrationExamples/
├── OrchestrationExamples.sln
├── PLAN.md
├── Sequential/
│   ├── Sequential.csproj
│   └── Program.cs
├── Concurrent/
│   ├── Concurrent.csproj
│   └── Program.cs
├── GroupChat/
│   ├── GroupChat.csproj
│   └── Program.cs
└── Handoffs/
    ├── Handoffs.csproj
    └── Program.cs
```

## Example Scenarios & Agent Roles

### 1. Sequential Orchestration - "Loan Application Pipeline"
**Industry**: Banking / Financial Services
**Scenario**: A customer loan application flows through multiple approval stages

| Agent | Role | Responsibility |
|-------|------|----------------|
| **DocumentCollector** | Application Intake | Validates application completeness, gathers required documents |
| **CreditAnalyst** | Credit Assessment | Analyzes credit history, calculates credit score implications |
| **RiskAssessor** | Risk Evaluation | Evaluates loan risk, debt-to-income ratio, collateral |
| **LoanOfficer** | Final Decision | Makes approval/denial decision with terms and conditions |

**Flow**: Application → DocumentCollector → CreditAnalyst → RiskAssessor → LoanOfficer → Decision

**Business Value**: Ensures consistent, thorough loan processing with clear audit trail

---

### 2. Concurrent Orchestration - "Insurance Claim Analysis"
**Industry**: Insurance
**Scenario**: Multiple specialists analyze an insurance claim simultaneously for faster processing

| Agent | Role | Perspective |
|-------|------|-------------|
| **PolicyExpert** | Coverage Analyst | Verifies policy coverage, exclusions, and limits |
| **FraudDetector** | Fraud Investigation | Analyzes claim patterns for potential fraud indicators |
| **DamageAssessor** | Loss Evaluation | Estimates damage/loss value and repair costs |
| **ComplianceOfficer** | Regulatory Check | Ensures claim handling meets regulatory requirements |

**Flow**: Claim → [PolicyExpert | FraudDetector | DamageAssessor | ComplianceOfficer] (parallel) → Consolidated Assessment

**Business Value**: Reduces claim processing time from days to minutes with comprehensive analysis

---

### 3. Group Chat Orchestration - "Risk Committee Meeting"
**Industry**: Banking / Corporate Finance
**Scenario**: Executive committee discusses and evaluates a major business decision

| Agent | Role | Perspective |
|-------|------|-------------|
| **ChiefRiskOfficer** | Risk Management | Identifies and quantifies potential risks |
| **CFO** | Financial Impact | Analyzes financial implications and ROI |
| **ComplianceHead** | Regulatory Compliance | Ensures adherence to regulations and policies |
| **OperationsDirector** | Operational Feasibility | Assesses implementation and operational impact |

**Flow**: Proposal → Round-robin discussion (5 turns) → Committee Recommendation

**Business Value**: Simulates executive decision-making with diverse expert perspectives

---

### 4. Handoffs Orchestration - "Banking Customer Service"
**Industry**: Banking / Customer Service
**Scenario**: Intelligent routing of customer inquiries to specialized support teams

| Agent | Role | Specialization |
|-------|------|----------------|
| **TriageAgent** | Initial Contact | Greets customer, identifies need, routes appropriately |
| **AccountServices** | Account Support | Balance inquiries, transfers, account updates, card issues |
| **LoanServices** | Lending Support | Loan applications, payment schedules, refinancing |
| **InvestmentAdvisor** | Wealth Management | Investment products, portfolio questions, retirement planning |
| **FraudSupport** | Security Team | Suspicious activity, dispute resolution, account security |

**Flow**: Customer → TriageAgent → [AccountServices | LoanServices | InvestmentAdvisor | FraudSupport] (based on need) → Back to Triage for follow-up

**Business Value**: Efficient customer routing with seamless handoffs between specialists

---

## Environment Variables Required

```bash
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o-mini
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317  # Optional: Aspire Dashboard
APPLICATIONINSIGHTS_CONNECTION_STRING=...          # Optional: Azure Monitor
```

## Running the Examples

```bash
# Run any example (each has both Console and DevUI modes)
dotnet run --project Sequential
dotnet run --project Concurrent
dotnet run --project GroupChat
dotnet run --project Handoffs

# Access DevUI at: https://localhost:5001/devui

# Run Aspire Dashboard for telemetry visualization (optional)
docker run -d -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest
# Dashboard: http://localhost:18888
```

## Architecture Notes

- Each project uses ASP.NET Core for hosting DevUI
- DI is configured via `WebApplicationBuilder.Services`
- OpenTelemetry captures traces at chat client, agent, and workflow levels
- Agents are wrapped with `OpenTelemetryAgent` for comprehensive telemetry
- Console output shows streaming responses with agent identification
- DevUI provides web-based testing interface at `/devui`
