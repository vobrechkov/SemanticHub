# Answer Component

A comprehensive React component for rendering AI chat responses with markdown, syntax highlighting, citations, and interactive features.

## Features

- **Markdown Rendering**: Full GitHub Flavored Markdown (GFM) support including tables, task lists, and strikethrough
- **Syntax Highlighting**: Code blocks with language detection and copy-to-clipboard functionality
- **Citations**: Inline citation references with expandable source list
- **Copy Functionality**: Copy entire answer or individual code blocks to clipboard
- **Streaming Support**: Visual indicator for messages being streamed in real-time
- **Security**: HTML sanitization using DOMPurify and rehype-sanitize
- **Responsive Design**: Mobile-friendly with breakpoints for different screen sizes
- **Dark Mode**: CSS variables for automatic dark mode support
- **Accessibility**: WCAG 2.1 compliant with keyboard navigation and screen reader support

## Installation

The component is already configured in the SemanticHub.WebApp project with all required dependencies:

```json
{
  "dependencies": {
    "@fluentui/react-components": "^9.72.3",
    "@fluentui/react-icons": "^2.0.258",
    "dompurify": "^3.3.0",
    "react-markdown": "^10.1.0",
    "react-syntax-highlighter": "^15.5.0",
    "rehype-raw": "^7.0.0",
    "rehype-sanitize": "^6.0.0",
    "remark-gfm": "^4.0.1"
  }
}
```

## Usage

### Basic Example

```tsx
import { Answer } from '@/components/chat';

function ChatMessage() {
  return (
    <Answer
      answer="Your **markdown** content here with [doc1] citations"
      citations={[
        {
          id: 'doc1',
          title: 'Source Document',
          content: 'Document excerpt...',
          filePath: 'docs/example.md',
          score: 0.95,
        },
      ]}
      messageId="msg-123"
      onCitationClick={(citation) => console.log('Clicked:', citation)}
    />
  );
}
```

### Streaming Example

```tsx
import { Answer } from '@/components/chat';
import { useState, useEffect } from 'react';

function StreamingChat() {
  const [content, setContent] = useState('');
  const [isStreaming, setIsStreaming] = useState(true);

  useEffect(() => {
    // Simulate streaming
    const text = 'Full response text...';
    let index = 0;

    const interval = setInterval(() => {
      if (index < text.length) {
        setContent(text.slice(0, index + 1));
        index++;
      } else {
        setIsStreaming(false);
        clearInterval(interval);
      }
    }, 50);

    return () => clearInterval(interval);
  }, []);

  return (
    <Answer
      answer={content}
      messageId="streaming-msg"
      isStreaming={isStreaming}
    />
  );
}
```

### With Custom Citation Handler

```tsx
import { Answer, type Citation } from '@/components/chat';
import { useState } from 'react';

function ChatWithCitations() {
  const [selectedCitation, setSelectedCitation] = useState<Citation | null>(null);

  const handleCitationClick = (citation: Citation) => {
    setSelectedCitation(citation);
    // Open modal, side panel, or navigate to source
    openCitationDetails(citation);
  };

  return (
    <>
      <Answer
        answer="AI response with [doc1] and [doc2] citations"
        citations={citations}
        messageId="msg-456"
        onCitationClick={handleCitationClick}
      />

      {selectedCitation && (
        <CitationModal citation={selectedCitation} onClose={() => setSelectedCitation(null)} />
      )}
    </>
  );
}
```

## Component Props

### AnswerProps

| Prop | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `answer` | `string` | Yes | - | The message content in markdown format |
| `citations` | `Citation[]` | No | `[]` | Array of source citations |
| `messageId` | `string` | Yes | - | Unique identifier for this message |
| `onCitationClick` | `(citation: Citation) => void` | No | - | Callback when a citation is clicked |
| `isStreaming` | `boolean` | No | `false` | Whether the message is still being streamed |
| `className` | `string` | No | - | Optional CSS class name |

### Citation Interface

```typescript
interface Citation {
  /** Unique identifier for the citation */
  id: string;
  /** The content/excerpt from the source */
  content: string;
  /** Title of the source document */
  title?: string;
  /** File path of the source */
  filePath?: string;
  /** URL of the source (if web-based) */
  url?: string;
  /** Chunk ID within the document */
  chunkId?: string;
  /** Optional part index for multi-part documents */
  partIndex?: number;
  /** Relevance score (0-1) */
  score?: number;
}
```

## Citation Format

Citations in the markdown should use the format `[docN]` where N is a 1-based index:

```markdown
This is a fact from the first document [doc1] and this is from the second [doc2].
The first document is cited again here [doc1].
```

The component will:
1. Parse these references
2. Replace them with superscript numbers (^1^, ^2^)
3. Make them clickable
4. Display the full citation list at the bottom

## Markdown Support

### Supported Markdown Features

- **Headings**: `# H1` through `###### H6`
- **Emphasis**: `*italic*`, `**bold**`, `***bold italic***`
- **Lists**: Ordered and unordered lists with nesting
- **Task Lists**: `- [ ] Unchecked` and `- [x] Checked`
- **Links**: `[text](url)`
- **Images**: `![alt](url)`
- **Code**: Inline \`code\` and fenced code blocks with language
- **Blockquotes**: `> quote`
- **Tables**: GitHub-style tables
- **Horizontal Rules**: `---` or `***`
- **Strikethrough**: `~~deleted~~`

### Code Blocks

Code blocks support syntax highlighting for 100+ languages:

````markdown
```typescript
async function example(): Promise<string> {
  return "Hello, World!";
}
```

```python
def example():
    return "Hello, World!"
```

```json
{
  "key": "value"
}
```
````

Each code block includes a copy button for easy clipboard copying.

## Styling

The component uses CSS Modules with CSS variables for theming:

### CSS Variables Used

```css
/* Colors */
--colorNeutralBackground1
--colorNeutralBackground2
--colorNeutralBackground3
--colorNeutralBackground4
--colorNeutralForeground1
--colorNeutralForeground2
--colorNeutralForeground3
--colorNeutralStroke1
--colorNeutralStroke2
--colorBrandBackground
--colorBrandForeground1
--colorBrandStroke1

/* Shadows */
--shadow4
```

### Custom Styling

You can override styles by passing a custom className:

```tsx
<Answer
  answer={content}
  messageId="msg"
  className="my-custom-answer"
/>
```

```css
.my-custom-answer {
  max-width: 1200px;
  padding: 24px;
}
```

## Security

The component implements multiple layers of security:

1. **rehype-sanitize**: Sanitizes HTML in markdown during rendering
2. **DOMPurify**: Additional client-side HTML sanitization
3. **Allowed Tags**: Whitelist of safe HTML elements
4. **Allowed Attributes**: Restricted attribute list
5. **No Script Execution**: Blocks all script tags and event handlers

### Sanitization Configuration

See `/src/utils/sanitize.ts` for the full configuration:

```typescript
const XSSAllowTags = [
  'a', 'abbr', 'article', 'b', 'blockquote', 'br', 'code',
  'div', 'em', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'hr',
  'img', 'li', 'ol', 'p', 'pre', 'span', 'strong', 'table',
  'tbody', 'td', 'th', 'thead', 'tr', 'ul', // ...
];

const XSSAllowAttributes = [
  'href', 'src', 'alt', 'title', 'class', 'id', 'target',
  'rel', 'type', 'align', 'colspan', 'rowspan', // ...
];
```

## Accessibility

The component follows WCAG 2.1 Level AA guidelines:

- **Keyboard Navigation**: All interactive elements are keyboard accessible
- **Focus Management**: Clear focus indicators on all interactive elements
- **ARIA Labels**: Descriptive labels for screen readers
- **Semantic HTML**: Proper use of semantic elements (article, button, etc.)
- **Color Contrast**: Meets contrast ratio requirements
- **Alternative Text**: Support for image alt text

### Keyboard Shortcuts

- **Tab**: Navigate through interactive elements
- **Enter/Space**: Activate buttons and toggle citations
- **Escape**: Close citation panel (when implemented)

## Performance

- **Memoization**: Uses `useMemo` to prevent unnecessary re-renders
- **Lazy Rendering**: Citations only rendered when expanded
- **Code Splitting**: Syntax highlighter loaded on demand
- **Optimized Re-renders**: Callback memoization with `useCallback`

## Browser Support

- Chrome 90+
- Firefox 88+
- Safari 14+
- Edge 90+

## Fluent UI v9 Components Used

The component uses the following Fluent UI v9 components:

- `Button`: Copy buttons and action buttons
- `Text`: Typography for labels and disclaimers
- `Tooltip`: Hover hints for buttons and citations

Note: Fluent UI v9 has a different API than v8. Key differences:

- Import from `@fluentui/react-components` (not `@fluentui/react`)
- Different component props and APIs
- No `Stack` component (use flexbox CSS instead)
- Design tokens instead of theme objects

## Related Files

- **Component**: `/src/components/chat/Answer.tsx`
- **Styles**: `/src/components/chat/Answer.module.css`
- **Utilities**:
  - `/src/utils/markdown.ts` - Citation parsing
  - `/src/utils/sanitize.ts` - HTML sanitization
- **Example**: `/src/components/chat/Answer.example.tsx`
- **Types**: Exported from component file

## Testing Recommendations

### Unit Tests

```typescript
import { render, screen, fireEvent } from '@testing-library/react';
import { Answer } from './Answer';

describe('Answer Component', () => {
  it('renders markdown content', () => {
    render(<Answer answer="**Bold** text" messageId="test" />);
    expect(screen.getByText('Bold')).toBeInTheDocument();
  });

  it('parses citations correctly', () => {
    const citations = [{ id: 'doc1', content: 'Test', title: 'Test Doc' }];
    render(
      <Answer answer="Text [doc1]" citations={citations} messageId="test" />
    );
    expect(screen.getByText(/1/)).toBeInTheDocument();
  });

  it('calls onCitationClick when citation is clicked', () => {
    const handleClick = jest.fn();
    const citations = [{ id: 'doc1', content: 'Test', title: 'Test Doc' }];

    render(
      <Answer
        answer="Text [doc1]"
        citations={citations}
        messageId="test"
        onCitationClick={handleClick}
      />
    );

    fireEvent.click(screen.getByRole('button', { name: /citation/i }));
    expect(handleClick).toHaveBeenCalledWith(citations[0]);
  });
});
```

### Integration Tests

- Test with real API responses
- Test streaming behavior
- Test citation navigation flow
- Test copy-to-clipboard functionality

### Accessibility Tests

- Test with screen readers (NVDA, JAWS, VoiceOver)
- Test keyboard-only navigation
- Use axe-core for automated accessibility testing

## Troubleshooting

### Citations not appearing

Ensure citations array is provided and citation format in markdown is correct (`[doc1]`, `[doc2]`, etc.).

### Code highlighting not working

Verify the language name in the code fence is valid. Use common names like `typescript`, `python`, `javascript`.

### Styling issues in dark mode

Check that your theme provides the required CSS variables. See the Styling section for the list of required variables.

### Copy button not working

The clipboard API requires HTTPS or localhost. Ensure your app is served over a secure connection.

## Contributing

When contributing to this component:

1. Maintain TypeScript strict mode compliance
2. Add tests for new features
3. Update this README with new props or features
4. Follow existing code style and patterns
5. Ensure accessibility is maintained

## License

Part of the SemanticHub project. See project root for license information.
