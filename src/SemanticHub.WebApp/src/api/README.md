# SemanticHub API Client

This directory contains the TypeScript API client for communicating with the SemanticHub.Api backend.

## Overview

The API client provides:
- **Type-safe** TypeScript interfaces for all API interactions
- **Streaming chat** via Server-Sent Events (SSE)
- **Conversation management** (CRUD operations)
- **Error handling** with typed errors
- **Request cancellation** via AbortController
- **Development logging** for debugging

## Files

- **`models.ts`** - TypeScript type definitions for requests/responses
- **`client.ts`** - API client implementation with all endpoint functions
- **`index.ts`** - Public exports for consuming code

## Configuration

The API base URL is configured via environment variable in `.env.local`:

```bash
NEXT_PUBLIC_API_BASE_URL=http://localhost:5000
```

If not set, defaults to `http://localhost:5000`.

## Usage Examples

### Import the API Client

```typescript
import {
  streamChatMessage,
  listConversations,
  createConversation,
  getConversation,
  updateConversationTitle,
  deleteConversation,
  deleteAllConversations,
  formatApiError,
  isApiError,
} from '@/api';

import type {
  ChatMessage,
  Conversation,
  SendMessageRequest,
  StreamedChatChunk,
} from '@/api';
```

### 1. Streaming Chat Messages

The primary use case - streaming chat responses from the agent:

```typescript
async function sendMessage(message: string, conversationId?: string) {
  const abortController = new AbortController();
  let fullContent = '';
  let citations = [];

  try {
    // Stream the response
    for await (const chunk of streamChatMessage(
      {
        message,
        conversationId,
        includeHistory: true
      },
      abortController.signal
    )) {
      // Accumulate content
      fullContent += chunk.content;

      // Update UI with partial content
      console.log('Received:', chunk.content);

      // Check if this is the final chunk
      if (chunk.isComplete) {
        console.log('Complete! Message ID:', chunk.messageId);

        // Citations are typically in the final chunk
        if (chunk.citations) {
          citations = chunk.citations;
          console.log('Citations:', citations);
        }

        break;
      }
    }

    return {
      content: fullContent,
      citations,
    };

  } catch (error) {
    if (isApiError(error)) {
      console.error('API Error:', formatApiError(error));

      if (error.status === 0) {
        console.log('Request was cancelled');
      }
    } else {
      console.error('Unexpected error:', error);
    }
    throw error;
  }
}

// Cancel a request
function cancelRequest() {
  abortController.abort();
}
```

### 2. React Component Example

```typescript
'use client';

import { useState } from 'react';
import { streamChatMessage, formatApiError } from '@/api';
import type { Citation } from '@/api';

export default function ChatComponent() {
  const [input, setInput] = useState('');
  const [response, setResponse] = useState('');
  const [citations, setCitations] = useState<Citation[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [abortController, setAbortController] = useState<AbortController | null>(null);

  const handleSend = async () => {
    setLoading(true);
    setError(null);
    setResponse('');
    setCitations([]);

    const controller = new AbortController();
    setAbortController(controller);

    try {
      for await (const chunk of streamChatMessage(
        { message: input },
        controller.signal
      )) {
        // Update response incrementally
        setResponse(prev => prev + chunk.content);

        if (chunk.isComplete && chunk.citations) {
          setCitations(chunk.citations);
        }
      }
    } catch (err) {
      setError(formatApiError(err));
    } finally {
      setLoading(false);
      setAbortController(null);
    }
  };

  const handleCancel = () => {
    abortController?.abort();
    setLoading(false);
  };

  return (
    <div>
      <input
        value={input}
        onChange={(e) => setInput(e.target.value)}
        placeholder="Type a message..."
        disabled={loading}
      />

      {loading ? (
        <button onClick={handleCancel}>Cancel</button>
      ) : (
        <button onClick={handleSend}>Send</button>
      )}

      {error && <div className="error">{error}</div>}

      {response && (
        <div className="response">
          <p>{response}</p>

          {citations.length > 0 && (
            <div className="citations">
              <h4>Sources:</h4>
              {citations.map((citation) => (
                <div key={citation.id}>
                  <strong>{citation.title || 'Source'}</strong>
                  <p>{citation.content}</p>
                  {citation.url && <a href={citation.url}>View Source</a>}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
```

### 3. List Conversations

```typescript
async function loadConversations() {
  try {
    const conversations = await listConversations(
      0,    // offset
      20,   // limit
      'user-123' // optional userId
    );

    console.log(`Found ${conversations.length} conversations`);

    conversations.forEach(conv => {
      console.log(`${conv.title} - ${conv.messages.length} messages`);
    });

    return conversations;

  } catch (error) {
    console.error('Failed to load conversations:', formatApiError(error));
    throw error;
  }
}
```

### 4. Create a Conversation

```typescript
async function startNewConversation(title?: string) {
  try {
    const conversation = await createConversation({
      title: title || 'New Chat',
      userId: 'user-123', // optional
    });

    console.log('Created conversation:', conversation.id);
    return conversation;

  } catch (error) {
    console.error('Failed to create conversation:', formatApiError(error));
    throw error;
  }
}
```

### 5. Get a Conversation

```typescript
async function loadConversation(conversationId: string) {
  try {
    const conversation = await getConversation(conversationId);

    console.log(`Loaded: ${conversation.title}`);
    console.log(`Messages: ${conversation.messages.length}`);

    conversation.messages.forEach(msg => {
      console.log(`[${msg.role}]: ${msg.content.substring(0, 50)}...`);
    });

    return conversation;

  } catch (error) {
    if (isApiError(error) && error.status === 404) {
      console.log('Conversation not found');
    } else {
      console.error('Failed to load conversation:', formatApiError(error));
    }
    throw error;
  }
}
```

### 6. Rename a Conversation

```typescript
async function renameConversation(conversationId: string, newTitle: string) {
  try {
    const updated = await updateConversationTitle(conversationId, newTitle);

    console.log('Updated title:', updated.title);
    return updated;

  } catch (error) {
    console.error('Failed to rename conversation:', formatApiError(error));
    throw error;
  }
}
```

### 7. Delete a Conversation

```typescript
async function removeConversation(conversationId: string) {
  try {
    await deleteConversation(conversationId);
    console.log('Conversation deleted successfully');

  } catch (error) {
    console.error('Failed to delete conversation:', formatApiError(error));
    throw error;
  }
}
```

### 8. Delete All Conversations

```typescript
async function clearAllConversations(userId?: string) {
  try {
    await deleteAllConversations(userId);
    console.log('All conversations deleted');

  } catch (error) {
    console.error('Failed to delete conversations:', formatApiError(error));
    throw error;
  }
}
```

### 9. Complete Chat Flow

```typescript
async function completeChatFlow() {
  try {
    // 1. Create a new conversation
    const conversation = await createConversation({
      title: 'My Chat Session',
    });
    console.log('Created conversation:', conversation.id);

    // 2. Send first message
    let fullResponse = '';
    for await (const chunk of streamChatMessage({
      message: 'Hello, how are you?',
      conversationId: conversation.id,
      includeHistory: true,
    })) {
      fullResponse += chunk.content;

      if (chunk.isComplete) {
        console.log('First response complete');
      }
    }

    // 3. Send follow-up message
    fullResponse = '';
    for await (const chunk of streamChatMessage({
      message: 'Tell me about semantic search',
      conversationId: conversation.id,
      includeHistory: true,
    })) {
      fullResponse += chunk.content;

      if (chunk.isComplete && chunk.citations) {
        console.log('Got citations:', chunk.citations.length);
      }
    }

    // 4. Load the conversation to see all messages
    const updated = await getConversation(conversation.id);
    console.log('Conversation has', updated.messages.length, 'messages');

    // 5. Rename the conversation
    await updateConversationTitle(conversation.id, 'Semantic Search Discussion');

    // 6. Optionally delete when done
    // await deleteConversation(conversation.id);

  } catch (error) {
    console.error('Chat flow error:', formatApiError(error));
  }
}
```

## Error Handling

The API client uses typed `ApiError` objects:

```typescript
interface ApiError {
  message: string;  // Human-readable error message
  status: number;   // HTTP status code (0 for network errors)
  details?: any;    // Additional error details from server
}
```

### Error Handling Best Practices

```typescript
import { isApiError, formatApiError } from '@/api';

async function handleApiCall() {
  try {
    const result = await listConversations();
    return result;

  } catch (error) {
    // Check if it's an API error
    if (isApiError(error)) {
      console.error('API Error:', error.message);
      console.error('Status:', error.status);

      // Handle specific error codes
      if (error.status === 401) {
        console.log('Unauthorized - please login');
      } else if (error.status === 404) {
        console.log('Resource not found');
      } else if (error.status === 0) {
        console.log('Network error - check connection');
      }

      // Get user-friendly message
      const message = formatApiError(error);
      alert(message);
    } else {
      // Unexpected error
      console.error('Unexpected error:', error);
    }

    throw error;
  }
}
```

## SSE Stream Details

The chat streaming endpoint uses Server-Sent Events (SSE) format:

```
data: {"messageId":"msg-123","content":"Hello","role":"assistant","isComplete":false,"timestamp":"2025-10-30T..."}

data: {"messageId":"msg-123","content":" world","role":"assistant","isComplete":false,"timestamp":"2025-10-30T..."}

data: {"messageId":"msg-123","content":"!","role":"assistant","isComplete":true,"citations":[],"timestamp":"2025-10-30T..."}
```

The client automatically:
- Parses each `data:` line as JSON
- Yields `StreamedChatChunk` objects
- Handles stream cancellation
- Cleans up resources properly
- Detects completion via `isComplete` flag

## Request Cancellation

All API functions support request cancellation via `AbortController`:

```typescript
const controller = new AbortController();

// Start request
const promise = listConversations(0, 50, undefined);

// Cancel after 5 seconds
setTimeout(() => {
  controller.abort();
  console.log('Request cancelled');
}, 5000);

try {
  const result = await promise;
} catch (error) {
  if (isApiError(error) && error.status === 0) {
    console.log('Request was cancelled');
  }
}
```

For streaming requests:

```typescript
const controller = new AbortController();

const stream = streamChatMessage(
  { message: 'Hello' },
  controller.signal
);

// Cancel at any time
controller.abort();
```

## Development Mode

When `NODE_ENV=development`, the client logs:
- All requests (method, URL, body)
- All responses (status, data)
- Stream chunks (metadata only)
- Errors with full details

This helps with debugging during development.

## Testing the Client

### Prerequisites

1. Start the backend services:
   ```bash
   cd src/SemanticHub.AppHost
   dotnet run
   ```

2. Verify backend is running:
   ```bash
   curl http://localhost:5000/health
   ```

### Manual Testing

Use the browser console or a Node.js script:

```typescript
// In browser console or Node script
import { listConversations, streamChatMessage } from '@/api';

// Test listing conversations
const conversations = await listConversations();
console.log('Conversations:', conversations);

// Test streaming chat
for await (const chunk of streamChatMessage({ message: 'Hello!' })) {
  console.log('Chunk:', chunk.content);
  if (chunk.isComplete) break;
}
```

### Integration with Components

The API client is designed to work seamlessly with React components:

```typescript
import { useEffect, useState } from 'react';
import { listConversations } from '@/api';
import type { Conversation } from '@/api';

function ConversationList() {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    async function load() {
      try {
        const data = await listConversations();
        setConversations(data);
      } catch (error) {
        console.error('Failed to load:', error);
      } finally {
        setLoading(false);
      }
    }
    load();
  }, []);

  if (loading) return <div>Loading...</div>;

  return (
    <ul>
      {conversations.map(conv => (
        <li key={conv.id}>{conv.title}</li>
      ))}
    </ul>
  );
}
```

## API Reference

### Chat Functions

#### `streamChatMessage(request, signal?)`
- **Purpose**: Stream chat messages from the agent
- **Params**:
  - `request: SendMessageRequest` - Message and conversation details
  - `signal?: AbortSignal` - Optional cancellation signal
- **Returns**: `AsyncGenerator<StreamedChatChunk>`
- **Throws**: `ApiError` on failure

### Conversation Functions

#### `listConversations(offset?, limit?, userId?)`
- **Purpose**: Get a list of conversations
- **Returns**: `Promise<Conversation[]>`

#### `createConversation(request?)`
- **Purpose**: Create a new conversation
- **Returns**: `Promise<Conversation>`

#### `getConversation(conversationId)`
- **Purpose**: Get a specific conversation by ID
- **Returns**: `Promise<Conversation>`

#### `updateConversationTitle(conversationId, title)`
- **Purpose**: Update a conversation's title
- **Returns**: `Promise<Conversation>`

#### `deleteConversation(conversationId)`
- **Purpose**: Delete a specific conversation
- **Returns**: `Promise<void>`

#### `deleteAllConversations(userId?)`
- **Purpose**: Delete all conversations (optionally filtered by user)
- **Returns**: `Promise<void>`

### Utility Functions

#### `isApiError(error)`
- **Purpose**: Type guard to check if an error is an `ApiError`
- **Returns**: `boolean`

#### `formatApiError(error)`
- **Purpose**: Get a user-friendly error message
- **Returns**: `string`

#### `createTimeoutController(timeoutMs)`
- **Purpose**: Create an AbortController that auto-cancels after timeout
- **Returns**: `AbortController`

## Backend Endpoints

The client communicates with these SemanticHub.Api endpoints:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/agents/chat/stream` | POST | Streaming chat with SSE |
| `/api/conversations` | GET | List conversations |
| `/api/conversations` | POST | Create conversation |
| `/api/conversations/{id}` | GET | Get conversation |
| `/api/conversations/{id}/title` | PUT | Update title |
| `/api/conversations/{id}` | DELETE | Delete conversation |
| `/api/conversations` | DELETE | Delete all conversations |

## Troubleshooting

### "NEXT_PUBLIC_API_BASE_URL not set" warning
- Set the environment variable in `.env.local`
- Restart the Next.js dev server after changing `.env.local`

### "Network error" when calling API
- Check that SemanticHub.Api is running (`dotnet run --project src/SemanticHub.AppHost`)
- Verify the API URL in `.env.local` matches the actual port
- Check browser console for CORS errors

### Streaming stops abruptly
- Check backend logs for errors
- Verify the stream is completing with `isComplete: true`
- Ensure AbortController isn't being triggered unintentionally

### TypeScript errors
- Ensure you're importing types correctly: `import type { Conversation } from '@/api'`
- Check that `tsconfig.json` has proper path mappings for `@/` alias
