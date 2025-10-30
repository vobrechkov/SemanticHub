'use client';

import React, { useState, useRef, useCallback, useEffect } from 'react';
import { Textarea, Button, Text, Tooltip, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { SendRegular } from '@fluentui/react-icons';
import styles from './QuestionInput.module.css';

export interface QuestionInputProps {
  onSend: (message: string) => void;
  disabled?: boolean;
  placeholder?: string;
  maxLength?: number;
}

const useWarningStyles = makeStyles({
  warning: {
    color: tokens.colorPaletteRedForeground1,
  },
});

const QuestionInputComponent = ({
  onSend,
  disabled = false,
  placeholder = 'Type your message here...',
  maxLength = 2000,
}: QuestionInputProps) => {
  const [message, setMessage] = useState('');
  const [isComposing, setIsComposing] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const warningStyles = useWarningStyles();

  // Auto-focus on mount (optional)
  useEffect(() => {
    if (textareaRef.current && !disabled) {
      textareaRef.current.focus();
    }
  }, [disabled]);

  const trimmedMessage = message.trim();
  const messageLength = message.length;
  const isNearLimit = messageLength > maxLength * 0.8;
  const isOverLimit = messageLength > maxLength;
  const canSend = !disabled && trimmedMessage.length > 0 && !isOverLimit;

  const handleSend = useCallback(() => {
    if (!canSend) {
      return;
    }

    onSend(trimmedMessage);
    setMessage('');

    // Reset textarea height after clearing
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto';
    }

    // Re-focus input after sending
    setTimeout(() => {
      textareaRef.current?.focus();
    }, 0);
  }, [canSend, trimmedMessage, onSend]);

  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLTextAreaElement>) => {
      // Don't send if composing (e.g., using IME for Japanese/Chinese)
      if (isComposing) {
        return;
      }

      // Enter without shift = send
      if (event.key === 'Enter' && !event.shiftKey) {
        event.preventDefault();
        handleSend();
      }

      // Shift+Enter = new line (default behavior, no action needed)
    },
    [handleSend, isComposing]
  );

  const handleChange = useCallback(
    (_event: React.ChangeEvent<HTMLTextAreaElement>, data: { value: string }) => {
      // Enforce max length
      if (data.value.length <= maxLength) {
        setMessage(data.value);
      }
    },
    [maxLength]
  );

  const handleCompositionStart = useCallback(() => {
    setIsComposing(true);
  }, []);

  const handleCompositionEnd = useCallback(() => {
    setIsComposing(false);
  }, []);

  const getCharacterCounterClass = () => {
    if (isOverLimit) {
      return `${styles.characterCounter} ${warningStyles.warning}`;
    }
    return styles.characterCounter;
  };

  return (
    <div className={styles.container}>
      <div className={styles.inputWrapper}>
        <Textarea
          ref={textareaRef}
          className={styles.textarea}
          value={message}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          onCompositionStart={handleCompositionStart}
          onCompositionEnd={handleCompositionEnd}
          placeholder={placeholder}
          disabled={disabled}
          resize="vertical"
          appearance="outline"
          aria-label="Message input"
          aria-describedby={isNearLimit ? 'char-counter' : undefined}
        />

        <div className={styles.actionsContainer}>
          {isNearLimit && (
            <Text
              id="char-counter"
              size={200}
              className={getCharacterCounterClass()}
              role="status"
              aria-live="polite"
            >
              {messageLength} / {maxLength}
            </Text>
          )}

          <Tooltip
            content={
              disabled
                ? 'Please wait...'
                : !canSend
                ? 'Enter a message to send'
                : 'Send message (Enter)'
            }
            relationship="label"
          >
            <Button
              appearance="primary"
              icon={disabled ? <Spinner size="tiny" /> : <SendRegular />}
              disabled={!canSend}
              onClick={handleSend}
              aria-label="Send message"
              className={styles.sendButton}
            />
          </Tooltip>
        </div>
      </div>

      {/* Bottom border accent */}
      <div className={styles.bottomBorder} />
    </div>
  );
};

// Memoize component for performance
export const QuestionInput = React.memo(QuestionInputComponent, (prevProps, nextProps) => {
  return (
    prevProps.onSend === nextProps.onSend &&
    prevProps.disabled === nextProps.disabled &&
    prevProps.placeholder === nextProps.placeholder &&
    prevProps.maxLength === nextProps.maxLength
  );
});

QuestionInput.displayName = 'QuestionInput';
