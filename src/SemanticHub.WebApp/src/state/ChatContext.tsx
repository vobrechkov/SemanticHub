'use client';

/**
 * ChatContext - Global state management for chat functionality
 * Provides state, dispatch, and helper functions for chat operations
 */

import React, { createContext, useContext, useReducer, useCallback, useRef, useEffect } from 'react';
import { chatReducer, initialChatState, ChatState, ChatAction } from './ChatReducer';
import {
  streamChatMessage,
  listConversations,
  createConversation,
  getConversation,
  deleteConversation as apiDeleteConversation,
  formatApiError,
} from '@/api/client';
import { ChatMessage, Citation } from '@/api/models';

// ============================================================================
// Context Interface
// ============================================================================

interface ChatContextValue {
  state: ChatState;
  dispatch: React.Dispatch<ChatAction>;

  // Helper functions
  sendMessage: (message: string) => Promise<void>;
  stopStreaming: () => void;
  loadConversations: () => Promise<void>;
  selectConversation: (conversationId: string) => Promise<void>;
  createNewConversation: () => Promise<void>;
  deleteConversation: (conversationId: string) => Promise<void>;
  toggleHistory: () => void;
  clearError: () => void;
  setSelectedCitation: (citation: Citation | null) => void;
  retryLastMessage: () => Promise<void>;
}

// ============================================================================
// Context Creation
// ============================================================================

const ChatContext = createContext<ChatContextValue | undefined>(undefined);

// ============================================================================
// Provider Component
// ============================================================================

export function ChatContextProvider({ children }: { children: React.ReactNode }) {
  const [state, dispatch] = useReducer(chatReducer, initialChatState);
  const abortControllerRef = useRef<AbortController | null>(null);
  const lastUserMessageRef = useRef<string | null>(null);

  // --------------------------------------------------------------------------
  // Helper: Load Conversations
  // --------------------------------------------------------------------------

  const loadConversations = useCallback(async () => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      console.log('[ChatContext] Loading conversations...');
      const conversations = await listConversations(0, 50);

      console.log(`[ChatContext] Loaded ${conversations.length} conversations`);
      dispatch({ type: 'SET_CONVERSATIONS', payload: conversations });
    } catch (error) {
      const errorMessage = formatApiError(error);
      console.error('[ChatContext] Failed to load conversations:', errorMessage);
      dispatch({ type: 'SET_ERROR', payload: errorMessage });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  }, []);

  // --------------------------------------------------------------------------
  // Helper: Select Conversation
  // --------------------------------------------------------------------------

  const selectConversation = useCallback(async (conversationId: string) => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      console.log('[ChatContext] Selecting conversation:', conversationId);
      const conversation = await getConversation(conversationId);

      console.log(`[ChatContext] Loaded conversation with ${conversation.messages.length} messages`);
      dispatch({ type: 'SET_CURRENT_CONVERSATION', payload: conversation });
    } catch (error) {
      const errorMessage = formatApiError(error);
      console.error('[ChatContext] Failed to load conversation:', errorMessage);
      dispatch({ type: 'SET_ERROR', payload: errorMessage });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  }, []);

  // --------------------------------------------------------------------------
  // Helper: Create New Conversation
  // --------------------------------------------------------------------------

  const createNewConversation = useCallback(async () => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      console.log('[ChatContext] Creating new conversation...');
      const conversation = await createConversation({
        title: 'New Chat',
      });

      console.log('[ChatContext] Created new conversation:', conversation.id);
      dispatch({ type: 'SET_CURRENT_CONVERSATION', payload: conversation });

      // Add to conversations list
      dispatch({
        type: 'SET_CONVERSATIONS',
        payload: [conversation, ...state.conversations],
      });
    } catch (error) {
      const errorMessage = formatApiError(error);
      console.error('[ChatContext] Failed to create conversation:', errorMessage);
      dispatch({ type: 'SET_ERROR', payload: errorMessage });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  }, [state.conversations]);

  // --------------------------------------------------------------------------
  // Helper: Delete Conversation
  // --------------------------------------------------------------------------

  const deleteConversation = useCallback(async (conversationId: string) => {
    try {
      dispatch({ type: 'SET_LOADING', payload: true });
      dispatch({ type: 'SET_ERROR', payload: null });

      console.log('[ChatContext] Deleting conversation:', conversationId);
      await apiDeleteConversation(conversationId);

      console.log('[ChatContext] Conversation deleted successfully');
      dispatch({ type: 'DELETE_CONVERSATION_LOCAL', payload: conversationId });
    } catch (error) {
      const errorMessage = formatApiError(error);
      console.error('[ChatContext] Failed to delete conversation:', errorMessage);
      dispatch({ type: 'SET_ERROR', payload: errorMessage });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  }, []);

  // --------------------------------------------------------------------------
  // Helper: Send Message
  // --------------------------------------------------------------------------

  const sendMessage = useCallback(
    async (message: string) => {
      // Cancel any existing stream
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }

      // Create new abort controller for this request
      const abortController = new AbortController();
      abortControllerRef.current = abortController;

      // Store last user message for retry functionality
      lastUserMessageRef.current = message;

      try {
        dispatch({ type: 'SET_ERROR', payload: null });

        // Add user message immediately
        const userMessage: ChatMessage = {
          id: `user-${Date.now()}`,
          conversationId: state.currentConversation?.id,
          role: 'user',
          content: message,
          timestamp: new Date().toISOString(),
        };

        console.log('[ChatContext] Adding user message:', userMessage.id);
        dispatch({ type: 'ADD_MESSAGE', payload: userMessage });

        // Create assistant message placeholder
        const assistantMessageId = `assistant-${Date.now()}`;
        const assistantMessage: ChatMessage = {
          id: assistantMessageId,
          conversationId: state.currentConversation?.id,
          role: 'assistant',
          content: '',
          timestamp: new Date().toISOString(),
          citations: [],
        };

        dispatch({ type: 'ADD_MESSAGE', payload: assistantMessage });
        dispatch({ type: 'SET_STREAMING', payload: true });

        console.log('[ChatContext] Starting message stream...');

        // Stream the response
        let fullContent = '';
        let finalCitations: Citation[] | undefined;
        let receivedConversationId: string | undefined;

        for await (const chunk of streamChatMessage(
          {
            message,
            conversationId: state.currentConversation?.id,
            includeHistory: true,
          },
          abortController.signal
        )) {
          // Check if aborted
          if (abortController.signal.aborted) {
            console.log('[ChatContext] Stream aborted by user');
            break;
          }

          // Accumulate content
          fullContent += chunk.content;

          // Store conversation ID from first chunk
          if (chunk.conversationId && !receivedConversationId) {
            receivedConversationId = chunk.conversationId;
          }

          // Store citations when received (typically in final chunk)
          if (chunk.citations && chunk.citations.length > 0) {
            finalCitations = chunk.citations;
          }

          // Update assistant message with accumulated content
          dispatch({
            type: 'UPDATE_MESSAGE',
            payload: {
              id: assistantMessageId,
              updates: {
                content: fullContent,
                citations: finalCitations,
                conversationId: receivedConversationId,
              },
            },
          });

          // If this is the final chunk, we can stop
          if (chunk.isComplete) {
            console.log('[ChatContext] Stream completed');
            break;
          }
        }

        // If we received a conversation ID and didn't have one before, update current conversation
        if (receivedConversationId && !state.currentConversation) {
          console.log('[ChatContext] Stream created new conversation:', receivedConversationId);
          // Optionally fetch the full conversation to update state
          try {
            const conversation = await getConversation(receivedConversationId);
            dispatch({ type: 'SET_CURRENT_CONVERSATION', payload: conversation });
            dispatch({
              type: 'SET_CONVERSATIONS',
              payload: [conversation, ...state.conversations],
            });
          } catch (error) {
            console.error('[ChatContext] Failed to fetch new conversation:', error);
          }
        }

        dispatch({ type: 'SET_STREAMING', payload: false });
      } catch (error) {
        console.error('[ChatContext] Failed to send message:', error);

        // Only show error if not aborted
        if (!abortController.signal.aborted) {
          const errorMessage = formatApiError(error);
          dispatch({ type: 'SET_ERROR', payload: errorMessage });
        }

        dispatch({ type: 'SET_STREAMING', payload: false });
      } finally {
        abortControllerRef.current = null;
      }
    },
    [state.currentConversation, state.conversations]
  );

  // --------------------------------------------------------------------------
  // Helper: Stop Streaming
  // --------------------------------------------------------------------------

  const stopStreaming = useCallback(() => {
    if (abortControllerRef.current) {
      console.log('[ChatContext] Stopping stream...');
      abortControllerRef.current.abort();
      abortControllerRef.current = null;
      dispatch({ type: 'SET_STREAMING', payload: false });
    }
  }, []);

  // --------------------------------------------------------------------------
  // Helper: Retry Last Message
  // --------------------------------------------------------------------------

  const retryLastMessage = useCallback(async () => {
    if (lastUserMessageRef.current) {
      console.log('[ChatContext] Retrying last message:', lastUserMessageRef.current);
      await sendMessage(lastUserMessageRef.current);
    }
  }, [sendMessage]);

  // --------------------------------------------------------------------------
  // Helper: Toggle History Sidebar
  // --------------------------------------------------------------------------

  const toggleHistory = useCallback(() => {
    dispatch({ type: 'TOGGLE_HISTORY' });
  }, []);

  // --------------------------------------------------------------------------
  // Helper: Clear Error
  // --------------------------------------------------------------------------

  const clearError = useCallback(() => {
    dispatch({ type: 'SET_ERROR', payload: null });
  }, []);

  // --------------------------------------------------------------------------
  // Helper: Set Selected Citation
  // --------------------------------------------------------------------------

  const setSelectedCitation = useCallback((citation: Citation | null) => {
    dispatch({ type: 'SET_SELECTED_CITATION', payload: citation });
  }, []);

  // --------------------------------------------------------------------------
  // Effect: Load Conversations on Mount
  // --------------------------------------------------------------------------

  useEffect(() => {
    console.log('[ChatContext] Mounting provider, loading conversations...');
    loadConversations();
  }, [loadConversations]);

  // --------------------------------------------------------------------------
  // Context Value
  // --------------------------------------------------------------------------

  const value: ChatContextValue = {
    state,
    dispatch,
    sendMessage,
    stopStreaming,
    retryLastMessage,
    loadConversations,
    selectConversation,
    createNewConversation,
    deleteConversation,
    toggleHistory,
    clearError,
    setSelectedCitation,
  };

  return <ChatContext.Provider value={value}>{children}</ChatContext.Provider>;
}

// ============================================================================
// Hook
// ============================================================================

export function useChatContext() {
  const context = useContext(ChatContext);
  if (context === undefined) {
    throw new Error('useChatContext must be used within a ChatContextProvider');
  }
  return context;
}
