# SemanticHub WebApp

A modern React/Next.js dashboard for the SemanticHub solution, providing a user interface for the Semantic Kernel Memory RAG system.

## Features

- **Home Page**: Welcome page with app information
- **Counter**: Interactive counter demonstrating client-side state management
- **Weather**: Displays weather forecast data fetched from the Knowledge API

## Tech Stack

- **Framework**: Next.js 15 (App Router)
- **UI Library**: React 19
- **Styling**: Bootstrap 5 + React-Bootstrap
- **Icons**: React Icons (Bootstrap Icons)
- **Data Fetching**: SWR for client-side data fetching
- **Language**: TypeScript

## Getting Started

### Prerequisites

- Node.js 18+ and npm/yarn/pnpm

### Installation

```bash
npm install
```

### Development

```bash
npm run dev
```

Open [http://localhost:3000](http://localhost:3000) in your browser.

### Build

```bash
npm run build
npm start
```

## Environment Variables

Create a `.env.local` file in the root directory:

```env
KNOWLEDGE_API_URL=http://localhost:5000
```

This URL will be automatically configured by .NET Aspire when running through the AppHost.

## Integration with .NET Aspire

This Next.js app is registered as an NPM application in the .NET Aspire AppHost and integrates with:

- **Knowledge API**: For data retrieval
- **Service Discovery**: Automatic endpoint configuration via Aspire

## Project Structure

```
src/
├── app/
│   ├── api/           # API routes (proxy to Knowledge API)
│   ├── counter/       # Counter page
│   ├── weather/       # Weather page
│   ├── layout.tsx     # Root layout
│   ├── page.tsx       # Home page
│   └── globals.css    # Global styles
└── components/
    └── layout/        # Layout components (MainLayout, NavMenu)
```

## License

MIT
