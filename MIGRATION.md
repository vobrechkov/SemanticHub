# Migration Summary: Blazor to Next.js/React

## Overview

Successfully migrated the `SemanticHub.Web` Blazor Server application to a new Next.js/React project named `SemanticHub.WebApp`. The new application maintains the same functionality while using modern React ecosystem tools and popular OSS UI frameworks.

## What Was Created

### Project Structure
```
SemanticHub.WebApp/
├── src/
│   ├── app/
│   │   ├── api/
│   │   │   └── weather/
│   │   │       └── route.ts           # API proxy to Knowledge API
│   │   ├── counter/
│   │   │   ├── page.tsx               # Counter page
│   │   │   └── layout.tsx             # Counter page metadata
│   │   ├── weather/
│   │   │   ├── page.tsx               # Weather page
│   │   │   └── layout.tsx             # Weather page metadata
│   │   ├── layout.tsx                 # Root layout
│   │   ├── page.tsx                   # Home page
│   │   └── globals.css                # Global styles
│   └── components/
│       └── layout/
│           ├── MainLayout.tsx         # Main layout component
│           ├── MainLayout.css         # Layout styles
│           ├── NavMenu.tsx            # Navigation menu
│           └── NavMenu.css            # Navigation styles
├── package.json                       # Dependencies and scripts
├── tsconfig.json                      # TypeScript configuration
├── next.config.ts                     # Next.js configuration
├── .eslintrc.json                     # ESLint configuration
├── .gitignore                         # Git ignore rules
├── .env.local.example                 # Environment variables example
└── README.md                          # Project documentation
```

### Technology Stack

| Category | Technology | Version |
|----------|-----------|---------|
| Framework | Next.js | 15.1.0 |
| UI Library | React | 19.0.0 |
| CSS Framework | Bootstrap | 5.3.3 |
| React Components | React-Bootstrap | 2.10.6 |
| Icons | React Icons | 5.4.0 |
| Data Fetching | SWR | 2.2.5 |
| Language | TypeScript | 5.7.2 |

## Features Migrated

### 1. Home Page
- **Original**: `/Components/Pages/Home.razor`
- **New**: `/src/app/page.tsx`
- **Status**: ✅ Complete
- Simple welcome page with same content

### 2. Counter Page
- **Original**: `/Components/Pages/Counter.razor`
- **New**: `/src/app/counter/page.tsx`
- **Status**: ✅ Complete
- Interactive counter using React hooks (`useState`)
- Uses React-Bootstrap Button component

### 3. Weather Page
- **Original**: `/Components/Pages/Weather.razor`
- **New**: `/src/app/weather/page.tsx`
- **Status**: ✅ Complete
- Fetches data from Knowledge API via Next.js API route
- Uses SWR for client-side data fetching with caching
- Uses React-Bootstrap Table and Spinner components
- Implements loading and error states

### 4. Navigation
- **Original**: `/Components/Layout/NavMenu.razor`
- **New**: `/src/components/layout/NavMenu.tsx`
- **Status**: ✅ Complete
- Client-side navigation using Next.js Link
- Active route highlighting using `usePathname` hook
- Bootstrap Icons via React Icons (`react-icons/bs`)

### 5. Main Layout
- **Original**: `/Components/Layout/MainLayout.razor`
- **New**: `/src/components/layout/MainLayout.tsx`
- **Status**: ✅ Complete
- Responsive sidebar layout
- Top navigation bar
- Preserved original styling

## Integration with .NET Aspire

### AppHost Configuration
The Next.js app is registered in `SemanticHub.AppHost/AppHost.cs`:

```csharp
var webApp = builder.AddNpmApp("webapp", "../SemanticHub.WebApp", "dev")
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("KNOWLEDGE_API_URL", api.GetEndpoint("http"))
    .WithReference(api).WaitFor(api);
```

### Dependencies Added
- **Package**: `Aspire.Hosting.NodeJs` (v9.5.0)
- **Project**: Added to solution file as Node.js project

### Service Discovery
The Next.js app receives the Knowledge API URL via environment variable `KNOWLEDGE_API_URL`, automatically configured by Aspire.

## Key Differences from Blazor

| Aspect | Blazor (Original) | Next.js/React (New) |
|--------|------------------|---------------------|
| Rendering | Server-side with SignalR | Server-side (SSR) + Client-side (CSR) |
| State Management | Component state | React hooks (useState, etc.) |
| Navigation | NavLink component | Next.js Link + usePathname |
| Data Fetching | Dependency injection | SWR + API routes |
| Styling | CSS + Blazor CSS isolation | CSS + Bootstrap classes |
| Icons | Bootstrap Icons (CSS) | React Icons library |
| Components | Razor components | React components |

## Running the Application

### Development Mode
```bash
# From the AppHost
dotnet run --project src/SemanticHub.AppHost

# Or standalone (requires KNOWLEDGE_API_URL env var)
cd src/SemanticHub.WebApp
npm run dev
```

### Production Build
```bash
cd src/SemanticHub.WebApp
npm run build
npm start
```

## Environment Variables

The app requires the following environment variable:
- `KNOWLEDGE_API_URL`: URL to the Knowledge API service

When running through Aspire, this is automatically configured. For standalone development, create a `.env.local` file:

```env
KNOWLEDGE_API_URL=http://localhost:5000
```

## Benefits of Migration

1. **Modern Stack**: Using the latest React and Next.js versions
2. **Popular Frameworks**: Bootstrap and React-Bootstrap are widely adopted
3. **Better Performance**: Next.js optimizations (code splitting, image optimization, etc.)
4. **Rich Ecosystem**: Access to vast npm package ecosystem
5. **Developer Experience**: Hot reload, TypeScript support, better tooling
6. **SEO Friendly**: Server-side rendering capabilities
7. **Flexible Deployment**: Can be deployed to Vercel, AWS, Azure, etc.

## Next Steps

1. **Add more pages**: Extend functionality as needed
2. **Implement authentication**: Add auth if required
3. **Connect to Knowledge API**: Implement knowledge base features
4. **Add tests**: Unit tests with Jest/React Testing Library
5. **Optimize performance**: Implement caching strategies
6. **Add monitoring**: OpenTelemetry integration
7. **Customize styling**: Enhance UI/UX with custom themes

## Testing

To verify the migration:

1. Start the AppHost: `dotnet run --project src/SemanticHub.AppHost`
2. Navigate to the webapp endpoint (shown in Aspire dashboard)
3. Test all three pages: Home, Counter, Weather
4. Verify navigation works correctly
5. Verify weather data loads from the API

## Notes

- The original `SemanticHub.Web` Blazor project remains untouched
- Both apps can run side-by-side in the Aspire AppHost
- The Next.js app uses Bootstrap 5, matching the Blazor version
- All OSS dependencies with permissive licenses (MIT, Apache 2.0)
