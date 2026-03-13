import { useEffect, useState } from 'react';

function getInitialTheme(): boolean {
  const stored = localStorage.getItem('theme');
  if (stored) return stored === 'dark';
  return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

export function ThemeToggle() {
  const [dark, setDark] = useState(getInitialTheme);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark);
    localStorage.setItem('theme', dark ? 'dark' : 'light');
  }, [dark]);

  return (
    <button className="rounded border px-3 py-2" onClick={() => setDark((x) => !x)}>
      {dark ? 'Dark' : 'Light'} Mode
    </button>
  );
}
