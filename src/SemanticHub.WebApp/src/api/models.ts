/**
 * Type definitions for SemanticHub API communication
 */

// ============================================================================
// Message Types
// ============================================================================

/**
 * Represents a chat message in a conversation
 */
export interface ChatMessage {
  id: string;
  conversationId?: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: string;
  citations?: Citation[];
}

// ============================================================================
// Citation Types
// ============================================================================

/**
 * Represents a citation/source reference for an assistant response
 */
export interface Citation {
  /** Part index within a multi-part response */
  partIndex?: number;
  /** Content excerpt from the cited source */
  content: string;
  /** Unique identifier for the citation */
  id: string;
  /** Title of the source document */
  title?: string;
  /** File path of the source document */
  filePath?: string;
  /** URL of the source document */
  url?: string;
  /** Chunk identifier within the source document */
  chunkId?: string;
  /** Relevance score (0-1) */
  score?: number;
}

// ============================================================================
// Conversation Types
// ============================================================================

/**
 * Represents a conversation with its metadata and messages
 */
export interface Conversation {
  id: string;
  userId?: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessage[];
}

// ============================================================================
// Request Types
// ============================================================================

/**
 * Request payload for sending a chat message
 */
export interface SendMessageRequest {
  /** The user's message content */
  message: string;
  /** Optional conversation ID to continue an existing conversation */
  conversationId?: string;
  /** Whether to include conversation history in the request */
  includeHistory?: boolean;
}

/**
 * Request payload for creating a new conversation
 */
export interface CreateConversationRequest {
  /** Optional title for the conversation */
  title?: string;
  /** Optional user ID */
  userId?: string;
}

/**
 * Request payload for updating a conversation title
 */
export interface UpdateConversationTitleRequest {
  /** New title for the conversation */
  title: string;
}

// ============================================================================
// Response Types
// ============================================================================

/**
 * Represents a single chunk in a streamed chat response
 */
export interface StreamedChatChunk {
  /** Message identifier */
  messageId: string;
  /** Conversation identifier (if part of a conversation) */
  conversationId?: string;
  /** Content chunk (may be partial) */
  content: string;
  /** Role of the message sender */
  role: string;
  /** Citations/sources (typically in final chunk) */
  citations?: Citation[];
  /** Indicates if this is the final chunk */
  isComplete: boolean;
  /** Timestamp of the chunk */
  timestamp: string;
}

/**
 * Response containing a list of conversations
 */
export interface ConversationListResponse {
  /** Array of conversations */
  conversations: Conversation[];
  /** Total number of conversations */
  total: number;
}

// ============================================================================
// Error Types
// ============================================================================

/**
 * Standardized API error response
 */
export interface ApiError {
  /** Error message */
  message: string;
  /** HTTP status code */
  status: number;
  /** Additional error details */
  details?: any;
}
