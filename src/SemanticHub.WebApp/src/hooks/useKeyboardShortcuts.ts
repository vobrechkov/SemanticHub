/**
 * useKeyboardShortcuts - Hook for managing global keyboard shortcuts
 *
 * Supported shortcuts:
 * - Ctrl/Cmd + N: New conversation
 * - Ctrl/Cmd + K: Toggle history sidebar
 * - Ctrl/Cmd + /: Focus input
 * - Ctrl/Cmd + ?: Show keyboard shortcuts help
 * - Esc: Close sidebar/panel if open
 */

import { useEffect, useCallback } from 'react';

export interface KeyboardShortcut {
  key: string;
  ctrl?: boolean;
  shift?: boolean;
  alt?: boolean;
  meta?: boolean;
  action: () => void;
  description: string;
}

export interface UseKeyboardShortcutsOptions {
  shortcuts: KeyboardShortcut[];
  enabled?: boolean;
}

/**
 * Hook for managing global keyboard shortcuts
 */
export function useKeyboardShortcuts({ shortcuts, enabled = true }: UseKeyboardShortcutsOptions) {
  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      if (!enabled) return;

      // Don't trigger shortcuts when typing in input/textarea
      const target = event.target as HTMLElement;
      if (
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        target.isContentEditable
      ) {
        // Allow Esc to work even in inputs
        if (event.key !== 'Escape') {
          return;
        }
      }

      // Check if any shortcut matches
      for (const shortcut of shortcuts) {
        const ctrlKey = event.ctrlKey || event.metaKey; // Support both Ctrl and Cmd
        const matches =
          event.key.toLowerCase() === shortcut.key.toLowerCase() &&
          (!shortcut.ctrl || ctrlKey) &&
          (!shortcut.shift || event.shiftKey) &&
          (!shortcut.alt || event.altKey);

        if (matches) {
          event.preventDefault();
          event.stopPropagation();
          shortcut.action();
          break;
        }
      }
    },
    [shortcuts, enabled]
  );

  useEffect(() => {
    if (!enabled) return;

    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [handleKeyDown, enabled]);
}

/**
 * Format shortcut for display
 */
export function formatShortcut(shortcut: KeyboardShortcut): string {
  const parts: string[] = [];
  const isMac = typeof navigator !== 'undefined' && navigator.platform.toUpperCase().indexOf('MAC') >= 0;

  if (shortcut.ctrl) {
    parts.push(isMac ? '⌘' : 'Ctrl');
  }
  if (shortcut.shift) {
    parts.push('Shift');
  }
  if (shortcut.alt) {
    parts.push(isMac ? '⌥' : 'Alt');
  }

  // Capitalize key for display
  const key = shortcut.key === ' ' ? 'Space' : shortcut.key.toUpperCase();
  parts.push(key);

  return parts.join(' + ');
}
