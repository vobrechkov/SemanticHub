'use client';

import React from 'react';
import { useTheme } from '@/contexts/ThemeContext';
import { BsSun, BsMoon } from 'react-icons/bs';

export default function ThemeToggle() {
  const { theme, toggleTheme } = useTheme();

  return (
    <button
      onClick={toggleTheme}
      className="theme-toggle"
      aria-label="Toggle theme"
      title={`Switch to ${theme === 'light' ? 'dark' : 'light'} mode`}
    >
      {theme === 'light' ? (
        <>
          <BsMoon size={18} />
          <span className="d-none d-sm-inline">Dark</span>
        </>
      ) : (
        <>
          <BsSun size={18} />
          <span className="d-none d-sm-inline">Light</span>
        </>
      )}
    </button>
  );
}
