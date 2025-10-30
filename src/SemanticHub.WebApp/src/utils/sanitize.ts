import DOMPurify from 'dompurify';

/**
 * Allowed HTML tags for sanitization
 * Based on common markdown elements and safe HTML tags
 */
export const XSSAllowTags = [
  'a',
  'abbr',
  'article',
  'aside',
  'audio',
  'b',
  'blockquote',
  'br',
  'caption',
  'code',
  'col',
  'colgroup',
  'dd',
  'del',
  'details',
  'div',
  'dl',
  'dt',
  'em',
  'figcaption',
  'figure',
  'footer',
  'h1',
  'h2',
  'h3',
  'h4',
  'h5',
  'h6',
  'header',
  'hr',
  'i',
  'img',
  'ins',
  'kbd',
  'li',
  'main',
  'mark',
  'nav',
  'ol',
  'p',
  'pre',
  'q',
  'section',
  'small',
  'span',
  'strong',
  'sub',
  'summary',
  'sup',
  'table',
  'tbody',
  'td',
  'tfoot',
  'th',
  'thead',
  'time',
  'tr',
  'u',
  'ul',
  'video',
];

/**
 * Allowed HTML attributes for sanitization
 */
export const XSSAllowAttributes = [
  'href',
  'src',
  'alt',
  'title',
  'class',
  'id',
  'target',
  'rel',
  'type',
  'align',
  'colspan',
  'rowspan',
  'start',
  'cite',
  'datetime',
];

/**
 * DOMPurify configuration for sanitizing HTML
 */
const sanitizeConfig = {
  ALLOWED_TAGS: XSSAllowTags,
  ALLOWED_ATTR: XSSAllowAttributes,
  ALLOW_DATA_ATTR: false,
  FORBID_TAGS: ['script', 'style', 'iframe', 'form', 'input', 'button'],
  FORBID_ATTR: ['onerror', 'onload', 'onclick', 'onmouseover'],
  RETURN_DOM: false,
  RETURN_DOM_FRAGMENT: false,
};

/**
 * Sanitizes HTML content using DOMPurify
 * @param dirty - The HTML string to sanitize
 * @param config - Optional custom DOMPurify configuration
 * @returns Sanitized HTML string
 */
export function sanitizeHtml(
  dirty: string,
  config: typeof sanitizeConfig = sanitizeConfig
): string {
  if (typeof window === 'undefined') {
    // Server-side: return original content (will be sanitized client-side)
    return dirty;
  }

  return DOMPurify.sanitize(dirty, config) as string;
}

/**
 * Sanitizes markdown content that may contain inline HTML
 * Uses stricter rules suitable for markdown with occasional HTML
 * @param markdown - The markdown string that may contain HTML
 * @returns Sanitized markdown string
 */
export function sanitizeMarkdown(markdown: string): string {
  if (typeof window === 'undefined') {
    return markdown;
  }

  // More permissive config for markdown content
  const markdownConfig = {
    ...sanitizeConfig,
    ALLOWED_TAGS: XSSAllowTags,
    KEEP_CONTENT: true, // Preserve content of forbidden tags
  };

  return DOMPurify.sanitize(markdown, markdownConfig) as string;
}

/**
 * Get a configured DOMPurify instance
 * Useful for advanced use cases requiring direct access
 */
export function getDOMPurify(): typeof DOMPurify {
  return DOMPurify;
}

const sanitizeUtils = {
  sanitizeHtml,
  sanitizeMarkdown,
  getDOMPurify,
  XSSAllowTags,
  XSSAllowAttributes,
};

export default sanitizeUtils;
