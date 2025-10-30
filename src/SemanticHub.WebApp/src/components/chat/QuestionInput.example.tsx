/**
 * Example usage of the QuestionInput component
 *
 * This file demonstrates how to use the QuestionInput component
 * in different scenarios within the chat interface.
 */

'use client';

import { useState } from 'react';
import { QuestionInput } from './QuestionInput';

/**
 * Basic usage - Simple chat input
 */
export const BasicExample = () => {
  const [messages, setMessages] = useState<string[]>([]);

  const handleSend = (message: string) => {
    setMessages([...messages, message]);
    console.log('Message sent:', message);
  };

  return (
    <div style={{ maxWidth: '800px', margin: '0 auto' }}>
      <QuestionInput
        onSend={handleSend}
        placeholder="Ask me anything..."
      />
    </div>
  );
};

/**
 * With loading state - Disable input while processing
 */
export const LoadingExample = () => {
  const [isLoading, setIsLoading] = useState(false);

  const handleSend = async (message: string) => {
    setIsLoading(true);

    try {
      // Simulate API call
      await new Promise(resolve => setTimeout(resolve, 2000));
      console.log('Message sent:', message);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div style={{ maxWidth: '800px', margin: '0 auto' }}>
      <QuestionInput
        onSend={handleSend}
        disabled={isLoading}
        placeholder="Type your message..."
      />
    </div>
  );
};

/**
 * Custom max length - Shorter message limit
 */
export const CustomMaxLengthExample = () => {
  const handleSend = (message: string) => {
    console.log('Message sent:', message);
  };

  return (
    <div style={{ maxWidth: '800px', margin: '0 auto' }}>
      <QuestionInput
        onSend={handleSend}
        maxLength={500}
        placeholder="Keep it brief (max 500 characters)..."
      />
    </div>
  );
};

/**
 * Chat interface - Full integration example
 */
export const ChatInterfaceExample = () => {
  const [messages, setMessages] = useState<Array<{ role: 'user' | 'assistant'; content: string }>>([
    { role: 'assistant', content: 'Hello! How can I help you today?' },
  ]);
  const [isLoading, setIsLoading] = useState(false);

  const handleSend = async (message: string) => {
    // Add user message
    setMessages(prev => [...prev, { role: 'user', content: message }]);
    setIsLoading(true);

    try {
      // Call API
      const response = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message }),
      });

      const data = await response.json();

      // Add assistant response
      setMessages(prev => [...prev, { role: 'assistant', content: data.response }]);
    } catch (error) {
      console.error('Failed to send message:', error);
      setMessages(prev => [...prev, {
        role: 'assistant',
        content: 'Sorry, I encountered an error. Please try again.'
      }]);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div style={{
      display: 'flex',
      flexDirection: 'column',
      height: '100vh',
      maxWidth: '800px',
      margin: '0 auto',
      padding: '20px',
    }}>
      {/* Messages area */}
      <div style={{
        flex: 1,
        overflowY: 'auto',
        marginBottom: '20px',
        display: 'flex',
        flexDirection: 'column',
        gap: '12px',
      }}>
        {messages.map((msg, idx) => (
          <div
            key={idx}
            style={{
              alignSelf: msg.role === 'user' ? 'flex-end' : 'flex-start',
              background: msg.role === 'user' ? '#0078d4' : '#f3f2f1',
              color: msg.role === 'user' ? 'white' : 'black',
              padding: '12px 16px',
              borderRadius: '8px',
              maxWidth: '70%',
            }}
          >
            {msg.content}
          </div>
        ))}
      </div>

      {/* Input area */}
      <QuestionInput
        onSend={handleSend}
        disabled={isLoading}
        placeholder={isLoading ? 'Waiting for response...' : 'Type your message...'}
      />
    </div>
  );
};

/**
 * With validation - Custom validation logic
 */
export const ValidationExample = () => {
  const [error, setError] = useState<string | null>(null);

  const handleSend = (message: string) => {
    // Custom validation
    if (message.length < 5) {
      setError('Message must be at least 5 characters');
      return;
    }

    if (message.includes('spam')) {
      setError('Message contains prohibited content');
      return;
    }

    // Clear error and send
    setError(null);
    console.log('Message sent:', message);
  };

  return (
    <div style={{ maxWidth: '800px', margin: '0 auto' }}>
      {error && (
        <div style={{
          padding: '12px',
          marginBottom: '12px',
          background: '#fde7e9',
          color: '#a4262c',
          borderRadius: '4px',
        }}>
          {error}
        </div>
      )}

      <QuestionInput
        onSend={handleSend}
        placeholder="Type your message..."
      />
    </div>
  );
};
