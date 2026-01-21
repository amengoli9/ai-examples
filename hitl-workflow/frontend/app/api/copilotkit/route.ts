import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";
import { NextRequest } from "next/server";

// Backend URL - the .NET AG-UI server
const BACKEND_URL = process.env.BACKEND_URL || "http://localhost:5000";

// Create an HttpAgent that connects to the .NET backend
const workflowAgent = new HttpAgent({
  url: BACKEND_URL,
  agentId: "workflow_assistant",
  description: "A workflow assistant that can execute tasks with human approval",
});

const runtime = new CopilotRuntime({
  agents: {
    workflow_assistant: workflowAgent,
  },
});

export const POST = async (req: NextRequest) => {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter: new ExperimentalEmptyAdapter(),
    endpoint: "/api/copilotkit",
  });

  return handleRequest(req);
};
