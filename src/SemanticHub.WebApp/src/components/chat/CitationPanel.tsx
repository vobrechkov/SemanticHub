'use client';

/**
 * CitationPanel Component
 * Displays detailed information about a selected citation in a slide-in panel
 *
 * Features:
 * - Slide-in animation from right
 * - Citation metadata display
 * - Copy to clipboard functionality
 * - Open source URL in new tab
 * - Keyboard navigation support (ESC to close)
 * - Click backdrop to close
 * - Responsive design (full-screen on mobile)
 */

import React, { useEffect, useCallback, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism';
import remarkGfm from 'remark-gfm';
import {
  Button,
  Text,
  Badge,
  Link,
  Tooltip,
} from '@fluentui/react-components';
import {
  Dismiss24Regular,
  Copy24Regular,
  Open24Regular,
  Checkmark24Regular,
  Document24Regular,
} from '@fluentui/react-icons';
import { Citation } from '@/api/models';
import { copyToClipboard } from '@/utils/clipboard';
import { formatScore, getScoreColor } from '@/utils/format';
import styles from './CitationPanel.module.css';

/**
 * Props for CitationPanel component
 */
export interface CitationPanelProps {
  /** The citation to display (null to show empty state) */
  citation: Citation | null;
  /** Callback when the panel is closed */
  onClose: () => void;
  /** Whether the panel is open */
  isOpen: boolean;
}

/**
 * CitationPanel - Slide-in panel for displaying citation details
 */
export const CitationPanel: React.FC<CitationPanelProps> = ({
  citation,
  onClose,
  isOpen,
}) => {
  const [copied, setCopied] = useState(false);

  // Handle ESC key to close panel
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleEscape);
      // Prevent body scroll when panel is open
      document.body.style.overflow = 'hidden';
    }

    return () => {
      document.removeEventListener('keydown', handleEscape);
      document.body.style.overflow = '';
    };
  }, [isOpen, onClose]);

  // Focus trap when panel opens
  useEffect(() => {
    if (isOpen) {
      const panel = document.getElementById('citation-panel');
      if (panel) {
        const focusableElements = panel.querySelectorAll(
          'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        );
        const firstElement = focusableElements[0] as HTMLElement;
        const lastElement = focusableElements[focusableElements.length - 1] as HTMLElement;

        const handleTab = (e: KeyboardEvent) => {
          if (e.key !== 'Tab') return;

          if (e.shiftKey) {
            if (document.activeElement === firstElement) {
              e.preventDefault();
              lastElement?.focus();
            }
          } else {
            if (document.activeElement === lastElement) {
              e.preventDefault();
              firstElement?.focus();
            }
          }
        };

        panel.addEventListener('keydown', handleTab as any);
        firstElement?.focus();

        return () => {
          panel.removeEventListener('keydown', handleTab as any);
        };
      }
    }
  }, [isOpen]);

  // Handle copy to clipboard
  const handleCopy = useCallback(async () => {
    if (!citation?.content) return;

    const success = await copyToClipboard(citation.content);
    if (success) {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  }, [citation?.content]);

  // Handle open source URL
  const handleOpenSource = useCallback(() => {
    if (citation?.url) {
      window.open(citation.url, '_blank', 'noopener,noreferrer');
    }
  }, [citation?.url]);

  // Handle backdrop click
  const handleBackdropClick = useCallback(() => {
    onClose();
  }, [onClose]);

  // Prevent panel click from closing
  const handlePanelClick = useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
  }, []);

  // Don't render if not open
  if (!isOpen) return null;

  // Render empty state
  if (!citation) {
    return (
      <div className={styles.overlay} onClick={handleBackdropClick}>
        <div
          id="citation-panel"
          className={styles.panel}
          onClick={handlePanelClick}
          role="dialog"
          aria-label="Citation details panel"
          aria-modal="true"
        >
          <div className={styles.header}>
            <div className={styles.headerTitle}>
              <Document24Regular />
              <Text weight="semibold">Source Document</Text>
            </div>
            <Tooltip content="Close panel" relationship="label">
              <Button
                appearance="subtle"
                icon={<Dismiss24Regular />}
                onClick={onClose}
                aria-label="Close citation panel"
              />
            </Tooltip>
          </div>

          <div className={styles.emptyState}>
            <Document24Regular className={styles.emptyIcon} />
            <Text size={400} weight="semibold">No citation selected</Text>
            <Text size={300} className={styles.emptyDescription}>
              Click on a citation reference in the message to view its details
            </Text>
          </div>
        </div>
      </div>
    );
  }

  // Get score color class
  const scoreColor = citation.score ? getScoreColor(citation.score) : null;

  return (
    <div className={styles.overlay} onClick={handleBackdropClick}>
      <div
        id="citation-panel"
        className={styles.panel}
        onClick={handlePanelClick}
        role="dialog"
        aria-label="Citation details panel"
        aria-modal="true"
      >
        {/* Header */}
        <div className={styles.header}>
          <div className={styles.headerTitle}>
            <Document24Regular />
            <Text weight="semibold">
              {citation.title || 'Source Document'}
            </Text>
          </div>
          <div className={styles.headerActions}>
            {citation.score !== undefined && (
              <Badge
                appearance="outline"
                color={
                  scoreColor === 'high' ? 'success' :
                  scoreColor === 'medium' ? 'warning' :
                  'danger'
                }
                className={styles.scoreBadge}
              >
                {formatScore(citation.score)} match
              </Badge>
            )}
            <Tooltip content="Close panel" relationship="label">
              <Button
                appearance="subtle"
                icon={<Dismiss24Regular />}
                onClick={onClose}
                aria-label="Close citation panel"
              />
            </Tooltip>
          </div>
        </div>

        {/* Content */}
        <div className={styles.content}>
          {/* Metadata Section */}
          <div className={styles.metadataSection}>
            <Text size={300} weight="semibold" className={styles.sectionTitle}>
              Document Information
            </Text>

            <div className={styles.metadataGrid}>
              {citation.title && (
                <div className={styles.metadataItem}>
                  <Text size={200} className={styles.metadataLabel}>Title</Text>
                  <Text size={300} className={styles.metadataValue}>{citation.title}</Text>
                </div>
              )}

              {citation.filePath && (
                <div className={styles.metadataItem}>
                  <Text size={200} className={styles.metadataLabel}>File Path</Text>
                  <Text size={300} className={styles.metadataValue}>{citation.filePath}</Text>
                </div>
              )}

              {citation.url && (
                <div className={styles.metadataItem}>
                  <Text size={200} className={styles.metadataLabel}>URL</Text>
                  <Link
                    href={citation.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className={styles.metadataLink}
                  >
                    {citation.url}
                  </Link>
                </div>
              )}

              {citation.chunkId && (
                <div className={styles.metadataItem}>
                  <Text size={200} className={styles.metadataLabel}>Chunk ID</Text>
                  <Text size={300} className={styles.metadataValue}>{citation.chunkId}</Text>
                </div>
              )}

              {citation.partIndex !== undefined && (
                <div className={styles.metadataItem}>
                  <Text size={200} className={styles.metadataLabel}>Part Index</Text>
                  <Text size={300} className={styles.metadataValue}>{citation.partIndex}</Text>
                </div>
              )}

              {citation.score !== undefined && (
                <div className={styles.metadataItem}>
                  <Text size={200} className={styles.metadataLabel}>Relevance Score</Text>
                  <Text
                    size={300}
                    className={styles.metadataValue}
                    style={{
                      color: scoreColor === 'high' ? 'var(--colorPaletteGreenForeground1)' :
                             scoreColor === 'medium' ? 'var(--colorPaletteDarkOrangeForeground1)' :
                             'var(--colorPaletteRedForeground1)'
                    }}
                  >
                    {formatScore(citation.score)} ({scoreColor} relevance)
                  </Text>
                </div>
              )}
            </div>
          </div>

          {/* Content Section */}
          <div className={styles.contentSection}>
            <div className={styles.contentHeader}>
              <Text size={300} weight="semibold" className={styles.sectionTitle}>
                Content Excerpt
              </Text>
              <Tooltip content={copied ? 'Copied!' : 'Copy content'} relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={copied ? <Checkmark24Regular /> : <Copy24Regular />}
                  onClick={handleCopy}
                  aria-label={copied ? 'Content copied' : 'Copy content'}
                >
                  {copied ? 'Copied' : 'Copy'}
                </Button>
              </Tooltip>
            </div>

            <div className={styles.contentText}>
              <ReactMarkdown
                remarkPlugins={[remarkGfm]}
                components={{
                  code({ node, inline, className, children, ...props }: any) {
                    const match = /language-(\w+)/.exec(className || '');
                    const language = match ? match[1] : 'text';
                    const code = String(children).replace(/\n$/, '');

                    if (!inline && match) {
                      return (
                        <SyntaxHighlighter
                          style={vscDarkPlus}
                          language={language}
                          PreTag="div"
                          {...props}
                        >
                          {code}
                        </SyntaxHighlighter>
                      );
                    }

                    return (
                      <code className={className} {...props}>
                        {children}
                      </code>
                    );
                  },
                }}
              >
                {citation.content}
              </ReactMarkdown>
            </div>
          </div>
        </div>

        {/* Actions */}
        <div className={styles.actions}>
          <Button
            appearance="secondary"
            icon={<Copy24Regular />}
            onClick={handleCopy}
            disabled={!citation.content}
          >
            Copy Content
          </Button>
          {citation.url && (
            <Button
              appearance="primary"
              icon={<Open24Regular />}
              onClick={handleOpenSource}
            >
              Open Source
            </Button>
          )}
          <Button
            appearance="subtle"
            onClick={onClose}
          >
            Close
          </Button>
        </div>
      </div>
    </div>
  );
};

export default CitationPanel;
