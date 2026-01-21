"use client";

import { useCopilotAction } from "@copilotkit/react-core";
import { useState } from "react";

interface ApprovalRequest {
  approval_id: string;
  function_name: string;
  function_arguments?: Record<string, unknown>;
  message?: string;
}

/**
 * ApprovalBadge component that renders approval UI for HITL workflow.
 * Uses useCopilotAction with renderAndWait to pause execution until user responds.
 */
export function ApprovalBadge() {
  const [isProcessing, setIsProcessing] = useState(false);

  // Register the request_approval action that the backend will call
  useCopilotAction({
    name: "request_approval",
    description: "Request user approval for executing a function",
    parameters: [
      {
        name: "request",
        type: "object",
        description: "The approval request details",
        required: true,
      },
    ],
    renderAndWait: ({ args, respond, status }) => {
      const request = args.request as ApprovalRequest | undefined;

      if (!request || !respond) {
        return null;
      }

      if (status === "complete") {
        return (
          <div className="approval-badge" style={{ background: "#d4edda", borderColor: "#28a745" }}>
            <h4 style={{ color: "#155724" }}>Approval Processed</h4>
            <p className="function-name">{request.function_name}</p>
          </div>
        );
      }

      const handleApprove = async () => {
        setIsProcessing(true);
        respond({
          approval_id: request.approval_id,
          approved: true,
        });
      };

      const handleReject = async () => {
        setIsProcessing(true);
        respond({
          approval_id: request.approval_id,
          approved: false,
        });
      };

      return (
        <div className="approval-badge">
          <h4>Approval Required</h4>
          <p className="function-name">{request.function_name}</p>
          {request.message && <p>{request.message}</p>}
          {request.function_arguments && (
            <div className="arguments">
              {JSON.stringify(request.function_arguments, null, 2)}
            </div>
          )}
          <div className="approval-buttons">
            <button
              className="approve-btn"
              onClick={handleApprove}
              disabled={isProcessing}
            >
              {isProcessing ? "Processing..." : "Approve"}
            </button>
            <button
              className="reject-btn"
              onClick={handleReject}
              disabled={isProcessing}
            >
              {isProcessing ? "Processing..." : "Reject"}
            </button>
          </div>
        </div>
      );
    },
  });

  // This component doesn't render anything directly - the useCopilotAction
  // hook handles rendering through the CopilotChat component
  return null;
}
