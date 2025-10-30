/**
 * Citation reference extracted from markdown content
 */
export interface CitationReference {
  /** The citation number (1-based display index) */
  number: number;
  /** The original citation ID from the markdown (e.g., "doc0", "doc1") */
  originalId: string;
  /** Position in the text where citation appears */
  position: number;
}

/**
 * Result of parsing citations from markdown
 */
export interface ParsedCitations {
  /** The markdown text with citation references replaced */
  text: string;
  /** Array of citation references found */
  references: CitationReference[];
}

/**
 * Parses citation references from markdown content
 * Extracts patterns like [doc0], [doc1], etc. and replaces them with superscript numbers
 *
 * @param markdown - The markdown content containing citation references
 * @returns Object containing processed text and citation references
 *
 * @example
 * const result = parseCitations("Some text [doc0] with citation [doc1]");
 * // result.text = "Some text ^1^ with citation ^2^"
 * // result.references = [{number: 1, originalId: "doc0", position: 10}, ...]
 */
export function parseCitations(markdown: string): ParsedCitations {
  const citationPattern = /\[(doc\d+)\]/g;
  const references: CitationReference[] = [];
  const seenIds = new Set<string>();
  let citationNumber = 0;
  let processedText = markdown;

  // Find all citation matches
  const matches = Array.from(markdown.matchAll(citationPattern));

  // Process matches in reverse order to maintain positions
  for (let i = matches.length - 1; i >= 0; i--) {
    const match = matches[i];
    const fullMatch = match[0]; // e.g., "[doc1]"
    const docId = match[1]; // e.g., "doc1"
    const position = match.index || 0;

    // Check if we've seen this citation ID before
    let number: number;
    const existingRef = references.find(ref => ref.originalId === docId);

    if (existingRef) {
      // Reuse existing citation number
      number = existingRef.number;
    } else {
      // Assign new citation number
      number = ++citationNumber;
      seenIds.add(docId);
    }

    // Replace citation with superscript number
    const replacement = ` ^${number}^ `;
    processedText =
      processedText.slice(0, position) +
      replacement +
      processedText.slice(position + fullMatch.length);

    // Add reference (only if it's new)
    if (!existingRef) {
      references.unshift({
        number,
        originalId: docId,
        position,
      });
    }
  }

  return {
    text: processedText,
    references: references.sort((a, b) => a.number - b.number),
  };
}

/**
 * Extracts the document index from a citation ID
 * @param citationId - Citation ID like "doc0", "doc1", etc.
 * @returns The numeric index, or null if invalid format
 *
 * @example
 * extractDocIndex("doc0") // returns 0
 * extractDocIndex("doc23") // returns 23
 */
export function extractDocIndex(citationId: string): number | null {
  const match = citationId.match(/^doc(\d+)$/);
  return match ? parseInt(match[1], 10) : null;
}

/**
 * Creates a citation superscript element string for markdown
 * @param citationNumber - The citation number to display
 * @returns Markdown superscript string
 */
export function createCitationMarker(citationNumber: number): string {
  return `^${citationNumber}^`;
}

/**
 * Checks if a string contains citation references
 * @param text - Text to check
 * @returns True if citations are found
 */
export function hasCitations(text: string): boolean {
  return /\[(doc\d+)\]/.test(text);
}

/**
 * Removes all citation markers from text
 * @param text - Text containing citation markers
 * @returns Clean text without citations
 */
export function removeCitations(text: string): string {
  return text.replace(/\[(doc\d+)\]/g, '').trim();
}

/**
 * Normalizes markdown content for better rendering
 * - Ensures proper spacing around elements
 * - Cleans up excessive whitespace
 *
 * @param markdown - Raw markdown content
 * @returns Normalized markdown
 */
export function normalizeMarkdown(markdown: string): string {
  return markdown
    // Normalize line endings
    .replace(/\r\n/g, '\n')
    // Remove excessive blank lines (more than 2)
    .replace(/\n{3,}/g, '\n\n')
    // Ensure space after headings
    .replace(/^(#{1,6})([^\s#])/gm, '$1 $2')
    // Trim trailing whitespace
    .split('\n')
    .map(line => line.trimEnd())
    .join('\n')
    .trim();
}

const markdownUtils = {
  parseCitations,
  extractDocIndex,
  createCitationMarker,
  hasCitations,
  removeCitations,
  normalizeMarkdown,
};

export default markdownUtils;
