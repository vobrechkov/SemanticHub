'use client';

import React, { useState } from 'react';
import { Answer, type Citation } from './Answer';

/**
 * Example usage of the Answer component
 */
export default function AnswerExample() {
  const [selectedCitation, setSelectedCitation] = useState<Citation | null>(null);

  // Example markdown content with citations
  const exampleAnswer = `
# Understanding RAG Systems

Retrieval-Augmented Generation (RAG) is a powerful AI architecture [doc1] that combines
the benefits of large language models with external knowledge retrieval [doc2].

## Key Components

1. **Vector Store**: Stores document embeddings for semantic search [doc3]
2. **Retrieval**: Finds relevant documents based on query similarity
3. **Generation**: LLM generates answers using retrieved context

## Code Example

Here's a simple implementation:

\`\`\`typescript
async function performRAG(query: string): Promise<string> {
  // Retrieve relevant documents
  const documents = await vectorStore.search(query, topK: 5);

  // Generate answer with context
  const context = documents.map(d => d.content).join('\\n');
  const answer = await llm.complete(\`Context: \${context}\\n\\nQuestion: \${query}\`);

  return answer;
}
\`\`\`

## Benefits

- **Accuracy**: Grounds responses in factual information [doc1]
- **Transparency**: Provides source citations for verification [doc2]
- **Flexibility**: Can update knowledge without retraining [doc3]

> RAG systems represent a significant advancement in AI's ability to provide
> accurate, traceable answers from large knowledge bases.

## Performance Considerations

| Metric | Value | Notes |
|--------|-------|-------|
| Latency | ~500ms | Includes retrieval + generation |
| Accuracy | 85-95% | Depends on document quality |
| Cost | $0.01/query | Approximate with GPT-4 |

Learn more about [RAG systems](https://example.com) and best practices for implementation.
`;

  // Example citations
  const exampleCitations: Citation[] = [
    {
      id: 'doc1',
      title: 'Introduction to RAG Systems',
      content: 'RAG combines retrieval with generation to provide accurate, grounded responses...',
      filePath: 'docs/ai/rag-introduction.md',
      chunkId: '0',
      score: 0.95,
      partIndex: 1,
    },
    {
      id: 'doc2',
      title: 'RAG Architecture Patterns',
      content: 'Modern RAG systems use vector databases for efficient semantic search...',
      filePath: 'docs/ai/rag-architecture.md',
      url: 'https://example.com/docs/rag-architecture',
      chunkId: '2',
      score: 0.89,
      partIndex: 3,
    },
    {
      id: 'doc3',
      title: 'Implementing Vector Stores',
      content: 'Vector stores like Azure AI Search enable fast similarity search...',
      filePath: 'docs/databases/vector-stores.md',
      chunkId: '5',
      score: 0.82,
      partIndex: 6,
    },
  ];

  const handleCitationClick = (citation: Citation) => {
    console.log('Citation clicked:', citation);
    setSelectedCitation(citation);
    alert(`Clicked citation: ${citation.title || citation.id}`);
  };

  return (
    <div style={{ padding: '24px', maxWidth: '900px', margin: '0 auto' }}>
      <h1>Answer Component Examples</h1>

      <section style={{ marginBottom: '48px' }}>
        <h2>Full Answer with Citations</h2>
        <Answer
          answer={exampleAnswer}
          citations={exampleCitations}
          messageId="example-1"
          onCitationClick={handleCitationClick}
        />
      </section>

      <section style={{ marginBottom: '48px' }}>
        <h2>Simple Answer (No Citations)</h2>
        <Answer
          answer="This is a simple answer without any citations. It still supports **markdown** formatting like _italic_ and `code`."
          messageId="example-2"
        />
      </section>

      <section style={{ marginBottom: '48px' }}>
        <h2>Streaming Answer</h2>
        <Answer
          answer="This answer is currently being streamed from the AI model..."
          messageId="example-3"
          isStreaming={true}
        />
      </section>

      <section style={{ marginBottom: '48px' }}>
        <h2>Code-Heavy Answer</h2>
        <Answer
          answer={`
Here's how to use the Answer component:

\`\`\`tsx
import { Answer, type Citation } from '@/components/chat';

function ChatMessage() {
  return (
    <Answer
      answer="Your markdown content here"
      citations={citations}
      messageId="msg-123"
      onCitationClick={(citation) => console.log(citation)}
    />
  );
}
\`\`\`

You can also include inline code like \`const x = 42\` in your text.

\`\`\`json
{
  "example": "JSON data",
  "features": ["markdown", "citations", "syntax highlighting"]
}
\`\`\`
`}
          messageId="example-4"
        />
      </section>

      <section style={{ marginBottom: '48px' }}>
        <h2>Answer with Tables and Lists</h2>
        <Answer
          answer={`
## Feature Comparison

| Feature | Basic | Pro | Enterprise |
|---------|-------|-----|------------|
| Users | 1-10 | 11-100 | Unlimited |
| Storage | 10GB | 100GB | Custom |
| Support | Email | Priority | 24/7 Phone |

### Key Features:

- [x] Markdown rendering
- [x] Code syntax highlighting
- [x] Citation support
- [x] Copy to clipboard
- [ ] Export to PDF
- [ ] Share via link

### Unordered List:

* Fast performance
* Responsive design
* Accessible UI
  * WCAG 2.1 compliant
  * Keyboard navigation
  * Screen reader support
`}
          messageId="example-5"
        />
      </section>

      {selectedCitation && (
        <section style={{
          padding: '16px',
          background: '#f0f0f0',
          borderRadius: '8px',
          marginTop: '24px'
        }}>
          <h3>Last Selected Citation:</h3>
          <pre>{JSON.stringify(selectedCitation, null, 2)}</pre>
        </section>
      )}
    </div>
  );
}
