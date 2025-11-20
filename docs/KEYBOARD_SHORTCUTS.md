# Keyboard Shortcuts Reference

## Global Shortcuts

These shortcuts work throughout the application:

### Conversation Management

| Shortcut | Description |
|----------|-------------|
| `Ctrl/Cmd + N` | Start a new conversation |
| `Ctrl/Cmd + K` | Toggle conversation history sidebar |

### Navigation

| Shortcut | Description |
|----------|-------------|
| `Ctrl/Cmd + /` | Focus message input field |
| `Esc` | Close open sidebar, panel, or dialog |

### Help

| Shortcut | Description |
|----------|-------------|
| `Ctrl/Cmd + ?` | Show keyboard shortcuts help dialog |

### Messaging

| Shortcut | Description |
|----------|-------------|
| `Enter` | Send message |
| `Shift + Enter` | Insert new line in message |

## Context-Specific Shortcuts

### Citation Panel

| Shortcut | Description |
|----------|-------------|
| `Esc` | Close citation panel |

### Conversation History

| Shortcut | Description |
|----------|-------------|
| `Arrow Up` | Select previous conversation |
| `Arrow Down` | Select next conversation |
| `Enter` | Open selected conversation |
| `Delete/Backspace` | Delete selected conversation (with confirmation) |
| `Esc` | Close conversation history sidebar |

### Dialogs

| Shortcut | Description |
|----------|-------------|
| `Esc` | Close active dialog |
| `Tab` | Navigate through dialog elements |
| `Shift + Tab` | Navigate backwards through dialog elements |

## Platform-Specific Notes

### Windows/Linux
- Use `Ctrl` for all shortcuts
- Example: `Ctrl + N`

### macOS
- Use `Cmd` (⌘) for most shortcuts
- Display shows: `⌘ N`

## Accessibility

All keyboard shortcuts are designed to work with:
- Screen readers
- Keyboard-only navigation
- Voice control software

## Customization

Currently, keyboard shortcuts are not customizable. If you need custom shortcuts, please contact the development team.

## Tips

1. **Learn Gradually**: Start with the most common shortcuts (`Ctrl/Cmd + N`, `Ctrl/Cmd + K`)
2. **View Anytime**: Press `Ctrl/Cmd + ?` to view shortcuts
3. **Muscle Memory**: Practice shortcuts during normal usage
4. **Productivity**: Shortcuts significantly speed up workflow

## Implementation Details

Shortcuts are implemented using the `useKeyboardShortcuts` hook located in `/src/hooks/useKeyboardShortcuts.ts`.

Features:
- Global event listener
- Smart input detection (shortcuts disabled in text fields except Esc)
- Cross-platform support (Ctrl/Cmd handling)
- Easy to extend

## Adding New Shortcuts

Developers can add new shortcuts in the main page component (`/src/app/page.tsx`):

```typescript
const shortcuts: KeyboardShortcut[] = [
  {
    key: 'n',           // Key to press
    ctrl: true,         // Requires Ctrl/Cmd
    action: myAction,   // Function to call
    description: 'Description for help dialog',
  },
  // ... more shortcuts
];
```

## Avoiding Conflicts

The implementation avoids conflicts with:
- Browser shortcuts (F1-F12, Ctrl+T, etc.)
- Operating system shortcuts
- Input field text editing
- Third-party extensions

## Testing

To test keyboard shortcuts:

1. **Manual Testing**: Go through each shortcut in the table
2. **Input Fields**: Verify shortcuts don't interfere with typing
3. **Dialogs**: Test shortcuts work correctly in different contexts
4. **Platform Testing**: Test on Windows, macOS, and Linux

## Troubleshooting

### Shortcut Not Working

1. Check if you're in an input field (most shortcuts disabled)
2. Verify correct modifier key (Ctrl vs Cmd)
3. Check for browser extension conflicts
4. Ensure dialog/panel is not blocking the shortcut

### Shortcut Conflicts

If a shortcut conflicts with browser/OS:
1. The browser/OS shortcut takes precedence
2. Report the issue to the development team
3. Alternative shortcut may be assigned

## Future Enhancements

Planned improvements:
- [ ] Customizable shortcuts
- [ ] Shortcut recording
- [ ] Context-aware shortcuts
- [ ] More granular control
- [ ] Shortcut profiles
