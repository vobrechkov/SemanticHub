'use client';

import type { ReactNode } from 'react';
import MainLayout from '@/components/layout/MainLayout';
import { ThemeProvider } from '@/contexts/ThemeContext';

export function ClientLayout({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <MainLayout>{children}</MainLayout>
    </ThemeProvider>
  );
}
