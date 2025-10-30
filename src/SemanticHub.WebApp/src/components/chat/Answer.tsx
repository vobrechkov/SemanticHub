'use client';

import React, { useState, useCallback, useMemo } from 'react';
import ReactMarkdown from 'react-markdown';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';
import {
  Button,
  Text,
  Tooltip,
} from '@fluentui/react-components';
import {
  Copy24Regular,
  Checkmark24Regular,
  ChevronRight16Regular,
} from '@fluentui/react-icons';
import { parseCitations } from '@/utils/markdown';
import { sanitizeMarkdown } from '@/utils/sanitize';
import styles from './Answer.module.css';

/**
 * Citation information for a source reference
 */
export interface Citation {
  /** Optional part index for multi-part documents */
  partIndex?: number;
  /** The content/excerpt from the source */
  content: string;
  /** Unique identifier for the citation */
  id: string;
  /** Title of the source document */
  title?: string;
  /** File path of the source */
  filePath?: string;
  /** URL of the source (if web-based) */
  url?: string;
  /** Chunk ID within the document */
  chunkId?: string;
  /** Relevance score (0-1) */
  score?: number;
}

/**
 * Props for the Answer component
 */
export interface AnswerProps {
  /** The message content in markdown format */
  answer: string;
  /** Optional array of source citations */
  citations?: Citation[];
  /** Unique identifier for this message */
  messageId: string;
  /** Callback when a citation is clicked */
  onCitationClick?: (citation: Citation) => void;
  /** Whether the message is still being streamed */
  isStreaming?: boolean;
  /** Optional CSS class name */
  className?: string;
}

/**
 * Answer component for rendering chat messages with markdown, syntax highlighting, and citations
 *
 * Features:
 * - Markdown rendering with GitHub Flavored Markdown support
 * - Syntax highlighting for code blocks with copy functionality
 * - Citation references with clickable markers
 * - Copy-to-clipboard for entire answer
 * - Streaming indicator
 * - Responsive design with dark mode support
 * - Memoized for performance optimization
 */
const AnswerComponent: React.FC<AnswerProps> = ({
  answer,
  citations = [],
  messageId,
  onCitationClick,
  isStreaming = false,
  className,
}) => {
  const [copiedCode, setCopiedCode] = useState<string | null>(null);
  const [copiedAnswer, setCopiedAnswer] = useState(false);
  const [citationsExpanded, setCitationsExpanded] = useState(false);

  // Parse citations from markdown content
  const parsedContent = useMemo(() => {
    const sanitized = sanitizeMarkdown(answer);
    const parsed = parseCitations(sanitized);
    return parsed;
  }, [answer]);

  // Map citation references to actual citation objects
  const mappedCitations = useMemo(() => {
    if (!citations || citations.length === 0) {
      return [];
    }

    return parsedContent.references
      .map(ref => {
        // Extract document index from originalId (e.g., "doc0" -> 0, "doc1" -> 1)
        const docIndexMatch = ref.originalId.match(/^doc(\d+)$/);
        if (!docIndexMatch) return null;

        const docIndex = parseInt(docIndexMatch[1], 10);
        const citation = citations[docIndex];

        if (!citation) return null;

        return {
          ...citation,
          displayNumber: ref.number,
        };
      })
      .filter((c): c is Citation & { displayNumber: number } => c !== null);
  }, [citations, parsedContent.references]);

  // Copy answer to clipboard
  const handleCopyAnswer = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(answer);
      setCopiedAnswer(true);
      setTimeout(() => setCopiedAnswer(false), 2000);
    } catch (err) {
      console.error('Failed to copy answer:', err);
    }
  }, [answer]);

  // Copy code block to clipboard
  const handleCopyCode = useCallback(async (code: string, language: string) => {
    try {
      await navigator.clipboard.writeText(code);
      setCopiedCode(`${language}-${code.substring(0, 20)}`);
      setTimeout(() => setCopiedCode(null), 2000);
    } catch (err) {
      console.error('Failed to copy code:', err);
    }
  }, []);

  // Handle citation click
  const handleCitationClick = useCallback(
    (citation: Citation) => {
      if (onCitationClick) {
        onCitationClick(citation);
      }
    },
    [onCitationClick]
  );

  // Format citation display text
  const formatCitationText = useCallback((citation: Citation): string => {
    if (citation.title) {
      return citation.title;
    }
    if (citation.filePath) {
      const parts = citation.filePath.split('/');
      return parts[parts.length - 1] || citation.filePath;
    }
    if (citation.url) {
      return citation.url;
    }
    return `Citation ${citation.id}`;
  }, []);

  // Format citation path
  const formatCitationPath = useCallback((citation: Citation): string => {
    const parts: string[] = [];

    if (citation.filePath) {
      parts.push(citation.filePath);
    } else if (citation.url) {
      parts.push(citation.url);
    }

    if (citation.chunkId) {
      parts.push(`Part ${citation.chunkId}`);
    } else if (citation.partIndex) {
      parts.push(`Part ${citation.partIndex}`);
    }

    return parts.join(' - ');
  }, []);

  // Custom markdown components
  const markdownComponents = useMemo(
    () => ({
      code({ node, inline, className, children, ...props }: any) {
        const match = /language-(\w+)/.exec(className || '');
        const language = match ? match[1] : 'text';
        const code = String(children).replace(/\n$/, '');
        const codeId = `${language}-${code.substring(0, 20)}`;
        const isCopied = copiedCode === codeId;

        if (!inline && match) {
          return (
            <div className={styles.codeBlockContainer}>
              <div className={styles.codeBlockHeader}>
                <span className={styles.codeBlockLanguage}>{language}</span>
                <Tooltip content={isCopied ? 'Copied!' : 'Copy code'} relationship="label">
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={isCopied ? <Checkmark24Regular /> : <Copy24Regular />}
                    onClick={() => handleCopyCode(code, language)}
                    className={styles.codeBlockCopyButton}
                    aria-label={isCopied ? 'Code copied' : 'Copy code'}
                  />
                </Tooltip>
              </div>
              <SyntaxHighlighter
                style={vscDarkPlus}
                language={language}
                PreTag="div"
                className={styles.codeBlockContent}
                {...props}
              >
                {code}
              </SyntaxHighlighter>
            </div>
          );
        }

        return (
          <code className={className} {...props}>
            {children}
          </code>
        );
      },
      // Handle superscript citation markers
      sup({ children, ...props }: any) {
        const citationNumber = parseInt(String(children), 10);
        if (!isNaN(citationNumber) && mappedCitations.length > 0) {
          const citation = mappedCitations.find(c => c.displayNumber === citationNumber);
          if (citation && onCitationClick) {
            return (
              <Tooltip content={formatCitationText(citation)} relationship="label">
                <sup
                  {...props}
                  onClick={() => handleCitationClick(citation)}
                  role="button"
                  tabIndex={0}
                  onKeyDown={e => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      handleCitationClick(citation);
                    }
                  }}
                  aria-label={`Citation ${citationNumber}: ${formatCitationText(citation)}`}
                >
                  {children}
                </sup>
              </Tooltip>
            );
          }
        }
        return <sup {...props}>{children}</sup>;
      },
    }),
    [
      copiedCode,
      handleCopyCode,
      mappedCitations,
      onCitationClick,
      handleCitationClick,
      formatCitationText,
    ]
  );

  return (
    <div
      className={`${styles.answerContainer} ${className || ''}`}
      id={messageId}
      role="article"
      aria-label="Chat answer"
    >
      <div className={styles.answerContent}>
        <div className={styles.answerText}>
          <ReactMarkdown
            remarkPlugins={[remarkGfm]}
            rehypePlugins={[rehypeRaw, rehypeSanitize]}
            components={markdownComponents}
          >
            {parsedContent.text}
          </ReactMarkdown>
          {isStreaming && (
            <span className={styles.streamingIndicator} aria-label="Message is streaming" />
          )}
        </div>

        <div className={styles.answerActions}>
          <Tooltip content={copiedAnswer ? 'Copied!' : 'Copy answer'} relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={copiedAnswer ? <Checkmark24Regular /> : <Copy24Regular />}
              onClick={handleCopyAnswer}
              className={styles.copyButton}
              aria-label={copiedAnswer ? 'Answer copied' : 'Copy answer'}
            />
          </Tooltip>
        </div>
      </div>

      {(mappedCitations.length > 0 || !isStreaming) && (
        <div className={styles.answerFooter}>
          {mappedCitations.length > 0 && (
            <div className={styles.citationsSection}>
              <div
                className={styles.citationsToggle}
                onClick={() => setCitationsExpanded(!citationsExpanded)}
                onKeyDown={e => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    setCitationsExpanded(!citationsExpanded);
                  }
                }}
                role="button"
                tabIndex={0}
                aria-expanded={citationsExpanded}
                aria-controls={`citations-${messageId}`}
                aria-label={`${citationsExpanded ? 'Hide' : 'Show'} ${mappedCitations.length} citation${mappedCitations.length === 1 ? '' : 's'}`}
              >
                <ChevronRight16Regular
                  className={`${styles.citationsToggleIcon} ${citationsExpanded ? styles.expanded : ''}`}
                />
                <Text className={styles.citationsText}>
                  {mappedCitations.length} {mappedCitations.length === 1 ? 'reference' : 'references'}
                </Text>
              </div>

              {citationsExpanded && (
                <div
                  className={styles.citationsList}
                  id={`citations-${messageId}`}
                  role="list"
                >
                  {mappedCitations.map((citation, index) => (
                    <div
                      key={`${citation.id}-${index}`}
                      className={styles.citationItem}
                      onClick={() => handleCitationClick(citation)}
                      onKeyDown={e => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          handleCitationClick(citation);
                        }
                      }}
                      role="listitem button"
                      tabIndex={0}
                      aria-label={`Citation ${citation.displayNumber}: ${formatCitationText(citation)}`}
                    >
                      <span className={styles.citationNumber}>
                        {citation.displayNumber}
                      </span>
                      <div className={styles.citationMeta}>
                        <span className={styles.citationTitle}>
                          {formatCitationText(citation)}
                        </span>
                        {formatCitationPath(citation) && (
                          <span className={styles.citationPath}>
                            {formatCitationPath(citation)}
                          </span>
                        )}
                      </div>
                      {citation.score !== undefined && (
                        <span className={styles.citationScore}>
                          {(citation.score * 100).toFixed(0)}%
                        </span>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          <div className={styles.disclaimer}>
            <Text size={200}>AI-generated content may be incorrect</Text>
          </div>
        </div>
      )}
    </div>
  );
};

// Memoize component for performance
export const Answer = React.memo(AnswerComponent, (prevProps, nextProps) => {
  // Custom comparison function to prevent unnecessary re-renders
  return (
    prevProps.answer === nextProps.answer &&
    prevProps.messageId === nextProps.messageId &&
    prevProps.isStreaming === nextProps.isStreaming &&
    prevProps.citations === nextProps.citations &&
    prevProps.onCitationClick === nextProps.onCitationClick &&
    prevProps.className === nextProps.className
  );
});

Answer.displayName = 'Answer';

export default Answer;
