'use client';

/**
 * ChatHistory - Sidebar component for displaying conversation history
 * Features: list conversations, select, delete, create new
 */

import React, { useState, useCallback, useRef, useEffect } from 'react';
import {
  Button,
  Text,
  Dialog,
  DialogTrigger,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Skeleton,
  SkeletonItem,
  Tooltip,
} from '@fluentui/react-components';
import {
  DismissRegular,
  AddRegular,
  DeleteRegular,
} from '@fluentui/react-icons';
import { Conversation } from '@/api/models';
import { formatRelativeTime } from '@/utils/date';
import styles from './ChatHistory.module.css';

// ============================================================================
// Props Interface
// ============================================================================

export interface ChatHistoryProps {
  /** List of all conversations */
  conversations: Conversation[];
  /** Currently selected conversation */
  currentConversation: Conversation | null;
  /** Handler for selecting a conversation */
  onSelectConversation: (conversationId: string) => void;
  /** Handler for deleting a conversation */
  onDeleteConversation: (conversationId: string) => void;
  /** Handler for creating a new conversation */
  onNewConversation: () => void;
  /** Handler for closing the sidebar */
  onClose: () => void;
  /** Loading state indicator */
  isLoading?: boolean;
}

// ============================================================================
// Component
// ============================================================================

export function ChatHistory({
  conversations,
  currentConversation,
  onSelectConversation,
  onDeleteConversation,
  onNewConversation,
  onClose,
  isLoading = false,
}: ChatHistoryProps) {
  const [conversationToDelete, setConversationToDelete] = useState<Conversation | null>(null);
  const [selectedIndex, setSelectedIndex] = useState<number>(-1);
  const listRef = useRef<HTMLDivElement>(null);

  // Update selected index when current conversation changes
  useEffect(() => {
    if (currentConversation) {
      const index = conversations.findIndex((c) => c.id === currentConversation.id);
      setSelectedIndex(index);
    } else {
      setSelectedIndex(-1);
    }
  }, [currentConversation, conversations]);

  // --------------------------------------------------------------------------
  // Handlers
  // --------------------------------------------------------------------------

  const handleSelect = useCallback(
    (conversation: Conversation) => {
      onSelectConversation(conversation.id);
    },
    [onSelectConversation]
  );

  const handleDeleteClick = useCallback((conversation: Conversation, e: React.MouseEvent) => {
    e.stopPropagation();
    setConversationToDelete(conversation);
  }, []);

  const handleDeleteConfirm = useCallback(() => {
    if (conversationToDelete) {
      onDeleteConversation(conversationToDelete.id);
      setConversationToDelete(null);
    }
  }, [conversationToDelete, onDeleteConversation]);

  const handleDeleteCancel = useCallback(() => {
    setConversationToDelete(null);
  }, []);

  // --------------------------------------------------------------------------
  // Keyboard Navigation
  // --------------------------------------------------------------------------

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (conversations.length === 0) return;

      switch (e.key) {
        case 'ArrowDown':
          e.preventDefault();
          setSelectedIndex((prev) => {
            const next = prev < conversations.length - 1 ? prev + 1 : prev;
            if (next !== prev) {
              onSelectConversation(conversations[next].id);
            }
            return next;
          });
          break;

        case 'ArrowUp':
          e.preventDefault();
          setSelectedIndex((prev) => {
            const next = prev > 0 ? prev - 1 : prev;
            if (next !== prev) {
              onSelectConversation(conversations[next].id);
            }
            return next;
          });
          break;

        case 'Delete':
        case 'Backspace':
          e.preventDefault();
          if (selectedIndex >= 0 && selectedIndex < conversations.length) {
            setConversationToDelete(conversations[selectedIndex]);
          }
          break;

        case 'Enter':
          e.preventDefault();
          if (selectedIndex >= 0 && selectedIndex < conversations.length) {
            onSelectConversation(conversations[selectedIndex].id);
          }
          break;
      }
    },
    [conversations, selectedIndex, onSelectConversation]
  );

  // --------------------------------------------------------------------------
  // Render Helpers
  // --------------------------------------------------------------------------

  const renderLoadingSkeleton = () => (
    <div className={styles.skeletonContainer}>
      {[1, 2, 3, 4, 5].map((i) => (
        <div key={i} className={styles.skeletonItem}>
          <Skeleton>
            <SkeletonItem size={16} />
          </Skeleton>
          <Skeleton>
            <SkeletonItem size={12} />
          </Skeleton>
        </div>
      ))}
    </div>
  );

  const renderEmptyState = () => (
    <div className={styles.emptyState}>
      <div className={styles.emptyStateIcon}>
        <AddRegular fontSize={48} />
      </div>
      <Text className={styles.emptyStateTitle}>No conversations yet</Text>
      <Text className={styles.emptyStateSubtitle}>Start a new chat to begin!</Text>
      <Button appearance="primary" onClick={onNewConversation} className={styles.emptyStateButton}>
        New Conversation
      </Button>
    </div>
  );

  const renderConversationItem = (conversation: Conversation, index: number) => {
    const isSelected = currentConversation?.id === conversation.id;
    const title = conversation.title || 'New Conversation';
    const timestamp = formatRelativeTime(conversation.updatedAt);

    return (
      <div
        key={conversation.id}
        className={`${styles.conversationItem} ${isSelected ? styles.selected : ''}`}
        onClick={() => handleSelect(conversation)}
        onKeyDown={handleKeyDown}
        role="button"
        tabIndex={0}
        aria-label={`${title}, ${timestamp}`}
        aria-current={isSelected ? 'true' : 'false'}
      >
        <div className={styles.conversationContent}>
          <Text className={styles.conversationTitle} weight="semibold">
            {title}
          </Text>
          <Text className={styles.conversationTimestamp} size={200}>
            {timestamp}
          </Text>
        </div>

        <Tooltip content="Delete conversation" relationship="label">
          <Button
            appearance="subtle"
            icon={<DeleteRegular />}
            size="small"
            className={styles.deleteButton}
            onClick={(e) => handleDeleteClick(conversation, e)}
            aria-label={`Delete ${title}`}
          />
        </Tooltip>
      </div>
    );
  };

  // --------------------------------------------------------------------------
  // Render
  // --------------------------------------------------------------------------

  return (
    <>
      <div className={styles.container} onKeyDown={handleKeyDown}>
        {/* Header */}
        <div className={styles.header}>
          <Text className={styles.headerTitle} weight="semibold">
            Conversations
          </Text>
          <div className={styles.headerActions}>
            <Tooltip content="New conversation" relationship="label">
              <Button
                appearance="subtle"
                icon={<AddRegular />}
                size="small"
                onClick={onNewConversation}
                aria-label="New conversation"
              />
            </Tooltip>
            <Tooltip content="Close sidebar" relationship="label">
              <Button
                appearance="subtle"
                icon={<DismissRegular />}
                size="small"
                onClick={onClose}
                aria-label="Close sidebar"
              />
            </Tooltip>
          </div>
        </div>

        {/* Content */}
        <div className={styles.content} ref={listRef} role="list">
          {isLoading ? (
            renderLoadingSkeleton()
          ) : conversations.length === 0 ? (
            renderEmptyState()
          ) : (
            <div className={styles.conversationList}>
              {conversations.map((conversation, index) =>
                renderConversationItem(conversation, index)
              )}
            </div>
          )}
        </div>
      </div>

      {/* Delete Confirmation Dialog */}
      <Dialog open={conversationToDelete !== null} onOpenChange={(e, data) => {
        if (!data.open) {
          handleDeleteCancel();
        }
      }}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete Conversation</DialogTitle>
            <DialogContent>
              <Text>
                Are you sure you want to delete &quot;{conversationToDelete?.title || 'New Conversation'}&quot;?
                This action cannot be undone.
              </Text>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={handleDeleteCancel}>
                Cancel
              </Button>
              <Button appearance="primary" onClick={handleDeleteConfirm}>
                Delete
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
}
