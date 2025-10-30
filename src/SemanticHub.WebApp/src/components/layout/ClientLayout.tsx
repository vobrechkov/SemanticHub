'use client';

import type { ReactNode } from 'react';
import { usePathname } from 'next/navigation';
import MainLayout from '@/components/layout/MainLayout';
import { ThemeProvider, useTheme } from '@/contexts/ThemeContext';
import { ChatContextProvider } from '@/state/ChatContext';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';

function FluentThemeWrapper({ children }: { children: ReactNode }) {
  const { theme, mounted } = useTheme();

  // Avoid flash of wrong theme
  if (!mounted) {
    return null;
  }

  const fluentTheme = theme === 'dark' ? webDarkTheme : webLightTheme;

  return <FluentProvider theme={fluentTheme}>{children}</FluentProvider>;
}

function LayoutWrapper({ children }: { children: ReactNode }) {
  const pathname = usePathname();

  // Full-screen routes that bypass MainLayout
  const fullScreenRoutes = ['/'];
  const isFullScreen = fullScreenRoutes.includes(pathname);

  if (isFullScreen) {
    return <>{children}</>;
  }

  return <MainLayout>{children}</MainLayout>;
}

export function ClientLayout({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <FluentThemeWrapper>
        <ChatContextProvider>
          <LayoutWrapper>{children}</LayoutWrapper>
        </ChatContextProvider>
      </FluentThemeWrapper>
    </ThemeProvider>
  );
}
