// Shell principal para pantallas autenticadas: <main> con las rutas + bottom nav fija.

import { NavLink, Outlet, useLocation } from 'react-router-dom';

const NAV = [
  { to: '/', label: 'Inicio', icon: '🏠' },
  { to: '/conversaciones', label: 'Chats', icon: '💬' },
  { to: '/agentes', label: 'Agentes', icon: '🤖' },
  { to: '/config', label: 'Config', icon: '⚙️' },
];

export default function AppShell() {
  const location = useLocation();
  // Ocultamos la bottom nav dentro del detalle de conversacion (para que el chat use todo el alto).
  const hideNav = /^\/conversaciones\/[^/]+$/.test(location.pathname);

  return (
    <div className="flex flex-col min-h-full">
      <main className={`flex-1 ${hideNav ? '' : 'pb-16'}`}>
        <Outlet />
      </main>
      {!hideNav && (
        <nav className="fixed bottom-0 left-0 right-0 bg-white border-t border-slate-100 flex" style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}>
          {NAV.map((n) => (
            <NavLink
              key={n.to}
              to={n.to}
              end={n.to === '/'}
              className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
            >
              <span className="text-lg leading-none">{n.icon}</span>
              <span className="mt-0.5">{n.label}</span>
            </NavLink>
          ))}
        </nav>
      )}
    </div>
  );
}
