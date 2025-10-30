/**
 * API Client Usage Examples
 *
 * These examples demonstrate how to use the SemanticHub API client.
 * You can copy these patterns into your components.
 */

import {
  streamChatMessage,
  listConversations,
  createConversation,
  getConversation,
  updateConversationTitle,
  deleteConversation,
  deleteAllConversations,
  formatApiError,
  isApiError,
} from './client';

import type {
  Conversation,
  StreamedChatChunk,
  Citation,
} from './models';

// ============================================================================
// Example 1: Basic Chat Streaming
// ============================================================================

export async function example1_BasicChatStreaming() {
  console.log('Example 1: Basic Chat Streaming');
  console.log('================================\n');

  try {
    let fullResponse = '';

    for await (const chunk of streamChatMessage({
      message: 'What is semantic search?',
    })) {
      // Accumulate the response
      fullResponse += chunk.content;

      // Print each chunk as it arrives
      process.stdout.write(chunk.content);

      if (chunk.isComplete) {
        console.log('\n\nStream complete!');
        console.log('Message ID:', chunk.messageId);

        if (chunk.citations && chunk.citations.length > 0) {
          console.log('\nCitations:');
          chunk.citations.forEach((citation, i) => {
            console.log(`  ${i + 1}. ${citation.title || 'Source'}`);
            console.log(`     ${citation.content.substring(0, 100)}...`);
          });
        }
        break;
      }
    }

    return fullResponse;
  } catch (error) {
    console.error('Error:', formatApiError(error));
    throw error;
  }
}

// ============================================================================
// Example 2: Chat with Conversation Context
// ============================================================================

export async function example2_ChatWithContext() {
  console.log('Example 2: Chat with Conversation Context');
  console.log('=========================================\n');

  try {
    // Create a new conversation
    const conversation = await createConversation({
      title: 'Learning about RAG',
    });
    console.log('Created conversation:', conversation.id);

    // Send first message
    console.log('\nUser: What is RAG?');
    console.log('Assistant: ');
    for await (const chunk of streamChatMessage({
      message: 'What is RAG?',
      conversationId: conversation.id,
      includeHistory: true,
    })) {
      process.stdout.write(chunk.content);
      if (chunk.isComplete) break;
    }

    // Send follow-up message (will include previous context)
    console.log('\n\nUser: How does it work?');
    console.log('Assistant: ');
    for await (const chunk of streamChatMessage({
      message: 'How does it work?',
      conversationId: conversation.id,
      includeHistory: true,
    })) {
      process.stdout.write(chunk.content);
      if (chunk.isComplete) break;
    }

    // Load the full conversation
    const fullConversation = await getConversation(conversation.id);
    console.log('\n\nFull conversation has', fullConversation.messages.length, 'messages');

    return fullConversation;
  } catch (error) {
    console.error('Error:', formatApiError(error));
    throw error;
  }
}

// ============================================================================
// Example 3: Managing Conversations
// ============================================================================

export async function example3_ManagingConversations() {
  console.log('Example 3: Managing Conversations');
  console.log('=================================\n');

  try {
    // List existing conversations
    console.log('Fetching conversations...');
    const conversations = await listConversations(0, 10);
    console.log(`Found ${conversations.length} conversations:`);

    conversations.forEach((conv, i) => {
      console.log(`  ${i + 1}. ${conv.title} (${conv.messages.length} messages)`);
    });

    // Create a new conversation
    console.log('\nCreating new conversation...');
    const newConv = await createConversation({
      title: 'Test Conversation',
    });
    console.log('Created:', newConv.id);

    // Rename it
    console.log('\nRenaming conversation...');
    const renamed = await updateConversationTitle(newConv.id, 'Renamed Conversation');
    console.log('New title:', renamed.title);

    // Delete it
    console.log('\nDeleting conversation...');
    await deleteConversation(newConv.id);
    console.log('Deleted successfully');

    return conversations;
  } catch (error) {
    console.error('Error:', formatApiError(error));
    throw error;
  }
}

// ============================================================================
// Example 4: Request Cancellation
// ============================================================================

export async function example4_RequestCancellation() {
  console.log('Example 4: Request Cancellation');
  console.log('===============================\n');

  const abortController = new AbortController();

  // Cancel after 2 seconds
  setTimeout(() => {
    console.log('\n\nCancelling request...');
    abortController.abort();
  }, 2000);

  try {
    console.log('Streaming response (will cancel after 2s):');
    console.log('Assistant: ');

    for await (const chunk of streamChatMessage(
      {
        message: 'Tell me a very long story about semantic search and RAG systems...',
      },
      abortController.signal
    )) {
      process.stdout.write(chunk.content);
      if (chunk.isComplete) break;
    }

    console.log('\n\nStream completed normally');
  } catch (error) {
    if (isApiError(error) && error.status === 0) {
      console.log('\n\nRequest was successfully cancelled');
    } else {
      console.error('Error:', formatApiError(error));
      throw error;
    }
  }
}

// ============================================================================
// Example 5: Error Handling
// ============================================================================

export async function example5_ErrorHandling() {
  console.log('Example 5: Error Handling');
  console.log('=========================\n');

  // Try to get a non-existent conversation
  try {
    console.log('Attempting to fetch non-existent conversation...');
    await getConversation('non-existent-id');
  } catch (error) {
    if (isApiError(error)) {
      console.log('Caught API error:');
      console.log('  Status:', error.status);
      console.log('  Message:', error.message);
      console.log('  Formatted:', formatApiError(error));

      if (error.status === 404) {
        console.log('  -> Handling 404: Resource not found');
      }
    } else {
      console.log('Unexpected error:', error);
    }
  }

  // Try to send an empty message
  try {
    console.log('\nAttempting to send empty message...');
    for await (const chunk of streamChatMessage({ message: '' })) {
      console.log('Chunk:', chunk.content);
    }
  } catch (error) {
    if (isApiError(error)) {
      console.log('Caught API error:');
      console.log('  Status:', error.status);
      console.log('  Message:', error.message);
      console.log('  Formatted:', formatApiError(error));
    }
  }
}

// ============================================================================
// Example 6: Extracting Citations
// ============================================================================

export async function example6_ExtractingCitations() {
  console.log('Example 6: Extracting Citations');
  console.log('================================\n');

  try {
    const citations: Citation[] = [];
    let fullResponse = '';

    console.log('User: What documentation is available about .NET Aspire?');
    console.log('Assistant: ');

    for await (const chunk of streamChatMessage({
      message: 'What documentation is available about .NET Aspire?',
    })) {
      fullResponse += chunk.content;
      process.stdout.write(chunk.content);

      if (chunk.isComplete && chunk.citations) {
        citations.push(...chunk.citations);
      }
    }

    console.log('\n\nExtracted Citations:');
    console.log('===================');

    if (citations.length === 0) {
      console.log('No citations found in response');
    } else {
      citations.forEach((citation, i) => {
        console.log(`\n${i + 1}. ${citation.title || 'Untitled'}`);
        console.log(`   ID: ${citation.id}`);
        console.log(`   Excerpt: ${citation.content.substring(0, 200)}...`);

        if (citation.url) {
          console.log(`   URL: ${citation.url}`);
        }

        if (citation.score !== undefined) {
          console.log(`   Relevance Score: ${(citation.score * 100).toFixed(1)}%`);
        }
      });
    }

    return { response: fullResponse, citations };
  } catch (error) {
    console.error('Error:', formatApiError(error));
    throw error;
  }
}

// ============================================================================
// Example 7: Pagination Through Conversations
// ============================================================================

export async function example7_PaginationThroughConversations() {
  console.log('Example 7: Pagination Through Conversations');
  console.log('===========================================\n');

  try {
    const pageSize = 5;
    let offset = 0;
    let allConversations: Conversation[] = [];
    let page = 1;

    while (true) {
      console.log(`Fetching page ${page} (offset: ${offset}, limit: ${pageSize})...`);

      const conversations = await listConversations(offset, pageSize);

      if (conversations.length === 0) {
        console.log('No more conversations');
        break;
      }

      console.log(`  Retrieved ${conversations.length} conversations`);
      allConversations.push(...conversations);

      conversations.forEach((conv, i) => {
        console.log(`    ${offset + i + 1}. ${conv.title}`);
      });

      if (conversations.length < pageSize) {
        console.log('Reached last page');
        break;
      }

      offset += pageSize;
      page++;
    }

    console.log(`\nTotal conversations loaded: ${allConversations.length}`);
    return allConversations;
  } catch (error) {
    console.error('Error:', formatApiError(error));
    throw error;
  }
}

// ============================================================================
// Example 8: Complete Workflow
// ============================================================================

export async function example8_CompleteWorkflow() {
  console.log('Example 8: Complete Workflow');
  console.log('============================\n');

  try {
    // Step 1: Create conversation
    console.log('Step 1: Creating conversation...');
    const conversation = await createConversation({
      title: 'Architecture Discussion',
    });
    console.log(`Created conversation: ${conversation.id}`);

    // Step 2: First message
    console.log('\nStep 2: Sending first message...');
    console.log('User: What is a microservices architecture?');
    console.log('Assistant: ');

    let firstResponse = '';
    for await (const chunk of streamChatMessage({
      message: 'What is a microservices architecture?',
      conversationId: conversation.id,
      includeHistory: true,
    })) {
      firstResponse += chunk.content;
      process.stdout.write(chunk.content);
      if (chunk.isComplete) break;
    }

    // Step 3: Follow-up message
    console.log('\n\nStep 3: Sending follow-up message...');
    console.log('User: What are the pros and cons?');
    console.log('Assistant: ');

    let secondResponse = '';
    for await (const chunk of streamChatMessage({
      message: 'What are the pros and cons?',
      conversationId: conversation.id,
      includeHistory: true,
    })) {
      secondResponse += chunk.content;
      process.stdout.write(chunk.content);
      if (chunk.isComplete) break;
    }

    // Step 4: Load full conversation
    console.log('\n\nStep 4: Loading full conversation...');
    const fullConv = await getConversation(conversation.id);
    console.log(`Conversation has ${fullConv.messages.length} messages`);

    // Step 5: Rename based on content
    console.log('\nStep 5: Renaming conversation...');
    await updateConversationTitle(conversation.id, 'Microservices Architecture Discussion');
    console.log('Renamed successfully');

    // Step 6: List all conversations to verify
    console.log('\nStep 6: Listing all conversations...');
    const allConversations = await listConversations();
    const ourConv = allConversations.find(c => c.id === conversation.id);
    console.log(`Found our conversation: "${ourConv?.title}"`);

    console.log('\nWorkflow completed successfully!');
    return conversation;
  } catch (error) {
    console.error('Error:', formatApiError(error));
    throw error;
  }
}

// ============================================================================
// Run Examples
// ============================================================================

/**
 * Main function to run all examples
 * Comment out examples you don't want to run
 */
export async function runExamples() {
  console.log('\n╔══════════════════════════════════════════════╗');
  console.log('║  SemanticHub API Client Usage Examples      ║');
  console.log('╚══════════════════════════════════════════════╝\n');

  try {
    // Run each example (comment out ones you don't want)
    // await example1_BasicChatStreaming();
    // await example2_ChatWithContext();
    // await example3_ManagingConversations();
    // await example4_RequestCancellation();
    // await example5_ErrorHandling();
    // await example6_ExtractingCitations();
    // await example7_PaginationThroughConversations();
    // await example8_CompleteWorkflow();

    console.log('\n✓ All examples completed successfully!\n');
  } catch (error) {
    console.error('\n✗ Examples failed with error:', error);
    process.exit(1);
  }
}

// Uncomment to run when executing this file directly
// if (require.main === module) {
//   runExamples();
// }
