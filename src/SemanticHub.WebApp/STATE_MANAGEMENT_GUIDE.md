# State Management and Chat Page Implementation Guide

## Overview

This document describes the state management architecture and main chat page implementation for SemanticHub.WebApp. The implementation uses React Context + Reducer pattern for global state management with streaming message support.

## Architecture

### State Management Pattern

**Pattern**: React Context + useReducer (Redux-style without external dependencies)

**Why this pattern?**
- Type-safe with TypeScript
- No external state library dependencies
- Predictable state updates
- Easy to test and debug
- Built-in React hooks

### Directory Structure

```
src/SemanticHub.WebApp/src/
├── state/
│   ├── ChatContext.tsx       # Context provider with helper functions
│   ├── ChatReducer.ts         # Reducer with actions and state types
│   └── index.ts               # Barrel export
├── app/
│   ├── page.tsx               # Main chat page
│   └── page.module.css        # Chat page styles
└── components/
    └── layout/
        └── ClientLayout.tsx   # Root layout with providers
```

## Implementation Details

### 1. State Structure (`ChatReducer.ts`)

```typescript
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
```

**Key Features:**
- Immutable state updates
- Pure reducer functions
- Strongly typed actions
- Separation of UI state and data

### 2. Available Actions

| Action Type | Payload | Purpose |
|------------|---------|---------|
| `SET_CONVERSATIONS` | `Conversation[]` | Load all conversations |
| `SET_CURRENT_CONVERSATION` | `Conversation \| null` | Switch active conversation |
| `SET_MESSAGES` | `ChatMessage[]` | Replace all messages |
| `ADD_MESSAGE` | `ChatMessage` | Add new message to list |
| `UPDATE_MESSAGE` | `{ id, updates }` | Update existing message (for streaming) |
| `SET_STREAMING` | `boolean` | Toggle streaming state |
| `SET_LOADING` | `boolean` | Toggle loading state |
| `SET_ERROR` | `string \| null` | Set/clear error message |
| `TOGGLE_HISTORY` | - | Toggle history sidebar |
| `SET_HISTORY_OPEN` | `boolean` | Set history sidebar state |
| `SET_SELECTED_CITATION` | `Citation \| null` | Select citation for panel |
| `DELETE_CONVERSATION_LOCAL` | `string` | Remove conversation from state |

### 3. Helper Functions (`ChatContext.tsx`)

The context provider exposes these helper functions:

#### `sendMessage(message: string): Promise<void>`
**Purpose**: Send a message and stream the response

**Flow**:
1. Add user message to state immediately
2. Create placeholder assistant message
3. Start streaming from API
4. Update assistant message with each chunk
5. Add citations when complete
6. Handle conversation creation if needed

**Implementation highlights**:
```typescript
// Add user message immediately
const userMessage: ChatMessage = {
  id: `user-${Date.now()}`,
  conversationId: state.currentConversation?.id,
  role: 'user',
  content: message,
  timestamp: new Date().toISOString(),
};
dispatch({ type: 'ADD_MESSAGE', payload: userMessage });

// Stream response
for await (const chunk of streamChatMessage(...)) {
  fullContent = chunk.content;
  dispatch({
    type: 'UPDATE_MESSAGE',
    payload: {
      id: assistantMessageId,
      updates: { content: fullContent, citations: chunk.citations }
    }
  });
}
```

#### `loadConversations(): Promise<void>`
Fetches all conversations from API and updates state

#### `selectConversation(conversationId: string): Promise<void>`
Loads a specific conversation with all messages

#### `createNewConversation(): Promise<void>`
Creates a new conversation and sets it as active

#### `deleteConversation(conversationId: string): Promise<void>`
Deletes a conversation from backend and local state

#### `toggleHistory(): void`
Toggles the history sidebar visibility

#### `clearError(): void`
Clears the error message

#### `setSelectedCitation(citation: Citation | null): void`
Sets the selected citation for the detail panel

### 4. Layout Architecture

**ClientLayout Structure**:
```
ThemeProvider
└── FluentProvider (theme-aware)
    └── ChatContextProvider
        └── LayoutWrapper (pathname-aware)
            ├── MainLayout (for /other routes)
            └── Full-screen (for / chat page)
```

**Routing Logic**:
- Chat page (`/`) renders full-screen without MainLayout
- Other routes render within MainLayout (sidebar + top nav)
- Controlled by `LayoutWrapper` component checking pathname

### 5. Chat Page Structure

```
┌─────────────────────────────────────────────┐
│ Header                                      │
│ [History] New Chat          [New] [Theme]   │
├─────────────────────────────────────────────┤
│ Error Banner (if error)                     │
├─────────┬───────────────────────────────────┤
│ History │ Messages Area                     │
│ Sidebar │ ┌─────────────────────────────┐   │
│ (toggle)│ │ User Message                │   │
│         │ └─────────────────────────────┘   │
│         │ ┌─────────────────────────────┐   │
│         │ │ Assistant Answer            │   │
│         │ │ [Citations...]              │   │
│         │ └─────────────────────────────┘   │
│         │                                   │
├─────────┴───────────────────────────────────┤
│ Input Area                                  │
│ [Type your message...]            [Send]    │
└─────────────────────────────────────────────┘
```

**Key UI Features**:
- Full viewport height (100vh)
- Sticky header and input
- Scrollable message area
- Collapsible history sidebar
- Auto-scroll to bottom on new messages
- Theme toggle (light/dark)
- Error banner with dismiss
- Loading states

### 6. Streaming Implementation

**Challenge**: Update UI in real-time as message chunks arrive

**Solution**:
1. Create placeholder message with empty content
2. For each chunk, update message content via `UPDATE_MESSAGE` action
3. Reducer merges updates into existing message
4. React re-renders only changed message
5. Auto-scroll maintains bottom position

**Performance**:
- Uses `useCallback` for event handlers
- Uses `useMemo` for computed values
- AbortController for canceling streams
- Efficient reducer updates (shallow copy)

## Testing

### Running the Application

1. **Start the backend** (in separate terminal):
   ```bash
   cd /Users/vesselin/Source/GitHub/SemanticKernelMemoryRAG
   dotnet run --project src/SemanticHub.AppHost
   ```

2. **Start the frontend**:
   ```bash
   cd /Users/vesselin/Source/GitHub/SemanticKernelMemoryRAG/src/SemanticHub.WebApp
   npm run dev
   ```

3. **Open browser**: http://localhost:3000

### Testing Checklist

#### Basic Chat Flow
- [ ] Send a message and see user bubble appear
- [ ] Watch streaming response render character by character
- [ ] See citations appear at bottom of assistant message
- [ ] Click citation number in text to see selection
- [ ] Auto-scroll follows new messages

#### Conversation Management
- [ ] Click "New Chat" button to start fresh
- [ ] Toggle history sidebar open/closed
- [ ] See conversation list (placeholder for now)
- [ ] Close sidebar with X button

#### Error Handling
- [ ] Stop backend and send message to trigger error
- [ ] See error banner appear
- [ ] Click dismiss button to clear error
- [ ] Input should be disabled during streaming

#### Theme Support
- [ ] Toggle between light and dark mode
- [ ] Colors update throughout UI
- [ ] Theme persists on page reload
- [ ] System preference detected on first load

#### Responsive Design
- [ ] Resize browser to mobile width
- [ ] "New Chat" text hides on mobile
- [ ] History sidebar becomes overlay on mobile
- [ ] Messages remain readable

#### Accessibility
- [ ] Navigate with keyboard (Tab, Enter, Space)
- [ ] Screen reader announces new messages (role="log")
- [ ] All buttons have aria-labels
- [ ] Focus visible indicators present

### Known Limitations (Current Chunk)

1. **History sidebar**: Placeholder only - shows message, no actual list
2. **Citation panel**: Not implemented - clicking citations logs to console
3. **Conversation persistence**: Not fully implemented on backend
4. **Message editing**: Not supported
5. **Message deletion**: Not supported
6. **File uploads**: Not supported

These will be addressed in future chunks (5A and 5B).

## Design Decisions

### Why Context + Reducer instead of Redux?

**Pros**:
- No external dependency
- Built-in to React
- Simple for this app size
- Easy to understand
- Type-safe

**Cons**:
- No devtools (can add Redux DevTools extension if needed)
- No middleware (not needed for this app)
- Re-renders entire context tree (acceptable for single chat page)

### Why Separate Reducer File?

- Easier to test reducer in isolation
- Cleaner separation of concerns
- Reducer is pure (no side effects)
- Context provider handles side effects (API calls)

### Why Helper Functions in Context?

Alternative: Components call API directly and dispatch

**Chosen approach**:
- Context provides helper functions
- Functions encapsulate complex logic (streaming)
- Easier to reuse across components
- Single source of truth for state updates

### Why `UPDATE_MESSAGE` Instead of Replacing Array?

**Performance**: Updating one message is O(n) but with shallow copy
**Alternative**: Replace entire array - worse performance and harder to track changes
**Benefit**: React efficiently re-renders only changed message component

## Troubleshooting

### Messages not appearing
- Check backend is running (http://localhost:5000/health)
- Open browser console for errors
- Verify API_BASE_URL in .env.local

### Streaming not working
- Check network tab for SSE connection
- Ensure backend supports text/event-stream
- Check for CORS errors

### Styling issues
- Clear .next directory: `rm -rf .next`
- Rebuild: `npm run build`
- Check browser console for CSS warnings

### Context error "must be used within provider"
- Ensure component is inside `<ClientLayout>`
- Check component is client-side ('use client')

## Future Enhancements

### Chunk 5A - Conversation History
- Display conversation list in sidebar
- Conversation selection
- Conversation deletion
- Search/filter conversations
- Auto-generated titles

### Chunk 5B - Citation Panel
- Side panel with citation details
- Full document content
- Relevance score display
- Link to source document
- Multiple citation comparison

### Performance Optimizations
- Virtualized message list for long conversations
- Message pagination/lazy loading
- Optimistic updates for better UX
- Service worker for offline support

### Additional Features
- Message editing
- Message regeneration
- Copy message to clipboard
- Export conversation
- Share conversation
- Voice input
- File attachments

## Resources

- [React Context API](https://react.dev/reference/react/createContext)
- [useReducer Hook](https://react.dev/reference/react/useReducer)
- [Fluent UI v9](https://react.fluentui.dev/)
- [Next.js App Router](https://nextjs.org/docs/app)
- [Server-Sent Events](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events)

## Summary

The state management implementation provides:
- ✅ Type-safe state management
- ✅ Streaming message support
- ✅ Conversation management
- ✅ Error handling
- ✅ Loading states
- ✅ Theme support
- ✅ Responsive design
- ✅ Accessibility
- ✅ Full-screen chat interface
- ✅ Placeholder history sidebar

The architecture is extensible and ready for Chunks 5A (history) and 5B (citation panel).
