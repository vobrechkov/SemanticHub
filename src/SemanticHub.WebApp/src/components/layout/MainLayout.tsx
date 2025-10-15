'use client';

import React from 'react';
import NavMenu from './NavMenu';
import ThemeToggle from './ThemeToggle';
import './MainLayout.css';

interface MainLayoutProps {
  children: React.ReactNode;
}

export default function MainLayout({ children }: MainLayoutProps) {
  return (
    <div className="page">
      <div className="sidebar">
        <NavMenu />
      </div>

      <main>
        <div className="top-row px-4">
          <a 
            href="https://learn.microsoft.com/aspnet/core/" 
            target="_blank" 
            rel="noopener noreferrer"
            className="text-decoration-none"
          >
            About
          </a>
          
          <ThemeToggle />
        </div>

        <article className="content px-4">
          {children}
        </article>
      </main>
    </div>
  );
}
