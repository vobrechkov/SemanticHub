/**
 * Formatting utility functions
 * Provides formatting for scores, dates, and other data types
 */

/**
 * Format a relevance score (0-1) as a percentage string
 *
 * @param score - The relevance score between 0 and 1
 * @param decimals - Number of decimal places (default: 0)
 * @returns Formatted percentage string (e.g., "95%")
 */
export function formatScore(score: number, decimals: number = 0): string {
  const percentage = score * 100;
  return `${percentage.toFixed(decimals)}%`;
}

/**
 * Get color class based on relevance score
 * Used for visual feedback of citation quality
 *
 * @param score - The relevance score between 0 and 1
 * @returns Color classification: 'high', 'medium', or 'low'
 */
export function getScoreColor(score: number): 'high' | 'medium' | 'low' {
  if (score >= 0.9) return 'high';
  if (score >= 0.7) return 'medium';
  return 'low';
}

/**
 * Get Fluent UI color token for a given score
 *
 * @param score - The relevance score between 0 and 1
 * @returns Fluent UI color token name
 */
export function getScoreColorToken(score: number): string {
  const color = getScoreColor(score);
  switch (color) {
    case 'high':
      return 'var(--colorPaletteGreenForeground1)';
    case 'medium':
      return 'var(--colorPaletteDarkOrangeForeground1)';
    case 'low':
      return 'var(--colorPaletteRedForeground1)';
  }
}

/**
 * Format a date to a human-readable string with time
 * Note: For simple date formatting without time, use formatDate from './date'
 *
 * @param date - Date string or Date object
 * @returns Formatted date string with time (e.g., "Jan 15, 2025, 10:30 AM")
 */
export function formatDateTime(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(d);
}

/**
 * Truncate text to a maximum length with ellipsis
 *
 * @param text - The text to truncate
 * @param maxLength - Maximum length before truncation
 * @returns Truncated text with ellipsis if needed
 */
export function truncateText(text: string, maxLength: number): string {
  if (text.length <= maxLength) return text;
  return text.substring(0, maxLength - 3) + '...';
}

/**
 * Extract filename from a file path
 *
 * @param filePath - Full file path
 * @returns Just the filename
 */
export function getFileName(filePath: string): string {
  const parts = filePath.split('/');
  return parts[parts.length - 1] || filePath;
}

/**
 * Format file size in bytes to human-readable string
 *
 * @param bytes - File size in bytes
 * @returns Formatted file size (e.g., "1.5 MB")
 */
export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 Bytes';

  const k = 1024;
  const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));

  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`;
}
