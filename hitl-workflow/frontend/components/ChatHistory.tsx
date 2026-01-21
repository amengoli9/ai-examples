"use client";

import { useCopilotChat } from "@copilotkit/react-core";
import { useEffect, useState, useCallback } from "react";

const STORAGE_KEY = "hitl-workflow-chat-history";

interface StoredMessage {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  timestamp: string;
}

/**
 * ChatHistory component that displays conversation history and persists to localStorage.
 */
export function ChatHistory() {
  const { visibleMessages } = useCopilotChat();
  const [storedHistory, setStoredHistory] = useState<StoredMessage[]>([]);
  const [isClient, setIsClient] = useState(false);

  // Ensure we're on the client side
  useEffect(() => {
    setIsClient(true);
  }, []);

  // Load history from localStorage on mount
  useEffect(() => {
    if (!isClient) return;

    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved) {
        const parsed = JSON.parse(saved) as StoredMessage[];
        setStoredHistory(parsed);
      }
    } catch (error) {
      console.error("Failed to load chat history:", error);
    }
  }, [isClient]);

  // Sync current messages to localStorage
  useEffect(() => {
    if (!isClient || !visibleMessages) return;

    const newMessages: StoredMessage[] = visibleMessages
      .filter((msg) => msg.role === "user" || msg.role === "assistant")
      .map((msg) => ({
        id: msg.id,
        role: msg.role as "user" | "assistant",
        content: typeof msg.content === "string" ? msg.content : "",
        timestamp: new Date().toISOString(),
      }))
      .filter((msg) => msg.content.trim() !== "");

    if (newMessages.length > 0) {
      // Merge with existing history, avoiding duplicates
      setStoredHistory((prev) => {
        const existingIds = new Set(prev.map((m) => m.id));
        const uniqueNew = newMessages.filter((m) => !existingIds.has(m.id));
        const merged = [...prev, ...uniqueNew];

        // Save to localStorage
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify(merged));
        } catch (error) {
          console.error("Failed to save chat history:", error);
        }

        return merged;
      });
    }
  }, [visibleMessages, isClient]);

  const clearHistory = useCallback(() => {
    if (!isClient) return;

    try {
      localStorage.removeItem(STORAGE_KEY);
      setStoredHistory([]);
    } catch (error) {
      console.error("Failed to clear chat history:", error);
    }
  }, [isClient]);

  const formatTimestamp = (timestamp: string) => {
    try {
      return new Date(timestamp).toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
      });
    } catch {
      return "";
    }
  };

  if (!isClient) {
    return (
      <div className="chat-history">
        <h3>Conversation History</h3>
        <div className="empty-history">Loading...</div>
      </div>
    );
  }

  return (
    <div className="chat-history">
      <h3>
        Conversation History
        {storedHistory.length > 0 && (
          <button className="clear-btn" onClick={clearHistory}>
            Clear
          </button>
        )}
      </h3>

      <div className="history-list">
        {storedHistory.length === 0 ? (
          <div className="empty-history">
            <p>No conversation history yet.</p>
            <p>Start chatting to see your messages here!</p>
          </div>
        ) : (
          storedHistory.map((message) => (
            <div key={message.id} className={`history-item ${message.role}`}>
              <div className="role">
                {message.role === "user" ? "You" : "Assistant"}
              </div>
              <div className="content">{message.content}</div>
              <div className="timestamp">{formatTimestamp(message.timestamp)}</div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
