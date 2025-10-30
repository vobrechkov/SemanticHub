/**
 * ChatReducer - Redux-style reducer for chat state management
 * Handles all state mutations for the chat interface
 */

import { ChatMessage, Conversation, Citation } from '@/api/models';

// ============================================================================
// State Interface
// ============================================================================

export interface ChatState {
  // Conversations
  conversations: Conversation[];
  currentConversation: Conversation | null;

  // Messages
  messages: ChatMessage[];
  isStreaming: boolean;

  // UI State
  isLoading: boolean;
  error: string | null;
  isHistoryOpen: boolean;

  // Citation panel
  selectedCitation: Citation | null;
}

// ============================================================================
// Action Types
// ============================================================================

export type ChatAction =
  | { type: 'SET_CONVERSATIONS'; payload: Conversation[] }
  | { type: 'SET_CURRENT_CONVERSATION'; payload: Conversation | null }
  | { type: 'SET_MESSAGES'; payload: ChatMessage[] }
  | { type: 'ADD_MESSAGE'; payload: ChatMessage }
  | { type: 'UPDATE_MESSAGE'; payload: { id: string; updates: Partial<ChatMessage> } }
  | { type: 'SET_STREAMING'; payload: boolean }
  | { type: 'SET_LOADING'; payload: boolean }
  | { type: 'SET_ERROR'; payload: string | null }
  | { type: 'TOGGLE_HISTORY' }
  | { type: 'SET_HISTORY_OPEN'; payload: boolean }
  | { type: 'SET_SELECTED_CITATION'; payload: Citation | null }
  | { type: 'DELETE_CONVERSATION_LOCAL'; payload: string };

// ============================================================================
// Initial State
// ============================================================================

export const initialChatState: ChatState = {
  conversations: [],
  currentConversation: null,
  messages: [],
  isStreaming: false,
  isLoading: false,
  error: null,
  isHistoryOpen: false,
  selectedCitation: null,
};

// ============================================================================
// Reducer
// ============================================================================

export function chatReducer(state: ChatState, action: ChatAction): ChatState {
  switch (action.type) {
    case 'SET_CONVERSATIONS':
      return {
        ...state,
        conversations: action.payload,
      };

    case 'SET_CURRENT_CONVERSATION':
      return {
        ...state,
        currentConversation: action.payload,
        // Load messages from the conversation
        messages: action.payload?.messages || [],
      };

    case 'SET_MESSAGES':
      return {
        ...state,
        messages: action.payload,
      };

    case 'ADD_MESSAGE':
      return {
        ...state,
        messages: [...state.messages, action.payload],
      };

    case 'UPDATE_MESSAGE': {
      const { id, updates } = action.payload;
      return {
        ...state,
        messages: state.messages.map(msg =>
          msg.id === id ? { ...msg, ...updates } : msg
        ),
      };
    }

    case 'SET_STREAMING':
      return {
        ...state,
        isStreaming: action.payload,
      };

    case 'SET_LOADING':
      return {
        ...state,
        isLoading: action.payload,
      };

    case 'SET_ERROR':
      return {
        ...state,
        error: action.payload,
      };

    case 'TOGGLE_HISTORY':
      return {
        ...state,
        isHistoryOpen: !state.isHistoryOpen,
      };

    case 'SET_HISTORY_OPEN':
      return {
        ...state,
        isHistoryOpen: action.payload,
      };

    case 'SET_SELECTED_CITATION':
      return {
        ...state,
        selectedCitation: action.payload,
      };

    case 'DELETE_CONVERSATION_LOCAL': {
      const conversationId = action.payload;
      return {
        ...state,
        conversations: state.conversations.filter(c => c.id !== conversationId),
        // If the deleted conversation was active, clear it
        currentConversation:
          state.currentConversation?.id === conversationId
            ? null
            : state.currentConversation,
        messages:
          state.currentConversation?.id === conversationId
            ? []
            : state.messages,
      };
    }

    default:
      return state;
  }
}
