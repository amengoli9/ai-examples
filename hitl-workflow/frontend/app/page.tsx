"use client";

import { CopilotChat } from "@copilotkit/react-ui";
import { ApprovalBadge } from "@/components/ApprovalBadge";
import { ChatHistory } from "@/components/ChatHistory";

export default function Home() {
  return (
    <div className="container">
      <header className="header">
        <h1>Human-in-the-Loop Workflow</h1>
        <p>
          Ask the assistant to perform tasks - it will request your approval
          before executing commands.
        </p>
      </header>

      <main className="main-content">
        <section className="chat-section">
          <ApprovalBadge />
          <CopilotChat
            labels={{
              title: "Workflow Assistant",
              initial: "Hi! I can help you execute tasks. What would you like me to do?",
              placeholder: "Ask me to perform a task...",
            }}
            instructions="You are a helpful workflow assistant. When the user asks you to perform an action, use the ExecuteCommand tool to execute it. Always explain what you're about to do."
          />
        </section>

        <aside className="history-section">
          <ChatHistory />
        </aside>
      </main>
    </div>
  );
}
