/**
 * API client for SemanticHub.Api communication
 * Handles streaming chat, conversation management, and error handling
 */

import {
  ApiError,
  ChatMessage,
  Conversation,
  ConversationListResponse,
  CreateConversationRequest,
  SendMessageRequest,
  StreamedChatChunk,
  UpdateConversationTitleRequest
} from './models';

// ============================================================================
// Configuration
// ============================================================================

/**
 * Get the API base URL from environment or use default
 * Priority order:
 * 1. NEXT_PUBLIC_CHAT_API_URL (set by Aspire service discovery)
 * 2. NEXT_PUBLIC_API_BASE_URL (local development override in .env.local)
 * 3. http://localhost:5000 (hardcoded fallback)
 */
function getApiBaseUrl(): string {
  // First, try Aspire-provided URL
  if (process.env.NEXT_PUBLIC_CHAT_API_URL) {
    console.log(
      '[API Client] Using Aspire service discovery URL:',
      process.env.NEXT_PUBLIC_CHAT_API_URL
    );
    return process.env.NEXT_PUBLIC_CHAT_API_URL;
  }

  // Second, try local development override
  if (process.env.NEXT_PUBLIC_API_BASE_URL) {
    console.log(
      '[API Client] Using local development URL:',
      process.env.NEXT_PUBLIC_API_BASE_URL
    );
    return process.env.NEXT_PUBLIC_API_BASE_URL;
  }

  // Final fallback
  const fallbackUrl = 'http://localhost:5000';
  console.warn(
    '[API Client] No environment variables set. Using fallback:',
    fallbackUrl
  );
  return fallbackUrl;
}

const API_BASE_URL = getApiBaseUrl();
const isDevelopment = process.env.NODE_ENV === 'development';

// ============================================================================
// Error Handling
// ============================================================================

/**
 * Creates a standardized API error from a fetch response
 */
async function createApiError(response: Response, context: string): Promise<ApiError> {
  let message = `${context} failed`;
  let details: any;

  try {
    const contentType = response.headers.get('content-type');
    if (contentType?.includes('application/json')) {
      const errorData = await response.json();
      message = errorData.message || errorData.error || message;
      details = errorData;
    } else {
      const text = await response.text();
      if (text) {
        message = text;
      }
    }
  } catch (err) {
    // If we can't parse the error response, use default message
    if (isDevelopment) {
      console.error('[API Client] Error parsing error response:', err);
    }
  }

  return {
    message,
    status: response.status,
    details
  };
}

/**
 * Throws a typed ApiError
 */
function throwApiError(message: string, status: number, details?: any): never {
  const error: ApiError = { message, status, details };
  throw error;
}

// ============================================================================
// HTTP Helpers
// ============================================================================

/**
 * Makes a fetch request with standard error handling
 */
async function fetchWithErrorHandling(
  url: string,
  options: RequestInit,
  context: string
): Promise<Response> {
  try {
    if (isDevelopment) {
      console.log(`[API Client] ${options.method || 'GET'} ${url}`);
      if (options.body) {
        console.log('[API Client] Request body:', options.body);
      }
    }

    const response = await fetch(url, options);

    if (isDevelopment) {
      console.log(`[API Client] Response status: ${response.status}`);
    }

    if (!response.ok) {
      const apiError = await createApiError(response, context);
      throw apiError;
    }

    return response;
  } catch (error) {
    if ((error as ApiError).status !== undefined) {
      // Already an ApiError, re-throw
      throw error;
    }

    // Network error or other fetch error
    console.error(`[API Client] ${context} error:`, error);
    throwApiError(
      `Network error: ${error instanceof Error ? error.message : 'Unknown error'}`,
      0,
      error
    );
  }
}

/**
 * Makes a GET request
 */
async function get<T>(
  endpoint: string,
  signal?: AbortSignal,
  context: string = 'GET request'
): Promise<T> {
  const url = `${API_BASE_URL}${endpoint}`;
  const response = await fetchWithErrorHandling(
    url,
    {
      method: 'GET',
      headers: {
        'Content-Type': 'application/json',
      },
      signal,
    },
    context
  );

  const data = await response.json();

  if (isDevelopment) {
    console.log('[API Client] Response data:', data);
  }

  return data as T;
}

/**
 * Makes a POST request
 */
async function post<T>(
  endpoint: string,
  body?: any,
  signal?: AbortSignal,
  context: string = 'POST request'
): Promise<T> {
  const url = `${API_BASE_URL}${endpoint}`;
  const response = await fetchWithErrorHandling(
    url,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: body ? JSON.stringify(body) : undefined,
      signal,
    },
    context
  );

  const data = await response.json();

  if (isDevelopment) {
    console.log('[API Client] Response data:', data);
  }

  return data as T;
}

/**
 * Makes a PUT request
 */
async function put<T>(
  endpoint: string,
  body?: any,
  signal?: AbortSignal,
  context: string = 'PUT request'
): Promise<T> {
  const url = `${API_BASE_URL}${endpoint}`;
  const response = await fetchWithErrorHandling(
    url,
    {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: body ? JSON.stringify(body) : undefined,
      signal,
    },
    context
  );

  const data = await response.json();

  if (isDevelopment) {
    console.log('[API Client] Response data:', data);
  }

  return data as T;
}

/**
 * Makes a DELETE request
 */
async function del(
  endpoint: string,
  signal?: AbortSignal,
  context: string = 'DELETE request'
): Promise<void> {
  const url = `${API_BASE_URL}${endpoint}`;
  await fetchWithErrorHandling(
    url,
    {
      method: 'DELETE',
      headers: {
        'Content-Type': 'application/json',
      },
      signal,
    },
    context
  );
}

// ============================================================================
// SSE Stream Parsing
// ============================================================================

/**
 * Parses Server-Sent Events (SSE) stream and yields parsed chunks
 */
async function* parseSSEStream(
  response: Response,
  signal?: AbortSignal
): AsyncGenerator<StreamedChatChunk, void, unknown> {
  const reader = response.body?.getReader();
  if (!reader) {
    throwApiError('No response body in streaming response', 500);
  }

  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      // Check if request was aborted
      if (signal?.aborted) {
        if (isDevelopment) {
          console.log('[API Client] Stream aborted by client');
        }
        break;
      }

      const { done, value } = await reader.read();

      if (done) {
        if (isDevelopment) {
          console.log('[API Client] Stream completed');
        }
        break;
      }

      // Decode the chunk and add to buffer
      buffer += decoder.decode(value, { stream: true });

      // Process complete lines
      const lines = buffer.split('\n');
      // Keep the last incomplete line in the buffer
      buffer = lines.pop() || '';

      for (const line of lines) {
        // SSE format: "data: {json}"
        if (line.startsWith('data: ')) {
          const data = line.slice(6).trim();

          // Skip empty data lines
          if (!data) {
            continue;
          }

          try {
            const chunk = JSON.parse(data) as StreamedChatChunk;

            if (isDevelopment) {
              console.log('[API Client] Received chunk:', {
                messageId: chunk.messageId,
                contentLength: chunk.content.length,
                isComplete: chunk.isComplete,
                hasCitations: !!chunk.citations
              });
            }

            yield chunk;

            // If this is the final chunk, we can exit early
            if (chunk.isComplete) {
              if (isDevelopment) {
                console.log('[API Client] Final chunk received');
              }
              return;
            }
          } catch (error) {
            console.error('[API Client] Error parsing SSE chunk:', error, 'Data:', data);
            // Continue processing other chunks
          }
        }
        // Ignore comment lines (starting with :) and empty lines
      }
    }

    // Process any remaining data in buffer
    if (buffer.trim()) {
      if (buffer.startsWith('data: ')) {
        const data = buffer.slice(6).trim();
        if (data) {
          try {
            const chunk = JSON.parse(data) as StreamedChatChunk;
            yield chunk;
          } catch (error) {
            console.error('[API Client] Error parsing final SSE chunk:', error);
          }
        }
      }
    }
  } finally {
    // Always release the reader lock
    reader.releaseLock();
  }
}

// ============================================================================
// Chat API
// ============================================================================

/**
 * Streams chat messages from the agent API
 *
 * @param request - The chat message request
 * @param signal - Optional AbortSignal for cancellation
 * @yields StreamedChatChunk objects as they arrive
 *
 * @example
 * ```typescript
 * const abortController = new AbortController();
 *
 * try {
 *   for await (const chunk of streamChatMessage(
 *     { message: 'Hello!', conversationId: '123' },
 *     abortController.signal
 *   )) {
 *     console.log('Chunk:', chunk.content);
 *
 *     if (chunk.isComplete) {
 *       console.log('Citations:', chunk.citations);
 *       break;
 *     }
 *   }
 * } catch (error) {
 *   if (error.status === 0) {
 *     console.log('Request was cancelled');
 *   } else {
 *     console.error('Error:', error);
 *   }
 * }
 * ```
 */
export async function* streamChatMessage(
  request: SendMessageRequest,
  signal?: AbortSignal
): AsyncGenerator<StreamedChatChunk, void, unknown> {
  const url = `${API_BASE_URL}/api/agents/chat/stream`;

  if (isDevelopment) {
    console.log('[API Client] Starting chat stream:', request);
  }

  let response: Response;

  try {
    response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'text/event-stream',
      },
      body: JSON.stringify(request),
      signal,
    });

    if (!response.ok) {
      const apiError = await createApiError(response, 'Chat stream');
      throw apiError;
    }

    // Check that we got a streaming response
    const contentType = response.headers.get('content-type');
    if (!contentType?.includes('text/event-stream') && !contentType?.includes('text/plain')) {
      throwApiError(
        'Expected streaming response but got: ' + contentType,
        response.status
      );
    }

  } catch (error) {
    if ((error as ApiError).status !== undefined) {
      throw error;
    }

    // Network error
    console.error('[API Client] Chat stream error:', error);
    throwApiError(
      `Network error: ${error instanceof Error ? error.message : 'Unknown error'}`,
      0,
      error
    );
  }

  // Parse and yield the SSE stream
  yield* parseSSEStream(response, signal);
}

// ============================================================================
// Conversation API
// ============================================================================

/**
 * Lists all conversations
 *
 * @param offset - Pagination offset (default: 0)
 * @param limit - Maximum number of conversations to return (default: 50)
 * @param userId - Optional user ID to filter by
 * @returns Array of conversations
 *
 * @example
 * ```typescript
 * const conversations = await listConversations(0, 20);
 * console.log('Found', conversations.length, 'conversations');
 * ```
 */
export async function listConversations(
  offset: number = 0,
  limit: number = 50,
  userId?: string
): Promise<Conversation[]> {
  const params = new URLSearchParams({
    offset: offset.toString(),
    limit: limit.toString(),
  });

  if (userId) {
    params.append('userId', userId);
  }

  const endpoint = `/api/conversations?${params.toString()}`;
  const response = await get<Conversation[]>(endpoint, undefined, 'List conversations');

  return response;
}

/**
 * Creates a new conversation
 *
 * @param request - Optional conversation creation parameters
 * @returns The created conversation
 *
 * @example
 * ```typescript
 * const conversation = await createConversation({
 *   title: 'My Chat Session'
 * });
 * console.log('Created conversation:', conversation.id);
 * ```
 */
export async function createConversation(
  request?: CreateConversationRequest
): Promise<Conversation> {
  const response = await post<Conversation>(
    '/api/conversations',
    request,
    undefined,
    'Create conversation'
  );

  return response;
}

/**
 * Gets a specific conversation by ID
 *
 * @param conversationId - The conversation ID
 * @returns The conversation with all messages
 *
 * @example
 * ```typescript
 * const conversation = await getConversation('conv-123');
 * console.log('Conversation has', conversation.messages.length, 'messages');
 * ```
 */
export async function getConversation(
  conversationId: string
): Promise<Conversation> {
  const response = await get<Conversation>(
    `/api/conversations/${conversationId}`,
    undefined,
    'Get conversation'
  );

  return response;
}

/**
 * Updates a conversation's title
 *
 * @param conversationId - The conversation ID
 * @param title - The new title
 * @returns The updated conversation
 *
 * @example
 * ```typescript
 * const updated = await updateConversationTitle('conv-123', 'New Title');
 * console.log('Updated title:', updated.title);
 * ```
 */
export async function updateConversationTitle(
  conversationId: string,
  title: string
): Promise<Conversation> {
  const request: UpdateConversationTitleRequest = { title };

  const response = await put<Conversation>(
    `/api/conversations/${conversationId}/title`,
    request,
    undefined,
    'Update conversation title'
  );

  return response;
}

/**
 * Deletes a specific conversation
 *
 * @param conversationId - The conversation ID to delete
 *
 * @example
 * ```typescript
 * await deleteConversation('conv-123');
 * console.log('Conversation deleted');
 * ```
 */
export async function deleteConversation(
  conversationId: string
): Promise<void> {
  await del(
    `/api/conversations/${conversationId}`,
    undefined,
    'Delete conversation'
  );
}

/**
 * Deletes all conversations for a user
 *
 * @param userId - Optional user ID (if not provided, deletes all)
 *
 * @example
 * ```typescript
 * await deleteAllConversations();
 * console.log('All conversations deleted');
 * ```
 */
export async function deleteAllConversations(
  userId?: string
): Promise<void> {
  const endpoint = userId
    ? `/api/conversations?userId=${encodeURIComponent(userId)}`
    : '/api/conversations';

  await del(endpoint, undefined, 'Delete all conversations');
}

// ============================================================================
// Utilities
// ============================================================================

/**
 * Creates an AbortController that automatically aborts after a timeout
 *
 * @param timeoutMs - Timeout in milliseconds
 * @returns AbortController that will abort after the timeout
 *
 * @example
 * ```typescript
 * const controller = createTimeoutController(30000); // 30 seconds
 *
 * try {
 *   const conversations = await listConversations(0, 50, undefined);
 * } catch (error) {
 *   console.error('Request timed out or failed:', error);
 * }
 * ```
 */
export function createTimeoutController(timeoutMs: number): AbortController {
  const controller = new AbortController();

  setTimeout(() => {
    controller.abort();
  }, timeoutMs);

  return controller;
}

/**
 * Checks if an error is an ApiError
 */
export function isApiError(error: unknown): error is ApiError {
  return (
    typeof error === 'object' &&
    error !== null &&
    'message' in error &&
    'status' in error
  );
}

/**
 * Formats an API error for display
 */
export function formatApiError(error: unknown): string {
  if (isApiError(error)) {
    if (error.status === 0) {
      return 'Network error. Please check your connection.';
    }
    return error.message;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return 'An unknown error occurred';
}
