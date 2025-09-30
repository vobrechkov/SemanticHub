'use client';

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { BsHouseDoorFill, BsPlusSquareFill, BsListNested } from 'react-icons/bs';
import './NavMenu.css';

export default function NavMenu() {
  const pathname = usePathname();

  const isActive = (path: string) => {
    if (path === '/' && pathname === '/') return true;
    if (path !== '/' && pathname.startsWith(path)) return true;
    return false;
  };

  return (
    <div className="nav-menu">
      <div className="top-row ps-3 navbar navbar-dark">
        <div className="container-fluid">
          <span className="navbar-brand">SemanticHub</span>
        </div>
      </div>

      <nav className="nav flex-column">
        <div className="nav-item px-3">
          <Link href="/" className={`nav-link ${isActive('/') && pathname === '/' ? 'active' : ''}`}>
            <BsHouseDoorFill className="me-2" aria-hidden="true" /> Home
          </Link>
        </div>

        <div className="nav-item px-3">
          <Link href="/counter" className={`nav-link ${isActive('/counter') ? 'active' : ''}`}>
            <BsPlusSquareFill className="me-2" aria-hidden="true" /> Counter
          </Link>
        </div>

        <div className="nav-item px-3">
          <Link href="/weather" className={`nav-link ${isActive('/weather') ? 'active' : ''}`}>
            <BsListNested className="me-2" aria-hidden="true" /> Weather
          </Link>
        </div>
      </nav>
    </div>
  );
}
