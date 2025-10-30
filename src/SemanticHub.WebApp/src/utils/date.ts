/**
 * Date utility functions for formatting timestamps
 */

/**
 * Formats a date string into a relative time format
 * @param dateString - ISO date string or timestamp
 * @returns Formatted relative time string
 *
 * @example
 * formatRelativeTime('2025-10-30T10:30:00') // "2 hours ago"
 * formatRelativeTime('2025-10-29T10:30:00') // "Yesterday"
 * formatRelativeTime('2025-10-20T10:30:00') // "Oct 20, 2025"
 */
export function formatRelativeTime(dateString: string): string {
  const date = new Date(dateString);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffSeconds = Math.floor(diffMs / 1000);
  const diffMinutes = Math.floor(diffSeconds / 60);
  const diffHours = Math.floor(diffMinutes / 60);
  const diffDays = Math.floor(diffHours / 24);

  // Less than 1 minute
  if (diffSeconds < 60) {
    return 'Just now';
  }

  // Less than 1 hour
  if (diffMinutes < 60) {
    return diffMinutes === 1 ? '1 minute ago' : `${diffMinutes} minutes ago`;
  }

  // Less than 1 day
  if (diffHours < 24) {
    return diffHours === 1 ? '1 hour ago' : `${diffHours} hours ago`;
  }

  // Yesterday
  if (diffDays === 1) {
    return 'Yesterday';
  }

  // Less than 7 days
  if (diffDays < 7) {
    return `${diffDays} days ago`;
  }

  // Older than 7 days - show formatted date
  return formatShortDate(date);
}

/**
 * Formats a date into a short readable format
 * @param date - Date object to format
 * @returns Formatted date string (e.g., "Jan 15, 2025")
 */
export function formatShortDate(date: Date): string {
  const months = [
    'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
    'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'
  ];

  const month = months[date.getMonth()];
  const day = date.getDate();
  const year = date.getFullYear();

  return `${month} ${day}, ${year}`;
}

/**
 * Formats a date into a full timestamp
 * @param dateString - ISO date string or timestamp
 * @returns Formatted timestamp string (e.g., "Oct 30, 2025 at 10:30 AM")
 */
export function formatFullTimestamp(dateString: string): string {
  const date = new Date(dateString);
  const dateStr = formatShortDate(date);

  const hours = date.getHours();
  const minutes = date.getMinutes();
  const ampm = hours >= 12 ? 'PM' : 'AM';
  const displayHours = hours % 12 || 12;
  const displayMinutes = minutes.toString().padStart(2, '0');

  return `${dateStr} at ${displayHours}:${displayMinutes} ${ampm}`;
}
