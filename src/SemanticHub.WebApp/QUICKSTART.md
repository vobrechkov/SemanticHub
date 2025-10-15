# Quick Start Guide: SemanticHub WebApp (Next.js)

## ✅ Migration Complete

The Blazor `SemanticHub.Web` application has been successfully migrated to a modern Next.js/React application called `SemanticHub.WebApp`.

## 🚀 Running the Application

### Option 1: Run via .NET Aspire (Recommended)

This will start all services including the Next.js app:

```bash
cd /Users/vesselin/Source/GitHub/SemanticKernelMemoryRAG/src
dotnet run --project SemanticHub.AppHost
```

The Aspire dashboard will show all running services. Look for the `webapp` entry to find the Next.js app URL (typically http://localhost:3000).

### Option 2: Run Standalone (Development)

For frontend-only development:

```bash
cd /Users/vesselin/Source/GitHub/SemanticKernelMemoryRAG/src/SemanticHub.WebApp
npm run dev
```

**Note**: You'll need to ensure the Knowledge API is running separately and update the `KNOWLEDGE_API_URL` in `.env.local`.

## 📦 What's Included

### Pages
- **Home** (`/`) - Welcome page
- **Counter** (`/counter`) - Interactive counter demo
- **Weather** (`/weather`) - Weather forecast from Knowledge API

### Technology Stack
- **Next.js 15.5** - React framework with App Router
- **React 19** - UI library
- **Bootstrap 5** - CSS framework
- **React-Bootstrap** - React components for Bootstrap
- **React Icons** - Icon library
- **SWR** - Data fetching and caching
- **TypeScript** - Type safety

## 🔧 Development Commands

```bash
# Install dependencies
npm install

# Run development server
npm run dev

# Build for production
npm run build

# Run production server
npm start

# Lint code
npm run lint
```

## 🌍 Environment Variables

The app uses the following environment variable:

- `KNOWLEDGE_API_URL` - URL to the Knowledge API service

When running through Aspire, this is automatically configured. For standalone development, it's set in `.env.local`.

## 📁 Project Structure

```
SemanticHub.WebApp/
├── src/
│   ├── app/                    # Next.js App Router
│   │   ├── api/weather/       # API route for weather data
│   │   ├── counter/           # Counter page
│   │   ├── weather/           # Weather page
│   │   ├── layout.tsx         # Root layout
│   │   ├── page.tsx           # Home page
│   │   └── globals.css        # Global styles
│   └── components/
│       └── layout/            # Layout components
├── package.json               # Dependencies
├── tsconfig.json              # TypeScript config
├── next.config.ts             # Next.js config
└── README.md                  # Documentation
```

## 🔗 Integration Points

### With Aspire AppHost
- Registered as NPM app in `AppHost.cs`
- Automatic service discovery for Knowledge API
- Environment variables injected by Aspire

### With Knowledge API
- Weather data fetched via `/api/weather` route
- API proxy pattern to avoid CORS issues
- SWR for client-side caching

## ✨ Key Features

1. **Server-Side Rendering**: Fast initial page loads
2. **Client-Side Navigation**: Instant page transitions
3. **Data Caching**: SWR provides automatic caching and revalidation
4. **Type Safety**: Full TypeScript support
5. **Responsive Design**: Bootstrap grid system
6. **Hot Reload**: Instant feedback during development

## 🎨 Styling

The app uses Bootstrap 5 with custom CSS for layout:
- Sidebar navigation
- Top header bar
- Responsive design (mobile-friendly)
- Bootstrap components via React-Bootstrap

## 📝 Next Steps

To extend the application:

1. **Add new pages**: Create folders in `src/app/`
2. **Add new components**: Create in `src/components/`
3. **Add new API routes**: Create in `src/app/api/`
4. **Customize styling**: Edit CSS files or add new ones
5. **Add state management**: Consider Redux or Zustand if needed
6. **Add authentication**: Implement auth provider (NextAuth, etc.)

## 🐛 Troubleshooting

### Build fails
```bash
# Clear Next.js cache
rm -rf .next
npm run build
```

### Port already in use
```bash
# Kill process on port 3000
lsof -ti:3000 | xargs kill -9
```

### API not connecting
- Verify Knowledge API is running
- Check `KNOWLEDGE_API_URL` environment variable
- Check Aspire dashboard for service status

## 📚 Additional Resources

- [Next.js Documentation](https://nextjs.org/docs)
- [React Documentation](https://react.dev)
- [React-Bootstrap](https://react-bootstrap.github.io/)
- [Bootstrap Documentation](https://getbootstrap.com/docs/5.3/)
- [SWR Documentation](https://swr.vercel.app/)

## ✅ Verification Checklist

- [x] Next.js project created
- [x] All dependencies installed
- [x] Home page working
- [x] Counter page working
- [x] Weather page working
- [x] Navigation working
- [x] API integration working
- [x] Added to .NET solution
- [x] Registered in Aspire AppHost
- [x] Build successful
- [x] TypeScript configured
- [x] Environment variables configured

## 🎉 Success!

The migration is complete. Both the original Blazor app (`SemanticHub.Web`) and the new Next.js app (`SemanticHub.WebApp`) are now available in the solution and can run simultaneously through the Aspire AppHost.
