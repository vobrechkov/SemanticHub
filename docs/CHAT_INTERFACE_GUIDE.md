# SemanticHub Chat Interface - User Guide

## Overview

The SemanticHub chat interface provides an intelligent conversational experience for interacting with your document knowledge base. This guide covers all features, keyboard shortcuts, and usage patterns.

## Features

### 1. Chat Messaging

- **Send Messages**: Type your question and press Enter (or click Send button)
- **Multi-line Input**: Use Shift+Enter for line breaks
- **Character Limit**: Messages are limited to 2000 characters
- **Auto-focus**: Input automatically focuses after sending a message

### 2. Streaming Responses

- **Real-time Streaming**: Assistant responses stream in real-time
- **Stop Generation**: Click "Stop Generating" button to cancel streaming
- **Streaming Indicator**: Blinking cursor shows when message is streaming

### 3. Message Features

#### Citations

- **Inline Citations**: Superscript numbers in responses (e.g., [1], [2])
- **Citation Details**: Click citation numbers to view source details
- **Citation Panel**: Slide-in panel with full source information
  - Document title and path
  - Relevance score
  - Content excerpt
  - Copy content
  - Open source URL

#### Copy Functionality

- **Copy Answer**: Click copy button to copy entire message
- **Copy Code Blocks**: Each code block has individual copy button
- **Copy Citations**: Copy citation content from panel

#### Markdown Support

- **Rich Formatting**: Full markdown support (headers, lists, tables, etc.)
- **Code Highlighting**: Syntax highlighting for code blocks
- **GitHub Flavored Markdown**: Tables, task lists, strikethrough, etc.

### 4. Conversation Management

#### Chat History

- **View History**: Click history icon or press Ctrl+K
- **Select Conversation**: Click conversation to load it
- **Delete Conversation**: Click delete icon (requires confirmation)
- **New Conversation**: Click "New Chat" or press Ctrl+N
- **Conversation Metadata**: Shows title, timestamp, and message count

#### Auto-scroll Behavior

- **Smart Scroll**: Auto-scrolls only when you're at the bottom
- **Manual Scroll**: Scroll up to read previous messages without interruption
- **Scroll to Bottom**: Floating button appears when scrolled up
- **Conversation Switch**: Auto-scrolls to bottom when switching conversations

### 5. Error Handling

- **Error Display**: Errors shown in prominent banner at top
- **Retry Functionality**: Click "Retry" to resend failed message
- **Error Dismissal**: Click X to dismiss error
- **Network Errors**: Automatic detection and user-friendly messages

### 6. Keyboard Shortcuts

Press `Ctrl+?` (or `Cmd+?` on Mac) to view all shortcuts:

| Shortcut | Action |
|----------|--------|
| `Ctrl/Cmd + N` | Start new conversation |
| `Ctrl/Cmd + K` | Toggle conversation history |
| `Ctrl/Cmd + /` | Focus message input |
| `Ctrl/Cmd + ?` | Show keyboard shortcuts |
| `Esc` | Close sidebar or dialog |
| `Enter` | Send message |
| `Shift + Enter` | New line in message |

### 7. Theme Support

- **Dark/Light Mode**: Toggle with theme button in header
- **System Preference**: Respects system theme preference
- **Persistent**: Theme choice saved to local storage

### 8. Accessibility

#### Screen Reader Support

- **ARIA Labels**: All interactive elements properly labeled
- **Live Regions**: New messages announced to screen readers
- **Error Announcements**: Errors announced with role="alert"

#### Keyboard Navigation

- **Full Keyboard Access**: All features accessible via keyboard
- **Focus Management**: Logical focus order and visible indicators
- **Focus Trapping**: Dialogs trap focus appropriately
- **Skip Links**: Navigate efficiently through interface

#### Responsive Design

- **Mobile Optimized**: Touch-friendly targets (min 44x44px)
- **Tablet Support**: Optimized layouts for medium screens
- **Desktop Enhanced**: Full feature set on large screens
- **Viewport Scaling**: Adapts to all screen sizes

### 9. Performance Optimizations

- **Component Memoization**: Prevents unnecessary re-renders
- **Callback Optimization**: Stable function references
- **Smart Rendering**: Only updates changed components
- **Efficient Scrolling**: Optimized scroll behavior

## Usage Patterns

### Starting a Conversation

1. **New User**: Welcome screen with example prompts
2. **Click Example**: Click suggested question to start
3. **Type Question**: Or type your own question
4. **View Response**: Streaming response appears below

### Working with Citations

1. **Identify Citations**: Look for superscript numbers [1], [2]
2. **Click Citation**: Opens citation panel on right
3. **Review Source**: See document details and excerpt
4. **Copy Content**: Use copy button if needed
5. **Open Source**: Click "Open Source" for full document
6. **Close Panel**: Press Esc or click close button

### Managing Conversations

1. **View History**: Press Ctrl+K or click history icon
2. **Browse Conversations**: Scroll through list
3. **Select Conversation**: Click to load
4. **Delete Conversation**: Click delete icon (careful - permanent!)
5. **New Conversation**: Press Ctrl+N or click button

### Handling Errors

1. **Error Appears**: Red banner at top with message
2. **Read Message**: Understand what went wrong
3. **Retry**: Click retry button to try again
4. **Or Dismiss**: Click X to dismiss and continue

## Testing Checklist

### Functional Testing

- [ ] Send message successfully
- [ ] Receive streaming response
- [ ] Stop generation mid-stream
- [ ] Click citation to open panel
- [ ] Copy message content
- [ ] Copy code block
- [ ] Create new conversation
- [ ] Switch between conversations
- [ ] Delete conversation
- [ ] Retry failed message
- [ ] Dismiss error

### Keyboard Navigation

- [ ] All shortcuts work correctly
- [ ] Tab through all interactive elements
- [ ] Enter/Space activate buttons
- [ ] Esc closes panels/dialogs
- [ ] Focus visible on all elements
- [ ] Focus order logical

### Visual Testing

- [ ] Messages animate smoothly
- [ ] Citations display correctly
- [ ] Code blocks formatted properly
- [ ] Buttons have hover states
- [ ] Loading states clear
- [ ] Dark mode works
- [ ] Light mode works

### Responsive Testing

- [ ] Mobile layout correct
- [ ] Tablet layout correct
- [ ] Desktop layout correct
- [ ] Touch targets large enough
- [ ] Text readable at all sizes
- [ ] Scroll behavior smooth

### Accessibility Testing

- [ ] Screen reader announces messages
- [ ] All images have alt text
- [ ] Color contrast sufficient
- [ ] Focus indicators visible
- [ ] Keyboard only navigation works
- [ ] ARIA labels present

### Performance Testing

- [ ] No unnecessary re-renders
- [ ] Smooth scrolling
- [ ] Fast message sending
- [ ] Quick conversation switching
- [ ] Animations smooth

### Error Handling Testing

- [ ] Network errors handled
- [ ] Invalid input handled
- [ ] Server errors handled
- [ ] Retry works correctly
- [ ] Error messages clear

## Known Limitations

1. **Message Length**: Limited to 2000 characters per message
2. **Conversation History**: Limited to 50 recent conversations in sidebar
3. **Citation Preview**: Shows excerpt only, not full document
4. **Offline Support**: Requires active internet connection
5. **Browser Support**: Modern browsers only (Chrome, Firefox, Safari, Edge)

## Tips and Tricks

1. **Quick Start**: Use Ctrl+N to quickly start new conversation
2. **Focus Input**: Press Ctrl+/ to jump to input from anywhere
3. **Keyboard Shortcuts**: Learn shortcuts for faster workflow
4. **Scroll Lock**: Scroll up to pause auto-scroll while reading
5. **Citation Browsing**: Keep citation panel open while reading response
6. **Dark Mode**: Enable for reduced eye strain in low light
7. **Mobile Usage**: Swipe to close sidebar on mobile devices

## Troubleshooting

### Messages Not Sending

1. Check internet connection
2. Ensure message not empty
3. Check character limit
4. Try retry button if error appears

### Citations Not Loading

1. Ensure API connection active
2. Check document indexing status
3. Verify search service running

### Slow Performance

1. Clear browser cache
2. Close unnecessary browser tabs
3. Check network speed
4. Restart browser if needed

### Keyboard Shortcuts Not Working

1. Ensure not typing in input field (except Esc)
2. Check for browser extension conflicts
3. Verify correct modifier key (Ctrl vs Cmd)

## Feedback

For issues, bugs, or feature requests, please contact the development team or create an issue in the repository.

## Version History

- **v1.0** - Initial release with core chat features
- **v1.1** - Added keyboard shortcuts and accessibility improvements
- **v1.2** - Performance optimizations and visual polish
- **v1.3** - Enhanced error handling and retry functionality
