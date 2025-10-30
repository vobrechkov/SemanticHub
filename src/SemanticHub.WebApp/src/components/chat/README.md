# QuestionInput Component

A modern, accessible chat input component built with Fluent UI v9 and Next.js 15.

## Features

### Core Functionality
- **Multiline text input** with auto-resize
- **Send button** with loading states
- **Character counter** (appears when approaching limit)
- **Keyboard shortcuts** for optimal UX
- **Input validation** (whitespace trimming, empty message prevention)
- **Accessibility** compliant (ARIA labels, screen reader support)

### User Experience
- Auto-focus on mount (optional behavior)
- Smooth textarea auto-resize (60px - 200px)
- Visual feedback when sending
- Prevent double-send (button disabled immediately on click)
- Support for IME composition (Japanese, Chinese, etc.)

### Styling
- Responsive layout (mobile-optimized)
- Dark mode support
- High contrast mode support
- Custom bottom border accent
- Fluent UI v9 design system

## Props

```typescript
interface QuestionInputProps {
  onSend: (message: string) => void;  // Callback when message is sent
  disabled?: boolean;                  // Disable input (e.g., while loading)
  placeholder?: string;                // Input placeholder text
  maxLength?: number;                  // Max message length (default: 2000)
}
```

## Usage

### Basic Example

```tsx
import { QuestionInput } from '@/components/chat';

export default function ChatPage() {
  const handleSend = (message: string) => {
    console.log('User sent:', message);
    // Send to API, update state, etc.
  };

  return (
    <QuestionInput
      onSend={handleSend}
      placeholder="Ask me anything..."
    />
  );
}
```

### With Loading State

```tsx
import { useState } from 'react';
import { QuestionInput } from '@/components/chat';

export default function ChatPage() {
  const [isLoading, setIsLoading] = useState(false);

  const handleSend = async (message: string) => {
    setIsLoading(true);
    try {
      await fetch('/api/chat', {
        method: 'POST',
        body: JSON.stringify({ message }),
      });
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <QuestionInput
      onSend={handleSend}
      disabled={isLoading}
      placeholder="Type your message..."
    />
  );
}
```

### Custom Max Length

```tsx
import { QuestionInput } from '@/components/chat';

export default function ChatPage() {
  return (
    <QuestionInput
      onSend={(msg) => console.log(msg)}
      maxLength={500}
      placeholder="Keep it brief (max 500 chars)..."
    />
  );
}
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Enter** | Send message |
| **Shift+Enter** | New line (multiline) |
| **Tab** | Focus send button |
| **Shift+Tab** | Focus previous element |

## Accessibility

The component follows WCAG 2.1 AA guidelines:

- **ARIA labels** on textarea and send button
- **Screen reader support** with status announcements for character counter
- **Keyboard navigation** fully supported
- **Focus indicators** clearly visible
- **High contrast mode** compatible
- **Color contrast** meets AA standards

## Design Decisions

### Why Fluent UI v9?
- Modern, actively maintained design system
- Built-in accessibility features
- Excellent TypeScript support
- Performance optimized for React 19

### Why Auto-Resize Textarea?
- Better UX than fixed-height input
- Supports multiline messages
- Visual feedback for message length

### Why Character Counter?
- Appears only when approaching limit (80%+)
- Warns users before hitting max length
- Non-intrusive (small, secondary text)

### Why IME Composition Support?
- Prevents premature send on Enter during composition
- Essential for Japanese, Chinese, Korean input
- Improves international UX

## Fluent UI v9 Components Used

- `Textarea` - Multiline input field
- `Button` - Send button with primary appearance
- `Text` - Character counter text
- `Tooltip` - Contextual hints for buttons
- `Spinner` - Loading indicator
- `makeStyles` - CSS-in-JS styling with tokens
- `tokens` - Design system tokens (colors, spacing)

## Browser Support

- Chrome/Edge 90+
- Firefox 88+
- Safari 14+
- Mobile browsers (iOS Safari, Chrome Mobile)

## Performance

- Lightweight component (~5KB gzipped)
- Optimized re-renders with `useCallback` hooks
- CSS Modules for scoped styles
- No runtime CSS-in-JS overhead

## Future Enhancements

Potential features for future iterations:

- [ ] Image upload support
- [ ] Voice input
- [ ] File attachments
- [ ] Mentions/autocomplete
- [ ] Emoji picker
- [ ] Message templates
- [ ] Draft persistence (localStorage)
- [ ] Command shortcuts (e.g., `/help`)

## Related Components

- `ChatMessage` - Message display component
- `ChatContainer` - Chat layout container
- `ChatHistory` - Message history list

## References

- [Fluent UI v9 Documentation](https://react.fluentui.dev/)
- [Next.js Documentation](https://nextjs.org/docs)
- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
