/**
 * Clipboard utility functions
 * Provides cross-browser clipboard operations with fallback support
 */

/**
 * Copy text to clipboard
 * Uses modern Clipboard API with fallback for older browsers
 *
 * @param text - The text to copy to clipboard
 * @returns Promise resolving to true if successful, false otherwise
 */
export async function copyToClipboard(text: string): Promise<boolean> {
  try {
    // Modern Clipboard API (preferred)
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return true;
    }

    // Fallback for older browsers or non-secure contexts
    return fallbackCopyToClipboard(text);
  } catch (error) {
    console.error('Failed to copy to clipboard:', error);
    return false;
  }
}

/**
 * Fallback clipboard copy using deprecated execCommand
 * Used when Clipboard API is not available
 *
 * @param text - The text to copy
 * @returns true if successful, false otherwise
 */
function fallbackCopyToClipboard(text: string): boolean {
  try {
    const textArea = document.createElement('textarea');
    textArea.value = text;

    // Make the textarea invisible and unclickable
    textArea.style.position = 'fixed';
    textArea.style.left = '-999999px';
    textArea.style.top = '-999999px';
    textArea.setAttribute('readonly', '');

    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();

    const successful = document.execCommand('copy');
    document.body.removeChild(textArea);

    return successful;
  } catch (error) {
    console.error('Fallback copy failed:', error);
    return false;
  }
}

/**
 * Check if clipboard API is available
 *
 * @returns true if clipboard operations are supported
 */
export function isClipboardSupported(): boolean {
  return (
    (navigator.clipboard && window.isSecureContext) ||
    document.queryCommandSupported?.('copy') === true
  );
}
