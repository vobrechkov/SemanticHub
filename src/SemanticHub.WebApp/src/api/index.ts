/**
 * SemanticHub API Client
 *
 * Exports all API client functions and types for use throughout the application.
 */

// Export all types
export type {
  ChatMessage,
  Citation,
  Conversation,
  SendMessageRequest,
  CreateConversationRequest,
  UpdateConversationTitleRequest,
  StreamedChatChunk,
  ConversationListResponse,
  ApiError,
} from './models';

// Export all API functions
export {
  streamChatMessage,
  listConversations,
  createConversation,
  getConversation,
  updateConversationTitle,
  deleteConversation,
  deleteAllConversations,
  createTimeoutController,
  isApiError,
  formatApiError,
} from './client';
