// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace HitlWorkflow.Models;

/// <summary>
/// Represents the state of the workflow agent.
/// </summary>
public sealed class AgentState
{
    /// <summary>
    /// The unique identifier for this state snapshot.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The timestamp when this state was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The current status of the workflow.
    /// </summary>
    [JsonPropertyName("status")]
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Idle;

    /// <summary>
    /// Any pending approval requests.
    /// </summary>
    [JsonPropertyName("pendingApprovals")]
    public List<PendingApproval> PendingApprovals { get; set; } = [];
}

/// <summary>
/// Workflow status enumeration.
/// </summary>
public enum WorkflowStatus
{
    Idle,
    Processing,
    AwaitingApproval,
    Completed,
    Error
}

/// <summary>
/// Represents a pending approval request.
/// </summary>
public sealed class PendingApproval
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("functionName")]
    public required string FunctionName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("requestedAt")]
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}
