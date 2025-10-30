'use client';

import { useEffect, useRef, useCallback, useState } from 'react';
import { Answer } from '@/components/chat/Answer';
import { QuestionInput } from '@/components/chat/QuestionInput';
import { ChatHistory } from '@/components/chat/ChatHistory';
import { CitationPanel } from '@/components/chat/CitationPanel';
import { KeyboardShortcutsDialog } from '@/components/chat/KeyboardShortcutsDialog';
import { useChatContext } from '@/state/ChatContext';
import { useKeyboardShortcuts, KeyboardShortcut } from '@/hooks/useKeyboardShortcuts';
import {
  Button,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  History24Regular,
  Dismiss24Regular,
  Add24Regular,
  WeatherMoon24Regular,
  WeatherSunny24Regular,
  Stop24Filled,
  ArrowCounterclockwise24Regular,
  ArrowDown24Filled,
} from '@fluentui/react-icons';
import { useTheme } from '@/contexts/ThemeContext';
import styles from './page.module.css';

const useWelcomeStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    padding: tokens.spacingVerticalXXXL,
    textAlign: 'center',
  },
  title: {
    fontSize: tokens.fontSizeHero900,
    fontWeight: tokens.fontWeightSemibold,
    marginBottom: tokens.spacingVerticalL,
    color: tokens.colorNeutralForeground1,
  },
  subtitle: {
    fontSize: tokens.fontSizeBase500,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXXL,
    maxWidth: '600px',
  },
  suggestions: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    width: '100%',
    maxWidth: '600px',
  },
  suggestionButton: {
    textAlign: 'left',
    justifyContent: 'flex-start',
    height: 'auto',
    minHeight: '60px',
    padding: tokens.spacingVerticalM,
  },
});

export default function HomePage() {
  const {
    state,
    sendMessage,
    stopStreaming,
    retryLastMessage,
    clearError,
    setSelectedCitation,
    createNewConversation,
    toggleHistory,
    selectConversation,
    deleteConversation,
  } = useChatContext();
  const { theme, toggleTheme } = useTheme();
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const messagesContainerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const welcomeStyles = useWelcomeStyles();

  // Keyboard shortcuts help dialog state
  const [showShortcutsDialog, setShowShortcutsDialog] = useState(false);

  // Smart auto-scroll state
  const [isUserScrolled, setIsUserScrolled] = useState(false);
  const [showScrollButton, setShowScrollButton] = useState(false);

  // Auto-scroll to bottom when new messages arrive (only if user hasn't scrolled up)
  const scrollToBottom = useCallback((behavior: ScrollBehavior = 'smooth') => {
    if (messagesEndRef.current) {
      messagesEndRef.current.scrollIntoView({ behavior });
    }
  }, []);

  // Check if user is at the bottom of messages
  const checkScrollPosition = useCallback(() => {
    if (messagesContainerRef.current) {
      const { scrollTop, scrollHeight, clientHeight } = messagesContainerRef.current;
      const isAtBottom = scrollHeight - scrollTop - clientHeight < 100; // 100px threshold
      setIsUserScrolled(!isAtBottom);
      setShowScrollButton(!isAtBottom && state.messages.length > 0);
    }
  }, [state.messages.length]);

  // Handle scroll event
  useEffect(() => {
    const container = messagesContainerRef.current;
    if (!container) return;

    container.addEventListener('scroll', checkScrollPosition);
    return () => container.removeEventListener('scroll', checkScrollPosition);
  }, [checkScrollPosition]);

  // Auto-scroll when new messages arrive (only if user is at bottom)
  useEffect(() => {
    if (!isUserScrolled) {
      scrollToBottom();
    }
  }, [state.messages, isUserScrolled, scrollToBottom]);

  // Scroll to bottom when conversation changes
  useEffect(() => {
    setIsUserScrolled(false);
    scrollToBottom('auto');
  }, [state.currentConversation?.id, scrollToBottom]);

  // Handle sending messages
  const handleSendMessage = useCallback(
    async (message: string) => {
      await sendMessage(message);
    },
    [sendMessage]
  );

  // Handle citation clicks
  const handleCitationClick = useCallback(
    (citation: any) => {
      console.log('[ChatPage] Citation clicked:', citation);
      setSelectedCitation(citation);
    },
    [setSelectedCitation]
  );

  // Handle closing citation panel
  const handleCloseCitation = useCallback(() => {
    setSelectedCitation(null);
  }, [setSelectedCitation]);

  // Handle stop streaming
  const handleStopStreaming = useCallback(() => {
    stopStreaming();
  }, [stopStreaming]);

  // Handle retry
  const handleRetry = useCallback(async () => {
    await retryLastMessage();
  }, [retryLastMessage]);

  // Handle scroll to bottom button click
  const handleScrollToBottom = useCallback(() => {
    setIsUserScrolled(false);
    scrollToBottom();
  }, [scrollToBottom]);

  // Focus input helper
  const focusInput = useCallback(() => {
    const textarea = document.querySelector('textarea[aria-label="Message input"]') as HTMLTextAreaElement;
    if (textarea) {
      textarea.focus();
    }
  }, []);

  // Define keyboard shortcuts
  const shortcuts: KeyboardShortcut[] = [
    {
      key: 'n',
      ctrl: true,
      action: createNewConversation,
      description: 'Start a new conversation',
    },
    {
      key: 'k',
      ctrl: true,
      action: toggleHistory,
      description: 'Toggle conversation history',
    },
    {
      key: '/',
      ctrl: true,
      action: focusInput,
      description: 'Focus message input',
    },
    {
      key: '?',
      ctrl: true,
      action: () => setShowShortcutsDialog(true),
      description: 'Show keyboard shortcuts',
    },
    {
      key: 'Escape',
      action: () => {
        if (state.selectedCitation) {
          setSelectedCitation(null);
        } else if (state.isHistoryOpen) {
          toggleHistory();
        } else if (showShortcutsDialog) {
          setShowShortcutsDialog(false);
        }
      },
      description: 'Close sidebar or dialog',
    },
  ];

  // Register keyboard shortcuts
  useKeyboardShortcuts({
    shortcuts,
    enabled: true,
  });

  // Render welcome screen when no messages
  const renderWelcome = () => (
    <div className={welcomeStyles.container}>
      <Text className={welcomeStyles.title}>Welcome to SemanticHub</Text>
      <Text className={welcomeStyles.subtitle}>
        Ask questions about your documents and get intelligent answers powered by AI.
      </Text>
      <div className={welcomeStyles.suggestions}>
        <Button
          appearance="outline"
          className={welcomeStyles.suggestionButton}
          onClick={() => handleSendMessage('What documents do you have available?')}
        >
          <Text>What documents do you have available?</Text>
        </Button>
        <Button
          appearance="outline"
          className={welcomeStyles.suggestionButton}
          onClick={() => handleSendMessage('How does semantic search work?')}
        >
          <Text>How does semantic search work?</Text>
        </Button>
        <Button
          appearance="outline"
          className={welcomeStyles.suggestionButton}
          onClick={() => handleSendMessage('Tell me about the ingestion pipeline')}
        >
          <Text>Tell me about the ingestion pipeline</Text>
        </Button>
      </div>
    </div>
  );

  // Render messages list
  const renderMessages = () => (
    <>
      {state.messages.map((message) => {
        if (message.role === 'assistant') {
          return (
            <Answer
              key={message.id}
              answer={message.content}
              citations={message.citations}
              messageId={message.id}
              onCitationClick={handleCitationClick}
              isStreaming={state.isStreaming && message.id === state.messages[state.messages.length - 1]?.id}
              className={styles.message}
            />
          );
        } else if (message.role === 'user') {
          return (
            <div key={message.id} className={styles.userMessage}>
              <div className={styles.userMessageContent}>
                <Text>{message.content}</Text>
              </div>
            </div>
          );
        }
        return null;
      })}
    </>
  );

  return (
    <div className={styles.pageContainer}>
      {/* Header */}
      <div className={styles.header}>
        <div className={styles.headerContent}>
          <div className={styles.headerLeft}>
            <Button
              appearance="subtle"
              icon={<History24Regular />}
              onClick={toggleHistory}
              aria-label="Toggle conversation history"
              title="Conversation history"
            />
            <Text className={styles.headerTitle}>
              {state.currentConversation?.title || 'New Chat'}
            </Text>
          </div>
          <div className={styles.headerActions}>
            <Button
              appearance="primary"
              icon={<Add24Regular />}
              onClick={createNewConversation}
              aria-label="Start new conversation"
            >
              New Chat
            </Button>
            <Button
              appearance="subtle"
              icon={theme === 'dark' ? <WeatherSunny24Regular /> : <WeatherMoon24Regular />}
              onClick={toggleTheme}
              aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
              title={`Switch to ${theme === 'dark' ? 'light' : 'dark'} mode`}
            />
          </div>
        </div>
      </div>

      {/* Error Banner */}
      {state.error && (
        <div className={styles.errorBanner}>
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Error</MessageBarTitle>
              {state.error}
            </MessageBarBody>
            <div className={styles.errorActions}>
              <Button
                appearance="primary"
                size="small"
                icon={<ArrowCounterclockwise24Regular />}
                onClick={handleRetry}
                aria-label="Retry last message"
              >
                Retry
              </Button>
              <Button
                appearance="transparent"
                icon={<Dismiss24Regular />}
                onClick={clearError}
                aria-label="Dismiss error"
              />
            </div>
          </MessageBar>
        </div>
      )}

      {/* Main Content Area */}
      <div className={styles.mainContent}>
        {/* History Sidebar */}
        {state.isHistoryOpen && (
          <div className={styles.historySidebar}>
            <ChatHistory
              conversations={state.conversations}
              currentConversation={state.currentConversation}
              onSelectConversation={selectConversation}
              onDeleteConversation={deleteConversation}
              onNewConversation={createNewConversation}
              onClose={toggleHistory}
              isLoading={state.isLoading}
            />
          </div>
        )}

        {/* Messages Area */}
        <div className={styles.messagesContainer} ref={messagesContainerRef} role="log" aria-live="polite" aria-atomic="false">
          <div className={styles.messagesContent}>
            {state.messages.length === 0 ? renderWelcome() : renderMessages()}

            {/* Loading indicator */}
            {state.isLoading && state.messages.length === 0 && (
              <div className={styles.loadingContainer}>
                <Spinner size="large" label="Loading..." />
              </div>
            )}

            {/* Scroll anchor */}
            <div ref={messagesEndRef} />
          </div>

          {/* Scroll to Bottom Button */}
          {showScrollButton && (
            <div className={styles.scrollToBottomContainer}>
              <Button
                appearance="secondary"
                icon={<ArrowDown24Filled />}
                onClick={handleScrollToBottom}
                className={styles.scrollToBottomButton}
                aria-label="Scroll to bottom"
                title="Scroll to bottom"
              />
            </div>
          )}
        </div>

        {/* Input Area */}
        <div className={styles.inputContainer}>
          {/* Stop Streaming Button */}
          {state.isStreaming && (
            <div className={styles.stopStreamingContainer}>
              <Button
                appearance="secondary"
                icon={<Stop24Filled />}
                onClick={handleStopStreaming}
                className={styles.stopStreamingButton}
                aria-label="Stop generating response"
              >
                Stop Generating
              </Button>
            </div>
          )}

          <div className={styles.inputWrapper}>
            <QuestionInput
              onSend={handleSendMessage}
              disabled={state.isStreaming || state.isLoading}
              placeholder={
                state.isStreaming
                  ? 'Waiting for response...'
                  : 'Ask a question about your documents...'
              }
            />
          </div>
        </div>
      </div>

      {/* Citation Panel */}
      <CitationPanel
        citation={state.selectedCitation}
        onClose={handleCloseCitation}
        isOpen={state.selectedCitation !== null}
      />

      {/* Keyboard Shortcuts Dialog */}
      <KeyboardShortcutsDialog
        isOpen={showShortcutsDialog}
        onClose={() => setShowShortcutsDialog(false)}
        shortcuts={shortcuts}
      />
    </div>
  );
}
